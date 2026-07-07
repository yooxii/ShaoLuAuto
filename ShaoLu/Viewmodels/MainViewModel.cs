using CommunityToolkit.Mvvm.ComponentModel;

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

        #endregion

    }
}
