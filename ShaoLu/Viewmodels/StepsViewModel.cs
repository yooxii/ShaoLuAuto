using CommunityToolkit.Mvvm.DependencyInjection;
using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ShaoLu.Viewmodels
{
    public class StepsViewModel : ObservableObject
    {
        private readonly static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        private CancellationTokenSource _cts;
        private readonly StepSettingsModel _stepSettings = SingletonLocator.Settings.Step;
        private readonly StepExecutionContext _executionContext = new();

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
                    UpStepCommand.RaiseCanExecuteChanged();
                    DownStepCommand.RaiseCanExecuteChanged();
                    CopyStepCommand.RaiseCanExecuteChanged();
                    CutStepCommand.RaiseCanExecuteChanged();
                    PasteStepCommand.RaiseCanExecuteChanged();
                }
            }
        }


        // 选中步骤
        private AutomationStepBase _selectedStep;
        public AutomationStepBase SelectedStep { get => _selectedStep; set => SetProperty(ref _selectedStep, value); }

        private AutomationStepBase _errorStep;
        public AutomationStepBase ErrorStep { get => _errorStep; set => SetProperty(ref _errorStep, value); }

        public ObservableCollection<AutomationStepBase> SelectedSteps { get; set; } = [];

        public ObservableCollection<AutomationStepBase> PasteSteps { get; set; } = [];


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


        private RelayCommand upStepCommand;
        public RelayCommand UpStepCommand => upStepCommand ??= new RelayCommand(UpStep, CanAlertStep);


        private RelayCommand downStepCommand;
        public RelayCommand DownStepCommand => downStepCommand ??= new RelayCommand(DownStep, CanAlertStep);


        private RelayCommand selectStepCommand;
        public RelayCommand SelectStepCommand => selectStepCommand ??= new RelayCommand(SelectStep, CanAlertStep);


        private RelayCommand copyStepCommand;
        public RelayCommand CopyStepCommand => copyStepCommand ??= new RelayCommand(CopyStep, CanAlertStep);


        private RelayCommand cutStepCommand;
        public RelayCommand CutStepCommand => cutStepCommand ??= new RelayCommand(CutStep, CanAlertStep);


        private RelayCommand pasteStepCommand;
        public RelayCommand PasteStepCommand => pasteStepCommand ??= new RelayCommand(PasteStep, CanAlertStep);

        #endregion

        public StepsViewModel()
        {
            AutomationStepBases.CollectionChanged += (s, e) =>
            {
                UpdateAutomationStepBases();
                if (e.OldItems != null)
                {
                    foreach (var item in e.OldItems)
                    {
                        if (item is IDisposable disposable)
                        {
                            disposable.Dispose();
                        }
                    }
                }
            };
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

        /// <summary>
        /// 确保用户已登录，未登录时弹出登录窗口
        /// </summary>
        /// <returns>登录成功返回true，取消或失败返回false</returns>
        private bool EnsureLoggedIn()
        {
            if (SingletonLocator.UserService.CurrentUser != null)
                return true;

            var loginVm = Ioc.Default.GetRequiredService<LoginViewModel>();
            var loginWindow = new WindowLogin(loginVm);
            loginWindow.Owner = Application.Current.MainWindow;
            var result = loginWindow.ShowDialog();

            if (result == true)
            {
                SingletonLocator.Main.RefreshLoginState();
            }
            return result == true;
        }

        private void AddStep(object parameter)
        {
            if (!EnsureLoggedIn()) return;

            AutomationStepBase step;
            if (parameter is string param)
            {
                AutomationStepBases ??= [];
                step = param switch
                {
                    "ClickImage" => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
                    "FindImage" => new FindImageStep($"FindImage_{AutomationStepBases.Count(t => t.Type == StepType.FindImage) + 1}"),
                    "ClickImages" => new ClickImagesStep($"ClickImages_{AutomationStepBases.Count(t => t.Type == StepType.ClickImages) + 1}"),
                    "FindImages" => new FindImagesStep($"FindImages_{AutomationStepBases.Count(t => t.Type == StepType.FindImages) + 1}"),
                    "TypeText" => new TypeTextStep($"TypeText_{AutomationStepBases.Count(t => t.Type == StepType.TypeText) + 1}"),
                    "TypeTextMore" => new TypeTextMoreStep($"TypeTextMore_{AutomationStepBases.Count(t => t.Type == StepType.TypeTextMore) + 1}"),
                    "TypeTextFromFile" => new TypeTextFromFileStep($"TypeTextFromFile_{AutomationStepBases.Count(t => t.Type == StepType.TypeTextFromFile) + 1}"),
                    "Popup" => new PopupStep($"Popup_{AutomationStepBases.Count(t => t.Type == StepType.Popup) + 1}"),
                    "TextOCR" => new TextOCRStep($"TextOCR_{AutomationStepBases.Count(t => t.Type == StepType.TextOCR) + 1}"),
                    "MouseAction" => new MouseActionStep($"MouseAction_{AutomationStepBases.Count(t => t.Type == StepType.MouseAction) + 1}"),
                    "Statistics" => new StatisticsStep($"Statistics_{AutomationStepBases.Count(t => t.Type == StepType.Statistics) + 1}"),
                    _ => new ClickImageStep($"ClickImage_{AutomationStepBases.Count(t => t.Type == StepType.ClickImage) + 1}"),
                };
                ApplyDefaultSettings(step);
                if (SelectedStep is AutomationStepBase automationStepBase)
                {
                    int index = AutomationStepBases.IndexOf(automationStepBase) + 1;
                    AutomationStepBases.Insert(index, step);
                }
                else
                {
                    AutomationStepBases.Add(step);
                }
            }
            else
            {
                step = new ClickImageStep();
                ApplyDefaultSettings(step);
                AutomationStepBases.Add(step);
            }
            SelectedStep = step;
        }

        /// <summary>
        /// 将设置中的默认参数应用到新建步骤
        /// </summary>
        private void ApplyDefaultSettings(AutomationStepBase step)
        {
            step.SelfReferenceLimit = _stepSettings.DefaultSelfReferenceLimit;
            step.WaitTime = _stepSettings.DefaultWaitTime;

            if (step is ImageRecognitionBase imageStep)
            {
                imageStep.SimilarityThreshold = _stepSettings.DefaultSimilarityThreshold;
            }
            if (step is ClickImageStep clickStep)
            {
                clickStep.Clicks = _stepSettings.DefaultClicks;
                clickStep.Timeout = _stepSettings.DefaultTimeout;
            }
            else if (step is FindImageStep findStep)
            {
                findStep.Timeout = _stepSettings.DefaultTimeout;
            }
        }


        private async void DelStep()
        {
            if (!EnsureLoggedIn()) return;
            await RemoveStepsAsync(confirm: true);
        }

        /// <summary>
        /// 核心删除逻辑，confirm=true 时弹出确认对话框
        /// </summary>
        private async Task RemoveStepsAsync(bool confirm)
        {
            try
            {
                if (SelectedSteps.Count == 0) return;

                // 检查待删除步骤是否被其他步骤引用
                var deletingUids = SelectedSteps.Select(s => s.Uid).ToHashSet();
                var referencingSteps = new List<string>();

                foreach (var step in AutomationStepBases)
                {
                    if (deletingUids.Contains(step.Uid)) continue; // 跳过待删除的步骤本身

                    bool references = false;
                    if (step.TrueGotoUid.HasValue && deletingUids.Contains(step.TrueGotoUid.Value))
                        references = true;
                    if (step.FalseGotoUid.HasValue && deletingUids.Contains(step.FalseGotoUid.Value))
                        references = true;
                    if (step.Conditions != null)
                    {
                        foreach (var cond in step.Conditions)
                        {
                            if (cond.StepUid.HasValue && deletingUids.Contains(cond.StepUid.Value))
                            {
                                references = true;
                                break;
                            }
                        }
                    }

                    if (references)
                        referencingSteps.Add($"{step.LineNo} - {step.Name}");
                }

                // 如果需要确认，弹出确认对话框
                if (confirm)
                {
                    string message = LanguageService.GetLocalizedString("Msg_ConfirmDeleteSteps");
                    if (referencingSteps.Count > 0)
                    {
                        message += "\n\n" + LanguageService.GetLocalizedString("Msg_DeleteReferenced") + "\n"
                            + string.Join("\n", referencingSteps);
                    }

                    var (_, popup) = WindowAsyncPopup.Show(message, LanguageService.GetLocalizedString("DeleteStepTitle"), PopupButtons.YesNo, MessageBoxImage.Warning);
                    var res = await popup;
                    if (res != PopupButton.YesValue)
                        return;
                }

                // 删除前清空引用
                foreach (var step in AutomationStepBases)
                {
                    if (deletingUids.Contains(step.Uid)) continue;
                    if (step.TrueGotoUid.HasValue && deletingUids.Contains(step.TrueGotoUid.Value))
                        step.TrueGotoUid = null;
                    if (step.FalseGotoUid.HasValue && deletingUids.Contains(step.FalseGotoUid.Value))
                        step.FalseGotoUid = null;
                    if (step.Conditions != null)
                    {
                        foreach (var cond in step.Conditions)
                        {
                            if (cond.StepUid.HasValue && deletingUids.Contains(cond.StepUid.Value))
                                cond.StepUid = null;
                        }
                    }
                }

                for (int i = SelectedSteps.Count - 1; i >= 0; i--)
                {
                    AutomationStepBases.Remove(SelectedSteps[i]);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, "RemoveSteps error: ");
            }
        }

        private void UpStep()
        {
            if (!EnsureLoggedIn()) return;
            if (SelectedStep == null)
                return;
            if (SelectedStep.LineNo <= 1)
                return;
            AutomationStepBases.Move(SelectedStep.LineNo - 1, SelectedStep.LineNo - 2);
        }

        private void DownStep()
        {
            if (!EnsureLoggedIn()) return;
            if (SelectedStep == null)
                return;
            if (SelectedStep.LineNo >= AutomationStepBases.Count)
                return;
            AutomationStepBases.Move(SelectedStep.LineNo - 1, SelectedStep.LineNo);
        }

        private void SelectStep()
        {
            if (SelectedStep == null) return;
            if (SelectedSteps == null || SelectedSteps.Count == 0) return;
            foreach (var step in SelectedSteps)
            {
                step.IsNeed = !step.IsNeed;
            }
        }

        public void InsertSteps(ObservableCollection<AutomationStepBase> steps, int index = -1)
        {
            AutomationStepBases ??= [];
            if (index >= 0 && index < AutomationStepBases.Count)
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (AutomationStepBases.Contains(steps[i]))
                    {
                        AutomationStepBases.Insert(index + i, steps[i].Clone());
                    }
                    else
                    {
                        AutomationStepBases.Insert(index + i, steps[i]);
                    }
                }
            }
            else
            {
                for (int i = 0; i < steps.Count; i++)
                {
                    if (AutomationStepBases.Contains(steps[i]))
                    {
                        AutomationStepBases.Add(steps[i].Clone());
                    }
                    else
                    {
                        AutomationStepBases.Add(steps[i]);
                    }
                }
            }
        }

        private void CopyStep()
        {
            if (!EnsureLoggedIn()) return;

            PasteSteps.Clear();
            // 使用 LINQ 调用每个步骤的 Clone 方法
            var clonedSteps = SelectedSteps.Select(step => step.Clone()).ToList();

            // 将克隆后的步骤添加到 PasteSteps
            foreach (var step in clonedSteps)
            {
                PasteSteps.Add(step);
            }
        }

        private async void CutStep()
        {
            CopyStep();
            await RemoveStepsAsync(confirm: false);
        }

        private void PasteStep()
        {
            if (!EnsureLoggedIn()) return;

            if (SelectedStep != null)
            {
                InsertSteps(PasteSteps, SelectedStep.LineNo - 1);
            }
            else
            {
                InsertSteps(PasteSteps);
            }
        }

        #endregion

        #region 步骤执行

        private void Stop()
        {
            logger.Info("Stop Auto");
            StopSignal = true;
            _cts?.Cancel();
        }

        private bool CanRun()
        {
            return AutomationStepBases != null && AutomationStepBases.Count > 0 && !_isRunning;
        }

        private void PreRun()
        {
            // 重置停止信号
            StopSignal = false;
            // 初始化自动化引擎
            Autogui.StartAuto();
            // 清空执行上下文
            _executionContext.Clear();
            if (_stepSettings.MinimizeOnRun)
                Application.Current.MainWindow.WindowState = WindowState.Minimized;
            foreach (var step in AutomationStepBases)
            {
                // 重置自引用计数器
                step.SelfReferenceCount = 0;
                step.ErrorType = StepErrorType.None;
                step.LastResult = null;
                if (step is TypeTextMoreStep textMoreStep)
                {
                    if (textMoreStep.ReloadText)
                        textMoreStep.Reload();
                }
                else if (step is TypeTextFromFileStep textFromFileStep)
                {
                    if (textFromFileStep.ReloadIndex)
                        textFromFileStep.Index = 0;
                }
            }
        }

        /// <summary>
        /// 启动自动化运行，将耗时任务移至后台线程
        /// </summary>
        public async void Run()
        {
            try
            {
                // 运行前确认
                if (_stepSettings.ConfirmBeforeRun)
                {
                    var (_, confirmTask) = WindowAsyncPopup.Show(
                        LanguageService.GetLocalizedString("Msg_ConfirmRun"), LanguageService.GetLocalizedString("Question"),
                        PopupButtons.YesNo, MessageBoxImage.Question);
                    var confirmResult = await confirmTask;
                    if (confirmResult != PopupButton.YesValue)
                        return;
                }

                PreRun();
                _cts = new CancellationTokenSource();
                var token = _cts.Token;

                logger.Info("Start Auto");

                for (int i = 0; i < AutomationStepBases.Count; i++)
                {
                    // 在每个步骤开始前检查停止信号和取消标志
                    token.ThrowIfCancellationRequested();
                    if (StopSignal || i < 0)
                    {
                        break;
                    }
                    var step = AutomationStepBases[i];

                    // StepReached 模式：检查是否有弹窗需要在到达此步骤时关闭
                    CheckAndCloseStepReachedPopups(step.Uid);

                    if (!step.IsNeed)
                        continue;

                    try
                    {
                        SelectedStep = step;

                        // 计时执行
                        var sw = Stopwatch.StartNew();
                        await step.RunAsync(token);
                        sw.Stop();

                        step.IsError = false;
                        step.ErrorType = StepErrorType.None;

                        // 构建执行结果
                        var result = step.LastResult ?? new StepExecutionResult();
                        result.ExecutionTimeMs = sw.Elapsed.TotalMilliseconds;
                        result.IsTrue = step.IsTrue;
                        result.ExecutedAt = DateTime.Now;
                        step.LastResult = result;

                        // 存入执行上下文
                        _executionContext.SetResult(step.LineNo, result);
                        _executionContext.SetResultByUid(step.Uid, result);

                        // 自定义条件判断
                        if (step.ConditionMode == ConditionMode.Custom && step.Conditions.Count > 0)
                        {
                            step.IsTrue = ConditionEvaluator.Evaluate(step.Conditions, _executionContext, result);
                        }

                        // 通用日志记录
                        if (step.EnableLog)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(SingletonLocator.Main.StepFilePath ?? "unsaved");
                            string logContent = $"[Result:{step.IsTrue}] [Time:{result.ExecutionTimeMs:F0}ms]";
                            if (result.Similarity >= 0)
                                logContent += $" [Similarity:{result.Similarity:F3}]";
                            ExecutionLogService.Log(step.Uid, fileName, step.Name, logContent, result.OCRText);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        logger.Info("Operation Canceled");
                        step.ErrorType = StepErrorType.CancelledByUser;
                        break;
                    }
                    catch (Exception ex)
                    {
                        // 记录单个步骤的错误，防止整个流程崩溃
                        logger.Warn(ex, "Step \"{0}\" execution Failed:", step.Name);

                        step.IsError = true;
                        step.ErrorMessage = ex.Message;
                        step.ErrorType = InferErrorType(ex);
                        if (_stepSettings.ShowErrorPopup)
                        {
                            var (_, popupTask) = WindowAsyncPopup.Show(
                                $"{LanguageService.GetLocalizedString("Step")}{step.Name}{LanguageService.GetLocalizedString("ExecutionFailed")}{step.ErrorMessage}", "Error",
                                PopupButtons.YesCancel, MessageBoxImage.Error);
                            await popupTask;
                        }

                        if (!step.FalseGotoUid.HasValue || step.FalseGotoUid.Value == Guid.Empty)
                        {
                            StopSignal = true;
                            break;
                        }
                        else
                        {
                            step.IsTrue = false;
                        }
                    }

                    // 确定下一个执行索引（通过 Uid 查找目标步骤）
                    Guid? targetUid = step.IsTrue ? step.TrueGotoUid : step.FalseGotoUid;

                    if (targetUid.HasValue && targetUid.Value != Guid.Empty)
                    {
                        int nextIndex = FindIndexByUid(targetUid.Value);

                        if (nextIndex < 0)
                        {
                            // 目标步骤不存在（可能被删除），继续下一步
                            step.SelfReferenceCount = 0;
                        }
                        // 自引用检测
                        else if (nextIndex == i) // 指向自身
                        {
                            step.SelfReferenceCount++;
                            // SelfReferenceLimit: -1=无限制, 0=禁止自引用, >0=限制次数
                            if (step.SelfReferenceLimit >= 0 && step.SelfReferenceCount >= step.SelfReferenceLimit)
                            {
                                step.IsError = true;
                                step.ErrorType = StepErrorType.SelfReferenceLimit;
                                step.ErrorMessage = string.Format(LanguageService.GetLocalizedString("Msg_SelfReferenceLimit"), step.Name, step.SelfReferenceLimit);
                                step.SelfReferenceCount = 0;
                                // 不跳转，继续下一步
                            }
                            else
                            {
                                i = nextIndex - 1; // -1 因为 for 循环会 i++
                            }
                        }
                        else
                        {
                            step.SelfReferenceCount = 0; // 非自引用时重置
                            i = nextIndex - 1;
                        }
                    }
                    // targetUid 为空时不修改 i，for 循环 i++ 自然进入下一步
                }
                StopSignal = true;
                Application.Current.MainWindow.WindowState = WindowState.Normal;
                Application.Current.Dispatcher.Invoke(() =>
                {
                    Application.Current.MainWindow.Activate();
                });
                logger.Info("Auto Finished");
            }
            catch (OperationCanceledException)
            {
                logger.Info("Operation Canceled");
                StopSignal = true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Run Error: ");
            }
        }

        /// <summary>
        /// 根据异常类型推断 ErrorType
        /// </summary>
        private static StepErrorType InferErrorType(Exception ex)
        {
            return ex switch
            {
                FileNotFoundException => StepErrorType.FileNotFound,
                OperationCanceledException => StepErrorType.CancelledByUser,
                IndexOutOfRangeException => StepErrorType.IndexOutOfRange,
                InvalidOperationException => StepErrorType.Unknown,
                TimeoutException => StepErrorType.ExecutionTimeout,
                _ => StepErrorType.Unknown
            };
        }

        /// <summary>
        /// StepReached 模式：检查所有 PopupStep，如果有活跃弹窗的目标步骤是当前步骤，则关闭弹窗
        /// </summary>
        private void CheckAndCloseStepReachedPopups(Guid currentStepUid)
        {
            foreach (var s in AutomationStepBases)
            {
                if (s is PopupStep popupStep
                    && popupStep.CloseMode == PopupCloseMode.StepReached
                    && popupStep.CloseOnStepUid == currentStepUid
                    && popupStep.ActivePopupWindow != null)
                {
                    var defaultResult = popupStep.PopupButtons.DefaultButton?.Value ?? string.Empty;
                    popupStep.ActivePopupWindow.CloseWithResult(defaultResult);
                }
            }
        }

        #endregion

        /// <summary>
        /// 根据 Uid 查找步骤在集合中的索引，找不到返回 -1
        /// </summary>
        public int FindIndexByUid(Guid uid)
        {
            if (AutomationStepBases == null) return -1;
            for (int i = 0; i < AutomationStepBases.Count; i++)
            {
                if (AutomationStepBases[i].Uid == uid) return i;
            }
            return -1;
        }

    }
}
