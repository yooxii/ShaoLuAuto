using ShaoLu.Services;
using ShaoLu.Viewmodels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Point = ShaoLu.Models.Point;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowEditImage.xaml 的交互逻辑
    /// </summary>
    public partial class WindowEditImage : Window
    {
        public EditImageViewModel editImageViewModel = new();
        private bool _isDrawingOCR;
        private System.Windows.Point _ocrStartPoint;

        public WindowEditImage()
        {
            InitializeComponent();
            DataContext = editImageViewModel;
            editImageViewModel.OnCropRectChanged += (rect) =>
            { 
                EditImage.SetCropRect(rect);
            };
            editImageViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(EditImageViewModel.OCRRect))
                    UpdateOCRDisplay();
            };
        }

        private void CropImage_Click(object sender, RoutedEventArgs e)
        {
            editImageViewModel.ImgDst = EditImage.CurrentAreaBitmap;
            editImageViewModel.CropRect = EditImage.CurrentRect;
        }

        private async void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is EditImageViewModel vm)
            {
                if (vm.ImgDst == null)
                {
                    WindowAsyncPopup.Show(LanguageService.GetLocalizedString("NoCropImage"), "Error", PopupButtons.OK, MessageBoxImage.Error);
                    return;
                }
                var res = await vm.SaveCroppedImage();
                if (res)
                    WindowAsyncPopup.Show(LanguageService.GetLocalizedString("Saved"), LanguageService.GetLocalizedString("Success"), PopupButtons.OK, MessageBoxImage.Information);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        #region OCR 矩形绘制

        private void SetOCRRegion_Click(object sender, RoutedEventArgs e)
        {
            _isDrawingOCR = true;
            OCRCanvas.Visibility = Visibility.Visible;
            OCRCanvas.Cursor = Cursors.Cross;
            OCRRectOverlay.Visibility = Visibility.Collapsed;
        }

        private void ClearOCRRegion_Click(object sender, RoutedEventArgs e)
        {
            editImageViewModel.OCRRect = Rect.Empty;
            UpdateOCRDisplay();
        }

        private void OCRCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawingOCR) return;
            _ocrStartPoint = e.GetPosition(OCRCanvas);
            OCRRectOverlay.Visibility = Visibility.Visible;
            Canvas.SetLeft(OCRRectOverlay, _ocrStartPoint.X);
            Canvas.SetTop(OCRRectOverlay, _ocrStartPoint.Y);
            OCRRectOverlay.Width = 0;
            OCRRectOverlay.Height = 0;
            OCRCanvas.CaptureMouse();
        }

        private void OCRCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDrawingOCR || e.LeftButton != MouseButtonState.Pressed) return;
            var cur = e.GetPosition(OCRCanvas);
            double x = Math.Min(_ocrStartPoint.X, cur.X);
            double y = Math.Min(_ocrStartPoint.Y, cur.Y);
            double w = Math.Abs(cur.X - _ocrStartPoint.X);
            double h = Math.Abs(cur.Y - _ocrStartPoint.Y);
            Canvas.SetLeft(OCRRectOverlay, x);
            Canvas.SetTop(OCRRectOverlay, y);
            OCRRectOverlay.Width = w;
            OCRRectOverlay.Height = h;
        }

        private void OCRCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDrawingOCR) return;
            _isDrawingOCR = false;
            OCRCanvas.ReleaseMouseCapture();
            OCRCanvas.Visibility = Visibility.Collapsed;
            OCRCanvas.Cursor = Cursors.Arrow;

            var end = e.GetPosition(OCRCanvas);
            double x = Math.Min(_ocrStartPoint.X, end.X);
            double y = Math.Min(_ocrStartPoint.Y, end.Y);
            double w = Math.Abs(end.X - _ocrStartPoint.X);
            double h = Math.Abs(end.Y - _ocrStartPoint.Y);

            if (w > 3 && h > 3)
            {
                editImageViewModel.OCRRect = new Rect(x, y, w, h);
            }
            UpdateOCRDisplay();
        }

        private void UpdateOCRDisplay()
        {
            var rect = editImageViewModel.OCRRect;
            if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0)
            {
                OCRRectDisplay.Visibility = Visibility.Collapsed;
                OCRLabel.Visibility = Visibility.Collapsed;
                return;
            }
            Canvas.SetLeft(OCRRectDisplay, rect.X);
            Canvas.SetTop(OCRRectDisplay, rect.Y);
            OCRRectDisplay.Width = rect.Width;
            OCRRectDisplay.Height = rect.Height;
            OCRRectDisplay.Visibility = Visibility.Visible;

            Canvas.SetLeft(OCRLabel, rect.X);
            Canvas.SetTop(OCRLabel, rect.Y - 14);
            OCRLabel.Visibility = Visibility.Visible;
        }

        #endregion

        private void ClickPointThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb) return;

            var editImage = DataContext as EditImageViewModel;
            if (editImage?.ImgSrc == null) return;

            var editThumb = thumb.DataContext as ClickThumb;

            double horizontalChange = e.HorizontalChange;
            double verticalChange = e.VerticalChange;

            // 1. 获取当前 Canvas 中的位置
            double currentLeft = Canvas.GetLeft(thumb);
            double currentTop = Canvas.GetTop(thumb);

            // 处理初始 NaN 的情况
            if (double.IsNaN(currentLeft)) currentLeft = 0;
            if (double.IsNaN(currentTop)) currentTop = 0;

            // 2. 计算新位置
            // 关键：减去 _dragStartOffset，这样鼠标就会“粘”在点击的那个相对位置上
            // 如果希望鼠标严格在中心，可以直接忽略 _dragStartOffset 或者强制设为 0
            // 但为了手感自然，通常我们让 Thumb 跟随鼠标移动，保持点击时的相对位置不变

            // 如果你希望**严格**让鼠标在中心，可以简单地使用：
            // double newLeft = currentLeft + e.HorizontalChange;
            // double newTop = currentTop + e.VerticalChange;
            // 然后最后计算 Offset 时加上半径。

            // 但更通用的“手柄”逻辑是：
            double newLeft = currentLeft + horizontalChange;
            double newTop = currentTop + verticalChange;

            // 3. 边界检查 (基于图片原始像素大小)
            // 注意：ImgDst.Width 是双精度，可能包含小数，建议用 PixelWidth/PixelHeight 如果是 BitmapSource
            double imgWidth = editImage.ImgSrc is System.Windows.Media.Imaging.BitmapSource bmp ? bmp.PixelWidth : editImage.ImgSrc.Width;
            double imgHeight = editImage.ImgSrc is System.Windows.Media.Imaging.BitmapSource bmp2 ? bmp2.PixelHeight : editImage.ImgSrc.Height;

            double thumbSize = editThumb.ThumbSize;

            // 限制 Left
            newLeft = Math.Max(-thumbSize / 2, Math.Min(newLeft, imgWidth - thumbSize / 2));
            // 限制 Top
            newTop = Math.Max(-thumbSize / 2, Math.Min(newTop, imgHeight - thumbSize / 2));

            Canvas.SetLeft(thumb, newLeft);
            Canvas.SetTop(thumb, newTop);

            // 4. 更新 UI
            editThumb.ThumbX = newLeft;
            editThumb.ThumbY = newTop;

        }

        private void ClickPointThumb_Reset(object sender, RoutedEventArgs e)
        {
            // 1. 获取被点击的 MenuItem
            if (sender is not MenuItem menuItem) return;

            // 2. 获取 MenuItem 的父级 ContextMenu
            if (menuItem.Parent is not ContextMenu contextMenu) return;

            // 3. 获取 ContextMenu 的 DataContext (即我们刚才绑定的 Thumb 的数据对象)
            if (contextMenu.DataContext is ClickThumb thumb)
            {
                // 4. 调用 ViewModel 的方法删除该 Thumb
                editImageViewModel.ResetThumb(thumb);
            }
        }

        private void ClickPointThumb_Delete(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            if (menuItem.Parent is not ContextMenu contextMenu) return;

            if (contextMenu.DataContext is ClickThumb thumb)
            {
                editImageViewModel.DeleteThumb(thumb);
            }
        }

        private void ClickPointThumb_ChangeVisibility(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem) return;

            if (menuItem.Parent is not ContextMenu contextMenu) return;

            if (contextMenu.DataContext is ClickThumb thumb)
            {
                editImageViewModel.ChangeThumbVisibility(thumb);
            }
        }
    }
}
