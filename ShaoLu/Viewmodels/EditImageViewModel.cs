using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace ShaoLu.Viewmodels
{
    public class EditImageViewModel : ObservableObject
    {

        private ImageSource _imgSrc;
        public ImageSource ImgSrc {
            get => _imgSrc;
            set => SetProperty(ref _imgSrc, value);
        }

        private RelayCommand saveCommand;
        public ICommand SaveCommand => saveCommand ??= new RelayCommand(Save);

        private void Save()
        {

        }
    }
}
