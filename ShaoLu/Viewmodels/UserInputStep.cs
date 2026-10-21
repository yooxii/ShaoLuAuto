using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NLog;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 「用户输入」步骤：在打开工程 / 开始运行前弹出窗口，让使用者填写若干字段。
    /// 字段可增删改，值随工程保存；字段值会写入执行上下文，供「上传Acbel」等步骤按字段引用，
    /// 也可在条件判断里用「用户输入字段」引用。
    /// </summary>
    public class UserInputStep : AutomationStepBase
    {
        private ObservableCollection<UserInputField> _fields = new();
        private string _promptTitle = string.Empty;
        private bool _askOnFileOpen = true;
        private bool _askOnRun;

        public UserInputStep() : base()
        {
            Type = StepType.UserInput;
        }

        public UserInputStep(string name) : this()
        {
            Name = name;
        }

        public UserInputStep(string name, string description) : this()
        {
            Name = name;
            Description = description;
        }

        /// <summary>弹窗标题（留空时用「用户输入」）</summary>
        public string PromptTitle
        {
            get => _promptTitle;
            set => SetProperty(ref _promptTitle, value);
        }

        /// <summary>打开工程文件时询问</summary>
        public bool AskOnFileOpen
        {
            get => _askOnFileOpen;
            set => SetProperty(ref _askOnFileOpen, value);
        }

        /// <summary>开始运行前询问</summary>
        public bool AskOnRun
        {
            get => _askOnRun;
            set => SetProperty(ref _askOnRun, value);
        }

        /// <summary>字段集合（可增删改，值随工程保存）</summary>
        public ObservableCollection<UserInputField> Fields
        {
            get => _fields;
            set
            {
                if (ReferenceEquals(_fields, value)) return;
                if (_fields != null)
                    _fields.CollectionChanged -= Fields_CollectionChanged;
                _fields = value ?? new ObservableCollection<UserInputField>();
                _fields.CollectionChanged += Fields_CollectionChanged;
                OnPropertyChanged(nameof(HasFields));
                OnPropertyChanged(nameof(FieldNames));
            }
        }

        private void Fields_CollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            OnPropertyChanged(nameof(HasFields));
            OnPropertyChanged(nameof(FieldNames));
        }

        [JsonIgnore]
        public bool HasFields => Fields != null && Fields.Count > 0;

        /// <summary>已定义字段名（引用菜单/条件面板用）</summary>
        [JsonIgnore]
        public IReadOnlyList<string> FieldNames => Fields?
            .Where(f => f != null && !string.IsNullOrWhiteSpace(f.Name))
            .Select(f => f.Name.Trim())
            .ToList() ?? new System.Collections.Generic.List<string>();

        /// <summary>未填写的必填字段名（为空表示全部满足）</summary>
        [JsonIgnore]
        public IReadOnlyList<string> MissingRequiredFieldNames => Fields?
            .Where(f => f != null && f.IsRequired && string.IsNullOrWhiteSpace(f.Value))
            .Select(f => string.IsNullOrWhiteSpace(f.Name) ? LanguageService.GetLocalizedString("Msg_UnnamedField", "(未命名字段)") : f.Name.Trim())
            .ToList() ?? new System.Collections.Generic.List<string>();

        #region 详情面板命令

        private RelayCommand _addFieldCommand;
        /// <summary>新增一个字段</summary>
        [JsonIgnore]
        public ICommand AddFieldCommand => _addFieldCommand ??= new RelayCommand(() =>
        {
            Fields.Add(new UserInputField { Name = LanguageService.GetLocalizedString("Msg_FieldName", "字段{0}", Fields.Count + 1) });
        });

        private RelayParameterCommand _removeFieldCommand;
        /// <summary>删除指定字段</summary>
        [JsonIgnore]
        public ICommand RemoveFieldCommand => _removeFieldCommand ??= new RelayParameterCommand(p =>
        {
            if (p is UserInputField inputField)
                Fields.Remove(inputField);
        });

        private RelayCommand _askNowCommand;
        /// <summary>立即重新填写（不等到下次打开工程/运行）</summary>
        [JsonIgnore]
        public ICommand AskNowCommand => _askNowCommand ??= new RelayCommand(() =>
        {
            WindowUserInput.ShowAndEdit(new[] { this }, Application.Current?.MainWindow);
        });

        #endregion

        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            if (WaitTime > 0)
                await Task.Delay((int)(WaitTime * 1000), cancellationToken);

            var missing = MissingRequiredFieldNames;
            if (missing.Count > 0)
            {
                IsTrue = false;
                IsError = true;
                ErrorType = StepErrorType.TextEmpty;
                ErrorMessage = string.Format(
                    LanguageService.GetLocalizedString("UserInput_MissingRequired", "必填项未填写：{0}"),
                    string.Join("、", missing));
                return false;
            }

            // 把当前字段值写入执行上下文，供「上传Acbel」与条件判断按字段引用
            try
            {
                StepExecutionContext.Instance.SetUserInput(Uid, Fields);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "UserInput: 写入执行上下文失败");
            }

            IsError = false;
            ErrorType = StepErrorType.None;
            ErrorMessage = null;
            IsTrue = true;
            return true;
        }

        public override AutomationStepBase Clone()
        {
            var clone = new UserInputStep(Name, Description);
            CopyBaseState(clone);
            clone.PromptTitle = PromptTitle;
            clone.AskOnFileOpen = AskOnFileOpen;
            clone.AskOnRun = AskOnRun;
            clone.Fields.Clear();
            if (Fields != null)
            {
                foreach (var field in Fields)
                {
                    if (field != null)
                        clone.Fields.Add(field.Clone());
                }
            }
            return clone;
        }

        /// <summary>复制基类公共状态（各派生步骤 Clone 复用）</summary>
        protected void CopyBaseState(AutomationStepBase clone)
        {
            clone.WaitTime = WaitTime;
            clone.TrueGotoUid = TrueGotoUid;
            clone.FalseGotoUid = FalseGotoUid;
            clone.IsNeed = IsNeed;
            clone.EnableLog = EnableLog;
            clone.SelfReferenceLimit = SelfReferenceLimit;
            clone.ConditionMode = ConditionMode;
            clone.Conditions = new(Conditions);
            clone.IsSave = IsSave;
        }
    }
}
