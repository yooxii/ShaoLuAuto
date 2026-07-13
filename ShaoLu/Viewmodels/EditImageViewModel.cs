using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShaoLu.Viewmodels
{
    public class EditImageViewModel : ObservableObject
    {

        private ImageSource _imgSrc;
        public ImageSource ImgSrc
        {
            get => _imgSrc;
            set => SetProperty(ref _imgSrc, value);
        }

        private double _imgSrcWidth;
        public double ImgSrcWidth
        {
            get => _imgSrcWidth;
            set => SetProperty(ref _imgSrcWidth, value);
        }

        private ImageSource _imgDst;
        public ImageSource ImgDst
        {
            get => _imgDst;
            set
            {
                if (SetProperty(ref _imgDst, value))
                {
                    if (value is ImageSource img)
                    {
                        // 初始化为图片中心
                        OffsetX = img.Width / 2.0;
                        OffsetY = img.Height / 2.0;
                    }
                }
            }
        }

        private Rect _cropRect;
        public Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public OpenCvSharp.Point ClickOffset => new((int)OffsetX, (int)OffsetY);

        private double _offsetX = 0;
        public double OffsetX
        {
            get => _offsetX;
            set { if (SetProperty(ref _offsetX, value)) { ThumbX = _offsetX - ThumbSize / 2; } }
        }

        private double _offsetY = 0;
        public double OffsetY
        {
            get => _offsetY;
            set { if (SetProperty(ref _offsetY, value)) { ThumbY = _offsetY - ThumbSize / 2; } }
        }

        public double ThumbSize => 20;

        private double _thumbX;
        public double ThumbX { get => _thumbX; set => SetProperty(ref _thumbX, value); }

        private double _thumbY;
        public double ThumbY { get => _thumbY; set => SetProperty(ref _thumbY, value); }

        public event Action<ImageSource, Rect, OpenCvSharp.Point> OnImageSaved;

        public void SaveCroppedImage(ImageSource croppedImage, Rect rect)
        {
            if (croppedImage != null)
            {
                OnImageSaved?.Invoke(croppedImage, rect, ClickOffset);
            }
        }

        public void SetOffset(Point offset)
        {
            if (offset == null) return;
            if (offset.X < 0 || offset.Y < 0) return;
            OffsetX = offset.X;
            OffsetY = offset.Y;
        } 
    }
}
