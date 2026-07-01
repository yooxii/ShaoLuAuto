using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ImageTool
{
    /// <summary>
    /// MainWin.xaml 的交互逻辑
    /// </summary>
    public partial class MainWin : Window
    {
        public MainWin()
        {
            InitializeComponent();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            ImageEditor.MainWin=this;
        }
    }
}
