using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using WPFDevelopers.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// WindowCropImage.xaml 的交互逻辑
    /// </summary>
    public partial class WindowCropImage : Window
    {
        private readonly Services.PathServices fileServices = new();
        public WindowCropImage()
        {
            InitializeComponent();
        }
        double ConvertBytesToMB(long bytes)
        {
            return (double)bytes / (1024 * 1024);
        }

        private void OnImportClickHandler(object sender, RoutedEventArgs e)
        {
            var filePath = fileServices.OpenPathDialog("打开图片", "图像文件(*.jpg;*.jpeg;*.png;)|*.jpg;*.jpeg;*.png;");
            var fileInfo = new FileInfo(filePath);
            var fileSize = fileInfo.Length;
            var mb = ConvertBytesToMB(fileSize);
            if (mb > 1)
            {
                WPFDevelopers.Controls.MessageBox.Show("图片不能大于 1M ", "提示", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();

            //if (bitmap.PixelWidth > 500 || bitmap.PixelHeight > 500)
            //{
            //    var width = (int)(bitmap.PixelWidth * 0.5);
            //    var height = (int)(bitmap.PixelHeight * 0.5);
            //    var croppedBitmap = new CroppedBitmap(bitmap, new Int32Rect(0, 0, width, height));
            //    var bitmapNew = new BitmapImage();
            //    bitmapNew.BeginInit();
            //    bitmapNew.DecodePixelWidth = width;
            //    bitmapNew.DecodePixelHeight = height;
            //    var memoryStream = new MemoryStream();
            //    var encoder = new JpegBitmapEncoder();
            //    encoder.Frames.Add(BitmapFrame.Create(croppedBitmap.Source));
            //    encoder.Save(memoryStream);
            //    memoryStream.Seek(0, SeekOrigin.Begin);
            //    bitmapNew.StreamSource = memoryStream;
            //    bitmapNew.EndInit();
            //    MyCropImage.Source = bitmapNew;
            //}
            //else
            //{
            //}
            MyCropImage.Source = bitmap;
        }

        private void SaveCrop_Click(object sender, RoutedEventArgs e)
        {
            if (MyCropImage.CurrentAreaBitmap == null)
            {
                Toast.Push("请选择图片", ToastImage.Warning);
                return;
            }
            var dlg = new SaveFileDialog
            {
                FileName = $"WPFDevelopers_CropImage_{DateTime.Now:yyyyMMddHHmmss}.jpg",
                DefaultExt = ".jpg",
                Filter = "image file|*.jpg"
            };
            if (dlg.ShowDialog() == true)
            {
                var pngEncoder = new PngBitmapEncoder();
                pngEncoder.Frames.Add(BitmapFrame.Create((BitmapSource)MyCropImage.CurrentAreaBitmap));
                using var fs = File.OpenWrite(dlg.FileName);
                pngEncoder.Save(fs);
                fs.Dispose();
                fs.Close();
            }

        }
    }
}
