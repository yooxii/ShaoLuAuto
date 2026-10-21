using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ShaoLu.Models
{
    /// <summary>
    /// 用户输入字段类型（由使用者在步骤详情里配置）
    /// </summary>
    public enum UserInputFieldType
    {
        /// <summary>单行文本</summary>
        Text,
        /// <summary>数字</summary>
        Number,
        /// <summary>日期（yyyy-MM-dd）</summary>
        Date,
        /// <summary>密码（输入时掩码，保存时与普通文本相同）</summary>
        Password,
        /// <summary>下拉选择（候选值在 Options 里，每行一个）</summary>
        Dropdown,
    }

    /// <summary>
    /// 「用户输入」步骤里的一个字段定义：名称、当前值、类型、是否必填。
    /// 值随工程保存；字段名用于被其他步骤（例如「上传Acbel」）按字段引用。
    /// </summary>
    public class UserInputField : ObservableObject
    {
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private string _value = string.Empty;
        private UserInputFieldType _fieldType = UserInputFieldType.Text;
        private string _options = string.Empty;
        private bool _isRequired;
        private string _description = string.Empty;

        /// <summary>字段标识（克隆时保留，便于稳定引用）</summary>
        public Guid Id { get => _id; set => SetProperty(ref _id, value); }

        /// <summary>字段名（同一工程内建议唯一，引用时按名称匹配）</summary>
        public string Name
        {
            get => _name;
            set
            {
                if (SetProperty(ref _name, value))
                    OnPropertyChanged(nameof(DisplayName));
            }
        }

        /// <summary>字段值（随工程保存，打开工程时预填）</summary>
        public string Value
        {
            get => _value;
            set
            {
                // 锁定只保护"已有值"：反序列化/首次填写（当前为空）时仍允许写入，
                // 所以不依赖 IsValueConfirmed 与 Value 的反序列化顺序
                if (IsValueLocked && !string.IsNullOrEmpty(_value))
                {
                    // 冻结字段未登录：拒绝修改（界面层负责提示登录，这里兜底防止绑定/其他路径绕过）
                    LockedEditAttempted?.Invoke(this);
                    OnPropertyChanged(nameof(Value));
                    return;
                }

                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(HasValue));
                    OnPropertyChanged(nameof(IsValueLocked));
                    OnPropertyChanged(nameof(CanEditValue));
                    OnPropertyChanged(nameof(LockHint));
                }
            }
        }

        /// <summary>字段类型</summary>
        public UserInputFieldType FieldType
        {
            get => _fieldType;
            set
            {
                if (SetProperty(ref _fieldType, value))
                {
                    OnPropertyChanged(nameof(IsDropdown));
                    OnPropertyChanged(nameof(IsPassword));
                    OnPropertyChanged(nameof(DropdownOptions));
                }
            }
        }

        /// <summary>下拉候选值（每行一个，仅 Dropdown 使用）</summary>
        public string Options
        {
            get => _options;
            set
            {
                if (SetProperty(ref _options, value))
                    OnPropertyChanged(nameof(DropdownOptions));
            }
        }

        /// <summary>是否必填（运行前未填会判该步骤失败）</summary>
        public bool IsRequired { get => _isRequired; set => SetProperty(ref _isRequired, value); }

        /// <summary>说明（弹窗里显示在输入框下方）</summary>
        public string Description { get => _description; set => SetProperty(ref _description, value); }

        private bool _isFrozen;

        /// <summary>
        /// 冻结字段：第一次确认填写后锁定，未登录时不能再改（需要修改会提示登录）。
        /// </summary>
        public bool IsFrozen
        {
            get => _isFrozen;
            set
            {
                if (SetProperty(ref _isFrozen, value))
                {
                    OnPropertyChanged(nameof(IsValueLocked));
                    OnPropertyChanged(nameof(CanEditValue));
                }
            }
        }

        private bool _isValueConfirmed;

        /// <summary>
        /// 是否已经「确认填写过一次」（冻结字段填写确认后置位，随工程保存）。
        /// 只置一次，之后即使未登录也不会自动清除。
        /// </summary>
        public bool IsValueConfirmed
        {
            get => _isValueConfirmed;
            set
            {
                if (SetProperty(ref _isValueConfirmed, value))
                {
                    OnPropertyChanged(nameof(IsValueLocked));
                    OnPropertyChanged(nameof(CanEditValue));
                }
            }
        }

        /// <summary>当前是否已登录（冻结字段的解锁条件）</summary>
        [JsonIgnore]
        public static bool IsUserLoggedIn
        {
            get
            {
                try { return Utils.SingletonLocator.UserService?.CurrentUser != null; }
                catch { return false; }
            }
        }

        /// <summary>字段值是否非空</summary>
        [JsonIgnore]
        public bool HasValue => !string.IsNullOrWhiteSpace(Value);

        /// <summary>是否处于锁定状态：勾了冻结 + 已确认填过 + 当前未登录</summary>
        [JsonIgnore]
        public bool IsValueLocked => IsFrozen && IsValueConfirmed && !IsUserLoggedIn;

        /// <summary>当前是否允许修改值（冻结字段未登录时只读）</summary>
        [JsonIgnore]
        public bool CanEditValue => !IsValueLocked;

        /// <summary>锁定时再次尝试修改（供界面提示登录，例如详情面板编辑拦截）</summary>
        public static event Action<UserInputField> LockedEditAttempted;

        /// <summary>通知「登录状态已变化」，让界面重新计算冻结字段的可编辑性</summary>
        public void RefreshLockState()
        {
            OnPropertyChanged(nameof(IsValueLocked));
            OnPropertyChanged(nameof(CanEditValue));
            OnPropertyChanged(nameof(LockHint));
        }

        /// <summary>显示给用户的锁定提示（解锁后为空）</summary>
        [JsonIgnore]
        public string LockHint => IsValueLocked ? "(冻结字段，登录后可修改)" : string.Empty;

        [JsonIgnore]
        public bool IsDropdown => FieldType == UserInputFieldType.Dropdown;

        [JsonIgnore]
        public bool IsPassword => FieldType == UserInputFieldType.Password;

        /// <summary>下拉候选值列表</summary>
        [JsonIgnore]
        public List<string> DropdownOptions => string.IsNullOrWhiteSpace(Options)
            ? new List<string>()
            : Options.Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToList();

        /// <summary>显示名（空名称时回退为「未命名字段」）</summary>
        [JsonIgnore]
        public string DisplayName => string.IsNullOrWhiteSpace(Name) ? "(未命名字段)" : Name;

        /// <summary>所有字段类型（供 ComboBox 绑定）</summary>
        [JsonIgnore]
        public static List<UserInputFieldType> AllTypes { get; } = new()
        {
            UserInputFieldType.Text,
            UserInputFieldType.Number,
            UserInputFieldType.Date,
            UserInputFieldType.Password,
            UserInputFieldType.Dropdown,
        };

        public UserInputField Clone() => new()
        {
            Id = Id,
            Name = Name,
            Value = Value,
            FieldType = FieldType,
            Options = Options,
            IsRequired = IsRequired,
            Description = Description,
            IsFrozen = IsFrozen,
            IsValueConfirmed = IsValueConfirmed,
        };
    }
}
