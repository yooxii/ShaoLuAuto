using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Services;
using ShaoLu.Utils;
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
        private string _tempPath = Path.GetTempPath();
        private string _stepFilePath;
        private string _stepImageWorkDir;
        private string _imageFilePath;
        private bool _isLoggedIn;

        /// <summary>
        /// 是否已登录（拥有编辑权限）
        /// </summary>
        public bool IsLoggedIn
        {
            get => _isLoggedIn;
            set => SetProperty(ref _isLoggedIn, value);
        }

        /// <summary>
        /// 当前登录用户名（未登录时为空）
        /// </summary>
        public string CurrentUsername => SingletonLocator.UserService.CurrentUser?.Username;

        /// <summary>
        /// 刷新登录状态（登录/注销后调用）
        /// </summary>
        public void RefreshLoginState()
        {
            IsLoggedIn = SingletonLocator.UserService.CurrentUser != null;
            OnPropertyChanged(nameof(CurrentUsername));
        }

        public string RootDir { get => _rootPath; set => _rootPath = value; }
        public string TempDir { get => _tempPath; set => _tempPath = value; }

        /// <summary>
        /// 当前打开/保存的 .autostep 文件完整路径
        /// </summary>
        public string StepFilePath { get => _stepFilePath; set => SetProperty(ref _stepFilePath, value); }

        /// <summary>
        /// 当前步骤包的图片解压工作目录
        /// </summary>
        public string StepImageWorkDir { get => _stepImageWorkDir; set => SetProperty(ref _stepImageWorkDir, value); }

        public string ImageFilePath { get => _imageFilePath; set => _imageFilePath = value; }


        #endregion

    }
}
