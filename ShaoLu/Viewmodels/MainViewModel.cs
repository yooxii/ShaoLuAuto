using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using WPFDevelopers.Controls;
using WPFLocalizeExtension.Extensions;

namespace ShaoLu.Viewmodels
{
    public class MainViewModel : ObservableObject
    {
        #region 属性

        #region 语言
        private bool _english;
        public bool English { get => _english; set => SetProperty(ref _english, value); }


        private bool _simplified;
        public bool Simplified { get => _simplified; set => SetProperty(ref _simplified, value); }


        private bool _traditional;
        public bool Tranditional { get => _traditional; set => SetProperty(ref _traditional, value); }

        #endregion

                private AutomationStepBase _selectedStep;

        public AutomationStepBase SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }

        private volatile bool _stopSignal = false;

        public bool StopSignal
        {
            get => _stopSignal;
            set => _stopSignal = value;
        }

        private ObservableCollection<AutomationStepBase> _automationStepBases = [];
        public ObservableCollection<AutomationStepBase> AutomationStepBases { get => _automationStepBases; set => SetProperty(ref _automationStepBases, value); }


        #endregion


        #region 命令

        private RelayCommand addImageStepCommand;
        public ICommand AddImageStepCommand => addImageStepCommand ??= new RelayCommand(AddImageStep);


        private RelayCommand runCommand;
        public ICommand RunCommand => runCommand ??= new RelayCommand(Run);

        private RelayCommand stopCommand;
        public ICommand StopCommand => stopCommand ??= new RelayCommand(Stop);

        #endregion

        public MainViewModel()
        {
            AutomationStepBases.CollectionChanged += (s, e) => UpdateLineNumbers();
        }

        private void UpdateLineNumbers()
        {
            // 遍历集合并更新每个步骤的行号 (从1开始)
            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                AutomationStepBases[i].LineNo = i + 1;
            }
        }

        private void AddImageStep()
        {
            var imgStep = new ClickImageStep();
            AutomationStepBases.Add(imgStep);
        }

        private void Stop()
        {
            StopSignal = true;
        }

        /// <summary>
        /// 启动自动化运行，将耗时任务移至后台线程
        /// </summary>
        public void Run()
        {
            // 重置停止信号
            _stopSignal = false;

            // 2. 将耗时任务转到线程池线程执行，避免阻塞 UI
            Task.Run(() =>
            {
                try
                {
                    // 初始化自动化引擎（假设此方法也是耗时的或需要在线程上下文中运行）
                    Autogui.StartAuto();

                    while (true)
                    {
                        // 检查停止信号
                        if (_stopSignal || AutomationStepBases == null || AutomationStepBases.Count == 0)
                        {
                            break;
                        }

                        foreach (var step in AutomationStepBases)
                        {
                            // 在每个步骤开始前再次检查停止信号，提高响应速度
                            if (_stopSignal)
                            {
                                return;
                            }

                            try
                            {
                                ExecuteStep(step);
                            }
                            catch (Exception ex)
                            {
                                // 记录单个步骤的错误，防止整个流程崩溃
                                ShowErrorOnUi($"Step execution failed: {ex.Message}");
                            }
                        }

                        // 可选：如果步骤列表为空或执行过快，添加微小延迟防止 CPU 空转
                        // Thread.Sleep(10); 
                    }
                }
                catch (Exception ex)
                {
                    // 捕获顶层未处理异常
                    ShowErrorOnUi($"Automation critical error: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// 执行单个自动化步骤
        /// </summary>
        private void ExecuteStep(AutomationStepBase step)
        {
            switch (step.Type)
            {
                case Models.StepType.ClickImage:
                    HandleImageRecognitionStep(step);
                    break;
                case Models.StepType.TypeText:
                    HandleTypeTextStep(step);
                    break;
                default:
                    // 忽略未知类型或记录日志
                    break;
            }
        }

        private void HandleImageRecognitionStep(AutomationStepBase step)
        {
            // 使用模式匹配进行安全的类型转换
            if (step is ClickImageStep imgStep)
            {
                var img = imgStep.ImgSrc;

                // 防御性检查：确保图像源不为空
                if (img == null)
                {
                    ShowErrorOnUi($"Error!-Image source is null for path: {imgStep.ImagePath}");
                    return;
                }

                // 转换图像
                // 注意：假设 ConvertImageSourceToBitmap 返回的是一个需要被 ClickImageOnScreen 使用的对象
                // 如果它返回 IDisposable，理想情况下应在使用后 Dispose。
                // 这里保持原逻辑调用，但包裹在 try-catch 中以捕获可能的 GDI+ 错误
                var bitmap = Autogui.ConvertImageSourceToBitmap(img);

                bool isSuccess = Autogui.ClickImageOnScreen(bitmap, Autogui.Position.RightDown, new OpenCvSharp.Point(-70, -30));

                if (!isSuccess)
                {
                    ShowErrorOnUi($"Error!-Not Find image{imgStep.ImagePath} On Screen.");
                }
            }
            else
            {
                ShowErrorOnUi("Internal Error: Step type mismatch for ImageRecognition.");
            }
        }

        private void HandleTypeTextStep(AutomationStepBase step)
        {
            if (step is TypeTextStep textStep)
            {
                // 原文中此处为空，保持现状
                // TODO: Implement TypeText logic
            }
        }

        /// <summary>
        /// 在 UI 线程上显示错误消息
        /// </summary>
        private void ShowErrorOnUi(string message)
        {
            // 5. 确保 UI 操作在 Dispatcher 线程执行
            if (Application.Current != null && Application.Current.Dispatcher != null)
            {
                // 使用 InvokeAsync 避免阻塞后台线程
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        WPFDevelopers.Controls.MessageBox.Show(message, "Error", System.Windows.MessageBoxImage.Error);
                    }
                    catch (Exception)
                    {
                        // 防止在应用关闭期间调用 Dispatcher 导致异常
                        System.Diagnostics.Debug.WriteLine($"Failed to show messagebox: {message}");
                    }
                });
            }
        }
    }
}
