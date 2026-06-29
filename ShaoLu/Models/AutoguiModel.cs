using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaoLu.Models
{
    public class AutoguiModel
    {
        public class Apoint
        {
            public Point Center { get; set; }
            public Point LeftTop { get; set; }
            public Point RightTop { get { return new Point(2 * Center.X - LeftTop.X, LeftTop.Y); } }
            public Point LeftDown { get { return new Point(LeftTop.X, 2 * Center.Y - LeftTop.Y); } }
            public Point RightDown { get { return new Point(2 * Center.X - LeftTop.X, 2 * Center.Y - LeftTop.Y); } }
            public double Similarity { get; set; }
            public bool IsEmpty { get { return Center == null; } }
        }
    }
}
