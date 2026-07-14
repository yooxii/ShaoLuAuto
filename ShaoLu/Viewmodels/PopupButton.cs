using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Expression.Drawing.Core;
using ShaoLu.Services;
using System.Collections.ObjectModel;

namespace ShaoLu.Viewmodels
{
    public class PopupButton : ObservableObject
    {
        private string _value;
        private string _displayText;

        public string Value { get => _value; set => SetProperty(ref _value, value); }
        public string DisplayText { get => _displayText; set => SetProperty(ref _displayText, value); }

        public static PopupButton OK = new() { Value = "Yes", DisplayText = LanguageService.GetLocalizedString("OK") };
        public static PopupButton Yes = new() { Value = "Yes", DisplayText = LanguageService.GetLocalizedString("Yes") };
        public static PopupButton No = new() { Value = "No", DisplayText = LanguageService.GetLocalizedString("No") };
        public static PopupButton Cancel = new() { Value = "Cancel", DisplayText = LanguageService.GetLocalizedString("Cancel") };

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
