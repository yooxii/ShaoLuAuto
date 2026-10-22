using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ShaoLu.Models
{
    public enum StepType
    {
        Empty = -1,
        ClickImage,
        TypeText,
        FindImage,
        ClickImages = 100,
        FindImages,
        TypeTextMore,
        TypeTextFromFile,
        GetInput = 200,
        MouseAction = 201,
        Statistics = 202,
        BurnInConfig = 203,
        FocusWindow = 204,
        Popup = 1000,
        AcbelUpload = 1001,     // 上传Acbel（ACBEL_INSERT_DATA）
        UserInput = 1002,       // 用户输入（打开工程/运行前弹窗填写，供其他步骤引用）
        ScanCode = 1003,        // 扫码（验证网络 → 扫码 → 验证条码，供上传步骤引用条码）

        // 多线程步骤
        MultiThread = 300,      // 多线程开始
        MultiThreadEnd = 301,   // 多线程区域结束
        SendSignal = 302,       // 发出信号
        ReceiveSignal = 303,    // 接收信号
        Interrupt = 304,        // 中断
        // 其他步骤类型...
    }

    public enum StepErrorType
    {
        None,
        ImageNotFound,
        ImageLoadFailed,
        ImageMatchFailed,
        TextEmpty,
        FileNotFound,
        FileReadError,
        ExecutionTimeout,
        SelfReferenceLimit,
        CancelledByUser,
        PopupError,
        ConversionError,
        IndexOutOfRange,
        OCRError,
        Unknown
    }

    /// <summary>
    /// 弹出窗口关闭方式（Flags，可组合多种方式，任满足其一即关闭）
    /// </summary>
    [Flags]
    public enum PopupCloseMode
    {
        /// <summary>未设置任何关闭方式</summary>
        None = 0,
        /// <summary>用户点击弹窗按钮关闭</summary>
        ButtonClick = 1,
        /// <summary>按时间自动关闭</summary>
        Timeout = 2,
        /// <summary>到某个步骤时关闭</summary>
        StepReached = 4,
    }

    /// <summary>
    /// 弹出窗口样式
    /// </summary>
    public enum PopupWindowStyle
    {
        /// <summary>正常窗口</summary>
        Normal,
        /// <summary>紧凑窗口（未设置的部分不占空间，边距更小）</summary>
        Compact,
    }

    public class AutoPoint
    {
    }
}
