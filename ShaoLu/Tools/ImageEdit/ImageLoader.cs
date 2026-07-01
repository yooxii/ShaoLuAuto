using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ImageTool
{
    public enum ImageSize
    {
        Original,
        Thumb
    }

    public static class ImageLoader
    {
        static readonly ResourceDictionary Resources = new ResourceDictionary { Source = new Uri("pack://application:,,,/ShaoLu;component/Resources/ImageLoadState.xaml", UriKind.Absolute) };


        public static readonly DependencyProperty ImageSourceProperty = DependencyProperty.RegisterAttached("ImageSource", typeof(Image), typeof(ImageLoader), new PropertyMetadata(new PropertyChangedCallback(OnImageSourcePropertyChangedCallback)));
        public static Image GetImageSource(DependencyObject obj) => (Image)obj.GetValue(ImageSourceProperty);
        public static void SetImageSource(DependencyObject obj, object value) => obj.SetValue(ImageSourceProperty, value);


        public static readonly DependencyProperty ImageSizeProperty = DependencyProperty.RegisterAttached("ImageSize", typeof(ImageSize), typeof(ImageLoader), new PropertyMetadata(ImageSize.Original));
        public static ImageSize GetImageSize(DependencyObject obj) => (ImageSize)obj.GetValue(ImageSizeProperty);
        public static void SetImageSize(DependencyObject obj, object value) => obj.SetValue(ImageSizeProperty, value);


        private static void OnImageSourcePropertyChangedCallback(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        {
            if (sender is System.Windows.Controls.Image image)
            {
                BindSource(image);
            }
        }

        public static void BindSource(System.Windows.Controls.Image image)
        {
            BindImageLoading(image);

            var imageSource = GetImageSource(image);
            var imageSize = GetImageSize(image);

            ThreadPool.QueueUserWorkItem(async _ =>
            {
                await System.Threading.Tasks.Task.Delay(100);

                try
                {
                    if (imageSource != null)
                    {
                        BitmapSource bitmapSource = imageSize switch
                        {
                            ImageSize.Thumb => imageSource.GetThumbSource(),
                            _ => imageSource.GetBitmapSource(),
                        };

                        if (bitmapSource != null)
                        {
                            BindImage(image, bitmapSource);
                        }

                        return;
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine(ex, "ThumbLoader : Bind Source Error...");
                }

                BindImageFailed(image);
            });
        }

        public static void BindImage(System.Windows.Controls.Image image, ImageSource imageSource)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                image.Source = null;
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
                image.UseLayoutRounding = true;
                image.Source = imageSource;
                image.Stretch = Stretch.Uniform;
            });
        }

        public static void BindImageLoading(System.Windows.Controls.Image image)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                image.Source = null;
                image.Source = Resources["ImageLoading"] as DrawingImage;
                image.Stretch = Stretch.None;
            });
        }

        public static void BindImageFailed(System.Windows.Controls.Image image)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                image.Source = null;
                image.Source = Resources["ImageFailed"] as DrawingImage;
                image.Stretch = Stretch.None;
            });
        }
    }
}
