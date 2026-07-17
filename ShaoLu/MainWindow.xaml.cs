using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ShaoLu
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly Viewmodels.MainViewModel mainViewModel = SingletonLocator.Main;
        private readonly Viewmodels.StepsViewModel stepsViewModel = SingletonLocator.Steps;
        readonly FileServices fileServer = SingletonLocator.FileServices;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = mainViewModel;

            Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Check_current_lang();
        }

        #region 输入校验
        private void TextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            TextBox textBox = sender as TextBox;
            Regex regex = new(@"[^0-9\.]+");

            // 1. 拦截非数字和小数点的字符
            // 2. 拦截重复的小数点
            if (regex.IsMatch(e.Text) || (e.Text == "." && textBox.Text.Contains(".")))
            {
                e.Handled = true;
            }
        }

        private void TextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(string)))
            {
                string text = (string)e.DataObject.GetData(typeof(string));
                Regex regex = new(@"[^0-9\.]+");
                // 如果粘贴的内容包含非法字符，取消粘贴操作
                if (regex.IsMatch(text))
                {
                    e.CancelCommand();
                }
            }
            else
            {
                e.CancelCommand();
            }
        }
        #endregion

        private void Check_current_lang()
        {
            mainViewModel.English = mainViewModel.Simplified = mainViewModel.Tranditional = false;
            var current_lang = Services.LanguageService.GetCurrentLanguage();
            switch (current_lang)
            {
                case "en-US":
                    mainViewModel.English = true;
                    break;
                case "zh-CN":
                    mainViewModel.Simplified = true;
                    break;
                case "zh-TW":
                    mainViewModel.Tranditional = true;
                    break;
                default:
                    mainViewModel.Simplified = true;
                    break;
            }
        }

        private void Language_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem item)
            {
                Services.LanguageService.SetLanguage(item.Tag as string);
                Check_current_lang();
            }
        }

        private void Quit_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void TestCrop_Click(object sender, RoutedEventArgs e)
        {
            //WindowCropImage windowCropImage = new();
            //windowCropImage.Show();
            WindowEditImage windowEditImage = new();
            windowEditImage.Show();
        }

        private void New_Click(object sender, RoutedEventArgs e)
        {
            stepsViewModel.AutomationStepBases.Clear();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            Services.PathServices fileServices = new();
            var filePath = fileServices.OpenPathDialog("打开文件", "步骤文件|*.json");
            if (filePath != null)
            {
                var AutomationStepBases = StepsFile.LoadStepsFromJson(filePath);
                stepsViewModel.AutomationStepBases.Clear();
                stepsViewModel.InsertSteps(AutomationStepBases);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Services.PathServices fileServices = new();
            var filePath = fileServices.SavePathDialog("保存文件", "步骤文件|*.json");
            if (filePath != null)
            {
                foreach (var step in stepsViewModel.AutomationStepBases)
                {
                    step.IsSave = true;
                    if (step is ImageRecognitionBase imageRecognition)
                    {
                        fileServer.UnmarkForDeletion(imageRecognition.CroppedImagePath);
                    }
                }
                StepsFile.SaveStepsToJson(stepsViewModel.AutomationStepBases, filePath);

                fileServer.CommitPendingDeletions();
            }
        }

        private void Setting_Click(object sender, RoutedEventArgs e)
        {
            WindowSettings windowSettings = new();
            windowSettings.ShowDialog();
        }
    }
}
