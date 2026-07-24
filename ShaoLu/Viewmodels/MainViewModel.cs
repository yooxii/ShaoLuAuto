using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;

namespace ShaoLu.Viewmodels
{
    public class MainViewModel : ObservableObject
    {
        #region 属性

        #region 语言

        private bool _english;
        private bool _simplified;
        private bool _traditional;


        public bool English { get => _english; set => SetProperty(ref _english, value); }
        public bool Simplified { get => _simplified; set => SetProperty(ref _simplified, value); }
        public bool Tranditional { get => _traditional; set => SetProperty(ref _traditional, value); }

        #endregion


        private string _rootPath = Directory.GetCurrentDirectory();
        private string _stepFilePath;
        private string _imageFilePath;

        public string RootDir { get => _rootPath; set => _rootPath = value; }
        public string StepFileDir { get => _stepFilePath; set => _stepFilePath = value; }
        public string ImageFilePath { get => _imageFilePath; set => _imageFilePath = value; }


        #endregion

    }
}
