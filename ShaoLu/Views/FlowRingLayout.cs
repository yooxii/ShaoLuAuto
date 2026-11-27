using System.Collections.Generic;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// 流程图自动排列的「回字形」位置表：从左上角起顺时针绕一圈（左上 → 右上 → 右下 → 左下），
    /// 绕回起点旁边时不再往中间填，而是整体向外扩一圈继续（每往外一圈，四个方向各让出一格）。
    /// 格子尺寸固定，节点之间始终留出连线通道，不会互相遮挡。
    /// 这里只算像素坐标（格子左上角），不碰界面，NUnitTest 可以单独验证。
    /// </summary>
    public static class FlowRingLayout
    {
        /// <summary>格子宽度：卡片 220 + 左右各一排连接口，再留出竖直走线的通道</summary>
        public const double CellWidth = 300d;

        /// <summary>格子高度：卡片 96，再留出上下走线的通道</summary>
        public const double CellHeight = 150d;

        /// <summary>
        /// 第一圈的形状（横向 / 纵向各放几个格子）：步骤少时缩小矩形成一圈，
        /// 步骤多时保持这个形状、不够就往外扩圈，不把格子挤小。
        /// </summary>
        public static (int Columns, int Rows) BaseShape(int count)
        {
            if (count <= 4) return (2, 2);
            if (count <= 6) return (3, 2);
            if (count <= 10) return (4, 3);
            if (count <= 14) return (5, 3);
            return (5, 4);
        }

        /// <summary>一圈能在矩形四条边上放下几个节点</summary>
        public static int RingCapacity(int columns, int rows) => 2 * (columns + rows) - 4;

        /// <summary>
        /// 算出 count 个节点的位置（像素，最左上角对齐到 0,0）：
        /// 顺序就是「左上 → 右上 → 右下 → 左下」，一圈放不下就从外圈接着放。
        /// </summary>
        public static List<Point> Build(int count)
        {
            var cells = new List<Point>();
            if (count <= 0) return cells;

            var (columns, rows) = BaseShape(count);

            int ring = 0;
            while (cells.Count < count)
            {
                int colMin = -ring, colMax = columns - 1 + ring;
                int rowMin = -ring, rowMax = rows - 1 + ring;

                // 上边：左上 → 右上
                for (int c = colMin; c <= colMax && cells.Count < count; c++)
                    cells.Add(Cell(c, rowMin));

                // 右边：右上 → 右下
                for (int r = rowMin + 1; r <= rowMax && cells.Count < count; r++)
                    cells.Add(Cell(colMax, r));

                // 下边：右下 → 左下
                for (int c = colMax - 1; c >= colMin && cells.Count < count; c--)
                    cells.Add(Cell(c, rowMax));

                // 左边：左下 → 左上（走到起点旁边就到位了，下一圈从这里往外扩）
                for (int r = rowMax - 1; r >= rowMin + 1 && cells.Count < count; r--)
                    cells.Add(Cell(colMin, r));

                ring++;
            }

            // 外圈会落到负坐标：整体平移到 0,0 起，交给视图加边距
            double minX = double.MaxValue, minY = double.MaxValue;
            foreach (var cell in cells)
            {
                if (cell.X < minX) minX = cell.X;
                if (cell.Y < minY) minY = cell.Y;
            }

            for (int i = 0; i < cells.Count; i++)
                cells[i] = new Point(cells[i].X - minX, cells[i].Y - minY);

            return cells;
        }

        private static Point Cell(int column, int row) => new Point(column * CellWidth, row * CellHeight);
    }
}
