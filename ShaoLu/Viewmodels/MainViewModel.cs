using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ShaoLu.Viewmodels
{
    public class MainViewModel : ObservableObject
    {
        #region 属性

        #region 语言
        private bool _english;
        public bool English { get => _english; set => SetProperty(ref _english, value); }


        private bool _simplified;
        public bool Simplified { get => _simplified; set => SetProperty(ref _simplified, value); }


        private bool _traditional;
        public bool Tranditional { get => _traditional; set => SetProperty(ref _traditional, value); }

        #endregion

        private System.Windows.Media.ImageSource img;

        public System.Windows.Media.ImageSource Img { get => img; set => SetProperty(ref img, value); }


        private ObservableCollection<AutomationStepBase> _automationStepBases = [];
        public ObservableCollection<AutomationStepBase> AutomationStepBases { get => _automationStepBases; set => SetProperty(ref _automationStepBases, value); }

        #endregion


        #region 命令

        private RelayCommand addImageStepCommand;
        public ICommand AddImageStepCommand => addImageStepCommand ??= new RelayCommand(AddImageStep);


        private void AddImageStep()
        {
            var imgStep = new ImageRecognitionStep() {
                StepId = AutomationStepBases.Count,
                StepName = $"{AutomationStepBases.Count + 1}",
                Type = Models.StepType.ImageRecognition
            };
            AutomationStepBases.Add(imgStep);
        }

        private RelayCommand runCommand;
        public ICommand RunCommand => runCommand ??= new RelayCommand(Run);

        private void Run()
        {
        }

        #endregion
    }
}
