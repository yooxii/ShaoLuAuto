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

    public class AutoPoint
    {
    }
}
