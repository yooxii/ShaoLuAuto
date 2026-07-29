using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Expression.Drawing.Core;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using static ShaoLu.Services.LanguageService;

namespace ShaoLu.Viewmodels
{
    public class PopupButton : ObservableObject
    {
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

        public static readonly PopupButton OK = new() { Value = "OK" };
        public static readonly PopupButton Yes = new() { Value = "Yes" };
        public static readonly PopupButton No = new() { Value = "No" };
        public static readonly PopupButton Cancel = new() { Value = "Cancel" };

        [JsonIgnore]
        public List<string> DefaultValues { get; set; } = ["OK", "Yes", "No", "Cancel"];

    }

    public class PopupButtons : ObservableObject
    {
        private PopupButton _defaultButton;

        public ObservableCollection<PopupButton> Buttons { get; set; } = [];
        public PopupButton DefaultButton { get => _defaultButton; set => SetProperty(ref _defaultButton, value); }

        public static readonly PopupButtons OK = new([PopupButton.OK]);
        public static readonly PopupButtons Yes = new([PopupButton.Yes]);
        public static readonly PopupButtons No = new([PopupButton.No]);
        public static readonly PopupButtons Cancel = new([PopupButton.Cancel]);
        public static readonly PopupButtons YesNo = new([PopupButton.Yes, PopupButton.No]);
        public static readonly PopupButtons YesCancel = new([PopupButton.Yes, PopupButton.Cancel]);
        public static readonly PopupButtons YesNoCancel = new([PopupButton.Yes, PopupButton.No, PopupButton.Cancel]);

        public PopupButtons() { }
        public PopupButtons(PopupButtons buttons)
        {
            Buttons.AddRange(buttons.Buttons);
            DefaultButton = buttons.DefaultButton;
        }

        public PopupButtons(ObservableCollection<PopupButton> buttons)
        {
            Buttons.AddRange(buttons);
            DefaultButton = buttons[0];
        }
    }
}
