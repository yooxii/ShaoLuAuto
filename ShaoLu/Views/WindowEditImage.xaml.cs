using ShaoLu.Viewmodels;
using System;
using System.Collections.Generic;
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
using System.Windows.Shapes;
using WPFDevelopers.Controls;

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

                this.Close();
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
