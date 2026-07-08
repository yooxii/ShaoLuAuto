using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using WPFLocalizeExtension.Engine;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 基类
    public abstract class AutomationStepBase : ObservableObject
    {
        private int _lineNo;

        /// <summary>
        /// 步骤行号。
        /// 注意：此值应由包含该步骤的集合（如 ObservableCollection）在增删改时统一维护，
        /// 或者在 UI 绑定时通过 Index 计算。此处保留 SetProperty 以支持手动刷新。
        /// </summary>
        public int LineNo
        {
            get => _lineNo;
            set => SetProperty(ref _lineNo, value);
        }

        private string _name;
        /// <summary>
        /// 步骤名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                // 简单的防御性编程，防止 Null 导致绑定崩溃
                if (value == null) throw new ArgumentNullException(nameof(Name));
                SetProperty(ref _name, value);
            }
        }

        private string _description;
        /// <summary>
        /// 步骤描述
        /// </summary>
        public string Description
        {
            get => _description;
            set => SetProperty(ref _description, value);
        }

        private StepType _type;
        /// <summary>
        /// 步骤类型
        /// </summary>
        public StepType Type
        {
            get => _type;
            set => SetProperty(ref _type, value);
        }

        private bool _isTrue;
        public bool IsTrue { get => _isTrue; set => SetProperty(ref _isTrue, value); }

        private int _trueGoto;
        /// <summary>
        /// 如果真,去执行某行
        /// </summary>
        public int TrueGoto { get => _trueGoto; set => SetProperty(ref _trueGoto, value); }

        private int _falseGoto;
        public int FalseGoto { get => _falseGoto; set => SetProperty(ref _falseGoto, value); }


        /// <summary>
        /// 构造函数
        /// 确保创建时即分配唯一 ID 和默认值
        /// </summary>
        protected AutomationStepBase()
        {
            this.LineNo = 0;
            this.Name = string.Empty;
            this.Description = string.Empty;
        }

        /// <summary>
        /// 带参构造函数（方便测试和初始化）
        /// </summary>
        protected AutomationStepBase(string name, StepType type) : this()
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type;
        }

        public abstract Task<bool> RunAsync(CancellationToken cancellationToken = default);
        public bool Run()
        {
            return RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        // 其他公共属性...
    }

    // 图像基类
    public abstract class ImageRecognitionBase : AutomationStepBase
    {
        readonly Services.FileServices fileServices = new();

        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetProperty(ref _imagePath, value))
                {
                    // 当原图路径改变时，如果已有裁剪图，可能需要重新考虑是否失效，这里暂不处理
                    ImgSrc = null; // 重置缓存
                }
            }
        }

        [JsonIgnore]
        private ImageSource _imgSrc;
        [JsonIgnore]
        public ImageSource ImgSrc
        {
            get
            {
                _imgSrc ??= LoadImage(ImagePath);
                return _imgSrc;
            }
            set => SetProperty(ref _imgSrc, value);
        }

        public double ImgSrcWidth => (ImgSrc?.Width ?? 0);

        [JsonIgnore]
        public ImageSource _croppedImg;
        [JsonIgnore]
        public ImageSource CroppedImg
        {
            get
            {
                GetCroppedImageSavePath(out _, out string fullPath);
                if (System.IO.File.Exists(fullPath))
                    _croppedImg ??= LoadImage(fullPath);
                return _croppedImg;
            }
            set
            {
                if (SetProperty(ref _croppedImg, value))
                {
                    if (value != null)
                        // 当裁剪图更新时，自动保存到磁盘
                        SaveCroppedImageToDisk(value);
                }
            }
        }

        #region 图像属性
        public Rect _croppedRect;
        public Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }


        private OpenCvSharp.Point _offest;
        public OpenCvSharp.Point Offest { get => _offest; set => SetProperty(ref _offest, value); }


        private double _similarityThreshold = 0.85;
        public double SimilarityThreshold
        {
            get => _similarityThreshold;
            set
            {
                if (SetProperty(ref _similarityThreshold, value))
                {
                    _similarityThreshold = _similarityThreshold < 0 ? 0 : _similarityThreshold > 1 ? 1 : _similarityThreshold;
                }
            }
        }
        #endregion

        private RelayCommand selectImageCommand;
        public ICommand SelectImageCommand => selectImageCommand ??= new RelayCommand(SelectImage);

        private void SelectImage()
        {
            var title = LocalizeDictionary.Instance.GetLocalizedObject("Select_target_pic", null, null)?.ToString() ?? "Open Image File";
            var filter = (LocalizeDictionary.Instance.GetLocalizedObject("Image_File", null, null)?.ToString() ?? "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            ImagePath = fileServices.OpenPathDialog(title, filter);
        }

        private RelayCommand eidtImageCommand;
        public ICommand EditImageCommand => eidtImageCommand ??= new RelayCommand(EditImage);

        private ImageSource LoadImage(string imagePath)
        {
            var error_msg2 = "";
            string error_msg1;
            ImageSource res;
            if (!string.IsNullOrEmpty(imagePath) && System.IO.File.Exists(imagePath))
            {
                try
                {
                    res = new System.Windows.Media.Imaging.BitmapImage(new Uri(imagePath));
                    return res;
                }
                catch (Exception ex)
                {
                    // Handle exceptions (e.g., invalid image format)
                    System.Diagnostics.Debug.WriteLine($"Error loading image: {ex.Message}");
                    error_msg1 = (string)(LocalizeDictionary.Instance.GetLocalizedObject("Loading_img_Warning", null, null) ?? "Error loading image");
                    error_msg2 = ex.Message;
                }
            }
            else
            {
                error_msg1 = (string)(LocalizeDictionary.Instance.GetLocalizedObject("No_img_Warning", null, null) ?? "Error loading image");
            }

            res = null;
            var Warning_Title = LocalizeDictionary.Instance.GetLocalizedObject("Warning_Title", null, null) ?? "Warning";
            System.Windows.Forms.MessageBox.Show($"{error_msg1}: {error_msg2}", $"{Warning_Title}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return res;
        }

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
                    windowEditImage.editImageViewModel.ImgSrcWidth = ImgSrcWidth;
                    windowEditImage.editImageViewModel.ImgDst = CroppedImg;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect) => { CroppedImg = img; CroppedRect = rect; };
            }
        }

        /// <summary>
        /// 将裁剪后的图片保存到原图所在目录，文件名添加 Cropped_ 前缀
        /// </summary>
        private void SaveCroppedImageToDisk(ImageSource imageSource)
        {
            if (imageSource == null || string.IsNullOrEmpty(ImagePath))
                return;

            try
            {
                GetCroppedImageSavePath(out string extension, out string fullPath);

                // 2. 转换 ImageSource 为 Bitmap 并保存
                if (imageSource is System.Windows.Media.Imaging.BitmapSource bitmapSource)
                {
                    var encoder = GetEncoderByExtension(extension);
                    if (encoder != null)
                    {
                        using (var stream = new System.IO.FileStream(fullPath, System.IO.FileMode.Create))
                        {
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                            encoder.Save(stream);
                        }

                        // 可选：更新 ImagePath 指向新保存的裁剪图？
                        // 通常自动化步骤中，ImagePath 指向的是“模板图”，而 CroppedImg 是运行时截图或局部图。
                        // 这里我们只保存文件，不改变 ImagePath 绑定，以免混淆“模板”与“实例”。
                        System.Diagnostics.Debug.WriteLine($"Cropped image saved to: {fullPath}");
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save cropped image: {ex.Message}");
                // 在生产环境中，可能需要通过 UI 线程显示警告
            }
        }

        private void GetCroppedImageSavePath(out string extension, out string fullPath)
        {
            // 1. 确定保存路径
            string directory = System.IO.Path.GetDirectoryName(ImagePath);
            string fileNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(ImagePath);
            extension = System.IO.Path.GetExtension(ImagePath);

            // 保持与原图相同的格式，或者统一转为 PNG 以保证质量
            if (string.IsNullOrEmpty(extension)) extension = ".png";

            string newFileName = $"Cropped_{fileNameWithoutExt}{extension}";

            // 确保路径合法，防止路径遍历攻击（虽然 ImagePath 通常来自 OpenFileDialog，但仍需防御）
            fullPath = System.IO.Path.Combine(directory, newFileName);
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
    }

    // 识图步骤
    public class ClickImageStep : ImageRecognitionBase
    {
        private int _clicks;
        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }


        private double _clickGap;
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }


        private double _waitTime;
        public double WaitTime { get => _waitTime; set => SetProperty(ref _waitTime, value); }


        private double _timeout;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

        public ClickImageStep() : base()
        {
            Type = StepType.ClickImage;
            Clicks = 1;
            ClickGap = 0.1;
            WaitTime = 0.3;
            Timeout = 3;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = CroppedImg ?? ImgSrc;

            if (sourceImage == null)
            {
                System.Diagnostics.Debug.WriteLine("No image available for clicking.");
                return false;
            }
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage);
            if (img == null) return false;
            var res = await Task.Run(() =>
            {
                return Autogui.ClickImageOnScreen(img, Autogui.Position.LeftTop, Offest, Clicks, ClickGap, SimilarityThreshold, WaitTime, Timeout);
            });
            img?.Dispose();

            return res;
        }
    }

    // 输入文字步骤
    public class TypeTextStep : AutomationStepBase
    {

        private string _textToType;
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }


        private int _delayBetweenKeys;
        public int DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }


        public TypeTextStep() : base()
        {
            this.Type = StepType.TypeText;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var res = await Task.Run(() =>
            {
                return Autogui.TypeText(TextToType, DelayBetweenKeys);
            });
            return res;
        }
    }

    // 条件步骤
    public class FindImageStep : ImageRecognitionBase
    {

        private double _gaptime;
        public double GapTime { get => _gaptime; set => SetProperty(ref _gaptime, value); }


        private double _timeout;
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

        public FindImageStep() : base()
        {
            Type = StepType.FindImage;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = CroppedImg ?? ImgSrc;

            if (sourceImage == null)
            {
                IsTrue = false;
                return false;
            }
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage);
            if (img == null)
            {
                IsTrue = false;
                return false;
            }
            var res = await Task.Run(() => { return Autogui.FindImageOnScreen(img, SimilarityThreshold, GapTime, Timeout); });
            img?.Dispose();
            IsTrue = !res.IsEmpty;
            return IsTrue;
        }
    }

    // 弹出框步骤
    public class PopupStep : AutomationStepBase
    {
        private string _title;
        public string Title { get => _title; set => SetProperty(ref _title, value); }


        private string _popupText;
        public string PopupText { get => _popupText; set => SetProperty(ref _popupText, value); }


        private string _popupType;
        public string PopupType { get => _popupType; set => SetProperty(ref _popupType, value); }

        public List<string> PopupTypes { get; set; } = ["Information", "Warning", "Error", "Question"];

        public PopupStep() : base()
        {
            this.Type = StepType.Popup;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var iconType = PopupType switch
            {
                "Information" => MessageBoxImage.Information,
                "Warning" => MessageBoxImage.Warning,
                "Error" => MessageBoxImage.Error,
                "Question" => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            var buttons = MessageBoxButton.OK; // 可以根据需要扩展属性来支持其他按钮类型

            // 1. 启动异步弹窗任务
            var (popupWindow, popupTask) = WindowAsyncPopup.Show(PopupText, Title, buttons, iconType);

            // 2. 等待任务完成或被取消
            try
            {
                // 将 CancellationToken 注册到 Task
                using (cancellationToken.Register(() =>
                {
                    // 当取消发生时，在 UI 线程关闭窗口
                    // 检查窗口是否仍然存在且未关闭
                    if (popupWindow != null && !popupWindow.Dispatcher.HasShutdownStarted)
                    {
                        popupWindow.Dispatcher.InvokeAsync(() =>
                        {
                            if (popupWindow.IsVisible)
                            {
                                popupWindow.Close();
                            }
                        });
                    }
                }))
                {
                    // WaitAsync 是 .NET 6+ 的方法。在 .NET Framework 4.8 中，我们需要手动处理
                    // 这里使用 Task.WhenAny 来模拟

                    var cancelTask = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelTask.TrySetResult(true)))
                    {
                        var completedTask = await Task.WhenAny(popupTask, cancelTask.Task);

                        if (completedTask == cancelTask.Task)
                        {
                            // 用户点击了停止
                            IsTrue = false;
                            return false;
                        }

                        // 用户点击了弹窗按钮
                        var result = await popupTask;
                        IsTrue = (result == MessageBoxResult.OK || result == MessageBoxResult.Yes);
                        return IsTrue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Popup error: {ex.Message}");
                IsTrue = false;
                return false;
            }
        }
    }

    public class EmptyStep : AutomationStepBase
    {
        public EmptyStep() : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
        }
        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            return IsTrue;
        }
    }
}
