using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace ShaoLu.Views
{
    /// <summary>
    /// 「用户输入」步骤的填写窗口：按步骤分组显示字段，按字段类型生成输入控件。
    /// 打开工程 / 开始运行前由主流程调用；只做本次填写，值直接写回步骤字段（随工程保存）。
    /// </summary>
    public partial class WindowUserInput : Window
    {
        private readonly List<UserInputStep> _steps = new();
        private readonly List<(UserInputField Field, Control Editor)> _editors = new();

        public WindowUserInput()
        {
            InitializeComponent();
        }

        public WindowUserInput(List<UserInputStep> steps) : this()
        {
            _steps = steps ?? new List<UserInputStep>();
            BuildEditors();
        }

        /// <summary>
        /// 模态显示填写窗口；用户点「确定」返回 true。
        /// 没有需要填写的步骤时直接返回 true，不弹空窗口。
        /// </summary>
        public static bool ShowAndEdit(IEnumerable<UserInputStep> steps, Window owner = null)
        {
            var list = steps?
                .Where(s => s != null && s.Fields != null && s.Fields.Count > 0)
                .ToList() ?? new List<UserInputStep>();

            if (list.Count == 0) return true;

            var window = new WindowUserInput(list);
            if (owner != null && owner.IsVisible) window.Owner = owner;
            return window.ShowDialog() == true;
        }

        private void BuildEditors()
        {
            FieldsHost.Children.Clear();
            _editors.Clear();

            string title = _steps.FirstOrDefault()?.PromptTitle;
            if (!string.IsNullOrWhiteSpace(title))
                Title = title;

            foreach (var step in _steps)
            {
                if (_steps.Count > 1 || !string.IsNullOrWhiteSpace(step.PromptTitle))
                {
                    FieldsHost.Children.Add(new TextBlock
                    {
                        Text = string.IsNullOrWhiteSpace(step.PromptTitle) ? step.Name : step.PromptTitle,
                        FontWeight = FontWeights.Bold,
                        Margin = new Thickness(0, 6, 0, 8),
                    });
                }

                foreach (var inputField in step.Fields)
                {
                    if (inputField == null) continue;
                    AddFieldEditor(inputField);
                }
            }

            // 有被冻结锁定的字段时，顶部提示「登录后可修改」并给出登录入口
            bool hasLockedField = _steps.Any(s => s.Fields != null && s.Fields.Any(f => f != null && f.IsValueLocked));
            FrozenHintPanel.Visibility = hasLockedField ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddFieldEditor(UserInputField inputField)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            string label = inputField.DisplayName;
            if (inputField.IsRequired) label += " *";
            if (inputField.IsValueLocked) label += " 🔒";

            panel.Children.Add(new TextBlock
            {
                Text = label,
                FontWeight = inputField.IsRequired ? FontWeights.SemiBold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 4),
            });

            if (inputField.IsValueLocked)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = LanguageService.GetLocalizedString("UserInput_FrozenLocked", "(冻结字段，登录后可修改)"),
                    Opacity = 0.7,
                    FontSize = 11,
                    Margin = new Thickness(0, 0, 0, 4),
                });
            }

            Control editor = CreateEditor(inputField);
            editor.Margin = new Thickness(0);
            // 冻结字段未登录时只读（setter 也会兜底拒绝修改）
            editor.IsEnabled = inputField.CanEditValue;
            panel.Children.Add(editor);

            if (!string.IsNullOrWhiteSpace(inputField.Description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = inputField.Description,
                    Opacity = 0.65,
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 3, 0, 0),
                });
            }

            FieldsHost.Children.Add(panel);
            _editors.Add((inputField, editor));
        }

        private Control CreateEditor(UserInputField inputField)
        {
            switch (inputField.FieldType)
            {
                case UserInputFieldType.Dropdown:
                {
                    var combo = new ComboBox { IsEditable = false, MinHeight = 28 };
                    foreach (var option in inputField.DropdownOptions)
                        combo.Items.Add(option);

                    // 已有值不在候选里时补进去，避免打开工程后显示为空白
                    if (!string.IsNullOrEmpty(inputField.Value) && !combo.Items.Contains(inputField.Value))
                        combo.Items.Add(inputField.Value);
                    combo.SelectedItem = inputField.Value;
                    combo.SelectionChanged += (_, __) =>
                    {
                        inputField.Value = combo.SelectedItem as string ?? string.Empty;
                    };
                    return combo;
                }

                case UserInputFieldType.Date:
                {
                    var picker = new DatePicker { SelectedDateFormat = DatePickerFormat.Short, MinHeight = 28 };
                    if (DateTime.TryParse(inputField.Value, out var date))
                        picker.SelectedDate = date;
                    picker.SelectedDateChanged += (_, __) =>
                    {
                        inputField.Value = picker.SelectedDate?.ToString("yyyy-MM-dd") ?? string.Empty;
                    };
                    return picker;
                }

                case UserInputFieldType.Password:
                {
                    var box = new PasswordBox { MinHeight = 28 };
                    box.Password = inputField.Value ?? string.Empty;
                    box.PasswordChanged += (_, __) => inputField.Value = box.Password;
                    return box;
                }

                default:
                {
                    var box = new TextBox { MinHeight = 28, Text = inputField.Value ?? string.Empty };
                    if (inputField.FieldType == UserInputFieldType.Number)
                    {
                        box.PreviewTextInput += (_, e) =>
                        {
                            // 数字字段：只允许数字、负号与小数点
                            if (!char.IsDigit(e.Text, 0) && e.Text != "-" && e.Text != ".")
                                e.Handled = true;
                        };
                    }
                    box.TextChanged += (_, __) => inputField.Value = box.Text;
                    return box;
                }
            }
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // 必填校验：缺项时停在窗口里，提示具体字段
            var missing = _steps
                .SelectMany(s => s.MissingRequiredFieldNames.Select(name => (Step: s, Name: name)))
                .ToList();
            if (missing.Count > 0)
            {
                WindowAsyncPopup.Show(
                    string.Format(LanguageService.GetLocalizedString("UserInput_MissingRequiredWindow", "以下必填项未填写：\n{0}"),
                        string.Join("\n", missing.Select(m => m.Name))),
                    LanguageService.GetLocalizedString("UserInput_Title", "用户输入"),
                    PopupButtons.OK, MessageBoxImage.Warning);
                return;
            }

            // 冻结字段：本次确认填写后置位（此后未登录不能再改）
            foreach (var inputField in _steps.SelectMany(s => s.Fields))
            {
                if (inputField != null && inputField.IsFrozen && inputField.HasValue)
                    inputField.IsValueConfirmed = true;
            }

            DialogResult = true;
        }

        private void Login_Click(object sender, RoutedEventArgs e)
        {
            if (UserInputField.IsUserLoggedIn) return;

            try
            {
                var loginVm = CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default
                    .GetRequiredService<LoginViewModel>();
                var loginWindow = new WindowLogin(loginVm);
                if (Owner != null && Owner.IsVisible) loginWindow.Owner = Owner;

                if (loginWindow.ShowDialog() == true)
                {
                    foreach (var inputField in _steps.SelectMany(s => s.Fields))
                        inputField?.RefreshLockState();

                    try { Utils.SingletonLocator.Main?.RefreshLoginState(); } catch { /* 刷新主窗状态失败不影响解锁 */ }

                    // 重建编辑器：冻结字段此时可编辑
                    BuildEditors();
                }
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Warn(ex, "UserInput: 打开登录窗口失败");
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
