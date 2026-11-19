using System;
using System.Collections.Generic;

namespace ShaoLu.Models
{
    /// <summary>
    /// 流程图的版面信息（跟随工程文件保存）：节点位置、锚定状态、连线锚点。
    /// 存在工程包里的 flow.json，与步骤数据分开，步骤格式保持不变。
    /// </summary>
    public sealed class FlowLayoutFile
    {
        public int Version { get; set; } = 1;

        public List<FlowNodeLayout> Nodes { get; set; } = new List<FlowNodeLayout>();

        public List<FlowEdgeLayout> Edges { get; set; } = new List<FlowEdgeLayout>();
    }

    /// <summary>一个节点在流程图里的位置与是否锚定</summary>
    public sealed class FlowNodeLayout
    {
        public Guid Uid { get; set; }

        public double X { get; set; }

        public double Y { get; set; }

        public bool Pinned { get; set; }
    }

    /// <summary>一条连线上的锚点（按「起点&gt;终点&gt;类型」匹配连线）</summary>
    public sealed class FlowEdgeLayout
    {
        public string Key { get; set; } = string.Empty;

        public List<FlowPoint> Points { get; set; } = new List<FlowPoint>();

        /// <summary>起点在节点边框上的落点（边长 + 比例，空表示自动分配）</summary>
        public string StartSide { get; set; }

        public double StartFraction { get; set; }

        /// <summary>终点在节点边框上的落点</summary>
        public string EndSide { get; set; }

        public double EndFraction { get; set; }
    }

    public sealed class FlowPoint
    {
        public double X { get; set; }

        public double Y { get; set; }
    }
}
