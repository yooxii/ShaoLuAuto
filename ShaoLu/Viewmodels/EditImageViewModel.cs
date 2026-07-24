using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Viewmodels
{
    public partial class EditImageViewModel : ObservableObject
    {

        private ImageSource _imgSrc;
        private ImageSource _imgDst;
        private Rect _cropRect;
        private double _startX = 0;
        private double _startY = 0;


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
                        StartX = img.Width / 2.0;
                        StartY = img.Height / 2.0;
                    }
                }
            }
        }

        public ImageSource ImgDst
        {
            get => _imgDst;
            set => SetProperty(ref _imgDst, value);
        }

        public Rect CropRect
        {
            get => _cropRect;
            set => SetProperty(ref _cropRect, value);
        }

        public double StartX
        {
            get => _startX;
            set
            {
                SetProperty(ref _startX, value);
            }
        }

        public double StartY
        {
            get => _startY;
            set
            {
                SetProperty(ref _startY, value);
            }
        }

        public double ThumbSize => 20;

        public ObservableCollection<ClickThumb> Thumbs { get; set; } = [];

        public EditImageViewModel()
        {
            Thumbs.CollectionChanged += (s, e) =>
            {
                if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Add)
                {
                    foreach (var item in e.NewItems)
                    {
                        if (item is ClickThumb thumb)
                        {
                            thumb.ThumbNo = Thumbs.Count;
                        }
                    }
                }
            };
        }



        public event Action<ImageSource, Rect, List<ClickThumb>> OnImageSaved;

        public void SetThumbs(List<ClickThumb> clickThumbs)
        {
            Thumbs.Clear();
            clickThumbs.ForEach(Thumbs.Add);
        }

        public void SaveCroppedImage()
        {
            if (ImgDst != null && Thumbs.Count > 0)
            {
                OnImageSaved?.Invoke(ImgDst, CropRect, Thumbs.ToList());
            }
        }

        [RelayCommand]
        private void AddThumb()
        {
            double x = StartX;
            double y = StartY;
            if (Thumbs.Count > 0)
            {
                var lastThumb = Thumbs.Last();
                x = lastThumb.ThumbX + lastThumb.ThumbSize + 10;
                y = lastThumb.ThumbY + lastThumb.ThumbSize + 10;
            }
            Thumbs.Add(new ClickThumb(x, y, ThumbSize, Visibility.Visible));
        }

        [RelayCommand]
        private void ClearThumbs()
        {
            Thumbs.Clear();
        }

        [RelayCommand]
        private void AllThumbVisibility(string visible)
        {
            foreach (var thumb in Thumbs)
                thumb.ThumbVisibility = visible == "1" ? Visibility.Visible : Visibility.Hidden;
        }

        public void DeleteThumb(ClickThumb thumb)
        {
            Thumbs.Remove(thumb);
        }

        public void ResetThumb(ClickThumb thumb)
        {
            thumb.ThumbX = StartX;
            thumb.ThumbY = StartY;
            thumb.ThumbSize = ThumbSize;
        }

        public void ChangeThumbVisibility(ClickThumb thumb)
        {
            thumb.ThumbVisibility = thumb.ThumbVisibility == Visibility.Visible ? Visibility.Hidden : Visibility.Visible;
        }
    }

    public class ClickThumb : ObservableObject
    {
        private int _thumbNo;
        private string _thumbCoordinates;
        private double _thumbX;
        private double _thumbY;
        private Visibility _thumbVisibility = Visibility.Visible;
        private double _thumbSize = 20;


        public int ThumbNo { get => _thumbNo; set => SetProperty(ref _thumbNo, value); }
        public string ThumbCoordinates { get => _thumbCoordinates; set => SetProperty(ref _thumbCoordinates, value); }
        public double ThumbX
        {
            get => _thumbX;
            set
            {
                if (SetProperty(ref _thumbX, value))
                {
                    ThumbCoordinates = $"Absolute position:{ClickPoint.X},{ClickPoint.Y}";
                }
            }
        }
        public double ThumbY
        {
            get => _thumbY;
            set
            {
                if (SetProperty(ref _thumbY, value))
                {
                    ThumbCoordinates = $"Absolute position:{ClickPoint.X},{ClickPoint.Y}";
                }
            }
        }
        [JsonIgnore]
        public Point ClickPoint => new(ThumbX + ThumbSize / 2, ThumbY + ThumbSize / 2);
        public Visibility ThumbVisibility { get => _thumbVisibility; set => SetProperty(ref _thumbVisibility, value); }
        public double ThumbSize { get => _thumbSize; set => SetProperty(ref _thumbSize, value); }

        public ClickThumb()
        {

        }

        public ClickThumb(double x, double y, double size, Visibility visibility)
        {
            ThumbX = x;
            ThumbY = y;
            ThumbSize = size;
            ThumbVisibility = visibility;
        }
    }
}
