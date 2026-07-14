using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ShaoLu.Viewmodels
{
    public class ConfigViewModel:ObservableObject
    {
        public string ConfigFilePath { get; set; } = Path.Combine(Directory.GetCurrentDirectory(),"config.txt");
    }
}
