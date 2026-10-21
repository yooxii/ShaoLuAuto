using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShaoLu.Services;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
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
        private Rect _ocrRect = Rect.Empty;
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

        /// <summary>
        /// OCR 识别区域（相对于原图像素坐标）
        /// </summary>
        public Rect OCRRect
        {
            get => _ocrRect;
            set => SetProperty(ref _ocrRect, value);
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


        public event Action<ImageSource, Rect, List<ClickThumb>, Rect> OnImageSaved;
        public event Action<Rect> OnCropRectChanged;

        public void SetThumbs(List<ClickThumb> clickThumbs)
        {
            Thumbs.Clear();
            clickThumbs.ForEach(Thumbs.Add);
        }

        public void SetCropRect(Rect rect)
        {
            OnCropRectChanged?.Invoke(rect);
        }

        /// <summary>
        /// 最近一次保存事件的落盘结果：true=成功，false=失败，null=订阅者未回报（视为成功，兼容旧流程）
        /// </summary>
        public bool? LastSaveSucceeded { get; private set; }

        /// <summary>最近一次落盘失败的原因（供保存失败提示）</summary>
        public string LastSaveError { get; private set; }

        /// <summary>
        /// 由 OnImageSaved 订阅者在真正落盘后回报结果，
        /// 使 SaveCroppedImage 的返回值反映落盘成败（不回报时保持旧行为）。
        /// </summary>
        public void ReportSaveResult(bool succeeded, string error = null)
        {
            LastSaveSucceeded = succeeded;
            LastSaveError = succeeded ? null : error;
        }

        public async Task<bool> SaveCroppedImage()
        {
            if (ImgDst == null)
            {
                var (_, task) = WindowAsyncPopup.Show(LanguageService.GetLocalizedString("NoCropImage"), "Error", PopupButtons.OK, MessageBoxImage.Error);
                await task;
                return false;
            }

            // 重置上一次的落盘结果，避免沿用旧状态
            LastSaveSucceeded = null;
            LastSaveError = null;

            OnImageSaved?.Invoke(ImgDst, CropRect, Thumbs.ToList(), OCRRect);

            // 落盘失败时不再当成功：不返回 true，并显式提示失败原因
            if (LastSaveSucceeded == false)
            {
                string message = string.IsNullOrWhiteSpace(LastSaveError)
                    ? LanguageService.GetLocalizedString("Save_Failed", "Save failed")
                    : LastSaveError;
                var (_, task) = WindowAsyncPopup.Show(message, "Error", PopupButtons.OK, MessageBoxImage.Error);
                await task;
                return false;
            }

            return true;
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

        public ClickThumb Clone()
        {
            return new ClickThumb()
            {
                ThumbNo = ThumbNo,
                ThumbX = ThumbX,
                ThumbY = ThumbY,
                ThumbSize = ThumbSize,
                ThumbVisibility = ThumbVisibility,
            };
        }
    }
}
