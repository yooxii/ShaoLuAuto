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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ImageTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        public MainWindow(string imagePath)
            : this()
        {
            this.ImageEditor.OpenImage(imagePath);
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox)
            {
                return;
            }

            FocusManager.SetIsFocusScope(this, true);
            FocusManager.SetFocusedElement(this, this);

            this.ImageEditor.PressShortcutKey(sender, e);

            if (e.Key == Key.Escape)
            {
                if (this.WindowState == WindowState.Normal)
                {
                    this.Close();
                }
                else
                {
                    this.ChangeWindowStateNormalized();
                }
            }
        }

        private void ChangeWindowStateNormalized()
        {
            this.Width = 800;
            this.Height = 640;
            this.Left = (SystemParameters.PrimaryScreenWidth - this.Width) / 2;
            this.Top = (SystemParameters.PrimaryScreenHeight - this.Height) / 2;
            this.WindowState = WindowState.Normal;
        }

        private void ImageViewHeader_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // 双击放大
            if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    this.ChangeWindowStateNormalized();
                }
                else
                {
                    this.WindowState = WindowState.Maximized;
                }
            }

            // 按下拖动
            else if (e.LeftButton == MouseButtonState.Pressed && this.WindowState == WindowState.Normal)
            {
                this.DragMove();
            }
        }

        private void Button_Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
