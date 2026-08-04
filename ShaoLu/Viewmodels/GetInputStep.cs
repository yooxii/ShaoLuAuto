using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 获取输入方式
    /// </summary>
    public enum GetInputMode
    {
        /// <summary>从屏幕指定区域执行 OCR 识别</summary>
        OCR,
        /// <summary>读取屏幕指定位置处可选择文本（UI Automation）</summary>
        ScreenText,
    }

    /// <summary>
    /// 获取输入步骤：从指定方式获取屏幕信息（OCR 识别 / 读取可选择文本）
    /// </summary>
    public class GetInputStep : AutomationStepBase
    {
        private Rect _ocrRegion = new();
        private string _ocrResultFull;
        private GetInputMode _inputMode = GetInputMode.OCR;
        private System.Windows.Point _textPoint = new();

        /// <summary>
        /// 获取输入方式
        /// </summary>
        public GetInputMode InputMode { get => _inputMode; set => SetProperty(ref _inputMode, value); }

        /// <summary>所有获取方式（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<GetInputMode> InputModes { get; } = new() { GetInputMode.OCR, GetInputMode.ScreenText };

        /// <summary>
        /// ScreenText 模式下的目标位置（屏幕逻辑像素）
        /// </summary>
        public System.Windows.Point TextPoint { get => _textPoint; set { if (SetProperty(ref _textPoint, value)) OnPropertyChanged(nameof(TextPointText)); } }

        /// <summary>
        /// ScreenText 目标位置描述文本（用于 UI 显示）
        /// </summary>
        [JsonIgnore]
        public string TextPointText => TextPoint == new System.Windows.Point(0, 0)
            ? LanguageService.GetLocalizedString("OCR_NoRegion", "未选择位置")
            : $"X:{TextPoint.X:F0}, Y:{TextPoint.Y:F0}";

        /// <summary>
        /// OCR 屏幕区域（绝对坐标）
        /// </summary>
        public Rect OCRRegion { get => _ocrRegion; set => SetProperty(ref _ocrRegion, value); }

        /// <summary>
        /// OCR 区域描述文本（用于 UI 显示）
        /// </summary>
        [JsonIgnore]
        public string OCRRegionText
        {
            get
            {
                if (OCRRegion.IsEmpty)
                    return LanguageService.GetLocalizedString("OCR_NoRegion", "未选择区域");
                return $"X:{OCRRegion.X:F0}, Y:{OCRRegion.Y:F0}, W:{OCRRegion.Width:F0}, H:{OCRRegion.Height:F0}";
            }
        }

        /// <summary>
        /// 获取结果完整文本（运行时）
        /// </summary>
        [JsonIgnore]
        public string OCRResultFull
        {
            get => _ocrResultFull;
            set
            {
                if (_ocrResultFull != value)
                {
                    _ocrResultFull = value;
                    OnPropertyChanged(nameof(OCRResultFull));
                    OnPropertyChanged(nameof(OCRResultPreview));
                }
            }
        }

        /// <summary>
        /// 获取结果预览文本（超过100字符时截断显示，点击可预览全文）
        /// </summary>
        [JsonIgnore]
        public string OCRResultPreview => Utils.TextHelper.Truncate(OCRResultFull);

        #region 命令

        [JsonIgnore]
        private ICommand selectRegionCommand;
        [JsonIgnore]
        public ICommand SelectRegionCommand => selectRegionCommand ??= new RelayCommand(SelectRegion);

        [JsonIgnore]
        private ICommand testOCRCommand;
        [JsonIgnore]
        public ICommand TestOCRCommand => testOCRCommand ??= new RelayCommand(TestOCR);

        [JsonIgnore]
        private ICommand selectTextPointCommand;
        [JsonIgnore]
        public ICommand SelectTextPointCommand => selectTextPointCommand ??= new RelayCommand(SelectTextPoint);

        [JsonIgnore]
        private ICommand previewResultCommand;
        /// <summary>点击截断的结果文本时预览完整内容</summary>
        [JsonIgnore]
        public ICommand PreviewResultCommand => previewResultCommand ??= new RelayCommand(PreviewResult);

        #endregion

        #region 构造

        public GetInputStep() : base()
        {
            Type = StepType.GetInput;
        }

        public GetInputStep(string name) : base()
        {
            Type = StepType.GetInput;
            Name = name;
        }

        public GetInputStep(string name, string description) : base()
        {
            Type = StepType.GetInput;
            Name = name;
            Description = description;
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            return new GetInputStep(Name, Description)
            {
                InputMode = InputMode,
                OCRRegion = new Rect(OCRRegion.X, OCRRegion.Y, OCRRegion.Width, OCRRegion.Height),
                TextPoint = new System.Windows.Point(TextPoint.X, TextPoint.Y),
                WaitTime = WaitTime,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            string text;

            if (InputMode == GetInputMode.ScreenText)
            {
                // 读取屏幕指定位置的可选择文本
                text = await Task.Run(() =>
                {
                    double dpiX = OCRService.CachedDpiX;
                    double dpiY = OCRService.CachedDpiY;
                    return Utils.ScreenTextReader.ReadTextAtPoint(TextPoint.X * dpiX, TextPoint.Y * dpiY);
                }, cancellationToken);
            }
            else
            {
                // OCR 模式
                if (OCRRegion == null || OCRRegion.IsEmpty || OCRRegion.Width <= 0 || OCRRegion.Height <= 0)
                {
                    IsError = true;
                    ErrorType = StepErrorType.OCRError;
                    ErrorMessage = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择OCR区域");
                    IsTrue = false;
                    return false;
                }

                // 如果设置开启，在屏幕上标示 OCR 区域
                if (Utils.SingletonLocator.Settings.Step.ShowOCRRegionOnRun)
                {
                    var overlay = Utils.SingletonLocator.Settings.Step.OCRRegionOverlay;
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                        WindowRegionOverlay.ShowRegion(OCRRegion, overlay.Color, overlay.Duration));
                }

                text = await Task.Run(() =>
                {
                    return OCRService.RecognizeRegion(OCRRegion);
                }, cancellationToken);
            }

            // 存储结果
            OCRResultFull = text;
            LastResult = new StepExecutionResult
            {
                IsTrue = !string.IsNullOrWhiteSpace(text),
                OCRText = text,
                ExecutedAt = DateTime.Now,
            };

            IsTrue = !string.IsNullOrWhiteSpace(text);
            IsError = false;
            ErrorType = StepErrorType.None;

            return IsTrue;
        }

        #region 私有方法

        private void SelectRegion()
        {
            var region = WindowEditOCR.ShowAndSelect();
            if (!region.IsEmpty)
            {
                OCRRegion = region;
                OnPropertyChanged(nameof(OCRRegionText));
            }
        }

        private void SelectTextPoint()
        {
            var point = WindowSelectPoint.ShowAndSelect();
            if (point.HasValue)
            {
                TextPoint = point.Value;
            }
        }

        private void PreviewResult()
        {
            if (string.IsNullOrEmpty(OCRResultFull)) return;
            WindowTextPreview.Show(Name, OCRResultFull);
        }

        private void TestOCR()
        {
            try
            {
                if (InputMode == GetInputMode.ScreenText)
                {
                    double dpiX = OCRService.CachedDpiX;
                    double dpiY = OCRService.CachedDpiY;
                    string screenText = Utils.ScreenTextReader.ReadTextAtPoint(TextPoint.X * dpiX, TextPoint.Y * dpiY);
                    OCRResultFull = string.IsNullOrWhiteSpace(screenText)
                        ? LanguageService.GetLocalizedString("OCR_NoResult", "未读取到文本")
                        : screenText;
                    return;
                }

                if (OCRRegion.IsEmpty || OCRRegion.Width <= 0 || OCRRegion.Height <= 0)
                {
                    OCRResultFull = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择区域");
                    return;
                }

                // 标示 OCR 区域
                if (Utils.SingletonLocator.Settings.Step.ShowOCRRegionOnRun)
                {
                    var overlay = Utils.SingletonLocator.Settings.Step.OCRRegionOverlay;
                    WindowRegionOverlay.ShowRegion(OCRRegion, overlay.Color, overlay.Duration);
                }

                string text = OCRService.RecognizeRegion(OCRRegion);
                OCRResultFull = string.IsNullOrWhiteSpace(text)
                    ? LanguageService.GetLocalizedString("OCR_NoResult", "未识别到文本")
                    : text;
            }
            catch (Exception ex)
            {
                OCRResultFull = $"Error: {ex.Message}";
                _logger.Error(ex, "Test OCR failed");
            }
        }

        #endregion
    }
}
