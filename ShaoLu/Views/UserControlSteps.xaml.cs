using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using System.Windows;
using System.Windows.Controls;
using WPFDevelopers.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// UserControlSteps.xaml 的交互逻辑
    /// </summary>
    public partial class UserControlSteps : UserControl
    {
        public StepsViewModel stepsViewModel = SingletonLocator.Steps;
        public UserControlSteps()
        {
            InitializeComponent();
            DataContext = stepsViewModel;
        }

        private void Refresh_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            UpdateLayout();
        }

        private void SplitButton_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            var btn = (SplitButton)sender;
            if (btn.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        private void MenuItemAddTextStep_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            AddTextStep.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }

        private void MenuItemAddImageStep_Click(object sender, RoutedEventArgs e)
        {
            var menuItem = (MenuItem)sender;
            AddImageStep.CommandParameter = menuItem.CommandParameter;
            if (menuItem.CommandParameter is string type)
            {
                stepsViewModel.AddStepCommand.Execute(type);
            }
        }
    }
}
