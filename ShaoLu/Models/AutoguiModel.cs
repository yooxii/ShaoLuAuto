namespace ShaoLu.Models
{
    public class Point
    {
        private int x;
        private int y;
        private bool isEmpty = true;

        public int X { get => x; set => x = value; }
        public int Y { get => y; set => y = value; }
        public bool IsEmpty { get => isEmpty; set => isEmpty = value; }

        public Point()
        {
        }

        public Point(int size)
        {
            X = size;
            Y = size;
        }

        public Point(double size)
        {
            X = (int)size;
            Y = (int)size;
        }

        public Point(OpenCvSharp.Point p)
        {
            X = p.X;
            Y = p.Y;
            IsEmpty = false;
        }

        public Point(System.Drawing.Point p)
        {
            X = p.X;
            Y = p.Y;
            IsEmpty = false;
        }
        public Point(System.Windows.Point p)
        {
            X = (int)p.X;
            Y = (int)p.Y;
            IsEmpty = false;
        }

        public Point(int x, int y)
        {
            X = x;
            Y = y;
            IsEmpty = false;
        }
        public Point(double x, double y)
        {
            X = (int)x;
            Y = (int)y;
            IsEmpty = false;
        }

        public static implicit operator Point(OpenCvSharp.Point v)
        {
            return new Point(v.X, v.Y);
        }

        public static implicit operator Point(System.Drawing.Point v)
        {
            return new Point(v.X, v.Y);
        }

        public static implicit operator Point(System.Windows.Point v)
        {
            return new Point(v.X, v.Y);
        }

        public static Point operator +(Point p1, Point p2)
        {
            return new Point(p1.X + p2.X, p1.Y + p2.Y);
        }

        public static Point operator -(Point p1, Point p2)
        {
            return new Point(p1.X - p2.X, p1.Y - p2.Y);
        }
    }


    public class AutoRect
    {
        private Point _center = new();
        private Point _leftTop = new();

        /// <summary>
        /// 中心点
        /// </summary>
        public Point Center
        {
            get => _center;
            set
            {
                _center = value;
            }
        }

        /// <summary>
        /// 左上角点
        /// </summary>
        public Point LeftTop
        {
            get => _leftTop;
            set
            {
                _leftTop = value;
            }
        }

        /// <summary>
        /// 右上角点 (基于中心对称计算)
        /// 假设矩形轴对齐，Y坐标与LeftTop相同，X坐标关于Center对称
        /// </summary>
        public Point RightTop => new(2 * _center.X - _leftTop.X, _leftTop.Y);

        /// <summary>
        /// 左下角点 (基于中心对称计算)
        /// 假设矩形轴对齐，X坐标与LeftTop相同，Y坐标关于Center对称
        /// </summary>
        public Point LeftDown => new(_leftTop.X, 2 * _center.Y - _leftTop.Y);

        /// <summary>
        /// 右下角点 (基于中心对称计算)
        /// </summary>
        public Point RightDown => new(2 * _center.X - _leftTop.X, 2 * _center.Y - _leftTop.Y);

        /// <summary>
        /// 相似度得分
        /// </summary>
        public double Similarity { get; set; }

        /// <summary>
        /// 是否为空（未初始化或无效）
        /// </summary>
        public bool IsEmpty => Center.IsEmpty || LeftTop.IsEmpty;
    }
}