using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Windows;
using System.Windows.Media;

namespace ShaoLu.Viewmodels
{
    public class EditImageViewModel : ObservableObject
    {

        private ImageSource _imgSrc;
        public ImageSource ImgSrc
        {
            get => _imgSrc;
            set
            {
                if (SetProperty(ref _imgSrc, value))
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

        private ImageSource _imgDst;
        public ImageSource ImgDst
        {
            get => _imgDst;
            set => SetProperty(ref _imgDst, value);
        }

        private Rect _cropRect;
        public Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public Point ClickOffset => new(OffsetX, OffsetY);

        private double _offsetX = 0;
        public double OffsetX
        {
            get => _offsetX;
            set
            {
                if (SetProperty(ref _offsetX, value))
                {
                    ThumbX = _offsetX - (ThumbSize / 2) + _cropRect.X;
                }
            }
        }

        private double _offsetY = 0;
        public double OffsetY
        {
            get => _offsetY;
            set
            {
                if (SetProperty(ref _offsetY, value))
                {
                    ThumbY = _offsetY - ThumbSize / 2 + _cropRect.Y;
                }
            }
        }

        public double ThumbSize => 20;

        private Visibility _thumbVisibility = Visibility.Hidden;
        public Visibility ThumbVisibility { get => _thumbVisibility; set => SetProperty(ref _thumbVisibility, value); }

        private double _thumbX;
        public double ThumbX { get => _thumbX; set => SetProperty(ref _thumbX, value); }

        private double _thumbY;
        public double ThumbY { get => _thumbY; set => SetProperty(ref _thumbY, value); }

        public event Action<ImageSource, Rect, Point> OnImageSaved;

        public void SaveCroppedImage()
        {
            if (ImgDst != null)
            {
                OnImageSaved?.Invoke(ImgDst, CropRect, ClickOffset);
            }
        }

        public void SaveOffset(Point offset)
        {
            if (offset == null) return;
            var tmp = offset - CropRect.TopLeft;
            OffsetX = tmp.X;
            OffsetY = tmp.Y;
        }

        public void SetOffset(System.Drawing.Point offset)
        {
            if (offset == null) return;
            OffsetX = offset.X;
            OffsetY = offset.Y;
        }
    }
}
