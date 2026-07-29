using System;

namespace ShaoLu.Models
{
    /// <summary>
    /// 步骤执行结果，记录每次执行的结构化数据
    /// </summary>
    public class StepExecutionResult
    {
        /// <summary>
        /// 步骤执行是否成功
        /// </summary>
        public bool IsTrue { get; set; }

        /// <summary>
        /// 执行耗时（毫秒）
        /// </summary>
        public double ExecutionTimeMs { get; set; }

        /// <summary>
        /// 图像识别相似度（-1 表示不适用）
        /// </summary>
        public double Similarity { get; set; } = -1;

        /// <summary>
        /// 点击位置
        /// </summary>
        public Point ClickPosition { get; set; }

        /// <summary>
        /// OCR 识别文本
        /// </summary>
        public string OCRText { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 执行时间
        /// </summary>
        public DateTime ExecutedAt { get; set; } = DateTime.Now;
    }
}
