using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Expression.Drawing.Core;
using NLog;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
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
using System.Windows.Forms;

namespace ShaoLu.Viewmodels.AutomationStep
{
    // 基类
    public abstract class AutomationStepBase : ObservableObject
    {
        internal readonly Logger _logger = LogManager.GetCurrentClassLogger();

        #region 属性

        private bool _isNeed = true;
        private bool _isSave = false;
        private bool _isError = false;
        private string _errorMessage;
        private int _lineNo;
        private string _name;
        private string _description;
        private StepType _type;
        private bool _isTrue = false;
        private AutomationStepBase _trueGoto;
        private AutomationStepBase _falseGoto;
        private double _waitTime = 0.1;


        public bool IsNeed
        {
            get => _isNeed;
            set => SetProperty(ref _isNeed, value);
        }

        private readonly Guid _uid = Guid.NewGuid();
        /// <summary>
        /// 步骤的唯一uid
        /// </summary>
        public Guid Uid => _uid;

        public bool IsSave { get => _isSave; set => SetProperty(ref _isSave, value); }

        public bool IsError { get => _isError; set => SetProperty(ref _isError, value); }

        public string ErrorMessage { get => _errorMessage; set => SetProperty(ref _errorMessage, value); }

        /// <summary>
        /// 步骤行号。
        /// 注意：此值应由包含该步骤的集合（如 ObservableCollection）在增删改时统一维护，
        /// 或者在 UI 绑定时通过 Index 计算。此处保留 SetProperty 以支持手动刷新。
        /// </summary>
        public int LineNo { get => _lineNo; set => SetProperty(ref _lineNo, value); }

        /// <summary>
        /// 步骤名称
        /// </summary>
        public string Name
        {
            get => _name;
            set
            {
                // 简单的防御性编程，防止 Null 导致绑定崩溃
                if (value == null) { IsError = true; throw new ArgumentNullException(nameof(Name)); }
                SetProperty(ref _name, value);
            }
        }

        /// <summary>
        /// 步骤描述
        /// </summary>
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        /// <summary>
        /// 步骤类型
        /// </summary>
        public StepType Type { get => _type; set => SetProperty(ref _type, value); }

        public bool IsTrue { get => _isTrue; set => SetProperty(ref _isTrue, value); }


        /// <summary>
        /// 如果真,去执行某行
        /// </summary>
        public AutomationStepBase TrueGoto { get => _trueGoto; set => SetProperty(ref _trueGoto, value); }
        public AutomationStepBase FalseGoto { get => _falseGoto; set => SetProperty(ref _falseGoto, value); }

        public double WaitTime { get => _waitTime; set => SetProperty(ref _waitTime, value); }

        #endregion

        /// <summary>
        /// 构造函数
        /// 确保创建时即分配唯一 ID 和默认值
        /// </summary>
        protected AutomationStepBase()
        {
            this.LineNo = 0;
            this.Name = string.Empty;
            this.Description = string.Empty;
        }

        /// <summary>
        /// 带参构造函数（方便测试和初始化）
        /// </summary>
        protected AutomationStepBase(string name, StepType type) : this()
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.Type = type;
        }

        public abstract AutomationStepBase Clone();

        public abstract Task<bool> RunAsync(CancellationToken cancellationToken = default);
        public bool Run()
        {
            return RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
        // 其他公共属性...
    }


    // 输入文字步骤
    public class TypeTextStep : AutomationStepBase
    {
        private string _textToType;
        private double _delayBetweenKeys = 0.05;

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        #region 构造
        public TypeTextStep() : base()
        {
            Type = StepType.TypeText;
        }
        public TypeTextStep(string name) : base()
        {
            Type = StepType.TypeText;
            Name = name;
        }
        public TypeTextStep(string name, string description) : base()
        {
            Type = StepType.TypeText;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextStep(Name, Description)
            {
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys,
                WaitTime = WaitTime,
            };
        }
        #endregion

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var res = await Task.Run(() =>
            {
                Thread.Sleep((int)WaitTime * 1000);
                if (DelayBetweenKeys < 0.01)
                {
                    return Autogui.TypeTextSafe(TextToType);
                }
                else
                {
                    return Autogui.TypeText(TextToType, (int)(DelayBetweenKeys * 1000));
                }
            });
            IsTrue = res;
            IsError = false;
            return res;
        }
    }

    public class TypeTextMoreStep : AutomationStepBase
    {
        readonly FileServices fileServices = SingletonLocator.FileServices;

        private string _filePath;
        private string _textToType;
        private double _delayBetweenKeys = 0.01;
        private string _prefix;
        private string _infix;
        private string _suffix;
        private int _index = 0;
        private ObservableCollection<string> _contents = [];
        private ObservableCollection<string> _previewContents = [];
        private string _delimiter = "\n,\r,\n\r,";


        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        public string Prefix { get => _prefix; set => SetProperty(ref _prefix, value); }

        public string Infix { get => _infix; set => SetProperty(ref _infix, value); }

        public string Suffix { get => _suffix; set => SetProperty(ref _suffix, value); }

        public int Index { get => _index; set => SetProperty(ref _index, value); }

        /// <summary>
        /// 待输入内容
        /// </summary>
        public ObservableCollection<string> Contents { get => _contents; set => SetProperty(ref _contents, value); }

        /// <summary>
        /// 待输入内容的预览
        /// </summary>
        public ObservableCollection<string> PreviewContents { get => _previewContents; set => SetProperty(ref _previewContents, value); }

        /// <summary>
        /// 分割符
        /// </summary>
        public string Delimiter { get => _delimiter; set => SetProperty(ref _delimiter, value); }

        #region 构造
        public TypeTextMoreStep() : base()
        {
            Type = StepType.TypeTextMore;
        }
        public TypeTextMoreStep(string name) : base()
        {
            Type = StepType.TypeTextMore;
            Name = name;
        }
        public TypeTextMoreStep(string name, string description) : base()
        {
            Type = StepType.TypeTextMore;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextMoreStep(Name, Description)
            {
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys
            };
        }
        #endregion

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            Thread.Sleep((int)WaitTime * 1000);
            var res = await Task.Run(() =>
            {
                return Autogui.TypeTextSafe(TextToType, (int)(DelayBetweenKeys * 1000));
            });
            IsTrue = res;
            IsError = false;
            return res;
        }
    }

    public partial class TypeTextFromFileStep : AutomationStepBase
    {
        readonly FileServices fileServices = SingletonLocator.FileServices;

        private string _filePath;
        private string _textToType;
        private double _delayBetweenKeys = 0.01;
        private int _index = 0;
        private ObservableCollection<string> _contents = [];
        private ObservableCollection<string> _previewContents = [];
        private string[] _delimiter = ["\n", "\r", "\n\r", "\t", ",", ";", "|"];


        public string FilePath { get => _filePath; set => SetProperty(ref _filePath, value); }

        /// <summary>
        /// 输入内容
        /// </summary>
        public string TextToType { get => _textToType; set => SetProperty(ref _textToType, value); }

        public double DelayBetweenKeys { get => _delayBetweenKeys; set => SetProperty(ref _delayBetweenKeys, value); }

        public int Index { get => _index; set => SetProperty(ref _index, value); }

        /// <summary>
        /// 待输入内容
        /// </summary>
        public ObservableCollection<string> Contents { get => _contents; set => SetProperty(ref _contents, value); }

        /// <summary>
        /// 待输入内容的预览
        /// </summary>
        public ObservableCollection<string> PreviewContents { get => _previewContents; set => SetProperty(ref _previewContents, value); }

        /// <summary>
        /// 分割符
        /// </summary>
        [JsonIgnore]
        public string[] Delimiter { get => _delimiter; set => SetProperty(ref _delimiter, value); }



        #region 构造
        public TypeTextFromFileStep() : base()
        {
            Type = StepType.TypeTextFromFile;
        }
        public TypeTextFromFileStep(string name) : base()
        {
            Type = StepType.TypeTextFromFile;
            Name = name;
        }
        public TypeTextFromFileStep(string name, string description) : base()
        {
            Type = StepType.TypeTextFromFile;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new TypeTextFromFileStep(Name, Description)
            {
                FilePath = FilePath,
                TextToType = TextToType,
                DelayBetweenKeys = DelayBetweenKeys,
                Contents = Contents,
                PreviewContents = PreviewContents,
            };
        }
        #endregion


        [RelayCommand]
        private void OpenFile()
        {
            var path = PathServices.OpenPathDialog(LanguageService.GetLocalizedString("OpenFile"), "All File|*.*|Text|*.txt;*.csv|Xlsx|*.xlsx");
            if (path != null) FilePath = path;
            LoadFile();
        }

        public void Increment()
        {

        }

        private void LoadFile()
        {
            string res;
            if (new List<string> { ".txt", ".csv", ".json" }.Contains(Path.GetExtension(FilePath).ToLower()))
            {
                res = fileServices.SmartReadTextFile(FilePath);
            }
            //else if (Path.GetExtension(FilePath).ToLower() == "xlsx")
            //{

            //}
            else
            {
                throw new Exception("No support file type.");
            }
            Contents.Clear();
            PreviewContents.Clear();
            Contents.AddRange(res.Split(Delimiter, StringSplitOptions.RemoveEmptyEntries).ToList());
            PreviewContents.AddRange(Contents.Take(Contents.Count > 10 ? 10 : Contents.Count)); //TODO:
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            if (Contents != null && Contents.Count > 0)
            {
                if (Index >= Contents.Count)
                {
                    IsTrue = false;
                    IsError = true;
                    throw new InvalidOperationException($"{Name}'s Contents is Finished.");
                }
                TextToType = Contents[Index];
                Index++;
            }
            var res = await Task.Run(() =>
            {
                Thread.Sleep((int)WaitTime * 1000);
                return Autogui.TypeTextSafe(TextToType, (int)(DelayBetweenKeys * 1000));
            });
            IsTrue = res;
            IsError = false;
            return res;
        }
    }


    // 弹出框步骤
    public class PopupStep : AutomationStepBase
    {
        private string _title;
        public string Title { get => _title; set => SetProperty(ref _title, value); }


        private string _popupText;
        public string PopupText { get => _popupText; set => SetProperty(ref _popupText, value); }

        private FontModel _popupFont = new()
        {
            FontFamily = "Arial",
            FontSize = 14,
            FontWeight = FontWeights.Regular,
            FontStyle = FontStyles.Normal,
            FontColor = 0x000000,
        };
        public FontModel PopupFont { get => _popupFont; set => SetProperty(ref _popupFont, value); }


        private string _popupType = "Information";
        public string PopupType { get => _popupType; set => SetProperty(ref _popupType, value); }

        private PopupButtons _popupButtons = PopupButtons.OK;
        public PopupButtons PopupButtons { get => _popupButtons; set => SetProperty(ref _popupButtons, value); }

        #region 命令

        [JsonIgnore]
        public List<string> PopupTypes { get; set; } = ["Information", "Warning", "Error", "Question"];

        [JsonIgnore]
        private RelayCommand fontSelectCommand;
        [JsonIgnore]
        public RelayCommand FontSelectCommand => fontSelectCommand ??= new RelayCommand(FontSelect);

        [JsonIgnore]
        private RelayCommand colorSelectCommand;
        [JsonIgnore]
        public RelayCommand ColorSelectCommand => colorSelectCommand ??= new RelayCommand(ColorSelect);

        [JsonIgnore]
        private RelayCommand addButtonCommand;
        [JsonIgnore]
        public RelayCommand AddButtonCommand => addButtonCommand ??= new RelayCommand(AddButton);

        [JsonIgnore]
        private RelayCommand delButtonCommand;
        [JsonIgnore]
        public RelayCommand DelButtonCommand => delButtonCommand ??= new RelayCommand(DelButton);

        #endregion

        public PopupStep() : base()
        {
            this.Type = StepType.Popup;
        }
        public PopupStep(string name) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Title = name;
        }
        public PopupStep(string name, string description) : base()
        {
            Type = StepType.Popup;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new PopupStep(Name, Description)
            {
                IsTrue = IsTrue,
                TrueGoto = TrueGoto,
                FalseGoto = FalseGoto,
                Title = Title,
                PopupText = PopupText,
                PopupFont = PopupFont,
                PopupType = PopupType,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            var iconType = PopupType switch
            {
                "Information" => MessageBoxImage.Information,
                "Warning" => MessageBoxImage.Warning,
                "Error" => MessageBoxImage.Error,
                "Question" => MessageBoxImage.Question,
                _ => MessageBoxImage.Information
            };

            // 1. 启动异步弹窗任务
            var (popupWindow, popupTask) = WindowAsyncPopup.Show(PopupText, Title, PopupFont, PopupButtons, iconType);

            // 2. 等待任务完成或被取消
            try
            {
                // 将 CancellationToken 注册到 Task
                using (cancellationToken.Register(() =>
                {
                    // 当取消发生时，在 UI 线程关闭窗口
                    // 检查窗口是否仍然存在且未关闭
                    if (popupWindow != null && !popupWindow.Dispatcher.HasShutdownStarted)
                    {
                        popupWindow.Dispatcher.InvokeAsync(() =>
                        {
                            if (popupWindow.IsVisible)
                            {
                                popupWindow.Close();
                            }
                        });
                    }
                }))
                {
                    var cancelTask = new TaskCompletionSource<bool>();
                    using (cancellationToken.Register(() => cancelTask.TrySetResult(true)))
                    {
                        var completedTask = await Task.WhenAny(popupTask, cancelTask.Task);

                        if (completedTask == cancelTask.Task)
                        {
                            // 用户点击了停止
                            IsTrue = false;
                            return false;
                        }

                        // 用户点击了弹窗按钮
                        var result = await popupTask;
                        IsTrue = (result == PopupButton.Yes.Value);
                        return IsTrue;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Popup error: {ex.Message}");
                IsTrue = false;
                return IsTrue;
            }
        }

        private void FontSelect()
        {
            PopupFont ??= new();
            var fontDialog = new FontDialog()
            {
                Font = PopupFont?.Font ?? new System.Drawing.Font("Arial", 12)
            };

            if (fontDialog.ShowDialog() == DialogResult.OK)
            {
                // 获取选择的字体信息
                System.Drawing.Font selectedFont = fontDialog.Font;

                // 将 Windows Forms 的字体转换为 WPF 的字体属性
                FontStyle fontStyle = selectedFont.Italic ? FontStyles.Italic : FontStyles.Normal;
                FontWeight fontWeight = selectedFont.Bold ? FontWeights.Bold : FontWeights.Normal;

                // 将选择的字体应用到 WPF 控件上（例如名为 TextBlockSample 的 TextBlock）
                PopupFont.FontFamily = selectedFont.FontFamily.Name;
                PopupFont.FontSize = selectedFont.Size;
                PopupFont.FontStyle = fontStyle;
                PopupFont.FontWeight = fontWeight;

                PopupFont.Style = selectedFont.Style;
                PopupFont.Unit = selectedFont.Unit;
            }
        }

        private void ColorSelect()
        {
            PopupFont ??= new();
            ColorDialog dialog = new()
            {
                Color = System.Drawing.Color.FromArgb(PopupFont.FontColor)
            };
            if (dialog.ShowDialog() == DialogResult.OK)
            {
                PopupFont.FontColor = dialog.Color.ToArgb();
            }
        }

        private void AddButton()
        {
            PopupButtons.Buttons.Add(new PopupButton());
        }

        private void DelButton()
        {
            if (PopupButtons.Buttons.Count > 0)
            {
                PopupButtons.Buttons.RemoveAt(PopupButtons.Buttons.Count - 1);
            }
        }
    }

    public class EmptyStep : AutomationStepBase
    {
        public EmptyStep() : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
        }
        public EmptyStep(string name) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
        }
        public EmptyStep(string name, string description) : base()
        {
            IsTrue = true;
            Type = StepType.Empty;
            Name = name;
            Description = description;
        }

        public override AutomationStepBase Clone()
        {
            return new EmptyStep(Name, Description)
            {
                WaitTime = WaitTime,
            };
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken)
        {
            Thread.Sleep((int)WaitTime * 1000);
            return IsTrue;
        }
    }

}
