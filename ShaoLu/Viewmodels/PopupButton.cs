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

        public static PopupButton OK = new() { Value = "OK", DisplayText = GetLocalizedString("OK") };
        public static PopupButton Yes = new() { Value = "Yes", DisplayText = GetLocalizedString("Yes") };
        public static PopupButton No = new() { Value = "No", DisplayText = GetLocalizedString("No") };
        public static PopupButton Cancel = new() { Value = "Cancel", DisplayText = GetLocalizedString("Cancel") };

        [JsonIgnore]
        public List<string> DefaultValues { get; set; } = ["OK", "Yes", "No", "Cancel"];

    }

    public class PopupButtons : ObservableObject
    {
        private PopupButton _defaultButton;

        public ObservableCollection<PopupButton> Buttons { get; set; } = [];
        public PopupButton DefaultButton { get => _defaultButton; set => SetProperty(ref _defaultButton, value); }

        public static PopupButtons OK = new([PopupButton.OK]);
        public static PopupButtons Yes = new([PopupButton.Yes]);
        public static PopupButtons No = new([PopupButton.No]);
        public static PopupButtons Cancel = new([PopupButton.Cancel]);
        public static PopupButtons YesNo = new([PopupButton.Yes, PopupButton.No]);
        public static PopupButtons YesCancel = new([PopupButton.Yes, PopupButton.Cancel]);
        public static PopupButtons YesNoCancel = new([PopupButton.Yes, PopupButton.No, PopupButton.Cancel]);

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
