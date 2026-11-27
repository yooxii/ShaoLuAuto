using System;
using System.Collections.Generic;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// 流程图自动排列的两种排法（只算像素坐标，不碰界面，NUnitTest 可以单独验证）：
    /// - <see cref="BuildPerimeter"/>「口字形」：节点只摆在矩形四条边上，从左上角起顺时针
    ///   左上 → 右上 → 右下 → 左下；节点多时把矩形撑大（边长加长），不往中间填、也不套第二圈。
    /// - <see cref="BuildVertical"/>「垂直流」：顶部居中一路向下排成一列，线自然走两边。
    /// 格子尺寸固定，节点之间始终留出连线通道，不会互相遮挡。
    /// </summary>
    public static class FlowAutoLayout
    {
        /// <summary>格子宽度：卡片 220 + 左右各一排连接口，再留出竖直走线的通道</summary>
        public const double CellWidth = 300d;

        /// <summary>格子高度：卡片 96，再留出上下走线的通道</summary>
        public const double CellHeight = 150d;

        /// <summary>垂直流的行距：比格子高一些，让相邻两格之间的通道更宽，线好绕到两边</summary>
        public const double VerticalGap = 180d;

        /// <summary>垂直流的列心（世界坐标），画完「适应窗口」后自然出现在屏幕中间</summary>
        public const double VerticalCenterX = 760d;

        /// <summary>
        /// 口字形矩形的形状（横向 / 纵向各几个格子）：按节点数把矩形撑到刚好放得下，
        /// 尽量保持「宽一些、别太高」的比例，看起来更像一个横向的口。
        /// </summary>
        public static (int Columns, int Rows) PerimeterShape(int count)
        {
            if (count <= 4) return (2, 2);

            // 四条边上的格子数 = 2 * (列 + 行) - 4，先求满足容量的最小「列 + 行」
            int half = (count + 4 + 1) / 2;
            int rows = Math.Max(2, (int)Math.Round(half / 2.25));
            int columns = Math.Max(2, half - rows);

            // 兜底：极端情况下容量还不够就补列，保证一整个矩形装得下所有节点
            while (PerimeterCapacity(columns, rows) < count) columns++;
            return (columns, rows);
        }

        /// <summary>一圈（矩形四条边）能放下几个节点</summary>
        public static int PerimeterCapacity(int columns, int rows) => 2 * (columns + rows) - 4;

        /// <summary>
        /// 口字形：count 个节点只占矩形四条边，顺序 左上 → 右上 → 右下 → 左下；
        /// 位置左上角对齐到 0,0。
        /// </summary>
        public static List<Point> BuildPerimeter(int count)
        {
            var cells = new List<Point>();
            if (count <= 0) return cells;

            var (columns, rows) = PerimeterShape(count);
            int colMax = columns - 1;
            int rowMax = rows - 1;

            // 上边：左上 → 右上
            for (int c = 0; c <= colMax && cells.Count < count; c++)
                cells.Add(Cell(c, 0));

            // 右边：右上 → 右下
            for (int r = 1; r <= rowMax && cells.Count < count; r++)
                cells.Add(Cell(colMax, r));

            // 下边：右下 → 左下
            for (int c = colMax - 1; c >= 0 && cells.Count < count; c--)
                cells.Add(Cell(c, rowMax));

            // 左边：左下 → 左上（最后回到起点上方，首尾相连）
            for (int r = rowMax - 1; r >= 1 && cells.Count < count; r--)
                cells.Add(Cell(0, r));

            return cells;
        }

        /// <summary>
        /// 垂直流：一列向下，水平居中在 <see cref="VerticalCenterX"/>，第 0 个在最上面。
        /// </summary>
        public static List<Point> BuildVertical(int count)
        {
            var cells = new List<Point>();
            if (count <= 0) return cells;

            double left = VerticalCenterX - (CellWidth / 2d);
            for (int i = 0; i < count; i++)
                cells.Add(new Point(left, i * VerticalGap));

            return cells;
        }

        private static Point Cell(int column, int row) => new Point(column * CellWidth, row * CellHeight);
    }
}
