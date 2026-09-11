using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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
        private ObservableCollection<TextPointItem> _textPoints = new();
        private bool _textPointsMigrated;
        private double _timeout = 3;

        /// <summary>
        /// 获取输入方式
        /// </summary>
        public GetInputMode InputMode { get => _inputMode; set => SetProperty(ref _inputMode, value); }

        /// <summary>所有获取方式（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public List<GetInputMode> InputModes { get; } = new() { GetInputMode.OCR, GetInputMode.ScreenText };

        /// <summary>
        /// 获取输入的超时时间（秒），小于等于 0 表示不限制。
        /// UI Automation 与 OCR 均为同步调用，目标程序无响应或识别区域过大时可能长时间阻塞，
        /// 超时后本步骤按执行超时处理，避免整条流程被单步卡住。
        /// </summary>
        public double Timeout { get => _timeout; set => SetProperty(ref _timeout, value); }

        /// <summary>
        /// ScreenText 模式下的目标位置（旧版单点，仅用于兼容迁移；新数据存 TextPoints）
        /// </summary>
        public System.Windows.Point TextPoint { get => _textPoint; set { if (SetProperty(ref _textPoint, value)) OnPropertyChanged(nameof(TextPointText)); } }

        /// <summary>
        /// ScreenText 模式的文本获取点列表：按顺序逐点获取，结果以换行合并。
        /// 支持绝对坐标与图像相对定位（同点击图像步骤的裁剪+点击点方式）
        /// </summary>
        public ObservableCollection<TextPointItem> TextPoints
        {
            get
            {
                // 兼容旧数据：首次访问时将旧版单点 TextPoint 迁移为列表项
                if (!_textPointsMigrated)
                {
                    _textPointsMigrated = true;
                    if (_textPoints.Count == 0 && _textPoint != new System.Windows.Point(0, 0))
                    {
                        _textPoints.Add(new TextPointItem { Source = TextPointSource.Absolute, X = _textPoint.X, Y = _textPoint.Y });
                        _textPoint = new System.Windows.Point(0, 0);
                    }
                }
                return _textPoints;
            }
            set => SetProperty(ref _textPoints, value);
        }

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
        /// <summary>为指定获取点点选屏幕绝对坐标（参数：TextPointItem）</summary>
        [JsonIgnore]
        public ICommand SelectTextPointCommand => selectTextPointCommand ??= new RelayCommand<object>(SelectTextPoint, null);

        [JsonIgnore]
        private ICommand addTextPointCommand;
        /// <summary>添加一个文本获取点（默认绝对坐标）</summary>
        [JsonIgnore]
        public ICommand AddTextPointCommand => addTextPointCommand ??= new RelayCommand(() => TextPoints.Add(new TextPointItem()));

        [JsonIgnore]
        private ICommand removeTextPointCommand;
        /// <summary>删除指定获取点（参数：TextPointItem）</summary>
        [JsonIgnore]
        public ICommand RemoveTextPointCommand => removeTextPointCommand ??= new RelayCommand<object>(p => { if (p is TextPointItem item) TextPoints.Remove(item); }, null);

        [JsonIgnore]
        private ICommand selectPointImageCommand;
        /// <summary>为相对定位获取点选择原图并打开图片编辑窗口（参数：TextPointItem）</summary>
        [JsonIgnore]
        public ICommand SelectPointImageCommand => selectPointImageCommand ??= new RelayCommand<object>(SelectPointImage, null);

        [JsonIgnore]
        private ICommand editPointImageCommand;
        /// <summary>打开相对定位获取点的图片编辑窗口（参数：TextPointItem）</summary>
        [JsonIgnore]
        public ICommand EditPointImageCommand => editPointImageCommand ??= new RelayCommand<object>(EditPointImage, null);

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
            var clone = new GetInputStep(Name, Description)
            {
                InputMode = InputMode,
                OCRRegion = new Rect(OCRRegion.X, OCRRegion.Y, OCRRegion.Width, OCRRegion.Height),
                TextPoint = new System.Windows.Point(TextPoint.X, TextPoint.Y),
                WaitTime = WaitTime,
                Timeout = Timeout,
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new(Conditions),
            };
            foreach (var p in TextPoints)
                clone.TextPoints.Add(p.Clone());
            return clone;
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            string text;

            if (InputMode == GetInputMode.ScreenText)
            {
                // 按顺序逐点读取文本（绝对坐标 / 图像相对定位），结果以换行合并
                if (TextPoints.Count == 0)
                {
                    IsError = true;
                    ErrorType = StepErrorType.OCRError;
                    ErrorMessage = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择位置");
                    IsTrue = false;
                    return false;
                }

                try
                {
                    // UI Automation 调用为同步阻塞，施加超时避免目标程序无响应时长时间卡住
                    text = await RunWithTimeoutAsync(
                        token => ReadScreenTextsAsync(token), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // 用户停止运行时保持取消语义，交由执行引擎处理
                    throw;
                }
                catch (TimeoutException)
                {
                    // 超时按执行错误处理，交由执行引擎统一记录为 ExecutionTimeout
                    throw;
                }
                catch (Exception ex)
                {
                    IsError = true;
                    ErrorType = StepErrorType.ImageNotFound;
                    ErrorMessage = ex.Message;
                    IsTrue = false;
                    return false;
                }
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

                // OCR 识别可能因首次初始化引擎或区域过大而耗时，施加超时
                text = await RunWithTimeoutAsync(
                    token => Task.Run(() => OCRService.RecognizeRegion(OCRRegion), token),
                    cancellationToken);
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

        /// <summary>
        /// 在 <see cref="Timeout"/> 秒内等待获取操作完成；超时抛出带本地化消息的 <see cref="TimeoutException"/>。
        /// Timeout 小于等于 0 时不限制超时。
        /// 说明：UI Automation 与 OCR 属于同步调用，无法被强制中断，超时后其后台任务仍会继续执行，
        /// 但本步骤会立即结束，避免整条流程被单步卡住。
        /// </summary>
        private async Task<string> RunWithTimeoutAsync(Func<CancellationToken, Task<string>> operation, CancellationToken cancellationToken)
        {
            if (Timeout <= 0)
                return await operation(cancellationToken);

            var timeout = System.TimeSpan.FromSeconds(Timeout);
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            var operationTask = operation(timeoutCts.Token);
            // 计时任务只跟随外部取消令牌，确保外部取消与超时能够区分
            var timeoutTask = Task.Delay(timeout, cancellationToken);

            var completedTask = await Task.WhenAny(operationTask, timeoutTask);
            if (completedTask != operationTask)
            {
                // 外部取消优先于超时
                cancellationToken.ThrowIfCancellationRequested();
                ObserveFault(operationTask);
                throw CreateTimeoutException();
            }

            try
            {
                return await operationTask;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // 操作因内部超时令牌被取消
                throw CreateTimeoutException();
            }
        }

        /// <summary>创建带本地化消息的获取输入超时异常</summary>
        private TimeoutException CreateTimeoutException()
        {
            string message = string.Format(
                LanguageService.GetLocalizedString("GetInput_Timeout", "Get input timed out ({0}s)"),
                Timeout);
            return new TimeoutException(message);
        }

        /// <summary>观察被放弃任务的异常，避免产生未观测的任务异常</summary>
        private static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(
                t => { _ = t.Exception; },
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private void SelectRegion()
        {
            var region = WindowEditOCR.ShowAndSelect();
            if (!region.IsEmpty)
            {
                OCRRegion = region;
                OnPropertyChanged(nameof(OCRRegionText));
            }
        }

        private void SelectTextPoint(object parameter)
        {
            if (parameter is not TextPointItem item) return;
            var point = WindowSelectPoint.ShowAndSelect();
            if (point.HasValue)
            {
                item.X = point.Value.X;
                item.Y = point.Value.Y;
            }
        }

        private void SelectPointImage(object parameter)
        {
            if (parameter is not TextPointItem item) return;

            var title = LanguageService.GetLocalizedString("Select_target_pic", "Open Image File");
            var filter = LanguageService.GetLocalizedString("Image_File", "Image Files") + "(*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg";
            string imagePath = PathServices.OpenPathDialog(title, filter);
            if (string.IsNullOrEmpty(imagePath)) return;

            // 重新选图后重置裁剪与点击点状态
            item.ImagePath = imagePath;
            item.CroppedImageName = null;
            item.CroppedRect = new Rect();
            item.ClickOffsets = new List<Models.Point>();
            OpenPointImageEditor(item);
        }

        private void EditPointImage(object parameter)
        {
            if (parameter is TextPointItem item)
                OpenPointImageEditor(item);
        }

        /// <summary>打开图片编辑窗口：裁剪识别区域并设置点击点（同点击图像步骤）</summary>
        private void OpenPointImageEditor(TextPointItem item)
        {
            // 优先原路径，失效时回退到包内打包的原图；并回填解析后的路径供下次使用
            string srcPath = item.ResolveSourceImagePath();
            if (string.IsNullOrEmpty(srcPath)) return;
            item.ImagePath = srcPath;

            var imgSrc = LoadFrozenBitmap(srcPath);
            if (imgSrc == null) return;

            var win = new WindowEditImage();
            win.Show();
            // 使用 Background 优先级，确保裁剪控件完成内部布局和渲染后再设置图片
            win.Dispatcher.BeginInvoke(new Action(() =>
            {
                win.editImageViewModel.ImgSrc = imgSrc;
                win.UpdateLayout();
                if (!item.CroppedRect.IsEmpty)
                {
                    win.editImageViewModel.SetCropRect(item.CroppedRect);
                    win.editImageViewModel.CropRect = item.CroppedRect;
                    // 还原已保存的点击点（偏移 → 原图坐标的缩略图位置）
                    if (item.ClickOffsets != null && item.ClickOffsets.Count > 0)
                    {
                        var thumbs = item.ClickOffsets
                            .Select(o => new ClickThumb(o.X + item.CroppedRect.X - 10, o.Y + item.CroppedRect.Y - 10, 20, Visibility.Visible))
                            .ToList();
                        win.editImageViewModel.SetThumbs(thumbs);
                    }
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
            win.editImageViewModel.OnImageSaved += (img, rect, thumbs, ocrRect) => SavePointImage(item, img, rect, thumbs);
        }

        /// <summary>保存裁剪图到工作目录 images/{Id}.png，并记录裁剪区与点击点偏移</summary>
        private void SavePointImage(TextPointItem item, System.Windows.Media.ImageSource img, Rect rect, List<ClickThumb> thumbs)
        {
            if (img == null) return;
            try
            {
                string workDir = Utils.SingletonLocator.Main?.StepImageWorkDir;
                if (string.IsNullOrEmpty(workDir))
                {
                    workDir = Path.Combine(Path.GetTempPath(), "AutoShaoLu", "images");
                    Utils.SingletonLocator.Main.StepImageWorkDir = workDir;
                }
                Directory.CreateDirectory(Path.Combine(workDir, "images"));

                string fileName = $"{item.Id}.png";
                string fullPath = Path.Combine(workDir, "images", fileName);
                if (img is System.Windows.Media.Imaging.BitmapSource bs)
                {
                    var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    using (var fs = new FileStream(fullPath, FileMode.Create))
                    {
                        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bs));
                        encoder.Save(fs);
                    }
                }

                item.CroppedImageName = $"images/{fileName}";
                item.CroppedRect = rect;
                // 点击点转成相对裁剪区左上角的偏移；未设点击点时取裁剪图中心
                item.ClickOffsets = thumbs != null && thumbs.Count > 0
                    ? thumbs.Select(t => t.ClickPoint - new Models.Point((int)rect.X, (int)rect.Y)).ToList()
                    : new List<Models.Point> { new Models.Point((int)(rect.Width / 2), (int)(rect.Height / 2)) };
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "SavePointImage failed");
            }
        }

        private System.Windows.Media.ImageSource LoadFrozenBitmap(string path)
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();
                return bitmap;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "LoadFrozenBitmap failed: {0}", path);
                return null;
            }
        }

        /// <summary>
        /// 按 TextPoints 顺序逐点获取文本：绝对坐标直接读取；相对定位先匹配图像再按偏移读取。结果以换行合并。
        /// 相对定位图像未找到时抛出异常。
        /// </summary>
        private async Task<string> ReadScreenTextsAsync(CancellationToken cancellationToken)
        {
            double dpiX = OCRService.CachedDpiX;
            double dpiY = OCRService.CachedDpiY;
            var results = new List<string>();

            foreach (var item in TextPoints)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (item.Source == TextPointSource.Absolute)
                {
                    string text = await Task.Run(
                        () => Utils.ScreenTextReader.ReadTextAtPoint(item.X * dpiX, item.Y * dpiY),
                        cancellationToken);
                    results.Add(text);
                    continue;
                }

                // 相对定位：匹配裁剪图后按点击点偏移读取（物理像素）
                string fullPath = item.CroppedImageFullPath;
                if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath))
                    throw new Exception(LanguageService.GetLocalizedString("No_Cropimage_Warning", "No Croped picture"));

                string relText = await Task.Run(() =>
                {
                    using (var template = new System.Drawing.Bitmap(fullPath))
                    {
                        // 单点匹配超时同样使用步骤配置的超时时间（<=0 时保持原默认 3s）
                        double searchTimeout = Timeout > 0 ? Timeout : 3;
                        var rect = Utils.Autogui.FindImageOnScreen(template, item.SimilarityThreshold, 0.1, searchTimeout);
                        var offsets = item.ClickOffsets != null && item.ClickOffsets.Count > 0
                            ? item.ClickOffsets
                            : new List<Models.Point> { new Models.Point(template.Width / 2, template.Height / 2) };

                        var sb = new System.Text.StringBuilder();
                        foreach (var off in offsets)
                        {
                            string s = Utils.ScreenTextReader.ReadTextAtPoint(rect.LeftTop.X + off.X, rect.LeftTop.Y + off.Y);
                            if (sb.Length > 0) sb.AppendLine();
                            sb.Append(s);
                        }
                        return sb.ToString();
                    }
                }, cancellationToken);
                results.Add(relText);
            }

            return string.Join("\r\n", results);
        }

        private void PreviewResult()
        {
            if (string.IsNullOrEmpty(OCRResultFull)) return;
            WindowTextPreview.Show(Name, OCRResultFull);
        }

        private async void TestOCR()
        {
            try
            {
                if (InputMode == GetInputMode.ScreenText)
                {
                    if (TextPoints.Count == 0)
                    {
                        OCRResultFull = LanguageService.GetLocalizedString("OCR_NoRegion", "未选择位置");
                        return;
                    }
                    // 必须异步等待：在 UI 线程上 GetAwaiter().GetResult() 会与回到调度器的延续死锁，导致界面卡死
                    string screenText = await RunWithTimeoutAsync(
                        token => ReadScreenTextsAsync(token), CancellationToken.None);
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

                string text = await RunWithTimeoutAsync(
                    token => Task.Run(() => OCRService.RecognizeRegion(OCRRegion), token), CancellationToken.None);
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
