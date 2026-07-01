using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Media.Imaging;

namespace ImageTool
{
    internal class ImageEditorViewModel : INotifyPropertyChanged
    {
        private Folder _folder;
        public Folder Folder
        {
            get
            {
                return _folder;
            }
            set
            {
                if (_folder != value)
                {
                    _folder = value;
                    OnPropertyChanged("Folder");
                }

                IsShowImageList = _folder?.Count > 1;
            }
        }

        private Image _currentImage;
        public Image CurrentImage
        {
            get
            {
                return _currentImage;
            }
            set
            {
                if (_currentImage != value)
                {
                    _currentImage?.Dispose();
                    _currentImage = value;
                    OnPropertyChanged("CurrentImage");
                }

                IsImageExist = _currentImage != null;
            }
        }

        private bool _isImageExist = false;
        public bool IsImageExist
        {
            get
            {
                return _isImageExist;
            }
            set
            {
                _isImageExist = value;
                OnPropertyChanged("IsImageExist");
            }
        }

        private bool _isShowImageList = false;
        public bool IsShowImageList
        {
            get
            {
                return _isShowImageList;
            }
            set
            {
                _isShowImageList = value;
                OnPropertyChanged("IsShowImageList");
            }
        }

        private bool _isImageLoading = false;
        public bool IsImageLoading
        {
            get
            {
                return _isImageLoading;
            }
            set
            {
                _isImageLoading = value;
                OnPropertyChanged("IsImageLoading");
            }
        }

        private bool _isImageLoadFailed = false;
        public bool IsImageLoadFailed
        {
            get
            {
                return _isImageLoadFailed;
            }
            set
            {
                _isImageLoadFailed = value;
                OnPropertyChanged("IsImageLoadingFailed");
            }
        }




        public event PropertyChangedEventHandler PropertyChanged;
        public void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
