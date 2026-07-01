using ImageMagick;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ImageTool
{
    internal static class ImageDecoder
    {
        [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        internal static extern bool DeleteObject(IntPtr hObject);

        internal static BitmapSource GetBitmapSource(Bitmap bitmap)
        {
            if (bitmap == null) return null;

            IntPtr handle = IntPtr.Zero;

            BitmapSource bitmapSource= null;

            try
            {
                handle = bitmap.GetHbitmap();
                bitmapSource = Imaging.CreateBitmapSourceFromHBitmap(handle, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bitmapSource.Freeze();
            }
            catch (Exception ex) 
            {
                Console.WriteLine(ex);
            }
            finally
            {
                if (handle != IntPtr.Zero)
                    DeleteObject(handle);
            }

            return bitmapSource;
        }

        internal static Bitmap GetImage(string file, out Dictionary<string, string> exif)
        {
            Bitmap image;
            exif = null;

            try
            {
                if (System.IO.Path.GetExtension(file).ToUpperInvariant() == ".GIF")
                {
                    image = new Bitmap(file);

                    using var magickImage = new MagickImage();
                    exif = GetExif(magickImage);
                }
                else
                {
                    using var magickImage = new MagickImage();
                    magickImage.Read(file);
                    magickImage.Quality = 100;

                    image = magickImage.ToBitmap();
                    exif = GetExif(magickImage);
                }

                return image;
            }
            catch (Exception e)
            {
#if DEBUG
                Trace.WriteLine("GetImage returned " + file + " null, \n" + e.Message);
#endif
                return null;
            }
        }

        internal static BitmapSource GetThumb(string file, byte quality = 100, uint size = 500)
        {
            try
            {
                MagickImage magickImage = new MagickImage
                {
                    Quality = quality,
                    ColorSpace = ColorSpace.Transparent
                };

                magickImage.Read(file);
                magickImage.AdaptiveResize(size, size);

                BitmapSource thumb = magickImage.ToBitmapSource();

                magickImage.Dispose();
                thumb.Freeze();

                return thumb;
            }
            catch (MagickException e)
            {
#if DEBUG
                Trace.WriteLine("GetThumb returned " + file + " null, \n" + e.Message);
#endif
                return null;
            }
        }

        internal static Dictionary<string, string> GetExif(MagickImage image)
        {
            var dictionary = new Dictionary<string, string>
            {
                { "图片名称", System.IO.Path.GetFileName(image.FileName) },
                { "图片路径", image.FileName }
            };

            var profile = image.GetExifProfile();

            if (profile != null)
            {
                dictionary.Add("图片宽度", image.Width.ToString());
                dictionary.Add("图片高度", image.Height.ToString());

                foreach (var value in profile.Values)
                {
                    if (TryGetExif(value, out string name, out string description))
                    {
                        if (!dictionary.ContainsKey(name))
                        {
                            dictionary.Add(name, description);
                        }
                    }
                }
            }

            return dictionary;
        }

        internal static bool TryGetExif(IExifValue value, out string name, out string description)
        {
            name = string.Empty;

            description = value.ToString();

            switch (value.Tag.ToString())
            {
                case "Model":
                    name = "相机型号";
                    break;
                case "LensModel":
                    name = "镜头类型";
                    break;
                case "DateTime":
                    name = "拍摄时间";
                    break;
                case "ImageHeight":
                    name = "照片高度";
                    break;
                case "ImageWidth":
                    name = "照片宽度";
                    break;
                case "ColorSpace":
                    name = "色彩空间";
                    break;

                case "FNumber":
                    name = "光圈";
                    break;
                case "ISOSpeedRatings":
                    name = "ISO";
                    description = ((ushort[])value.GetValue())[0].ToString();
                    break;
                case "ExposureBiasValue":
                    name = "曝光补偿";
                    break;
                case "FocalLength":
                    name = "焦距";
                    break;
                case "ExposureTime":
                    name = "曝光时间";
                    break;

                case "ExposureProgram":
                    name = "曝光程序";
                    break;
                case "MeteringMode":
                    name = "测光模式";
                    break;
                case "FlashMode":
                    name = "闪光灯";
                    break;
                case "WhiteBalanceMode":
                    name = "白平衡";
                    break;
                case "ExposureMode":
                    name = "曝光模式";
                    break;
                case "ContinuousDriveMode":
                    name = "驱动模式";
                    break;
                case "FocusMode":
                    name = "对焦模式";
                    break;

                case "Artist":
                    name = "作者";
                    break;
                case "Copyright":
                    name = "版权信息";
                    break;
                case "FileModifiedDate":
                    name = "修改时间";
                    break;
            }

            return name != String.Empty;
        }
    }
}
