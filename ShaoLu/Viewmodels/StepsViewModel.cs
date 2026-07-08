using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Expression.Drawing.Core;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels
{
    public class StepsViewModel : ObservableObject
    {
        #region 属性

        private volatile bool _stopSignal = false;

        public bool StopSignal { get => _stopSignal; set => _stopSignal = value; }


        // 选中步骤
        private AutomationStepBase _selectedStep;

        public AutomationStepBase SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }

        public ObservableCollection<AutomationStepBase> SelectedSteps { get; set; } = [];


        private ObservableCollection<AutomationStepBase> _automationStepBases = [];
        public ObservableCollection<AutomationStepBase> AutomationStepBases { get => _automationStepBases; set => SetProperty(ref _automationStepBases, value); }


        #endregion


        #region 命令

        private RelayCommand runCommand;
        public ICommand RunCommand => runCommand ??= new RelayCommand(Run);

        private RelayCommand stopCommand;
        public ICommand StopCommand => stopCommand ??= new RelayCommand(Stop);


        private RelayParameterCommand addStepCommand;
        public ICommand AddStepCommand => addStepCommand ??= new RelayParameterCommand(AddStep);


        private RelayCommand delStepCommand;
        public ICommand DelStepCommand => delStepCommand ??= new RelayCommand(DelStep);

        #endregion

        public StepsViewModel()
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

        #region 步骤增删

        private void AddStep(object parameter)
        {
            AutomationStepBase imgStep;
            if (parameter is string param)
            {
                AutomationStepBases ??= [];
                imgStep = param switch
                {
                    "ClickImage" => new ClickImageStep(),
                    "TypeText" => new TypeTextStep(),
                    "LogicalIf" => new FindImageStep(),
                    "Popup" => new PopupStep(),
                    _ => new ClickImageStep(),
                };
                if (SelectedStep is AutomationStepBase automationStepBase)
                {
                    int index = AutomationStepBases.IndexOf(automationStepBase) + 1;
                    AutomationStepBases.Insert(index, imgStep);
                }
                else
                {
                    AutomationStepBases.Add(imgStep);
                }
            }
            else
            {
                imgStep = new ClickImageStep();
                AutomationStepBases.Add(imgStep);
            }
            SelectedStep = imgStep;
        }


        private void DelStep()
        {
            if (SelectedSteps.Count > 0)
            {
                for (int i = SelectedSteps.Count - 1; i >= 0; i--)
                {
                    AutomationStepBases.Remove(SelectedSteps[i]);
                }
            }
        }

        public void CopySteps(ObservableCollection<AutomationStepBase> steps)
        {
            AutomationStepBases ??= [];
            AutomationStepBases.AddRange(steps);
        }

        #endregion

        #region 步骤执行
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
                                step.Run();
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
        #endregion
    }
}
