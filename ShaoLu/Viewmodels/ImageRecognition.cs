using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 图像基类
    public abstract partial class ImageRecognitionBase : AutomationStepBase, IDisposable
    {
        readonly MainViewModel mainVM = SingletonLocator.Main;
        readonly FileServices fileServer = SingletonLocator.FileServices;
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
        private Rect _croppedRect = Rect.Empty;
        private double _similarityThreshold = 0.85;
        private List<ClickThumb> _clickThumbs = [];


        [JsonIgnore]
        public ImageSource ImgSrc
        {
            get
            {
                var img = LoadImage(ImagePath);
                if (img == null)
                {
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("No_img_Warning", "No picture selected");
                }
                return img;
            }
        }

        [JsonIgnore]
        public ImageSource CroppedImg
        {
            get
            {
                _croppedImg ??= LoadImage(CroppedImageFullPath);
                if (_croppedImg == null)
                {
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("No_Cropimage_Warning", "No Croped picture");
                }
                return _croppedImg;
            }
            set
            {
                if (SetProperty(ref _croppedImg, value))
                {
                    if (value != null)
                    {
                        // 当裁剪图更新时，自动保存到磁盘
                        SaveCroppedImageToDisk(value);
                    }
                }
            }
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
                    if (CroppedRect != null && !CroppedRect.IsEmpty)
                        windowEditImage.editImageViewModel.SetCropRect(CroppedRect);
                    if (ClickThumbs != null && ClickThumbs.Count > 0)
                        windowEditImage.editImageViewModel.SetThumbs(ClickThumbs);
                    // 传递 OCR 矩形
                    if (this is FindImageStep findStep && !findStep.OCRRect.IsEmpty)
                        windowEditImage.editImageViewModel.OCRRect = findStep.OCRRect;
                }), System.Windows.Threading.DispatcherPriority.Background);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect, clickthumbs, ocrRect) =>
                {
                    CroppedImg = img;
                    CroppedRect = rect;
                    ClickThumbs = clickthumbs;
                    IsError = false;
                    // 保存 OCR 矩形
                    if (this is FindImageStep findStep)
                    {
                        findStep.OCRRect = ocrRect;
                        findStep.EnableOCR = !ocrRect.IsEmpty && ocrRect.Width > 0 && ocrRect.Height > 0;
                        findStep.OnPropertyChanged(nameof(findStep.OCRRegionText));
                    }
                };
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
                        // 添加 CacheOption.OnLoad 以允许文件在加载后被删除或移动（如果需要）
                        var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                        bitmap.BeginInit();
                        bitmap.UriSource = new Uri(path);
                        bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bitmap.EndInit();
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

        /// <summary>
        /// 将裁剪后的图片保存到工作目录 images/{Uid}.png
        /// </summary>
        public void SaveCroppedImageToDisk(ImageSource imageSource)
        {
            if (imageSource == null)
                return;

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
                        _logger.Info("Cropped image saved to: {0}", fullPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save cropped image");
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
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                IsNeed = IsNeed,
                ImagePath = ImagePath,
                CroppedImg = clonedImg,
                CroppedRect = new(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickThumbs = ClickThumbs?.Select(t => t.Clone()).ToList(),
                SimilarityThreshold = SimilarityThreshold,
                Clicks = Clicks,
                ClickGap = ClickGap,
                WaitTime = WaitTime,
                Timeout = Timeout,
            };
            if (clonedImg != null)
                res.SaveCroppedImageToDisk(clonedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for clicking.");

            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");

            // 如果没有设置点击点，使用图像中心点作为默认点击位置
            var clickPoints = ClickPoints;
            if (clickPoints == null || clickPoints.Count == 0)
            {
                var center = new Point(
                    (int)(sourceImage.Width / 2),
                    (int)(sourceImage.Height / 2));
                clickPoints = new List<Point> { center };
            }

            var rect = await Task.Run(async () =>
            {
                await Task.Delay((int)WaitTime * 1000, cancellationToken);
                return Autogui.ClickImageOnScreenEx(img, Autogui.Position.LeftTop, clickPoints, SimilarityThreshold, Clicks, ClickGap, NextClickTime, 0, Timeout);
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

        private Rect _ocrRect = Rect.Empty;
        /// <summary>
        /// OCR 识别区域（相对于原图像素坐标，在编辑窗口中设置）
        /// </summary>
        public Rect OCRRect { get => _ocrRect; set => SetProperty(ref _ocrRect, value); }

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
                if (OCRRect.IsEmpty)
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
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                IsNeed = IsNeed,
                ImagePath = ImagePath,
                CroppedImg = clonedImg,
                CroppedRect = new(CroppedRect.X, CroppedRect.Y, CroppedRect.Width, CroppedRect.Height),
                ClickThumbs = ClickThumbs?.Select(t => t.Clone()).ToList(),
                SimilarityThreshold = SimilarityThreshold,
                WaitTime = WaitTime,
                GapTime = GapTime,
                Timeout = Timeout,
                EnableOCR = EnableOCR,
                OCRRect = new(OCRRect.X, OCRRect.Y, OCRRect.Width, OCRRect.Height),
                EnableLog = EnableLog,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
            if (clonedImg != null)
                res.SaveCroppedImageToDisk(clonedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for finding.");
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
            var res = await Task.Run(async () =>
            {
                await Task.Delay((int)WaitTime * 1000, cancellationToken);
                return Autogui.FindImageOnScreen(img, SimilarityThreshold, GapTime, Timeout);
            });
            img?.Dispose();
            IsTrue = !res.IsEmpty;
            IsError = false;
            ErrorType = StepErrorType.None;

            // OCR 识别（如果启用且找到图片）
            string ocrText = null;
            if (EnableOCR && IsTrue && !OCRRect.IsEmpty && OCRRect.Width > 0 && OCRRect.Height > 0)
            {
                // 计算 OCR 区域在屏幕上的绝对坐标
                // res.LeftTop 是找到的图片在屏幕上的左上角坐标
                // OCRRect 是相对于原图的坐标，需减去 CroppedRect 偏移得到相对于裁剪图的坐标
                double ocrScreenX = res.LeftTop.X + (OCRRect.X - CroppedRect.X);
                double ocrScreenY = res.LeftTop.Y + (OCRRect.Y - CroppedRect.Y);
                var screenRegion = new Rect(ocrScreenX, ocrScreenY, OCRRect.Width, OCRRect.Height);

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
            };

            // 日志记录
            if (EnableLog)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(
                    Utils.SingletonLocator.Main.StepFilePath ?? "unsaved");
                string logContent = ocrText ?? (IsTrue ? "Found" : "Not Found");
                Services.ExecutionLogService.Log(Uid, fileName, Name, logContent);
            }

            return IsTrue;
        }
    }
}
