using System;
using System.Collections.Generic;
using System.Windows;

namespace ShaoLu.Services
{
    /// <summary>
    /// 流程图视图里节点的手动摆放位置（按步骤 Uid 记录）。
    /// 只保存在进程内：表格与流程图之间来回切换、窗口关掉再打开时位置不变；
    /// 不写入工程文件，重新打开工程时按自动布局重新排列。
    /// </summary>
    public static class FlowLayoutStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<Guid, Point> Positions = new Dictionary<Guid, Point>();
        private static readonly HashSet<Guid> Pinned = new HashSet<Guid>();

        public static bool TryGet(Guid uid, out Point point)
        {
            lock (Gate)
            {
                return Positions.TryGetValue(uid, out point);
            }
        }

        public static void Set(Guid uid, Point point)
        {
            lock (Gate)
            {
                Positions[uid] = point;
            }
        }

        public static void Remove(Guid uid)
        {
            lock (Gate)
            {
                Positions.Remove(uid);
            }
        }

        /// <summary>该步骤是否被「锚定」：自动整理时不动，只能手动拖动</summary>
        public static bool IsPinned(Guid uid)
        {
            lock (Gate)
            {
                return Pinned.Contains(uid);
            }
        }

        public static void SetPinned(Guid uid, bool pinned)
        {
            lock (Gate)
            {
                if (pinned) Pinned.Add(uid);
                else Pinned.Remove(uid);
            }
        }

        /// <summary>快照：所有手动摆放过的节点位置</summary>
        public static List<(Guid Uid, Point Position, bool Pinned)> Snapshot()
        {
            lock (Gate)
            {
                var list = new List<(Guid, Point, bool)>();
                foreach (var pair in Positions)
                    list.Add((pair.Key, pair.Value, Pinned.Contains(pair.Key)));
                return list;
            }
        }

        /// <summary>整体替换（打开工程时按文件内容恢复）</summary>
        public static void ReplaceAll(IEnumerable<(Guid Uid, Point Position, bool Pinned)> layout)
        {
            lock (Gate)
            {
                Positions.Clear();
                Pinned.Clear();
                if (layout == null) return;
                foreach (var (uid, position, pinned) in layout)
                {
                    Positions[uid] = position;
                    if (pinned) Pinned.Add(uid);
                }
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Positions.Clear();
                Pinned.Clear();
            }
        }
    }

    /// <summary>
    /// 连线两端的落点（用户手动拖到节点边框上的位置）：按「连线 Key|A 或 |B」记录边长与比例，
    /// 节点移动后端点仍贴着同一条边。只保存在进程内，随工程写进 flow.json。
    /// </summary>
    public static class FlowPortStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, (string Side, double Fraction)> Ports =
            new Dictionary<string, (string Side, double Fraction)>();

        public static bool TryGet(string key, out string side, out double fraction)
        {
            lock (Gate)
            {
                if (Ports.TryGetValue(key, out var value))
                {
                    side = value.Side;
                    fraction = value.Fraction;
                    return true;
                }
            }

            side = null;
            fraction = 0;
            return false;
        }

        public static void Set(string key, string side, double fraction)
        {
            lock (Gate)
            {
                Ports[key] = (side, Math.Min(0.94, Math.Max(0.06, fraction)));
            }
        }

        public static void Remove(string key)
        {
            lock (Gate)
            {
                Ports.Remove(key);
            }
        }

        public static Dictionary<string, (string Side, double Fraction)> Snapshot()
        {
            lock (Gate)
            {
                return new Dictionary<string, (string Side, double Fraction)>(Ports);
            }
        }

        public static void ReplaceAll(IDictionary<string, (string Side, double Fraction)> ports)
        {
            lock (Gate)
            {
                Ports.Clear();
                if (ports == null) return;
                foreach (var pair in ports)
                {
                    if (string.IsNullOrEmpty(pair.Key) || string.IsNullOrEmpty(pair.Value.Side)) continue;
                    Ports[pair.Key] = pair.Value;
                }
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Ports.Clear();
            }
        }
    }

    /// <summary>
    /// 连线锚点（按「起点 Uid&gt;终点 Uid&gt;类型」记录）：连线会经过这些手动放下的点，
    /// 自动整理不改动它们。同样只保存在进程内。
    /// </summary>
    public static class FlowEdgeAnchorStore
    {
        private static readonly object Gate = new object();
        private static readonly Dictionary<string, List<Point>> Anchors = new Dictionary<string, List<Point>>();

        public static IReadOnlyList<Point> Get(string key)
        {
            lock (Gate)
            {
                return Anchors.TryGetValue(key, out var list) ? list.ToArray() : Array.Empty<Point>();
            }
        }

        public static void Set(string key, IReadOnlyList<Point> points)
        {
            lock (Gate)
            {
                if (points == null || points.Count == 0) Anchors.Remove(key);
                else Anchors[key] = new List<Point>(points);
            }
        }

        /// <summary>快照：全部连线的锚点</summary>
        public static Dictionary<string, List<Point>> Snapshot()
        {
            lock (Gate)
            {
                var copy = new Dictionary<string, List<Point>>();
                foreach (var pair in Anchors)
                    copy[pair.Key] = new List<Point>(pair.Value);
                return copy;
            }
        }

        /// <summary>整体替换（打开工程时按文件内容恢复）</summary>
        public static void ReplaceAll(IDictionary<string, List<Point>> anchors)
        {
            lock (Gate)
            {
                Anchors.Clear();
                if (anchors == null) return;
                foreach (var pair in anchors)
                {
                    if (string.IsNullOrEmpty(pair.Key) || pair.Value == null || pair.Value.Count == 0) continue;
                    Anchors[pair.Key] = new List<Point>(pair.Value);
                }
            }
        }

        public static void Clear()
        {
            lock (Gate)
            {
                Anchors.Clear();
            }
        }
    }
}
