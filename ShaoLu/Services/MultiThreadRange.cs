using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;

namespace ShaoLu.Services
{
    /// <summary>
    /// 多线程区域解析器（纯函数，可脱离 UI 测试）。
    /// 区域不含「多线程开始」步骤本身，endIndex 为区域内最后一个步骤的索引；
    /// 两种划定方式都按 mode 判定，另一种配置被忽略且不算错误。
    /// </summary>
    public static class MultiThreadRange
    {
        /// <summary>
        /// 解析「多线程开始」步骤圈定的区域。
        /// </summary>
        /// <param name="steps">完整步骤集合</param>
        /// <param name="startIndex">「多线程开始」步骤的索引</param>
        /// <param name="mode">区域划定方式</param>
        /// <param name="rangeEndUid">ByLineNo 方式下的结束步骤 Uid（null = 到脚本末尾）</param>
        /// <param name="endIndex">成功时为区域内最后一个步骤的索引，失败为 -1</param>
        /// <param name="error">失败原因</param>
        public static bool TryResolve(IReadOnlyList<AutomationStepBase> steps, int startIndex, MultiThreadRangeMode mode, Guid? rangeEndUid, out int endIndex, out string error)
        {
            endIndex = -1;
            error = null;

            if (steps == null)
            {
                error = LanguageService.GetLocalizedString("Msg_MtRangeEmpty", "步骤列表为空，无法解析多线程区域");
                return false;
            }
            if (startIndex < 0 || startIndex >= steps.Count)
            {
                error = LanguageService.GetLocalizedString("Msg_MtRangeStartIndex", "多线程开始步骤索引越界：{0}", startIndex);
                return false;
            }

            if (mode == MultiThreadRangeMode.ByLineNo)
            {
                if (!rangeEndUid.HasValue || rangeEndUid.Value == Guid.Empty)
                {
                    // 未指定结束步骤：区域到脚本末尾
                    endIndex = steps.Count - 1;
                    return true;
                }

                int end = IndexOfUid(steps, rangeEndUid.Value);
                if (end < 0)
                {
                    error = LanguageService.GetLocalizedString("Msg_MtRangeEndMissing", "结束步骤不存在（可能已被删除）");
                    return false;
                }
                if (end <= startIndex)
                {
                    error = LanguageService.GetLocalizedString("Msg_MtRangeEndAfterStart", "结束步骤必须位于多线程开始步骤之后");
                    return false;
                }

                endIndex = end;
                return true;
            }

            // ByEndStep：取开始步骤之后最近的「多线程区域结束」标记，标记本身不执行
            for (int i = startIndex + 1; i < steps.Count; i++)
            {
                if (steps[i] is MultiThreadEndStep)
                {
                    endIndex = i - 1;
                    return true;
                }
            }

            error = LanguageService.GetLocalizedString("Msg_MtRangeEndNotPaired", "未找到配对的「多线程区域结束」步骤");
            return false;
        }

        private static int IndexOfUid(IReadOnlyList<AutomationStepBase> steps, Guid uid)
        {
            for (int i = 0; i < steps.Count; i++)
            {
                if (steps[i] != null && steps[i].Uid == uid) return i;
            }
            return -1;
        }
    }
}
