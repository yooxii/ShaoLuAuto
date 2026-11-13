using System;

namespace ShaoLu.Services.Ocr
{
    /// <summary>
    /// OCR 引擎契约。
    ///
    /// OCR 实现（Tesseract 及其原生库、语言数据）作为独立扩展发布，放在程序的
    /// <c>extensions\Ocr</c> 目录下；主程序只引用本接口，不引用任何 OCR 运行时文件。
    /// 扩展未安装时 <see cref="OcrExtensionHost.IsAvailable"/> 为 false，相关功能在界面上禁用。
    /// </summary>
    public interface IOcrEngine : IDisposable
    {
        /// <summary>引擎显示名，例如 “Tesseract 5.5.2”</summary>
        string Name { get; }

        /// <summary>
        /// 识别一张 PNG 图片，返回识别到的文本（没有文字时返回空字符串）。
        /// 由主程序负责把屏幕区域截成位图再编码成 PNG，扩展不接触 WPF/GDI 对象。
        /// </summary>
        string RecognizePng(byte[] pngBytes);
    }

    /// <summary>
    /// OCR 扩展入口。扩展程序集里必须有一个公开、带无参构造函数的实现，
    /// 主程序扫描 <c>extensions\Ocr</c> 下的 dll 并取第一个可用实现。
    /// </summary>
    public interface IOcrExtensionFactory
    {
        /// <summary>扩展显示名，例如 “Tesseract OCR”</summary>
        string Name { get; }

        /// <summary>扩展版本（显示用，例如 1.0.0）</summary>
        string Version { get; }

        /// <summary>
        /// 创建引擎。缺少运行时文件（tessdata / 原生库）或初始化失败时应抛异常，
        /// 主程序据此把状态标成“扩展不可用”并给出原因，而不是让功能静默失效。
        /// </summary>
        IOcrEngine CreateEngine();
    }
}
