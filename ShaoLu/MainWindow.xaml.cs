using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;

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
        readonly AppSettings appSettings = SingletonLocator.Settings;

        private const int HOTKEY_ID = 9000; // 自定义唯一ID
        private HwndSource _source;

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            _source = PresentationSource.FromVisual(this) as HwndSource;
            _source?.AddHook(WndProc);

            // 注册全局热键：例如 Ctrl + Alt + S
            NativeMethods.RegisterHotKey(
                new WindowInteropHelper(this).Handle,
                HOTKEY_ID, NativeMethods.MOD_ALT,
                (uint)KeyInterop.VirtualKeyFromKey(Key.F10)
            );
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == NativeMethods.WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                // 触发停止线程的逻辑
                stepsViewModel.StopCommand.Execute(null);
                handled = true;
            }
            return IntPtr.Zero;
        }


        public MainWindow()
        {
            InitializeComponent();
            DataContext = mainViewModel;

            Loaded += MainWindow_Loaded;

        }
        protected override void OnClosed(EventArgs e)
        {
            // 窗口关闭时务必注销热键，防止资源泄漏
            _source?.RemoveHook(WndProc);
            NativeMethods.UnregisterHotKey(new WindowInteropHelper(this).Handle, HOTKEY_ID);
            base.OnClosed(e);
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Check_current_lang();

            FontFamily = new System.Windows.Media.FontFamily(appSettings.App.WindowFont.FontFamily);
            FontSize = appSettings.App.WindowFont.FontSize;
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
            var filePath = PathServices.OpenPathDialog(LanguageService.GetLocalizedString("OpenFile"), $"{LanguageService.GetLocalizedString("StepFile")}|*.json");
            if (filePath != null)
            {
                mainViewModel.StepFileDir = Path.GetDirectoryName(filePath);
                var AutomationStepBases = StepsFile.LoadStepsFromJson(filePath);
                stepsViewModel.AutomationStepBases.Clear();
                stepsViewModel.InsertSteps(AutomationStepBases);
            }
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var filePath = PathServices.SavePathDialog(LanguageService.GetLocalizedString("OpenFile"), $"{LanguageService.GetLocalizedString("StepFile")}|*.json");
            if (filePath != null)
            {
                foreach (var step in stepsViewModel.AutomationStepBases)
                {
                    step.IsSave = true;
                    if (step is ImageRecognitionBase imageRecognition)
                    {
                        fileServer.UnmarkForDeletion(imageRecognition.CroppedImagePath);
                    }
                    if (step is ImagesRecognitionBase imagesRecognition)
                    {
                        foreach (var image in imagesRecognition.Images)
                        {
                            fileServer.UnmarkForDeletion(image.CroppedImagePath);
                        }
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

        private void About_Click(object sender, RoutedEventArgs e)
        {
            WindowAbout windowAbout = new();
            windowAbout.ShowDialog();
        }
    }
}
