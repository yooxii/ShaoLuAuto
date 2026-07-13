using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.IO;
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
        #region 属性
        private readonly Guid _uid = Guid.NewGuid();
        /// <summary>
        /// 步骤的唯一uid
        /// </summary>
        public Guid Uid => _uid;

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

        private bool _isTrue = false;
        public bool IsTrue { get => _isTrue; set => SetProperty(ref _isTrue, value); }

        private int _trueGoto;
        /// <summary>
        /// 如果真,去执行某行
        /// </summary>
        public int TrueGoto { get => _trueGoto; set => SetProperty(ref _trueGoto, value); }

        private int _falseGoto;
        public int FalseGoto { get => _falseGoto; set => SetProperty(ref _falseGoto, value); }

        #endregion

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

        public abstract AutomationStepBase Clone();

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

        private string _croppedImagePath;
        public string CroppedImagePath
        {
            get => _croppedImagePath;
            set
            {
                SetProperty(ref _croppedImagePath, value);
            }
        }

        [JsonIgnore]
        private ImageSource _croppedImg;
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
        public Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }

        [JsonIgnore]
        public OpenCvSharp.Point Offset => new(OffsetX, OffsetY);

        private int _offsetX = 0;
        public int OffsetX
        {
            get => _offsetX;
            set => SetProperty(ref _offsetX, value);
        }

        private int _offsetY = 0;
        public int OffsetY
        {
            get => _offsetY;
            set => SetProperty(ref _offsetY, value);
        }

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
        [JsonIgnore]
        public ICommand SelectImageCommand => selectImageCommand ??= new RelayCommand(SelectImage);

        private void SelectImage()
        {
            var title = LocalizeDictionary.Instance.GetLocalizedObject("Select_target_pic", null, null)?.ToString() ?? "Open Image File";
            var filter = (LocalizeDictionary.Instance.GetLocalizedObject("Image_File", null, null)?.ToString() ?? "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            ImagePath = fileServices.OpenPathDialog(title, filter);
        }

        private RelayCommand eidtImageCommand;
        [JsonIgnore]
        public ICommand EditImageCommand => eidtImageCommand ??= new RelayCommand(EditImage);

        ~ImageRecognitionBase()
        {
            var stepmodel = ViewModelLocator.Steps;
            if (!string.IsNullOrEmpty(CroppedImagePath) && File.Exists(CroppedImagePath))
            {
                stepmodel.ReadyToDeleteFiles.Add(CroppedImagePath);
            }
        }

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
                    windowEditImage.editImageViewModel.CropRect = CroppedRect;
                    windowEditImage.editImageViewModel.SetOffset(new Point(OffsetX, OffsetY));
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect, offset) => { CroppedImg = img; CroppedRect = rect; OffsetX = offset.X; OffsetY = offset.Y; };
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
                GetCroppedImageSavePath(out string extension, out _croppedImagePath);

                // 2. 转换 ImageSource 为 Bitmap 并保存
                if (imageSource is System.Windows.Media.Imaging.BitmapSource bitmapSource)
                {
                    var encoder = GetEncoderByExtension(extension);
                    if (encoder != null)
                    {
                        using (var stream = new System.IO.FileStream(CroppedImagePath, System.IO.FileMode.Create))
                        {
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmapSource));
                            encoder.Save(stream);
                        }

                        // 可选：更新 ImagePath 指向新保存的裁剪图？
                        // 通常自动化步骤中，ImagePath 指向的是“模板图”，而 CroppedImg 是运行时截图或局部图。
                        // 这里我们只保存文件，不改变 ImagePath 绑定，以免混淆“模板”与“实例”。
                        System.Diagnostics.Debug.WriteLine($"Cropped image saved to: {CroppedImagePath}");
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
            extension = null;
            fullPath = null;
            // 1. 确定保存路径
            if (string.IsNullOrEmpty(ImagePath)) return;
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
        private int _clicks = 1;
        public int Clicks { get => _clicks; set => SetProperty(ref _clicks, value); }


        private double _clickGap = 0.1;
        public double ClickGap { get => _clickGap; set => SetProperty(ref _clickGap, value); }


        private double _waitTime = 0;
        public double WaitTime { get => _waitTime; set => SetProperty(ref _waitTime, value); }


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
            return new ClickImageStep(Name, Description)
            {
                ImagePath = ImagePath,
                Clicks = Clicks,
                ClickGap = ClickGap,
                WaitTime = WaitTime,
                Timeout = Timeout,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                SimilarityThreshold = SimilarityThreshold,
                Type = Type,
                IsTrue = IsTrue,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                LineNo = LineNo,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for clicking.");
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
            var res = await Task.Run(() =>
            {
                return Autogui.ClickImageOnScreen(img, Autogui.Position.LeftTop, Offset, Clicks, ClickGap, SimilarityThreshold, WaitTime, Timeout);
            });
            img?.Dispose();
            IsTrue = res;

            return IsTrue;
        }
    }

    // 输入文字步骤
    public class TypeTextStep : AutomationStepBase
    {

        private string _textToType;
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }


        private double _delayBetweenKeys = 0.01;
        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }


        public TypeTextStep() : base()
        {
            Type = StepType.TypeText;
        }
        public TypeTextStep(string name) : base()
        {
            Type = StepType.TypeText;
            Name = name;
        }
        public TypeTextStep(string name, string description) : base()
        {
            Type = StepType.TypeText;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextStep(Name, Description)
            {
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var res = await Task.Run(() =>
            {
                return Autogui.TypeText(TextToType, (int)(DelayBetweenKeys * 1000));
            });
            IsTrue = res;
            return res;
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
            return new FindImageStep(Name, Description)
            {
                ImagePath = ImagePath,
                CroppedImagePath = CroppedImagePath,
                ImgSrc = ImgSrc,
                CroppedImg = CroppedImg,
                CroppedRect = CroppedRect,
                OffsetX = OffsetX,
                OffsetY = OffsetY,
                SimilarityThreshold = SimilarityThreshold
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var sourceImage = (CroppedImg ?? ImgSrc) ?? throw new Exception("No image available for finding.");
            var img = Autogui.ConvertImageSourceToBitmap(sourceImage) ?? throw new Exception("Image Convert Error.");
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

        private FontModel _popupFont = new()
        {
            FontFamily = "Arial",
            FontSize = 14,
            FontWeight = FontWeights.Regular,
            FontStyle = FontStyles.Normal,
            FontColor = 0x000000,
        };
        public FontModel PopupFont { get => _popupFont; set => SetProperty(ref _popupFont, value); }


        private string _popupType = "Information";
        public string PopupType { get => _popupType; set => SetProperty(ref _popupType, value); }


        [JsonIgnore]
        public List<string> PopupTypes { get; set; } = ["Information", "Warning", "Error", "Question"];

        [JsonIgnore]
        private RelayCommand fontSelectCommand;
        [JsonIgnore]
        public RelayCommand FontSelectCommand => fontSelectCommand ??= new RelayCommand(FontSelect);

        [JsonIgnore]
        private RelayCommand colorSelectCommand;
        [JsonIgnore]
        public RelayCommand ColorSelectCommand => colorSelectCommand ??= new RelayCommand(ColorSelect);

        public PopupStep() : base()
        {
            this.Type = StepType.Popup;
        }
        public PopupStep(string name) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Title = name;
        }
        public PopupStep(string name, string description) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new PopupStep(Name, Description)
            {
                LineNo = LineNo,
                IsTrue = IsTrue,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                Title = Title,
                PopupText = PopupText,
                PopupFont = PopupFont,
                PopupType = PopupType
            };
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

            var buttons = MessageBoxButton.YesNo; // 可以根据需要扩展属性来支持其他按钮类型

            // 1. 启动异步弹窗任务
            var (popupWindow, popupTask) = WindowAsyncPopup.Show(PopupText, Title, PopupFont, buttons, iconType);

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
                return IsTrue;
            }
        }

        private void FontSelect()
        {
            PopupFont ??= new();
            var fontDialog = new FontDialog()
            {
                Font = PopupFont?.Font ?? new System.Drawing.Font("Arial", 12)
            };

            if (fontDialog.ShowDialog() == DialogResult.OK)
            {
                // 获取选择的字体信息
                System.Drawing.Font selectedFont = fontDialog.Font;

                // 将 Windows Forms 的字体转换为 WPF 的字体属性
                FontStyle fontStyle = selectedFont.Italic ? FontStyles.Italic : FontStyles.Normal;
                FontWeight fontWeight = selectedFont.Bold ? FontWeights.Bold : FontWeights.Normal;

                // 将选择的字体应用到 WPF 控件上（例如名为 TextBlockSample 的 TextBlock）
                PopupFont.FontFamily = selectedFont.FontFamily.Name;
                PopupFont.FontSize = selectedFont.Size;
                PopupFont.FontStyle = fontStyle;
                PopupFont.FontWeight = fontWeight;

                PopupFont.Style = selectedFont.Style;
                PopupFont.Unit = selectedFont.Unit;
            }
        }

        private void ColorSelect()
        {
            PopupFont ??= new();
            ColorDialog dialog = new()
            {
                Color = System.Drawing.Color.FromArgb(PopupFont.FontColor)
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PopupFont.FontColor = dialog.Color.ToArgb();
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
        public EmptyStep(string name) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
        }
        public EmptyStep(string name, string description) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new EmptyStep(Name, Description);
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            return IsTrue;
        }
    }
}
