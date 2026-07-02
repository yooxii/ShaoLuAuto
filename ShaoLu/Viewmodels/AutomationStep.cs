using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Views;
using System;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using WPFLocalizeExtension.Engine;

namespace ShaoLu.Viewmodels
{
    // 基类
    public abstract class AutomationStepBase : ObservableObject
    {
        public int StepId { get; set; }
        public string StepName { get; set; }
        public StepType Type { get; set; }
        // 其他公共属性...
    }

    // 识图步骤
    public class ImageRecognitionStep : AutomationStepBase
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

        public Rect _croppedRect;
        public Rect CroppedRect { get => _croppedRect; set => SetProperty(ref _croppedRect, value); }


        private Point _offest;
        public Point Offest { get => _offest; set => SetProperty(ref _offest, value); }


        private float _similarityThreshold = 0.85F;
        public float SimilarityThreshold
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

    // 输入文字步骤
    public class TypeTextStep : AutomationStepBase
    {
        public string TextToType { get; set; }
        public int DelayBetweenKeys { get; set; }
    }
}
