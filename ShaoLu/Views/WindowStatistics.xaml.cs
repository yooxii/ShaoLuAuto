using ShaoLu.Services;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ShaoLu.Views
{
    /// <summary>
    /// 统计信息悬浮窗口（表格展示，大小由内容决定）
    /// </summary>
    public partial class WindowStatistics : Window
    {
        // 跟踪当前打开的统计窗口，供运行停止时统一关闭
        private static readonly List<WindowStatistics> OpenWindows = new();

        private readonly StatisticsStep _step;
        private readonly DispatcherTimer _refreshTimer;

        /// <summary>
        /// 运行停止时关闭所有勾选了 CloseOnStop 的统计窗口
        /// </summary>
        public static void CloseAllOnStop()
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                foreach (var window in OpenWindows.ToList())
                {
                    if (window._step.CloseOnStop && window.IsLoaded)
                    {
                        window.Close();
                    }
                }
            });
        }

        /// <summary>
        /// 查找指定步骤 UID 已打开的统计窗口（全局同一步骤仅一个窗口）
        /// </summary>
        public static WindowStatistics FindOpenByStepUid(Guid stepUid)
        {
            return OpenWindows.FirstOrDefault(w => w._step.Uid == stepUid && w.IsLoaded);
        }

        public WindowStatistics(StatisticsStep step)
        {
            InitializeComponent();
            _step = step;

            // 应用窗口配置
            Title = step.WindowTitle;
            Left = step.WindowX;
            Top = step.WindowY;
            Topmost = step.AlwaysOnTop;
            if (TitleText != null)
                TitleText.Text = step.WindowTitle;

            // 应用背景颜色与背景不透明度（仅影响背景，不影响内容）
            var bgColor = System.Windows.Media.Colors.White;
            try
            {
                if (!string.IsNullOrWhiteSpace(step.BackgroundColor))
                    bgColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(step.BackgroundColor);
            }
            catch { /* 解析失败使用默认白色 */ }
            Background = new System.Windows.Media.SolidColorBrush(bgColor)
            {
                Opacity = Math.Min(1.0, Math.Max(0.01, step.BackgroundOpacity / 100.0))
            };

            // 构建初始内容
            BuildContent();

            // 自动关闭
            if (step.AutoCloseSeconds > 0)
            {
                var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(step.AutoCloseSeconds) };
                closeTimer.Tick += (s, e) => { closeTimer.Stop(); Close(); };
                closeTimer.Start();
            }

            // 定时刷新统计
            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _refreshTimer.Tick += (s, e) => BuildContent();
            _refreshTimer.Start();

            OpenWindows.Add(this);
        }

        private void BuildContent()
        {
            ContentPanel.Children.Clear();
            var context = StepExecutionContext.Instance;
            var steps = Utils.SingletonLocator.Steps?.AutomationStepBases;

            foreach (var item in _step.StatisticsItems)
            {
                if (!item.IsEnabled) continue;

                if (item.Type == StatisticsItemType.TotalExecutionTime)
                {
                    AddTotalTimeSection(context);
                }
                else if (item.Type == StatisticsItemType.ConditionCount)
                {
                    AddConditionCountSection(item);
                }
                else if (steps != null)
                {
                    AddStepTable(item, context, steps);
                }
            }

            // 自定义条件（与默认统计项分开展示）
            foreach (var item in _step.CustomConditions)
            {
                if (!item.IsEnabled) continue;
                AddConditionCountSection(item);
            }
        }

        private void AddConditionCountSection(StatisticsItemConfig item)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock
            {
                Text = $"True: {item.TrueCount}    False: {item.FalseCount}",
                FontSize = 12,
                Margin = new Thickness(10, 0, 0, 0),
                Foreground = Brushes.DimGray,
            });
            ContentPanel.Children.Add(panel);
        }

        private void AddTotalTimeSection(StepExecutionContext context)
        {
            var title = LanguageService.GetLocalizedString("Stats_TotalTime");
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock { Text = title, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });
            panel.Children.Add(new TextBlock { Text = $"{context.TotalElapsedMs:F0} ms", FontSize = 12, Margin = new Thickness(10, 0, 0, 0) });
            ContentPanel.Children.Add(panel);
        }

        private void AddStepTable(StatisticsItemConfig item, StepExecutionContext context, System.Collections.ObjectModel.ObservableCollection<AutomationStepBase> steps)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            panel.Children.Add(new TextBlock { Text = item.DisplayName, FontWeight = FontWeights.Bold, FontSize = 13, Margin = new Thickness(0, 0, 0, 4) });

            // 表格
            var grid = new Grid { Margin = new Thickness(10, 0, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // 表头
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var headerStep = CreateCell(LanguageService.GetLocalizedString("StepName"), true);
            var headerValue = CreateCell(GetValueColumnName(item.Type), true);
            Grid.SetRow(headerStep, 0); Grid.SetColumn(headerStep, 0);
            Grid.SetRow(headerValue, 0); Grid.SetColumn(headerValue, 1);
            grid.Children.Add(headerStep);
            grid.Children.Add(headerValue);

            int rowIndex = 1;
            foreach (var step in steps)
            {
                if (!item.IsStepInScope(step)) continue;

                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var nameCell = CreateCell($"{step.LineNo}. {step.Name}", false);
                var valueCell = CreateValueCell(GetValueText(item.Type, context, step.Uid));
                Grid.SetRow(nameCell, rowIndex); Grid.SetColumn(nameCell, 0);
                Grid.SetRow(valueCell, rowIndex); Grid.SetColumn(valueCell, 1);
                grid.Children.Add(nameCell);
                grid.Children.Add(valueCell);
                rowIndex++;
            }

            panel.Children.Add(grid);
            ContentPanel.Children.Add(panel);
        }

        private TextBlock CreateCell(string text, bool isHeader)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                Margin = new Thickness(0, 1, 12, 1),
                FontWeight = isHeader ? FontWeights.SemiBold : FontWeights.Normal,
                Foreground = isHeader ? Brushes.Black : Brushes.DimGray,
            };
        }

        /// <summary>
        /// 创建值单元格：超过100字符截断显示，点击可预览全文
        /// </summary>
        private TextBlock CreateValueCell(string fullText)
        {
            var cell = CreateCell(Utils.TextHelper.Truncate(fullText), false);
            if (Utils.TextHelper.IsTruncated(fullText))
            {
                cell.TextDecorations = TextDecorations.Underline;
                cell.Foreground = new SolidColorBrush(Color.FromRgb(0x1E, 0x6F, 0xD9));
                cell.Cursor = System.Windows.Input.Cursors.Hand;
                cell.ToolTip = LanguageService.GetLocalizedString("ClickToPreview");
                cell.MouseLeftButtonUp += (s, e) => WindowTextPreview.Show(Title, fullText);
            }
            return cell;
        }

        private string GetValueColumnName(StatisticsItemType type)
        {
            return type switch
            {
                StatisticsItemType.StepExecutionTime => LanguageService.GetLocalizedString("Stats_ColTime"),
                StatisticsItemType.StepExecutionCount => LanguageService.GetLocalizedString("Stats_ColCount"),
                StatisticsItemType.StepExecutionResult => LanguageService.GetLocalizedString("Stats_ColResult"),
                _ => "",
            };
        }

        private string GetValueText(StatisticsItemType type, StepExecutionContext context, Guid uid)
        {
            return type switch
            {
                StatisticsItemType.StepExecutionTime => context.GetStepTime(uid) >= 0 ? $"{context.GetStepTime(uid):F0} ms" : "-",
                StatisticsItemType.StepExecutionCount => $"{context.GetStepCount(uid)}",
                StatisticsItemType.StepExecutionResult => context.GetStepResult(uid) ?? "-",
                _ => "-",
            };
        }

        protected override void OnClosed(EventArgs e)
        {
            _refreshTimer?.Stop();
            OpenWindows.Remove(this);
            base.OnClosed(e);
        }

        /// <summary>
        /// 无边框窗口：按住左键拖拽移动窗口
        /// </summary>
        private void Window_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed && e.Source is not Button)
            {
                try { DragMove(); } catch { }
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
