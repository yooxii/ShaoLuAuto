using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace ShaoLu.Views
{
    /// <summary>
    /// 流程图连线的正交绕障走线。
    /// 做法参考 libavoid / Adaptagrams 的正交连接器思路：
    /// 把每个节点的矩形（外扩一点留出安全距离）当成障碍，横向 / 纵向的候选线取在障碍的边上，
    /// 在这些候选线的交点构成的栅格图上跑 A*，代价 = 线段长度 + 每次拐弯的惩罚，
    /// 这样出来的折线既不会穿过节点，也会尽量少拐弯。
    /// </summary>
    public sealed class FlowRouteGrid
    {
        /// <summary>节点矩形外扩的安全距离</summary>
        public const double DefaultMargin = 12d;

        /// <summary>每次拐弯折算的额外长度</summary>
        public const double DefaultBendPenalty = 40d;

        private const double Eps = 0.5d;

        private readonly Rect[] _obstacles;
        private readonly double[] _xs;
        private readonly double[] _ys;
        private readonly bool[] _pointBlocked;
        private readonly bool[] _horizontalBlocked;
        private readonly bool[] _verticalBlocked;

        private FlowRouteGrid(Rect[] obstacles, double[] xs, double[] ys)
        {
            _obstacles = obstacles;
            _xs = xs;
            _ys = ys;

            int nx = xs.Length;
            int ny = ys.Length;
            _pointBlocked = new bool[nx * ny];
            _horizontalBlocked = new bool[(nx - 1) * ny];
            _verticalBlocked = new bool[nx * (ny - 1)];

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iy = 0; iy < ny; iy++)
                    _pointBlocked[iy * nx + ix] = IsBlocked(xs[ix], ys[iy]);

                if (ix + 1 < nx)
                {
                    for (int iy = 0; iy < ny; iy++)
                        _horizontalBlocked[iy * (nx - 1) + ix] = IsSegmentBlocked(xs[ix], ys[iy], xs[ix + 1], ys[iy]);
                }
            }

            for (int ix = 0; ix < nx; ix++)
            {
                for (int iy = 0; iy + 1 < ny; iy++)
                    _verticalBlocked[iy * nx + ix] = IsSegmentBlocked(xs[ix], ys[iy], xs[ix], ys[iy + 1]);
            }
        }

        /// <summary>
        /// 建栅格：横向 / 纵向候选线 = 障碍矩形外扩后的四条边 + 所有起终点的坐标。
        /// </summary>
        public static FlowRouteGrid Build(IEnumerable<Rect> obstacles, double margin, IEnumerable<Point> terminals)
        {
            var rects = (obstacles ?? Enumerable.Empty<Rect>()).Where(r => r.Width > 0 && r.Height > 0).ToList();
            var inflated = rects.Select(r => Inflate(r, margin)).ToList();
            var terminalList = (terminals ?? Enumerable.Empty<Point>()).ToList();

            var xs = new List<double>();
            var ys = new List<double>();
            foreach (var rect in inflated)
            {
                xs.Add(rect.Left);
                xs.Add(rect.Right);
                ys.Add(rect.Top);
                ys.Add(rect.Bottom);
            }

            foreach (var point in terminalList)
            {
                xs.Add(point.X);
                ys.Add(point.Y);
            }

            return new FlowRouteGrid(inflated.ToArray(), SortUnique(xs), SortUnique(ys));
        }

        /// <summary>
        /// 求从 <paramref name="source"/> 到 <paramref name="target"/> 的正交折线（不含起终点自身那两段短接头，
        /// 由调用方自行补上）。找不到通路时返回 null。
        /// </summary>
        public List<Point> Solve(Point source, Point target, double bendPenalty = DefaultBendPenalty)
        {
            int nx = _xs.Length;
            int ny = _ys.Length;
            int sourceX = IndexOf(_xs, source.X);
            int sourceY = IndexOf(_ys, source.Y);
            int targetX = IndexOf(_xs, target.X);
            int targetY = IndexOf(_ys, target.Y);
            if (sourceX < 0 || sourceY < 0 || targetX < 0 || targetY < 0) return null;
            int startPoint = sourceY * nx + sourceX;
            int goalPoint = targetY * nx + targetX;
            if (_pointBlocked[startPoint] || _pointBlocked[goalPoint]) return null;
            // 状态 = (栅格点, 进入方向)：0 = 水平，1 = 竖直
            int stateCount = nx * ny * 2;
            var cost = new double[stateCount];
            var previous = new int[stateCount];
            var closed = new bool[stateCount];
            for (int i = 0; i < stateCount; i++)
            {
                cost[i] = double.PositiveInfinity;
                previous[i] = -1;
            }

            var open = new SortedSet<(double Estimate, int State)>(new EstimateComparer());
            // 起点按水平方向入队：出口先水平挣脱节点
            for (int dir = 0; dir < 2; dir++)
            {
                int state = startPoint * 2 + dir;
                double initial = dir == 0 ? 0d : bendPenalty;
                cost[state] = initial;
                open.Add((initial + Heuristic(source, target), state));
            }

            while (open.Count > 0)
            {
                var (_, current) = open.Min;
                open.Remove(open.Min);
                if (closed[current]) continue;
                closed[current] = true;

                int point = current / 2;
                int incoming = current % 2;
                int ix = point % nx;
                int iy = point / nx;

                if (point == goalPoint)
                    return BuildPath(current, previous, nx, target);

                // 四个方向；边的代价里已经含了拐弯罚分
                for (int d = 0; d < 4; d++)
                {
                    int nextIx = ix + DeltaX(d);
                    int nextIy = iy + DeltaY(d);
                    if (nextIx < 0 || nextIx >= nx || nextIy < 0 || nextIy >= ny) continue;

                    int nextPoint = nextIy * nx + nextIx;
                    if (_pointBlocked[nextPoint]) continue;

                    int dir = DeltaY(d) == 0 ? 0 : 1;
                    if (dir == 0)
                    {
                        int edgeIndex = iy * (nx - 1) + Math.Min(ix, nextIx);
                        if (_horizontalBlocked[edgeIndex]) continue;
                    }
                    else
                    {
                        int edgeIndex = Math.Min(iy, nextIy) * nx + ix;
                        if (_verticalBlocked[edgeIndex]) continue;
                    }

                    double length = Math.Abs(_xs[nextIx] - _xs[ix]) + Math.Abs(_ys[nextIy] - _ys[iy]);
                    double step = length + (incoming == dir ? 0d : bendPenalty);
                    int nextState = nextPoint * 2 + dir;
                    double nextCost = cost[current] + step;
                    // 收尾要水平进入目标节点：竖直进来要再算一次拐弯
                    if (nextPoint == goalPoint && dir == 1) nextCost += bendPenalty;
                    if (nextCost >= cost[nextState]) continue;

                    cost[nextState] = nextCost;
                    previous[nextState] = current;
                    open.Add((nextCost + Heuristic(_xs[nextIx], _ys[nextIy], target), nextState));
                }
            }

            return null;
        }

        private List<Point> BuildPath(int endState, int[] previous, int nx, Point target)
        {
            var points = new List<Point>();
            var visited = new List<int>();
            for (int state = endState; state >= 0; state = previous[state])
                visited.Add(state);

            for (int i = visited.Count - 1; i >= 0; i--)
            {
                int point = visited[i] / 2;
                int ix = point % nx;
                int iy = point / nx;
                var next = new Point(_xs[ix], _ys[iy]);
                if (points.Count == 0 || Distance(points[points.Count - 1], next) > Eps)
                    points.Add(next);
            }

            if (points.Count == 0)
                return null;

            // 收尾补上真正的目标点，避免箭头悬空
            if (Distance(points[points.Count - 1], target) > Eps)
                points.Add(target);

            Simplify(points);
            return points;
        }

        /// <summary>去掉重复点与同向的中间点（保留每处拐角）</summary>
        private static void Simplify(List<Point> points)
        {
            for (int i = points.Count - 1; i > 0; i--)
            {
                if (Distance(points[i], points[i - 1]) <= Eps) points.RemoveAt(i);
            }

            for (int i = points.Count - 2; i >= 1; i--)
            {
                var a = points[i - 1];
                var b = points[i];
                var c = points[i + 1];
                if ((Math.Abs(a.X - b.X) <= Eps && Math.Abs(b.X - c.X) <= Eps)
                    || (Math.Abs(a.Y - b.Y) <= Eps && Math.Abs(b.Y - c.Y) <= Eps))
                {
                    points.RemoveAt(i);
                }
            }
        }

        private bool IsBlocked(double x, double y)
        {
            foreach (var rect in _obstacles)
            {
                if (x > rect.Left + Eps && x < rect.Right - Eps && y > rect.Top + Eps && y < rect.Bottom - Eps)
                    return true;
            }

            return false;
        }

        private bool IsSegmentBlocked(double x1, double y1, double x2, double y2)
        {
            double left = Math.Min(x1, x2);
            double right = Math.Max(x1, x2);
            double top = Math.Min(y1, y2);
            double bottom = Math.Max(y1, y2);

            foreach (var rect in _obstacles)
            {
                bool overlapsX = right > rect.Left + Eps && left < rect.Right - Eps;
                bool overlapsY = bottom > rect.Top + Eps && top < rect.Bottom - Eps;
                if (overlapsX && overlapsY) return true;
            }

            return false;
        }

        private static Rect Inflate(Rect rect, double margin)
            => new Rect(rect.Left - margin, rect.Top - margin, rect.Width + margin * 2, rect.Height + margin * 2);

        private static double[] SortUnique(List<double> values)
        {
            values.Sort();
            var result = new List<double>(values.Count);
            foreach (var value in values)
            {
                if (result.Count == 0 || Math.Abs(result[result.Count - 1] - value) > 0.01)
                    result.Add(value);
            }

            return result.ToArray();
        }

        private static int IndexOf(double[] values, double value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(values[i] - value) <= 0.01) return i;
            }

            return -1;
        }

        private static double Distance(Point a, Point b)
            => Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y);

        private static double Heuristic(Point from, Point to)
            => Math.Abs(from.X - to.X) + Math.Abs(from.Y - to.Y);

        private static double Heuristic(double x, double y, Point to)
            => Math.Abs(x - to.X) + Math.Abs(y - to.Y);

        private static int DeltaX(int direction) => direction switch
        {
            0 => 1,
            1 => -1,
            _ => 0,
        };

        private static int DeltaY(int direction) => direction switch
        {
            2 => 1,
            3 => -1,
            _ => 0,
        };

        private sealed class EstimateComparer : IComparer<(double Estimate, int State)>
        {
            public int Compare((double Estimate, int State) x, (double Estimate, int State) y)
            {
                int result = x.Estimate.CompareTo(y.Estimate);
                return result != 0 ? result : x.State.CompareTo(y.State);
            }
        }
    }
}
