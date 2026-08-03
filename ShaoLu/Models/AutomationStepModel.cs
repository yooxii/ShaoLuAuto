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
        TextOCR = 200,
        MouseAction = 201,
        Statistics = 202,
        Popup = 1000,
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
    /// 弹出窗口关闭模式
    /// </summary>
    public enum PopupCloseMode
    {
        /// <summary>默认：用户点击按钮关闭</summary>
        ButtonClick,
        /// <summary>按时间自动关闭</summary>
        Timeout,
        /// <summary>到某个步骤时关闭</summary>
        StepReached,
    }

    public class AutoPoint
    {
    }
}
