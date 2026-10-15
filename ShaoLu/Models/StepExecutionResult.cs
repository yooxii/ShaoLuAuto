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
        /// 文本来源：OCR=OCR 识别，Direct=直接读取屏幕（无文本时为空）
        /// </summary>
        public string TextSource { get; set; }

        /// <summary>
        /// 弹出框用户选择的按钮值
        /// </summary>
        public string PopupResult { get; set; }

        /// <summary>
        /// 接收信号步骤收到的信号内容（文本形式；非信号步骤为空）
        /// </summary>
        public string SignalValue { get; set; }

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
