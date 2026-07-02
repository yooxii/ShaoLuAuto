using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ShaoLu.Viewmodels
{
    public class EditImageViewModel : ObservableObject
    {

        private ImageSource _imgSrc;
        public ImageSource ImgSrc {
            get => _imgSrc;
            set => SetProperty(ref _imgSrc, value);
        }

        private ImageSource _imgDst;
        public ImageSource ImgDst {
            get => _imgDst;
            set => SetProperty(ref _imgDst, value);
        }

        private Rect _cropRect;
        public Rect CropRect {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public event Action<ImageSource, Rect> OnImageSaved;

        public void SaveCroppedImage(ImageSource croppedImage, Rect rect)
        {
            if (croppedImage != null)
            {
                OnImageSaved?.Invoke(croppedImage, rect);
            }
        }
    }
}
