using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 图像基类
    public abstract partial class ImageRecognitionBase : AutomationStepBase, IDisposable
    {
        // 延迟解析（与 AutomationStepBase.FileServices 一致）：两者都是 DI 单例，首次访问时解析即可，
        // 避免构造步骤对象就必须依赖已注册的 DI 容器
        private MainViewModel mainVM => SingletonLocator.Main;
        private FileServices fileServer => SingletonLocator.FileServices;
        private bool _isDisposed = false;

        #region 属性

        #region 路径

        private string _imagePath;
        private string _croppedImageName;

        /// <summary>
        /// 原图绝对路径（用户选取的外部图片）
        /// </summary>
        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        /// <summary>
        /// 裁剪图在包内的相对路径，如 "images/{Uid}.png"
        /// </summary>
        public string CroppedImageName
        {
            get => _croppedImageName;
            set => SetProperty(ref _croppedImageName, value);
        }

        /// <summary>
        /// 运行时从工作目录解析的裁剪图完整路径
        /// </summary>
        [JsonIgnore]
        public string CroppedImageFullPath
        {
            get
            {
                if (string.IsNullOrEmpty(CroppedImageName) || string.IsNullOrEmpty(mainVM.StepImageWorkDir))
                    return null;
                return Path.Combine(mainVM.StepImageWorkDir, CroppedImageName);
            }
        }

        #endregion


        private ImageSource _croppedImg;
        private Rect _croppedRect = new();
        private double _similarityThreshold = 0.85;
        private bool _highPrecision = false;
        private double _colorThreshold = 0.95;
        private List<ClickThumb> _clickThumbs = [];


        [JsonIgnore]
        public ImageSource ImgSrc
        {
            get
            {
                var img = LoadImage(ImagePath);
                // 原图路径失效时回退到包内打包的原图（images/{Uid}_src.*，保存时可选打包）
                if (img == null)
                    img = LoadImage(FindPackagedSourceImage());
                if (img == null)
                {
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("No_img_Warning", "No picture selected");
                }
                return img;
            }
        }

        /// <summary>在工作目录中查找打包的原图（约定名 images/{Uid}_src.*）；未找到返回 null</summary>
        private string FindPackagedSourceImage()
        {
            try
            {
                string workDir = mainVM?.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir)) return null;
                string imagesDir = Path.Combine(workDir, "images");
                if (!Directory.Exists(imagesDir)) return null;
                return Directory.GetFiles(imagesDir, $"{Uid}_src.*").FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        [JsonIgnore]
        public ImageSource CroppedImg
        {
            get
            {
                _croppedImg ??= LoadImage(CroppedImageFullPath);
                EnsureFrozenCroppedImg();
                if (_croppedImg == null)
                {
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("No_Cropimage_Warning", "No Croped picture");
                }
                return _croppedImg;
            }
            set
            {
                // 裁剪图在运行时段会被后台线程使用：赋值时先尽量冻结，未冻结对象跨线程访问会抛异常
                Autogui.FreezeIfPossible(value);
                if (SetProperty(ref _croppedImg, value))
                {
                    if (value != null)
                    {
                        // 当裁剪图更新时，自动保存到磁盘。
                        // 落盘失败必须显式记录（IsError/ErrorMessage + 日志），不能吞掉后仍提示“已保存”
                        if (!SaveCroppedImageToDisk(value))
                        {
                            IsError = true;
                            ErrorMessage = LanguageService.GetLocalizedString("SaveFailed", "Save failed");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 只在内存中更新裁剪图，不写磁盘。
        /// 克隆/快照等高频路径用它：避免每次属性变更或撤销快照都产生 PNG 写盘，真正的落盘交由保存动作完成。
        /// </summary>
        internal void SetCroppedImageInMemory(ImageSource image)
        {
            Autogui.FreezeIfPossible(image);
            SetProperty(ref _croppedImg, image, nameof(CroppedImg));
        }

        /// <summary>
        /// 保证缓存中的裁剪图已冻结：无法就地冻结时改用磁盘副本（LoadImage 加载后已冻结），
        /// 避免把未冻结的 WPF 对象交给运行时段的后台线程。
        /// </summary>
        private void EnsureFrozenCroppedImg()
        {
            if (!(_croppedImg is Freezable freezable) || IsFrozenSafe(freezable))
                return;

            Autogui.FreezeIfPossible(_croppedImg);
            if (IsFrozenSafe(freezable))
                return;

            var reloaded = LoadImage(CroppedImageFullPath);
            if (reloaded != null)
                _croppedImg = reloaded;
        }

        /// <summary>读取 Freezable.IsFrozen；跨线程读取未冻结对象会抛异常，此时按“未冻结”处理</summary>
        private static bool IsFrozenSafe(Freezable freezable)
        {
            try
            {
                return freezable.IsFrozen;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 运行时段取图：优先按文件路径读图（跨线程安全），取图顺序
        /// CroppedImageFullPath → FindPackagedSourceImage() → ImagePath；
        /// 有裁剪图时只取裁剪图自己的磁盘副本，避免退化成原图导致匹配区域错误。
        /// 文件路径全部不可用时才回退 WPF 图像转换，两条路都失败时抛 "Image Convert Error."。
        /// </summary>
        internal System.Drawing.Bitmap CreateRuntimeBitmap(ImageSource sourceImage, bool hasCroppedImage)
        {
            var paths = new List<string> { CroppedImageFullPath };
            if (!hasCroppedImage)
            {
                paths.Add(FindPackagedSourceImage());
                paths.Add(ImagePath);
            }
            return Autogui.CreateRuntimeBitmap(sourceImage, paths);
        }

        public Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }


        public List<ClickThumb> ClickThumbs { get => _clickThumbs; set => _clickThumbs = value; }

        [JsonIgnore]
        public List<Point> ClickPoints => ClickThumbs.Select(x => x.ClickPoint - CroppedRect.TopLeft).ToList();

        public double SimilarityThreshold
        {
            get => _similarityThreshold;
            set
            {
                if (SetProperty(ref _similarityThreshold, value))
                {
                    if (_similarityThreshold < 0)
                    {
                        _similarityThreshold = 0;
                    }
                    else if (_similarityThreshold > 1)
                    {
                        _similarityThreshold = 1;
                    }
                }
            }
        }

        /// <summary>
        /// 高识别精度（颜色识别）：勾选后除结构相似度外，还要求颜色相似度达标
        /// </summary>
        public bool HighPrecision
        {
            get => _highPrecision;
            set => SetProperty(ref _highPrecision, value);
        }

        /// <summary>
        /// 颜色相似度阈值（0~1，仅在高识别精度启用时参与判定）
        /// </summary>
        public double ColorThreshold
        {
            get => _colorThreshold;
            set
            {
                if (SetProperty(ref _colorThreshold, value))
                {
                    if (_colorThreshold < 0)
                    {
                        _colorThreshold = 0;
                    }
                    else if (_colorThreshold > 1)
                    {
                        _colorThreshold = 1;
                    }
                }
            }
        }


        #endregion

        #region 命令

        [RelayCommand]
        private void SelectImage()
        {
            var title = LanguageService.GetLocalizedString("Select_target_pic", "Open Image File");
            var filter = LanguageService.GetLocalizedString("Image_File", "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            ImagePath = PathServices.OpenPathDialog(title, filter);
        }

        [RelayCommand]
        private void EditImage()
        {
            if (ImgSrc != null)
            {
                WindowEditImage windowEditImage = new();
                windowEditImage.Show();
                // 使用 Background 优先级，确保 CropImage 控件完成内部布局和渲染后再设置裁剪框
                windowEditImage.Dispatcher.BeginInvoke(new Action(() =>
                {
                    windowEditImage.editImageViewModel.ImgSrc = ImgSrc;
                    windowEditImage.editImageViewModel.ImgDst = CroppedImg;
                    // 强制更新布局，确保 CropImage 的 Source 变更已触发 DrawImage
                    windowEditImage.UpdateLayout();
                    if (CroppedRect != null && (CroppedRect.Width != 0 || CroppedRect.Height != 0))
                        windowEditImage.editImageViewModel.SetCropRect(CroppedRect);
                    // 同步 CropRect 到 ViewModel，防止未裁剪直接保存时置空
                    windowEditImage.editImageViewModel.CropRect = CroppedRect;
                    if (ClickThumbs != null && ClickThumbs.Count > 0)
                        windowEditImage.editImageViewModel.SetThumbs(ClickThumbs);
                    // 传递 OCR 矩形（仅传递确实设置过区域的情况，“未设置”不当作有效区域）
                    if (this is FindImageStep findStep && findStep.HasOCRRegion)
                        windowEditImage.editImageViewModel.OCRRect = findStep.OCRRect;
                }), System.Windows.Threading.DispatcherPriority.Background);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect, clickthumbs, ocrRect) =>
                {
                    CroppedImg = img;
                    CroppedRect = rect;
                    ClickThumbs = clickthumbs;
                    // 保存 OCR 矩形：清空时统一归一化为 default(Rect) 语义，避免 Rect.Empty 的 ±∞ 进入克隆/序列化
                    if (this is FindImageStep findStep)
                    {
                        findStep.OCRRect = ocrRect;
                        findStep.EnableOCR = findStep.HasOCRRegion;
                    }
                    // 落盘失败时向上抛出：图片编辑窗口会捕获并提示“保存失败”，不再继续弹“已保存”
                    if (!LastCroppedImageSaved)
                    {
                        IsError = true;
                        ErrorMessage = LanguageService.GetLocalizedString("SaveFailed", "Save failed");
                        throw new InvalidOperationException(ErrorMessage);
                    }
                    IsError = false;
                };
            }
        }

        [RelayCommand]
        private void CaptureFullScreen()
        {
            using var bmp = Autogui.CaptureScreen();
            ApplyCapture(bmp);
        }

        [RelayCommand]
        private void CaptureRegion()
        {
            var win = new WindowSelectRegion();
            if (win.ShowDialog() != true || win.SelectedRegion == Rect.Empty) return;

            // SelectedRegion 是 WPF 逻辑像素（含虚拟桌面负坐标原点），CaptureScreenRegion 需要物理像素，
            // 两者坐标系必须一致，否则高 DPI 或副屏下截取区域会偏移
            double dpiX = OCRService.CachedDpiX;
            double dpiY = OCRService.CachedDpiY;
            var region = new System.Drawing.Rectangle(
                (int)(win.SelectedRegion.X * dpiX), (int)(win.SelectedRegion.Y * dpiY),
                (int)(win.SelectedRegion.Width * dpiX), (int)(win.SelectedRegion.Height * dpiY));
            using var bmp = Autogui.CaptureScreenRegion(region);
            ApplyCapture(bmp);
        }

        [RelayCommand]
        private void CaptureWindow()
        {
            var win = new WindowSelectWindowTitle();
            if (win.ShowDialog() != true || win.SelectedHandle == IntPtr.Zero) return;

            // 将目标窗口前置后截取其区域
            NativeMethods.SetForegroundWindow(win.SelectedHandle);
            Thread.Sleep(300);
            if (!NativeMethods.GetWindowRect(win.SelectedHandle, out var rect)) return;

            // GetWindowRect 返回的已经是屏幕物理像素（与 CaptureScreenRegion 同一坐标系，含副屏负坐标），
            // 不能再除以 DPI 缩放，否则窗口截取区域会缩小偏移
            var region = new System.Drawing.Rectangle(
                rect.Left, rect.Top,
                rect.Right - rect.Left, rect.Bottom - rect.Top);
            using var bmp = Autogui.CaptureScreenRegion(region);
            ApplyCapture(bmp);
        }

        /// <summary>
        /// 截图落地为新原图：保存到工作目录、重置裁剪/点击点状态并打开图片编辑窗口
        /// </summary>
        private void ApplyCapture(System.Drawing.Bitmap bmp)
        {
            if (bmp == null) return;
            try
            {
                string workDir = mainVM.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir))
                {
                    workDir = Path.Combine(Path.GetTempPath(), "AutoShaoLu", "images");
                    mainVM.StepImageWorkDir = workDir;
                }
                string imagesDir = Path.Combine(workDir, "images");
                Directory.CreateDirectory(imagesDir);

                string fullPath = Path.Combine(imagesDir, $"{Uid}_src.png");
                bmp.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

                // 重置裁剪与点击点状态，截图作为新原图
                CroppedImageName = null;
                _croppedImg = null;
                CroppedRect = new Rect();
                ClickThumbs = [];
                ImagePath = fullPath;
                IsError = false;
                OnPropertyChanged(nameof(ImgSrc));

                // 截图后直接打开图片编辑窗口进行裁剪
                EditImage();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Capture failed");
            }
        }

        #endregion

        private ImageSource LoadImage(params string[] paths)
        {
            foreach (var path in paths)
            {
                try
                {
                    if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    {
                        // 用 StreamSource 流式加载：避免 UriSource 按 URI 缓存导致同路径文件更新后仍返回旧图，且不锁文件
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read))
                        {
                            bitmap.BeginInit();
                            bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                            bitmap.StreamSource = fs;
                            bitmap.EndInit();
                        }
                        bitmap.Freeze(); // 冻结以提高性能并允许跨线程访问
                        IsError = false;
                        return bitmap;
                    }
                }
                catch (Exception ex)
                {
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("Loading_img_Warning", "Error loading image");
                    _logger.Error(ex, ErrorMessage);
                }
            }

            return null;
        }

        #region 文件操作

        private bool _lastCroppedImageSaved = true;

        /// <summary>
        /// 最近一次裁剪图落盘是否成功。
        /// 保存窗口在触发保存后据此判断是否提示失败（失败时不再弹“已保存”）。
        /// </summary>
        [JsonIgnore]
        public bool LastCroppedImageSaved => _lastCroppedImageSaved;

        /// <summary>
        /// 将裁剪后的图片保存到工作目录 images/{Uid}.png
        /// </summary>
        /// <returns>是否成功落盘；失败时写日志并返回 false（不抛异常）</returns>
        public bool SaveCroppedImageToDisk(ImageSource imageSource)
        {
            if (imageSource == null)
            {
                _lastCroppedImageSaved = false;
                return false;
            }

            try
            {
                string workDir = mainVM.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir))
                {
                    // 若尚未设置工作目录，使用临时目录
                    workDir = Path.Combine(Path.GetTempPath(), "AutoShaoLu", "images");
                    mainVM.StepImageWorkDir = workDir;
                }

                string imagesDir = Path.Combine(workDir, "images");
                if (!Directory.Exists(imagesDir))
                    Directory.CreateDirectory(imagesDir);

                string extension = ".png";
                if (!string.IsNullOrEmpty(ImagePath))
                {
                    var ext = Path.GetExtension(ImagePath)?.ToLower();
                    if (!string.IsNullOrEmpty(ext)) extension = ext;
                }

                string fileName = $"{Uid}{extension}";
                string fullPath = Path.Combine(imagesDir, fileName);

                if (imageSource is BitmapSource bitmapSource)
                {
                    var encoder = GetEncoderByExtension(extension);
                    if (encoder != null)
                    {
                        using (var stream = new FileStream(fullPath, FileMode.Create))
                        {
                            encoder.Frames.Add(BitmapFrame.Create(bitmapSource));
                            encoder.Save(stream);
                        }
                        CroppedImageName = $"images/{fileName}";
                        _lastCroppedImageSaved = true;
                        _logger.Info("Cropped image saved to: {0}", fullPath);
                        return true;
                    }
                }

                // 不是可编码的位图，视为保存失败（避免只改了名字却没有真实文件）
                _lastCroppedImageSaved = false;
                _logger.Warn("Cropped image skipped: unsupported image source type {0}", imageSource.GetType().Name);
                return false;
            }
            catch (Exception ex)
            {
                _lastCroppedImageSaved = false;
                _logger.Error(ex, "Failed to save cropped image");
                return false;
            }
        }

        private BitmapEncoder GetEncoderByExtension(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => new JpegBitmapEncoder(),
                ".bmp" => new BmpBitmapEncoder(),
                ".gif" => new GifBitmapEncoder(),
                _ => new PngBitmapEncoder(),
            };
        }


        #region IDisposable Implementation

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_isDisposed)
            {
                if (disposing)
                {
                    _croppedImg = null;
                }

                _isDisposed = true;
            }
        }

        ~ImageRecognitionBase()
        {
            Dispose(false);
        }

        #endregion

        #endregion
    }


    // 识图步骤
    public class ClickImageStep : ImageRecognitionBase
    {
        private int _clicks = 1;
        private double _clickGap = 0.1;
        private double _nextClickTime = 0.2;
        private double _timeout = 3;


        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }
        public double NextClickTime { get => _nextClickTime; set => SetProperty(ref _nextClickTime, value); }

        private System.Collections.ObjectModel.ObservableCollection<Models.MouseActionItem> _mouseActions = new() { new Models.MouseActionItem() };
        /// <summary>鼠标动作序列（顺序执行）</summary>
        public System.Collections.ObjectModel.ObservableCollection<Models.MouseActionItem> MouseActions { get => _mouseActions; set => SetProperty(ref _mouseActions, value); }

        [System.Text.Json.Serialization.JsonIgnore]
        private RelayCommand addMouseActionCommand;
        [System.Text.Json.Serialization.JsonIgnore]
        public RelayCommand AddMouseActionCommand => addMouseActionCommand ??= new RelayCommand(() => MouseActions.Add(new Models.MouseActionItem()));

        [System.Text.Json.Serialization.JsonIgnore]
        private RelayCommand removeMouseActionCommand;
        [System.Text.Json.Serialization.JsonIgnore]
        public RelayCommand RemoveMouseActionCommand => removeMouseActionCommand ??= new RelayCommand(() => { if (MouseActions.Count > 1) MouseActions.RemoveAt(MouseActions.Count - 1); });

        [System.Text.Json.Serialization.JsonIgnore]
        private RelayCommand<object> editDragPathCommand;
        /// <summary>编辑指定动作的拖拽路径（参数：MouseActionItem）</summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public RelayCommand<object> EditDragPathCommand => editDragPathCommand ??= new RelayCommand<object>(EditDragPath, null);

        private void EditDragPath(object parameter)
        {
            // 图像步骤的拖拽：预览原图与点击点，按住左键在相邻点击点间绘制移动轨迹
            if (parameter is Models.MouseActionItem item)
            {
                var window = new Views.WindowDragPathClick(ImgSrc, ClickThumbs, CroppedRect, item);
                window.Show();
                window.Activate();
            }
        }

        public ClickImageStep() : base()
        {
            Type = StepType.ClickImage;
        }
        public ClickImageStep(string name) : base()
        {
            Type = StepType.ClickImage;
            Name = name;
        }
        public ClickImageStep(string name, string description) : base()
        {
            Type = StepType.ClickImage;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            var clonedImg = (CroppedImg as BitmapSource)?.Clone();
            var res = new ClickImageStep(Name, Description)
            {
                Type = Type,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ImagePath = ImagePath,
                // 复用原步骤在包内的裁剪图，保证克隆体保存后仍能解析到同一张图
                CroppedImageName = CroppedImageName,
                CroppedRect = new(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickThumbs = ClickThumbs?.Select(t => t.Clone()).ToList(),
                SimilarityThreshold = SimilarityThreshold,
                HighPrecision = HighPrecision,
                ColorThreshold = ColorThreshold,
                Clicks = Clicks,
                ClickGap = ClickGap,
                WaitTime = WaitTime,
                Timeout = Timeout,
                MouseActions = new(MouseActions?.Select(a => a.Clone()).ToList()),
            };
            // 裁剪图只做内存克隆：克隆/快照是高频路径，不再每次都写 PNG（落盘由保存动作负责）
            if (clonedImg != null)
                res.SetCroppedImageInMemory(clonedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var cropped = CroppedImg;
            var sourceImage = (cropped ?? ImgSrc) ?? throw new Exception("No image available for clicking.");

            // 优先按磁盘文件取图，子线程不再依赖 WPF ImageSource 的线程归属
            var img = CreateRuntimeBitmap(sourceImage, cropped != null);

            try
            {
                // 如果没有设置点击点，使用图像中心点作为默认点击位置
                var clickPoints = ClickPoints;
                if (clickPoints == null || clickPoints.Count == 0)
                {
                    var center = new Point(
                        (int)(sourceImage.Width / 2),
                        (int)(sourceImage.Height / 2));
                    clickPoints = new List<Point> { center };
                }

                // 等待时间按秒换算毫秒：必须 (int)(WaitTime * 1000)，否则小数秒会被截断成 0
                var rect = await Task.Run(async () =>
                {
                    await Task.Delay((int)(WaitTime * 1000), cancellationToken);
                    if (MouseActions != null && MouseActions.Count > 0)
                        return Autogui.ClickImageOnScreenEx(img, Autogui.Position.LeftTop, clickPoints, SimilarityThreshold, MouseActions.ToList(), NextClickTime, 0, Timeout, HighPrecision, ColorThreshold);
                    return Autogui.ClickImageOnScreenEx(img, Autogui.Position.LeftTop, clickPoints, SimilarityThreshold, Clicks, ClickGap, NextClickTime, 0, Timeout, HighPrecision, ColorThreshold);
                });
                IsTrue = !rect.IsEmpty;
                IsError = false;
                ErrorType = StepErrorType.None;

                // 填充执行结果
                LastResult = new StepExecutionResult
                {
                    IsTrue = IsTrue,
                    Similarity = rect.Similarity,
                    ClickPosition = rect.IsEmpty ? null : rect.Center,
                };

                return IsTrue;
            }
            finally
            {
                // 无论成功、失败还是取消都释放 GDI+ 位图，避免长时间运行泄漏
                img.Dispose();
            }
        }
    }

    // 条件步骤
    public class FindImageStep : ImageRecognitionBase
    {

        private double _gaptime = 0.1;
        public double GapTime { get => _gaptime; set => SetProperty(ref _gaptime, value); }


        private double _timeout = 3;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

        #region OCR 功能

        private bool _enableOCR = false;
        /// <summary>
        /// 是否启用 OCR 识别（在找到图片后对指定区域进行文字识别）
        /// </summary>
        public bool EnableOCR { get => _enableOCR; set => SetProperty(ref _enableOCR, value); }

        private Rect _ocrRect = new();
        /// <summary>
        /// OCR 识别区域（相对于原图像素坐标，在编辑窗口中设置）。
        /// “未设置”统一用 default(Rect)（X/Y/Width/Height 均为 0）表示：
        /// Rect.Empty 的 X/Y 为 +∞、Width/Height 为 -∞，用 new Rect(...) 重建会抛 ArgumentException，
        /// JSON 序列化 ±∞ 也会失败。写入时统一归一化，读取时由 SafeRectJsonConverter 兜底，保证克隆与旧文件反序列化正常。
        /// </summary>
        [JsonConverter(typeof(SafeRectJsonConverter))]
        public Rect OCRRect
        {
            get => _ocrRect;
            set
            {
                // Rect.Empty（含 ±∞ 的哨兵值）一律归一化为 default(Rect)，其余数值保持原样
                var normalized = value.IsEmpty ? default : value;
                if (SetProperty(ref _ocrRect, normalized))
                    OnPropertyChanged(nameof(OCRRegionText));
            }
        }

        /// <summary>
        /// 是否已设置有效的 OCR 区域（宽高都大于 0 才算；未设置时为 default(Rect)）
        /// </summary>
        [JsonIgnore]
        public bool HasOCRRegion => !OCRRect.IsEmpty && OCRRect.Width > 0 && OCRRect.Height > 0;

        private string _ocrResultPreview;
        /// <summary>
        /// OCR 识别结果预览
        /// </summary>
        [JsonIgnore]
        public string OCRResultPreview
        {
            get => _ocrResultPreview;
            set => SetProperty(ref _ocrResultPreview, value);
        }

        /// <summary>
        /// OCR 区域描述文本（用于 UI 显示）
        /// </summary>
        [JsonIgnore]
        public string OCRRegionText
        {
            get
            {
                if (!HasOCRRegion)
                    return LanguageService.GetLocalizedString("OCR_NoRegion", "未选择区域");
                return $"X:{OCRRect.X:F0}, Y:{OCRRect.Y:F0}, W:{OCRRect.Width:F0}, H:{OCRRect.Height:F0}";
            }
        }

        #endregion

        public FindImageStep() : base()
        {
            Type = StepType.FindImage;
        }
        public FindImageStep(string name) : base()
        {
            Type = StepType.FindImage;
            Name = name;
        }
        public FindImageStep(string name, string description) : base()
        {
            Type = StepType.FindImage;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            var clonedImg = (CroppedImg as BitmapSource)?.Clone();
            var res = new FindImageStep(Name, Description)
            {
                Type = Type,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                ImagePath = ImagePath,
                // 复用原步骤在包内的裁剪图，保证克隆体保存后仍能解析到同一张图
                CroppedImageName = CroppedImageName,
                CroppedRect = new(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickThumbs = ClickThumbs?.Select(t => t.Clone()).ToList(),
                SimilarityThreshold = SimilarityThreshold,
                HighPrecision = HighPrecision,
                ColorThreshold = ColorThreshold,
                WaitTime = WaitTime,
                GapTime = GapTime,
                Timeout = Timeout,
                EnableOCR = EnableOCR,
                // OCRRect 用 IsEmpty 判断后再重建：Rect.Empty 的 ±∞ 直接 new Rect(...) 会抛 ArgumentException
                OCRRect = OCRRect.IsEmpty ? default : new(OCRRect.X, OCRRect.Y, OCRRect.Width, OCRRect.Height),
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
            // 裁剪图只做内存克隆：克隆/快照是高频路径，不再每次都写 PNG（落盘由保存动作负责）
            if (clonedImg != null)
                res.SetCroppedImageInMemory(clonedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var cropped = CroppedImg;
            var sourceImage = (cropped ?? ImgSrc) ?? throw new Exception("No image available for finding.");
            // 优先按磁盘文件取图，子线程不再依赖 WPF ImageSource 的线程归属
            var img = CreateRuntimeBitmap(sourceImage, cropped != null);
            AutoRect res;
            try
            {
                res = await Task.Run(async () =>
                {
                    // 等待时间按秒换算毫秒：必须 (int)(WaitTime * 1000)，否则小数秒会被截断成 0
                    await Task.Delay((int)(WaitTime * 1000), cancellationToken);
                    return Autogui.FindImageOnScreen(img, SimilarityThreshold, GapTime, Timeout, HighPrecision, ColorThreshold);
                });
            }
            finally
            {
                // 无论成功、失败还是取消都释放 GDI+ 位图
                img.Dispose();
            }
            IsTrue = !res.IsEmpty;
            IsError = false;
            ErrorType = StepErrorType.None;

            // 调试：标示找到图像的区域
            if (IsTrue && Utils.SingletonLocator.Settings.Step.ShowFoundImageRegionOnRun)
            {
                double dpiX = Services.OCRService.CachedDpiX;
                double dpiY = Services.OCRService.CachedDpiY;
                var srcImg = (CroppedImg ?? ImgSrc) as System.Windows.Media.Imaging.BitmapSource;
                double imgW = srcImg?.PixelWidth ?? 0;
                double imgH = srcImg?.PixelHeight ?? 0;
                var foundRegion = new Rect(
                    res.LeftTop.X / dpiX, res.LeftTop.Y / dpiY,
                    imgW / dpiX, imgH / dpiY);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    Views.WindowRegionOverlay.ShowRegion(foundRegion, Utils.SingletonLocator.Settings.Step.FoundImageOverlay.Color, Utils.SingletonLocator.Settings.Step.FoundImageOverlay.Duration));
            }

            // OCR 识别（如果启用且找到图片）
            string ocrText = null;
            if (EnableOCR && IsTrue && HasOCRRegion)
            {
                // res.LeftTop 是物理像素坐标（来自 OpenCV 模板匹配），需除以 DPI 缩放转为逻辑像素
                // OCRRect 是相对于原图的坐标，需减去 CroppedRect 偏移得到相对于裁剪图的坐标
                double dpiX = Services.OCRService.CachedDpiX;
                double dpiY = Services.OCRService.CachedDpiY;
                double ocrScreenX = (res.LeftTop.X + OCRRect.X - CroppedRect.X) / dpiX;
                double ocrScreenY = (res.LeftTop.Y + OCRRect.Y - CroppedRect.Y) / dpiY;
                var screenRegion = new Rect(ocrScreenX, ocrScreenY, OCRRect.Width / dpiX, OCRRect.Height / dpiY);

                // 如果设置开启，在屏幕上标示 OCR 区域
                if (Utils.SingletonLocator.Settings.Step.ShowOCRRegionOnRun)
                {
                    await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                        Views.WindowRegionOverlay.ShowRegion(screenRegion, Utils.SingletonLocator.Settings.Step.OCRRegionOverlay.Color, Utils.SingletonLocator.Settings.Step.OCRRegionOverlay.Duration));
                }

                ocrText = await Task.Run(() => Services.OCRService.RecognizeRegion(screenRegion), cancellationToken);
                OCRResultPreview = string.IsNullOrWhiteSpace(ocrText)
                    ? LanguageService.GetLocalizedString("OCR_NoResult", "未识别到文本")
                    : ocrText;
            }

            // 填充执行结果
            LastResult = new StepExecutionResult
            {
                IsTrue = IsTrue,
                Similarity = res.Similarity,
                ClickPosition = res.IsEmpty ? null : res.Center,
                OCRText = ocrText,
                TextSource = "OCR",
            };

            return IsTrue;
        }
    }

    /// <summary>
    /// 矩形 JSON 读写转换器（仅用于 OCR 区域这类“可能未设置”的矩形）。
    /// 目的：永远不写出 ±∞/NaN（Rect.Empty 的哨兵会导致 Utf8JsonWriter 抛异常），
    /// 并能兼容历史文件里已经写坏的 ±∞/负宽高数据（读取时归一化为 default(Rect)，不抛异常、不丢整个步骤文件）。
    /// 字段名保持 X/Y/Width/Height，与 System.Text.Json 默认格式一致，旧版本读取新文件不受影响。
    /// </summary>
    internal sealed class SafeRectJsonConverter : JsonConverter<Rect>
    {
        public override Rect Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject)
            {
                // 兼容历史上可能出现的 null / 数字等异常写法：直接跳过并视为未设置
                reader.Skip();
                return default;
            }

            double x = 0, y = 0, width = 0, height = 0;
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    reader.Skip();
                    continue;
                }

                string name = reader.GetString();
                reader.Read();
                double value = 0;
                if (reader.TokenType == JsonTokenType.Number)
                {
                    value = reader.GetDouble();
                }
                else
                {
                    // 值不是数字（null/嵌套对象等异常写法）：跳过，按未设置处理
                    reader.Skip();
                }
                switch (name)
                {
                    case nameof(Rect.X): x = value; break;
                    case nameof(Rect.Y): y = value; break;
                    case nameof(Rect.Width): width = value; break;
                    case nameof(Rect.Height): height = value; break;
                }
            }

            // ±∞ / NaN / 负宽高（Rect.Empty 或历史坏数据）统一视为“未设置区域”
            if (!IsFinite(x) || !IsFinite(y) || !IsFinite(width) || !IsFinite(height)
                || width <= 0 || height <= 0)
            {
                return default;
            }
            return new Rect(x, y, width, height);
        }

        public override void Write(Utf8JsonWriter writer, Rect value, JsonSerializerOptions options)
        {
            // 未设置时写全 0，且对任何非有限值/非正宽高做兜底，绝不把 ±∞ 写进文件
            Rect rect = value.IsEmpty ? default : value;
            double width = IsFinite(rect.Width) && rect.Width > 0 ? rect.Width : 0;
            double height = IsFinite(rect.Height) && rect.Height > 0 ? rect.Height : 0;
            double x = width > 0 && height > 0 && IsFinite(rect.X) ? rect.X : 0;
            double y = width > 0 && height > 0 && IsFinite(rect.Y) ? rect.Y : 0;

            writer.WriteStartObject();
            writer.WriteNumber(nameof(Rect.X), x);
            writer.WriteNumber(nameof(Rect.Y), y);
            writer.WriteNumber(nameof(Rect.Width), width);
            writer.WriteNumber(nameof(Rect.Height), height);
            writer.WriteEndObject();
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
