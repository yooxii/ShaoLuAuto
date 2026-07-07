using ShaoLu.Utils;
using ShaoLu.Viewmodels;
using System.Windows.Controls;

namespace ShaoLu.Views
{
    /// <summary>
    /// UserControlSteps.xaml 的交互逻辑
    /// </summary>
    public partial class UserControlSteps : UserControl
    {
        public StepsViewModel stepsViewModel = ViewModelLocator.Steps;
        public UserControlSteps()
        {
            InitializeComponent();
            DataContext = stepsViewModel;
        }
    }
}
