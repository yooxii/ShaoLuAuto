using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Views;
using System;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// TextOCR 步骤：从屏幕指定区域执行 OCR 识别
    /// </summary>
    public class TextOCRStep : AutomationStepBase
    {
        private Rect _ocrRegion = Rect.Empty;
        private string _ocrResultPreview;

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
        /// OCR 识别结果预览（运行时）
        /// </summary>
        [JsonIgnore]
        public string OCRResultPreview
        {
            get => _ocrResultPreview;
            set => SetProperty(ref _ocrResultPreview, value);
        }

        #region 命令

        [JsonIgnore]
        private ICommand selectRegionCommand;
        [JsonIgnore]
        public ICommand SelectRegionCommand => selectRegionCommand ??= new RelayCommand(SelectRegion);

        [JsonIgnore]
        private ICommand testOCRCommand;
        [JsonIgnore]
        public ICommand TestOCRCommand => testOCRCommand ??= new RelayCommand(TestOCR);

        #endregion

        #region 构造

        public TextOCRStep() : base()
        {
            Type = StepType.TextOCR;
        }

        public TextOCRStep(string name) : base()
        {
            Type = StepType.TextOCR;
            Name = name;
        }

        public TextOCRStep(string name, string description) : base()
        {
            Type = StepType.TextOCR;
            Name = name;
            Description = description;
        }

        #endregion

        public override AutomationStepBase Clone()
        {
            return new TextOCRStep(Name, Description)
            {
                OCRRegion = new Rect(OCRRegion.X, OCRRegion.Y, OCRRegion.Width, OCRRegion.Height),
                WaitTime = WaitTime,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            if (OCRRegion.IsEmpty || OCRRegion.Width <= 0 || OCRRegion.Height <= 0)
            {
                IsError = true;
                ErrorType = StepErrorType.OCRError;
                ErrorMessage = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择OCR区域");
                IsTrue = false;
                return false;
            }

            string text = await Task.Run(() =>
            {
                return OCRService.RecognizeRegion(OCRRegion);
            }, cancellationToken);

            // 存储结果
            OCRResultPreview = text;
            LastResult = new StepExecutionResult
            {
                IsTrue = !string.IsNullOrWhiteSpace(text),
                OCRText = text,
                ExecutedAt = DateTime.Now,
            };

            IsTrue = !string.IsNullOrWhiteSpace(text);
            IsError = false;
            ErrorType = StepErrorType.None;

            // 日志记录
            if (EnableLog)
            {
                string fileName = System.IO.Path.GetFileNameWithoutExtension(
                    Utils.SingletonLocator.Main.StepFilePath ?? "unsaved");
                ExecutionLogService.Log(Uid, fileName, Name, text ?? string.Empty);
            }

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

        private void TestOCR()
        {
            if (OCRRegion.IsEmpty || OCRRegion.Width <= 0 || OCRRegion.Height <= 0)
            {
                OCRResultPreview = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择区域");
                return;
            }

            try
            {
                string text = OCRService.RecognizeRegion(OCRRegion);
                OCRResultPreview = string.IsNullOrWhiteSpace(text)
                    ? LanguageService.GetLocalizedString("OCR_NoResult", "未识别到文本")
                    : text;
            }
            catch (Exception ex)
            {
                OCRResultPreview = $"Error: {ex.Message}";
                _logger.Error(ex, "Test OCR failed");
            }
        }

        #endregion
    }
}
