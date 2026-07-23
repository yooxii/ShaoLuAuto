using ShaoLu.Services;
using ShaoLu.Viewmodels;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Point = ShaoLu.Models.Point;

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
            editImageViewModel.CropRect = EditImage.CurrentRect;
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is EditImageViewModel vm)
            {
                if (vm.ImgDst == null)
                {
                    WindowAsyncPopup.Show(LanguageService.GetLocalizedString("NoCropImage"), "Error", PopupButtons.OK, MessageBoxImage.Error);
                    return;
                }
                vm.SaveCroppedImage();

                WindowAsyncPopup.Show(LanguageService.GetLocalizedString("Saved"), LanguageService.GetLocalizedString("Success"), PopupButtons.OK, MessageBoxImage.Information);
            }
        }

        private void Back_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

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
