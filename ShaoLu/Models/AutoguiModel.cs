using OpenCvSharp;

namespace ShaoLu.Models
{
    public class AutoguiModel
    {
        public class Apoint
        {
            private Point _center;
            private Point _leftTop;
            private bool _centerIsEmpty = true;
            private bool _leftTopIsEmpty = true;

            /// <summary>
            /// 中心点
            /// </summary>
            public Point Center
            {
                get => _center;
                set
                {
                    _center = value;
                    _centerIsEmpty = false;
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
                    _leftTopIsEmpty = false;
                }
            }

            /// <summary>
            /// 右上角点 (基于中心对称计算)
            /// 假设矩形轴对齐，Y坐标与LeftTop相同，X坐标关于Center对称
            /// </summary>
            public Point RightTop => new Point(2 * _center.X - _leftTop.X, _leftTop.Y);

            /// <summary>
            /// 左下角点 (基于中心对称计算)
            /// 假设矩形轴对齐，X坐标与LeftTop相同，Y坐标关于Center对称
            /// </summary>
            public Point LeftDown => new Point(_leftTop.X, 2 * _center.Y - _leftTop.Y);

            /// <summary>
            /// 右下角点 (基于中心对称计算)
            /// </summary>
            public Point RightDown => new Point(2 * _center.X - _leftTop.X, 2 * _center.Y - _leftTop.Y);

            /// <summary>
            /// 相似度得分
            /// </summary>
            public double Similarity { get; set; }

            /// <summary>
            /// 是否为空（未初始化或无效）
            /// </summary>
            public bool IsEmpty => _centerIsEmpty || _leftTopIsEmpty;
        }
    }
}