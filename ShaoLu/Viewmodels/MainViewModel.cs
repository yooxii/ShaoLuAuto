using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace ShaoLu.Viewmodels
{
    public class MainViewModel : ObservableObject
    {

        private bool _english;
        public bool English { get => _english; set => SetProperty(ref _english, value); }


        private bool _simplified;
        public bool Simplified { get => _simplified; set => SetProperty(ref _simplified, value); }


        private bool _traditional;
        public bool Tranditional { get => _traditional; set => SetProperty(ref _traditional, value); }


        private System.Windows.Media.ImageSource img;

        public System.Windows.Media.ImageSource Img { get => img; set => SetProperty(ref img, value); }

        public ObservableCollection<AutomationStepBase> AutomationSteps { get; set; } = [];


        private RelayCommand addImageStepCommand;
        public ICommand AddImageStepCommand => addImageStepCommand ??= new RelayCommand(AddImageStep);

        private void AddImageStep()
        {
            AutomationSteps.Add(new ImageRecognitionStep() { StepName = "1", Type=Models.StepType.ImageRecognition});
        }
    }
}
