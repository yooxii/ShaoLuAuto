using ImageTool.Helpers;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;


namespace ImageTool.Utils
{
    /// <summary>
    ///  图像工具类
    /// </summary>
    public static class ImageWork
    {

        [DllImport("gdi32")]
        static extern int DeleteObject(IntPtr o);
        /// <summary>
        /// Bitmap->BitmapSource
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapSource BitMapSourceFromBitmap(Bitmap bitmap)
        {
            IntPtr intPtrl = bitmap.GetHbitmap();
            BitmapSource bitmapSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(intPtrl,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            DeleteObject(intPtrl);
            return bitmapSource;
        }

        /// <summary>
        ///  Bitmap --> BitmapImage
        /// </summary>
        /// <param name="bitmap"></param>
        /// <returns></returns>
        public static BitmapImage BitmapToBitmapImage(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Bmp);
                stream.Position = 0;
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.StreamSource = stream;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        /// <summary>
        /// BitmapSource->Bitmap
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        public static System.Drawing.Bitmap WpfBitmapSourceToBitmap(BitmapSource s)
        {
            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(s.PixelWidth, s.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            System.Drawing.Imaging.BitmapData data = bmp.LockBits(new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size),
            System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            s.CopyPixels(Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride);
            bmp.UnlockBits(data);
            return bmp;
        }


        /// <summary>
        /// ImageSource --> Bitmap
        /// </summary>
        /// <param name="imageSource"></param>
        /// <returns></returns>
        public static System.Drawing.Bitmap ImageSourceToBitmap(System.Windows.Media.ImageSource imageSource)
        {
            BitmapSource m = (BitmapSource)imageSource;

            System.Drawing.Bitmap bmp = new System.Drawing.Bitmap(m.PixelWidth, m.PixelHeight, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            System.Drawing.Imaging.BitmapData data = bmp.LockBits(
            new System.Drawing.Rectangle(System.Drawing.Point.Empty, bmp.Size), System.Drawing.Imaging.ImageLockMode.WriteOnly, System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

            m.CopyPixels(Int32Rect.Empty, data.Scan0, data.Height * data.Stride, data.Stride); bmp.UnlockBits(data);

            return bmp;
        }


        public static BitmapImage ControllerToBitmapImage(FrameworkElement ui)
        {
            var bmp = new RenderTargetBitmap((int)Math.Round(ui.ActualWidth), (int)Math.Round(ui.ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
            var dv = new System.Windows.Media.DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var vb = new System.Windows.Media.VisualBrush(ui);
                dc.DrawRectangle(vb, null, new Rect(new System.Windows.Point(), new System.Windows.Size(ui.ActualWidth, ui.ActualHeight)));
            }
            bmp.Render(dv);

           
            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            BitmapImage bitmapImage = new BitmapImage();
            using (var memoryStream = new MemoryStream())
            {
                encoder.Save(memoryStream);
                memoryStream.Seek(0, SeekOrigin.Begin);
                bitmapImage.BeginInit();
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.StreamSource = memoryStream;
                bitmapImage.EndInit();

                var filePath = @"d:\currentImage.png";
                if (File.Exists(filePath)) File.Delete(filePath);

                SaveBitmapImageIntoFile(bitmapImage, filePath);               
            }

           
            return bitmapImage;
        }

        public static System.Drawing.Bitmap ControllerToBitmap(FrameworkElement ui) 
        {
            BitmapImage bitmapImage= ControllerToBitmapImage(ui);
            System.Drawing.Bitmap bitmap = BitmapFromSource(bitmapImage);
            return bitmap;
        }


        public static System.Drawing.Bitmap BitmapFromSource(BitmapSource bitmapsource)
        {
            System.Drawing.Bitmap bitmap;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(bitmapsource));
                enc.Save(outStream);
                bitmap = new System.Drawing.Bitmap(outStream);
            }
            return bitmap;
        }

        /// <summary>
        /// 保存图片到文件
        /// </summary>
        /// <param name="image">图片数据</param>
        /// <param name="filePath">保存路径</param>
        public static void SaveImageToFile(BitmapSource image, string filePath)
        {
            BitmapEncoder encoder = GetBitmapEncoder(filePath);
            encoder.Frames.Add(BitmapFrame.Create(image));

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                encoder.Save(stream);
            }
        }

        /// <summary>
        /// 根据文件扩展名获取图片编码器
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <returns>图片编码器</returns>
        public static BitmapEncoder GetBitmapEncoder(string filePath)
        {
            var extName = Path.GetExtension(filePath).ToLower();
            if (extName.Equals(".png"))
            {
                return new PngBitmapEncoder();
            }
            else
            {
                return new JpegBitmapEncoder();
            }
        }

        /// <summary>
        /// 把内存里的BitmapImage数据保存到硬盘中
        /// </summary>
        /// <param name="bitmapImage">BitmapImage数据</param>
        /// <param name="filePath">输出的文件路径</param>
        public static void SaveBitmapImageIntoFile(BitmapImage bitmapImage, string filePath)
        {
            BitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmapImage));

            using (var fileStream = new System.IO.FileStream(filePath, System.IO.FileMode.Create))
            {
                encoder.Save(fileStream);
            }
        }


        /// <summary>
        /// 剪切图像
        /// </summary>
        /// <param name="sourceImage"></param>
        /// <param name="sourceRect"></param>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <param name="targetRect"></param>
        /// <param name="bkgColor"></param>
        /// <returns></returns>
        public static System.Drawing.Image ClipImage(System.Drawing.Image sourceImage, System.Drawing.Rectangle sourceRect, int targetWidth, int targetHeight, System.Drawing.Rectangle targetRect, System.Drawing.Color bkgColor)
        {
            System.Drawing.Bitmap returnImage = new System.Drawing.Bitmap(targetWidth, targetHeight, System.Drawing.Imaging.PixelFormat.Format32bppRgb);

            //设置目标图像的水平，垂直分辨率
            returnImage.SetResolution(sourceImage.HorizontalResolution, sourceImage.VerticalResolution);

            //创建一个graphics object 
            System.Drawing.Graphics grImage = System.Drawing.Graphics.FromImage(returnImage);

            //清除整个绘图面并以指定的背景色填充
            grImage.Clear(bkgColor);

            //指定在缩放或旋转图像时使用的算法
            grImage.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            grImage.DrawImage(sourceImage,
                targetRect,
                sourceRect,
                System.Drawing.GraphicsUnit.Pixel);

            grImage.Dispose();
            return returnImage;
        }


        /// <summary>
        /// 剪切图片,不需要targetRect参数
        /// </summary>
        /// <param name="sourceImage"></param>
        /// <param name="sourceRect"></param>
        /// <param name="targetWidth"></param>
        /// <param name="targetHeight"></param>
        /// <param name="bkgColor"></param>
        /// <returns></returns>
        public static System.Drawing.Image ClipImage(System.Drawing.Image sourceImage, System.Drawing.Rectangle sourceRect, int targetWidth, int targetHeight, System.Drawing.Color bkgColor)
        {
            System.Drawing.Rectangle targetRect = new System.Drawing.Rectangle(0, 0, targetWidth, targetHeight);
            return ClipImage(sourceImage, sourceRect, targetWidth, targetHeight, targetRect, bkgColor);
        }

        /// <summary>
        /// 修正缩放比例
        /// </summary>
        /// <param name="sourceWidth"></param>
        /// <param name="sourceHeight"></param>
        /// <param name="fixedWidth"></param>
        /// <param name="fixedHeight"></param>
        /// <param name="targetMapOffsetX"></param>
        /// <param name="targetMapOffsetY"></param>
        /// <param name="targetMapWidth"></param>
        /// <param name="targetMapHeight"></param>
        /// <param name="targetMapRatio"></param>
        public static void FixedSize_ResizeRatios(
           int sourceWidth, int sourceHeight,
           int fixedWidth, int fixedHeight,
           ref int targetMapOffsetX, ref int targetMapOffsetY,
           ref int targetMapWidth, ref int targetMapHeight,
           ref float targetMapRatio
       )
        {
            targetMapOffsetX = 0;
            targetMapOffsetY = 0;
            targetMapWidth = 0;
            targetMapHeight = 0;
            targetMapRatio = 0f;

            float targetMapRatioWidth = ((float)fixedWidth / (float)sourceWidth);
            float targetMapRatioHeight = ((float)fixedHeight / (float)sourceHeight);
            if (targetMapRatioHeight < targetMapRatioWidth)
            {
                targetMapRatio = targetMapRatioHeight;
                targetMapOffsetX = System.Convert.ToInt16((fixedWidth - (sourceWidth * targetMapRatio)) / 2);
            }
            else
            {
                targetMapRatio = targetMapRatioWidth;
                targetMapOffsetY = System.Convert.ToInt16((fixedHeight - (sourceHeight * targetMapRatio)) / 2);
            }

            targetMapWidth = (int)(sourceWidth * targetMapRatio);
            targetMapHeight = (int)(sourceHeight * targetMapRatio);
        }
    }
}
