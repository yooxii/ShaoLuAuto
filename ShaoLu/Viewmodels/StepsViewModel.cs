using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Expression.Drawing.Core;
using ShaoLu.Models;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Windows;

namespace ShaoLu.Viewmodels
{
    public class StepsViewModel : ObservableObject
    {

        private CancellationTokenSource _cts;

        #region 属性

        private volatile bool _stopSignal = false;
        public bool StopSignal { get => _stopSignal; set { _stopSignal = value; IsRunning = !value; } }


        public bool _isRunning = false;
        public bool IsRunning
        {
            get => _isRunning;
            set
            {
                if (SetProperty(ref _isRunning, value))
                {
                    RunCommand.RaiseCanExecuteChanged();
                    StopCommand.RaiseCanExecuteChanged();
                    AddStepCommand.RaiseCanExecuteChanged();
                    DelStepCommand.RaiseCanExecuteChanged();
                }
            }
        }


        // 选中步骤
        private AutomationStepBase _selectedStep;

        public AutomationStepBase SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }

        public ObservableCollection<AutomationStepBase> SelectedSteps { get; set; } = [];


        private ObservableCollection<AutomationStepBase> _automationStepBases = [];
        public ObservableCollection<AutomationStepBase> AutomationStepBases { get => _automationStepBases; set => SetProperty(ref _automationStepBases, value); }


        #endregion


        #region 命令

        private RelayCommand runCommand;
        public RelayCommand RunCommand => runCommand ??= new RelayCommand(Run, CanRun);

        private RelayCommand stopCommand;
        public RelayCommand StopCommand => stopCommand ??= new RelayCommand(Stop);


        private RelayParameterCommand addStepCommand;
        public RelayParameterCommand AddStepCommand => addStepCommand ??= new RelayParameterCommand(AddStep, CanAlertStep);


        private RelayCommand delStepCommand;
        public RelayCommand DelStepCommand => delStepCommand ??= new RelayCommand(DelStep, CanAlertStep);

        #endregion

        public StepsViewModel()
        {
            AutomationStepBases.CollectionChanged += (s, e) => UpdateAutomationStepBases();
        }

        private void UpdateAutomationStepBases()
        {
            RunCommand.RaiseCanExecuteChanged();
            // 遍历集合并更新每个步骤的行号 (从1开始)
            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                AutomationStepBases[i].LineNo = i + 1;
            }
        }

        #region 步骤增删

        private bool CanAlertStep()
        {
            return !_isRunning;
        }

        private void AddStep(object parameter)
        {
            AutomationStepBase imgStep;
            if (parameter is string param)
            {
                AutomationStepBases ??= [];
                imgStep = param switch
                {
                    "ClickImage" => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
                    "TypeText" => new TypeTextStep($"TypeText_{AutomationStepBases.Count(t => t.Type == StepType.TypeText) + 1}"),
                    "FindImage" => new FindImageStep($"FindImage_{AutomationStepBases.Count(t => t.Type == StepType.FindImage) + 1}"),
                    "Popup" => new PopupStep($"Popup_{AutomationStepBases.Count(t => t.Type == StepType.Popup) + 1}"),
                    _ => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
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
            _cts?.Cancel();
        }

        private bool CanRun()
        {
            return AutomationStepBases != null && AutomationStepBases.Count > 0 && !_isRunning;
        }

        /// <summary>
        /// 启动自动化运行，将耗时任务移至后台线程
        /// </summary>
        public async void Run()
        {
            // 重置停止信号
            StopSignal = false;

            _cts = new CancellationTokenSource();
            var token = _cts.Token;


            // 初始化自动化引擎
            Autogui.StartAuto();

            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                // 在每个步骤开始前再次检查停止信号，提高响应速度
                if (StopSignal || token.IsCancellationRequested)
                {
                    break;
                }
                var step = AutomationStepBases[i];

                try
                {
                    SelectedStep = step;
                    var runSuccess = await step.RunAsync(token);
                    if (!runSuccess)
                    {
                        throw new Exception($"{step.Name} run fail!");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // 记录单个步骤的错误，防止整个流程崩溃
                    WindowAsyncPopup.Show($"Step execution failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    if (step.FalseGoto <= 0)
                    {
                        StopSignal = true;
                        break;
                    }
                    else
                    {
                        step.IsTrue = false;
                    }
                }
                if (step.IsTrue)
                {
                    // 跳转到指定步骤
                    if (step.TrueGoto > 0)
                        i = step.TrueGoto - 1 - 1;
                }
                else
                {
                    if (step.FalseGoto > 0)
                        i = step.FalseGoto - 1 - 1;
                }
            }
        }

        #endregion
    }
}
