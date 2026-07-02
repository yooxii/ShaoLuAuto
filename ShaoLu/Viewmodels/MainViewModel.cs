using CommunityToolkit.Mvvm.ComponentModel;
using OpenCvSharp;
using ShaoLu.Utils;
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
            var imgStep = new ImageRecognitionStep()
            {
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
            foreach (var step in AutomationStepBases)
            {
                Autogui.StartAuto();
                switch (step.Type)
                {
                    case Models.StepType.ImageRecognition:
                        var imgStep = step as ImageRecognitionStep;
                        var img = imgStep.ImgSrc;
                        if (!Autogui.ClickImageOnScreen(Autogui.ConvertImageSourceToBitmap(img), Autogui.Position.RightDown, new Point(-70, -30)))
                        {
                            WPFDevelopers.Controls.MessageBox.Show($"Error!-Not Find image{imgStep.ImagePath} On Screen.", "Error", System.Windows.MessageBoxImage.Error);
                        }
                        break;
                    case Models.StepType.TypeText:
                        var textStep = step as TypeTextStep;
                        break;
                }
            }
        }

        #endregion
    }
}
