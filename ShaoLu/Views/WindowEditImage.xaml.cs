using ShaoLu.Viewmodels;
using System.Windows;

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

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
