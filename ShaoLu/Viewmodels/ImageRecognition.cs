using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 图像基类
    public abstract partial class ImageRecognitionBase : AutomationStepBase, IDisposable
    {
        readonly PathServices pathServices = new();
        readonly FileServices fileServer = SingletonLocator.FileServices;
        private bool _isDisposed = false;

        #region 属性

        private string _imagePath;
        private string _croppedImagePath;
        private ImageSource _croppedImg;

        public string ImagePath
        {
            get => _imagePath;
            set => SetProperty(ref _imagePath, value);
        }

        [JsonIgnore]
        public ImageSource ImgSrc => LoadImage(ImagePath);

        public double ImgSrcWidth => (ImgSrc?.Width ?? 0);

        public string CroppedImagePath
        {
            get => _croppedImagePath; set => SetProperty(ref _croppedImagePath, value);
        }

        [JsonIgnore]
        public ImageSource CroppedImg
        {
            get
            {
                if (System.IO.File.Exists(CroppedImagePath))
                    _croppedImg ??= LoadImage(CroppedImagePath);
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

        #region 图像属性

        public Rect _croppedRect;
        private int _offsetX = 0;
        private int _offsetY = 0;
        private bool _hasOffset = false;
        private double _similarityThreshold = 0.85;
        private List<Point> _clickOffest = [];

        public Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }


        [JsonIgnore]
        public Point Offset => new(OffsetX, OffsetY);

        public List<Point> ClickOffest { get => _clickOffest; set => _clickOffest = value; }

        public int OffsetX { get => _offsetX; set => SetProperty(ref _offsetX, value); }

        public int OffsetY { get => _offsetY; set => SetProperty(ref _offsetY, value); }

        public bool HasOffset { get => _hasOffset; set => SetProperty(ref _hasOffset, value); }

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

        #endregion

        #region 命令

        [RelayCommand]
        private void SelectImage()
        {
            var title = LanguageService.GetLocalizedString("Select_target_pic", "Open Image File");
            var filter = LanguageService.GetLocalizedString("Image_File", "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            ImagePath = pathServices.OpenPathDialog(title, filter);
        }

        [RelayCommand]
        private void EditImage()
        {
            if (ImgSrc != null)
            {
                WindowEditImage windowEditImage = new();
                windowEditImage.Show();
                // 延迟赋值，等待 UI 线程完成当前布局和渲染
                windowEditImage.Dispatcher.BeginInvoke(new Action(() =>
                {
                    windowEditImage.editImageViewModel.ImgSrc = ImgSrc;
                    windowEditImage.editImageViewModel.ImgDst = CroppedImg;
                    windowEditImage.editImageViewModel.CropRect = CroppedRect;
                    if (HasOffset)
                        windowEditImage.editImageViewModel.SetOffset(Offset);
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect, offset) =>
                {
                    CroppedImg = img;
                    CroppedRect = rect;
                    OffsetX = offset.X;
                    OffsetY = offset.Y;
                    HasOffset = !offset.IsEmpty;
                };
            }
        }

        #endregion

        private ImageSource LoadImage(string imagePath)
        {
            ImageSource res;
            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    // 添加 CacheOption.OnLoad 以允许文件在加载后被删除或移动（如果需要）
                    var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(imagePath);
                    bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bitmap.EndInit();
                    bitmap.Freeze(); // 冻结以提高性能并允许跨线程访问
                    IsError = false;
                    return bitmap;
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., invalid image format)
                    IsError = true;
                    ErrorMessage = LanguageService.GetLocalizedString("Loading_img_Warning", "Error loading image");
                    _logger.Error(ex, ErrorMessage);
                }
            }
            else
            {
                IsError = true;
                ErrorMessage = LanguageService.GetLocalizedString("No_img_Warning", "Error loading image");
            }

            res = null;
            //var Warning_Title = LanguageService.GetLocalizedString("Warning", "Warning");
            //WindowAsyncPopup.Show($"{error_msg1}: {error_msg2}", $"{Warning_Title}", PopupButtons.OK, MessageBoxImage.Warning);
            return res;
        }

        #region 文件操作

        /// <summary>
        /// 将裁剪后的图片保存到原图所在目录，文件名添加 Cropped_ 前缀
        /// </summary>
        public void SaveCroppedImageToDisk(ImageSource imageSource)
        {
            if (imageSource == null || string.IsNullOrEmpty(ImagePath))
                return;

            try
            {
                var fullpath = GetCroppedImageSavePath(out string extension);
                // 【关键】如果路径变了，且旧路径存在，标记旧路径为待删除
                if (!string.IsNullOrEmpty(_croppedImagePath) && _croppedImagePath != fullpath)
                {
                    // 只有当这个步骤是“未保存”状态时，我们才清理旧的临时文件
                    // 如果 IsSave 为 true，说明用户已经正式保存过，旧文件可能是有用的历史版本，暂不删除
                    if (!IsSave)
                    {
                        fileServer.MarkForDeletion(_croppedImagePath);
                    }
                }

                // 2. 转换 ImageSource 为 Bitmap 并保存
                if (imageSource is System.Windows.Media.Imaging.BitmapSource bitmapSource)
                {
                    var encoder = GetEncoderByExtension(extension);
                    if (encoder != null)
                    {
                        using (var stream = new System.IO.FileStream(fullpath, System.IO.FileMode.Create))
                        {
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                            encoder.Save(stream);
                            CroppedImagePath = fullpath;
                        }

                        // 可选：更新 ImagePath 指向新保存的裁剪图？
                        // 通常自动化步骤中，ImagePath 指向的是“模板图”，而 CroppedImg 是运行时截图或局部图。
                        // 这里我们只保存文件，不改变 ImagePath 绑定，以免混淆“模板”与“实例”。
                        _logger.Info("Cropped image saved to: {0}", fullpath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save cropped image: ");
            }
        }

        private string GetCroppedImageSavePath(out string extension)
        {
            extension = null;
            string fullPath;
            // 1. 确定保存路径
            if (string.IsNullOrEmpty(ImagePath)) return null;
            string directory = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(ImagePath), "CropedImage");
            if (!System.IO.Directory.Exists(directory))
            {
                System.IO.Directory.CreateDirectory(directory);
            }
            string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(ImagePath);
            extension = System.IO.Path.GetExtension(ImagePath);

            // 保持与原图相同的格式，或者统一转为 PNG 以保证质量
            if (string.IsNullOrEmpty(extension)) extension = ".png";

            string newFileName = $"Cropped_{fileNameWithoutExt}_{Uid}{extension}";

            // 确保路径合法，防止路径遍历攻击（虽然 ImagePath 通常来自 OpenFileDialog，但仍需防御）
            fullPath = System.IO.Path.Combine(directory, newFileName);
            return fullPath;
        }

        private System.Windows.Media.Imaging.BitmapEncoder GetEncoderByExtension(string extension)
        {
            return extension.ToLower() switch
            {
                ".jpg" or ".jpeg" => new System.Windows.Media.Imaging.JpegBitmapEncoder(),
                ".bmp" => new System.Windows.Media.Imaging.BmpBitmapEncoder(),
                ".gif" => new System.Windows.Media.Imaging.GifBitmapEncoder(),
                _ => new System.Windows.Media.Imaging.PngBitmapEncoder(),// PNG 无损，推荐
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
                    // 释放托管资源
                    // 当步骤被移除时，立即标记其裁剪图为待删除
                    if (!IsSave)
                        fileServer.MarkForDeletion(CroppedImagePath);

                    // 如果有其他需要释放的资源（如 Bitmap 对象），在这里释放
                    _croppedImg = null;
                }

                _isDisposed = true;
            }
        }

        // 保留析构函数作为安全网，但主要逻辑已移至 Dispose
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
        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }


        private double _clickGap = 0.1;
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }


        private double _timeout = 3;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

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
            var res = new ClickImageStep(Name, Description)
            {
                Type = Type,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                ImagePath = ImagePath,
                CroppedImg = CroppedImg,
                CroppedRect = CroppedRect,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                SimilarityThreshold = SimilarityThreshold,
                Clicks = Clicks,
                ClickGap = ClickGap,
                WaitTime = WaitTime,
                Timeout = Timeout,
            };
            res.SaveCroppedImageToDisk(res.CroppedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for clicking.");

            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
            var res = await Task.Run(() =>
            {
                Thread.Sleep((int)WaitTime * 1000);
                return Autogui.ClickImageOnScreen(img, Autogui.Position.LeftTop, Offset, SimilarityThreshold, Clicks, ClickGap, 0, Timeout);
            });
            IsTrue = res;
            IsError = false;

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
            var res = new FindImageStep(Name, Description)
            {
                Type = Type,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                ImagePath = ImagePath,
                CroppedImg = CroppedImg,
                CroppedRect = CroppedRect,
                SimilarityThreshold = SimilarityThreshold,
                WaitTime = WaitTime,
                GapTime = GapTime,
                Timeout = Timeout
            };
            res.SaveCroppedImageToDisk(res.CroppedImg);
            return res;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for finding.");
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
            var res = await Task.Run(() =>
            {
                Thread.Sleep((int)WaitTime * 1000);
                return Autogui.FindImageOnScreen(img, SimilarityThreshold, GapTime, Timeout);
            });
            img?.Dispose();
            IsTrue = !res.IsEmpty;
            IsError = false;
            return IsTrue;
        }
    }
}
