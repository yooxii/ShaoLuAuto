using ShaoLu.Services.Ocr;
using System;

namespace ShaoLu.OcrExtension
{
    /// <summary>
    /// OCR 扩展入口：主程序扫描 <c>extensions\Ocr</c> 时实例化本类型。
    /// </summary>
    public sealed class TesseractOcrExtensionFactory : IOcrExtensionFactory
    {
        public string Name => "Tesseract OCR";

        public string Version
        {
            get
            {
                // 扩展自身程序集版本，主程序在日志/界面提示里显示用
                var version = typeof(TesseractOcrExtensionFactory).Assembly.GetName().Version;
                return version == null ? string.Empty : version.ToString(3);
            }
        }

        /// <summary>创建引擎；语言数据或原生库缺失时抛异常，由主程序转成“OCR 不可用 + 原因”</summary>
        public IOcrEngine CreateEngine() => new TesseractOcrEngine();
    }
}
