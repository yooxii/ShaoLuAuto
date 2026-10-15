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
        /// OCR 识别结果（若有）
        /// </summary>
        [Column(StringLength = -1)]
        public string OCRText { get; set; }

        /// <summary>
        /// 本步骤耗时（毫秒）
        /// </summary>
        public double? ExecutionTimeMs { get; set; }

        /// <summary>
        /// 执行结果（True / False）
        /// </summary>
        public bool? IsTrue { get; set; }

        /// <summary>
        /// 图像匹配相似度（负数表示不适用）
        /// </summary>
        public double? Similarity { get; set; }

        /// <summary>
        /// 执行该步骤的线程（主线程 / 子线程N）
        /// </summary>
        [Column(StringLength = 64)]
        public string ThreadName { get; set; }

        /// <summary>
        /// 文本来源：OCR=OCR 识别，Direct=直接读取屏幕
        /// </summary>
        [Column(StringLength = 16)]
        public string TextSource { get; set; }

        /// <summary>
        /// 错误消息（执行失败时写入）
        /// </summary>
        [Column(StringLength = -1)]
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        public DateTime ExecutedAt { get; set; }

        /// <summary>展示用：连续相同结果的重复次数（默认 1）</summary>
        [Column(IsIgnore = true)]
        public int RepeatCount { get; set; } = 1;

        /// <summary>展示用：结果列文本</summary>
        [Column(IsIgnore = true)]
        public string ResultDisplay { get; set; }

        /// <summary>展示用：执行时间列文本</summary>
        [Column(IsIgnore = true)]
        public string ExecutionTimeDisplay { get; set; }

        /// <summary>展示用：线程列文本</summary>
        [Column(IsIgnore = true)]
        public string ThreadDisplay { get; set; }

        /// <summary>展示用：本条记录已被并入上一行的重复组，列表中不再显示（默认 false）</summary>
        [Column(IsIgnore = true)]
        public bool IsMergedAway { get; set; }
    }
}
