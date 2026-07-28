using FreeSql.DataAnnotations;
using System;

namespace ShaoLu.Models
{
    [Table(Name = "step_execution_log")]
    public class StepExecutionLog
    {
        [Column(IsIdentity = true, IsPrimary = true)]
        public long Id { get; set; }

        /// <summary>
        /// 步骤的唯一 UID
        /// </summary>
        public Guid StepUid { get; set; }

        /// <summary>
        /// 步骤文件名（无扩展名），用于区分不同步骤文件
        /// </summary>
        [Column(StringLength = 256)]
        public string StepFileName { get; set; }

        /// <summary>
        /// 步骤名称（便于查看）
        /// </summary>
        [Column(StringLength = 256)]
        public string StepName { get; set; }

        /// <summary>
        /// 本次执行的输入内容
        /// </summary>
        [Column(StringLength = -1)]
        public string InputText { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        public DateTime ExecutedAt { get; set; }
    }
}
