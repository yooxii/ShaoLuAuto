using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using static ShaoLu.Services.LanguageService;

namespace ShaoLu.Viewmodels
{
    public class PopupButton : ObservableObject
    {
        // 常用按钮值常量
        public const string OKValue = "OK";
        public const string YesValue = "Yes";
        public const string NoValue = "No";
        public const string CancelValue = "Cancel";

        private string _value = "";
        private string _displayText = "";

        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    if (DefaultValues.Contains(value))
                    {
                        DisplayText = GetLocalizedString(value);
                    }
                }
            }
        }

        public string DisplayText
        {
            get => _displayText; set => SetProperty(ref _displayText, value);
        }

        [JsonIgnore]
        public List<string> DefaultValues { get; set; } = ["OK", "Yes", "No", "Cancel"];

        /// <summary>
        /// 创建深拷贝副本
        /// </summary>
        public PopupButton Clone() => new() { _value = _value, _displayText = _displayText };
    }

    public class PopupButtons : ObservableObject
    {
        private PopupButton _defaultButton;

        public ObservableCollection<PopupButton> Buttons { get; set; } = [];
        public PopupButton DefaultButton { get => _defaultButton; set => SetProperty(ref _defaultButton, value); }

        // 工厂属性：每次访问返回新实例，避免共享对象被污染
        public static PopupButtons OK => new(new[] { "OK" });
        public static PopupButtons Yes => new(new[] { "Yes" });
        public static PopupButtons No => new(new[] { "No" });
        public static PopupButtons Cancel => new(new[] { "Cancel" });
        public static PopupButtons YesNo => new(new[] { "Yes", "No" });
        public static PopupButtons YesCancel => new(new[] { "Yes", "Cancel" });
        public static PopupButtons YesNoCancel => new(new[] { "Yes", "No", "Cancel" });

        public PopupButtons() { }

        /// <summary>
        /// 深拷贝构造：复制按钮集合（每个按钮创建新实例）
        /// </summary>
        public PopupButtons(PopupButtons buttons)
        {
            foreach (var btn in buttons.Buttons)
            {
                Buttons.Add(btn.Clone());
            }
            if (buttons.DefaultButton != null)
            {
                // 默认按钮指向新集合中对应的项
                int idx = buttons.Buttons.IndexOf(buttons.DefaultButton);
                DefaultButton = idx >= 0 && idx < Buttons.Count ? Buttons[idx] : null;
            }
        }

        /// <summary>
        /// 深拷贝构造：从已有集合复制（每个按钮创建新实例）
        /// </summary>
        public PopupButtons(ObservableCollection<PopupButton> buttons)
        {
            foreach (var btn in buttons)
            {
                Buttons.Add(btn.Clone());
            }
            if (Buttons.Count > 0)
                DefaultButton = Buttons[0];
        }

        /// <summary>
        /// 内部工厂构造：根据 Value 列表创建按钮
        /// </summary>
        private PopupButtons(IEnumerable<string> values)
        {
            foreach (var v in values)
            {
                var btn = new PopupButton();
                btn.Value = v; // 触发 DisplayText 本地化
                Buttons.Add(btn);
            }
            if (Buttons.Count > 0)
                DefaultButton = Buttons[0];
        }
    }
}
