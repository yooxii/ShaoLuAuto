using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.Remoting.Messaging;
using System.Text.Json.Serialization;
using System.Threading;
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

        public abstract bool Run();
        // 其他公共属性...
    }

    // 图像基类
    public abstract class ImageRecognitionBase : AutomationStepBase
    {
        readonly Services.FileServices fileServices = new();

        private string _imagePath;
        public string ImagePath { get => _imagePath; set => SetProperty(ref _imagePath, value); }

        [JsonIgnore]
        private ImageSource _imgSrc;
        [JsonIgnore]
        public ImageSource ImgSrc
        {
            get
            {
                if (_imgSrc is null)
                    LoadImage();
                return _imgSrc;
            }
            set => SetProperty(ref _imgSrc, value);
        }


        [JsonIgnore]
        public ImageSource _croppedImg;
        [JsonIgnore]
        public ImageSource CroppedImg { get => _croppedImg; set => SetProperty(ref _croppedImg, value); }

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

        private bool LoadImage()
        {
            var error_msg2 = "";
            string error_msg1;
            if (!string.IsNullOrEmpty(ImagePath) && System.IO.File.Exists(ImagePath))
            {
                try
                {
                    ImgSrc = new System.Windows.Media.Imaging.BitmapImage(new Uri(ImagePath));
                    return true;
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

            ImgSrc = null;
            var Warning_Title = LocalizeDictionary.Instance.GetLocalizedObject("Warning_Title", null, null) ?? "Warning";
            System.Windows.Forms.MessageBox.Show($"{error_msg1}: {error_msg2}", $"{Warning_Title}", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void EditImage()
        {
            if (LoadImage())
            {
                WindowEditImage windowEditImage = new();
                windowEditImage.Show();
                // 延迟赋值，等待 UI 线程完成当前布局和渲染
                windowEditImage.Dispatcher.BeginInvoke(new Action(() =>
                {
                    windowEditImage.editImageViewModel.ImgSrc = ImgSrc;
                    windowEditImage.editImageViewModel.ImgDst = CroppedImg;
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                windowEditImage.editImageViewModel.OnImageSaved += (img, rect) => { CroppedImg = img; CroppedRect = rect; };
            }
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

        public override bool Run()
        {
            var img = Autogui.ConvertImageSourceToBitmap(ImgSrc); // TODO: 改为CropedImg
            return Autogui.ClickImageOnScreen(img, Autogui.Position.LeftTop, Offest, Clicks, ClickGap, SimilarityThreshold, WaitTime, Timeout);
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

        public override bool Run()
        {
            return Autogui.TypeText(TextToType, DelayBetweenKeys);
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

        public override bool Run()
        {
            var img = Autogui.ConvertImageSourceToBitmap(ImgSrc); // TODO: 改为CropedImg
            var res = Autogui.FindImageOnScreen(img, SimilarityThreshold, GapTime, Timeout);
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

        public override bool Run()
        {
            var iconType = PopupType switch
            {
                "Information" => MessageBoxImage.Information,
                "Warning" => MessageBoxImage.Warning,
                "Error" => MessageBoxImage.Error,
                "Question" => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            if (System.Windows.Application.Current != null && System.Windows.Application.Current.Dispatcher != null)
            {
                // 使用 InvokeAsync 避免阻塞后台线程
                var res = System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        return WPFDevelopers.Controls.MessageBox.Show(PopupText, Title, MessageBoxButton.OK, iconType);
                    }
                    catch (Exception)
                    {
                        // 防止在应用关闭期间调用 Dispatcher 导致异常
                        System.Diagnostics.Debug.WriteLine($"Failed to show messagebox: {PopupText}");
                        IsTrue = false;
                    }
                    return MessageBoxResult.None;
                });
            }
            return IsTrue;
        }
    }

    public class EmptyStep : AutomationStepBase
    {
        public EmptyStep() : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
        }
        public override bool Run()
        {
            return IsTrue;
        }
    }
}
