using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaoLu.Models
{
    public class Settings
    {

    }
    public class AppSettings
    {
        public string Theme { get; set; } = "Light";
        public double WindowWidth { get; set; } = 1000;
        public double WindowHeight { get; set; } = 600;

        public FontModel WindowFont { get; set; } = new FontModel();

    }
}
