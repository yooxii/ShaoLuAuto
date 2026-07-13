using ShaoLu.Viewmodels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowEditImage.xaml 的交互逻辑
    /// </summary>
    public partial class WindowEditImage : Window
    {
        public EditImageViewModel editImageViewModel = new();

        public WindowEditImage()
        {
            InitializeComponent();
            DataContext = editImageViewModel;
        }

        private void CropImage_Click(object sender, RoutedEventArgs e)
        {
            editImageViewModel.ImgDst = EditImage.CurrentAreaBitmap;
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is EditImageViewModel vm)
            {
                var croppedImg = editImageViewModel.ImgDst;
                var croppedRect = EditImage.CurrentRect;

                vm.SaveCroppedImage(croppedImg, croppedRect);

                WindowAsyncPopup.Show("Save Success!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }


        private void ClickPointThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is not Thumb thumb) return;

            var vm = this.DataContext as EditImageViewModel;
            if (vm?.ImgDst == null) return;

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
            double newLeft = currentLeft + e.HorizontalChange;
            double newTop = currentTop + e.VerticalChange;

            // 3. 边界检查 (基于图片原始像素大小)
            // 注意：ImgDst.Width 是双精度，可能包含小数，建议用 PixelWidth/PixelHeight 如果是 BitmapSource
            double imgWidth = vm.ImgDst is System.Windows.Media.Imaging.BitmapSource bmp ? bmp.PixelWidth : vm.ImgDst.Width;
            double imgHeight = vm.ImgDst is System.Windows.Media.Imaging.BitmapSource bmp2 ? bmp2.PixelHeight : vm.ImgDst.Height;

            double thumbSize = editImageViewModel.ThumbSize;

            // 限制 Left
            newLeft = Math.Max(-thumbSize / 2, Math.Min(newLeft, imgWidth - thumbSize / 2));
            // 限制 Top
            newTop = Math.Max(-thumbSize / 2, Math.Min(newTop, imgHeight - thumbSize / 2));

            // 4. 更新 UI
            Canvas.SetLeft(thumb, newLeft);
            Canvas.SetTop(thumb, newTop);

            // 5. 更新 ViewModel (计算中心点坐标)
            double centerX = newLeft + (thumbSize / 2);
            double centerY = newTop + (thumbSize / 2);

            vm.SetOffset(new Point(centerX, centerY));
        }
    }
}
