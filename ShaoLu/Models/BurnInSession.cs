using System;

namespace ShaoLu.Models
{
    /// <summary>
    /// 运行时的烧录会话信息（不持久化），
    /// 由输入弹窗获取、由 BurnInService 持有，流程结束时写入 BurnInRecord
    /// </summary>
    public class BurnInSession
    {
        /// <summary>工令号</summary>
        public string OrderNo { get; set; }

        /// <summary>操作者</summary>
        public string Operator { get; set; }

        /// <summary>零件名称</summary>
        public string PartName { get; set; }

        /// <summary>会话开始时间（= 本次运行的开始时间）</summary>
        public DateTime StartedAt { get; set; }
    }
}
