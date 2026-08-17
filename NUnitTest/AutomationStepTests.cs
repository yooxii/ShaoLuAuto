using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;

namespace NUnitTest
{
    /// <summary>
    /// 自动化步骤类的单元测试（构造函数、Clone、属性默认值）
    /// </summary>
    [TestFixture]
    public class AutomationStepTests
    {
        #region TypeTextStep

        [TestFixture]
        public class TypeTextStepTests
        {
            [Test]
            public void DefaultConstructor_SetsTypeCorrectly()
            {
                var step = new TypeTextStep();
                Assert.That(step.Type, Is.EqualTo(StepType.TypeText));
            }

            [Test]
            public void ConstructorWithName_SetsNameAndType()
            {
                var step = new TypeTextStep("输入测试");
                Assert.That(step.Name, Is.EqualTo("输入测试"));
                Assert.That(step.Type, Is.EqualTo(StepType.TypeText));
            }

            [Test]
            public void ConstructorWithNameAndDescription_SetsAll()
            {
                var step = new TypeTextStep("输入测试", "这是描述");
                Assert.That(step.Name, Is.EqualTo("输入测试"));
                Assert.That(step.Description, Is.EqualTo("这是描述"));
                Assert.That(step.Type, Is.EqualTo(StepType.TypeText));
            }

            [Test]
            public void DefaultProperties_AreCorrect()
            {
                var step = new TypeTextStep();
                Assert.That(step.IsNeed, Is.True);
                Assert.That(step.IsSave, Is.False);
                Assert.That(step.IsError, Is.False);
                Assert.That(step.IsTrue, Is.False);
                Assert.That(step.WaitTime, Is.EqualTo(0.1));
                Assert.That(step.DelayBetweenKeys, Is.EqualTo(0.05));
                Assert.That(step.SelfReferenceLimit, Is.EqualTo(10));
                Assert.That(step.ConditionMode, Is.EqualTo(ConditionMode.Default));
                Assert.That(step.EnableLog, Is.False);
            }

            [Test]
            public void Clone_CreatesIndependentCopy()
            {
                var original = new TypeTextStep("Test", "Desc")
                {
                    TextToType = "Hello World",
                    DelayBetweenKeys = 0.1,
                    WaitTime = 0.5,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000003"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000005"),
                    IsNeed = false,
                    EnableLog = true
                };

                var clone = (TypeTextStep)original.Clone();

                Assert.That(clone.Name, Is.EqualTo("Test"));
                Assert.That(clone.Description, Is.EqualTo("Desc"));
                Assert.That(clone.TextToType, Is.EqualTo("Hello World"));
                Assert.That(clone.DelayBetweenKeys, Is.EqualTo(0.1));
                Assert.That(clone.WaitTime, Is.EqualTo(0.5));
                Assert.That(clone.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000003")));
                Assert.That(clone.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000005")));
                Assert.That(clone.IsNeed, Is.False);
                Assert.That(clone.EnableLog, Is.True);
                Assert.That(clone.Type, Is.EqualTo(StepType.TypeText));
            }

            [Test]
            public void Clone_ModifyingClone_DoesNotAffectOriginal()
            {
                var original = new TypeTextStep("Original")
                {
                    TextToType = "ABC"
                };

                var clone = (TypeTextStep)original.Clone();
                clone.TextToType = "XYZ";
                clone.Name = "Modified";

                Assert.That(original.TextToType, Is.EqualTo("ABC"));
                Assert.That(original.Name, Is.EqualTo("Original"));
            }

            [Test]
            public void Uid_IsUnique()
            {
                var step1 = new TypeTextStep();
                var step2 = new TypeTextStep();
                Assert.That(step1.Uid, Is.Not.EqualTo(step2.Uid));
            }

            [Test]
            public void Name_NullValue_ThrowsArgumentNullException()
            {
                var step = new TypeTextStep();
                Assert.Throws<ArgumentNullException>((TestDelegate)(() => step.Name = null));
            }
        }

        #endregion

        #region TypeTextMoreStep

        [TestFixture]
        public class TypeTextMoreStepTests
        {
            [Test]
            public void DefaultConstructor_SetsTypeCorrectly()
            {
                var step = new TypeTextMoreStep();
                Assert.That(step.Type, Is.EqualTo(StepType.TypeTextMore));
            }

            [Test]
            public void PrefixInfixSuffix_CombinesToTextToType()
            {
                var step = new TypeTextMoreStep();
                step.Prefix = "SN";
                step.Infix = "001";
                step.Suffix = "END";

                Assert.That(step.TextToType, Is.EqualTo("SN001END"));
            }

            [Test]
            public void Clone_CopiesAllProperties()
            {
                var original = new TypeTextMoreStep("More", "Desc")
                {
                    Prefix = "PRE",
                    Infix = "MID",
                    Suffix = "SUF",
                    Prefix_gen = true,
                    Infix_gen = false,
                    Suffix_gen = true,
                    ReloadText = true,
                    WaitTime = 1.0,
                    DelayBetweenKeys = 0.02,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000004"),
                    IsNeed = false,
                    EnableLog = true
                };

                var clone = (TypeTextMoreStep)original.Clone();

                Assert.That(clone.Name, Is.EqualTo("More"));
                Assert.That(clone.Prefix, Is.EqualTo("PRE"));
                Assert.That(clone.Infix, Is.EqualTo("MID"));
                Assert.That(clone.Suffix, Is.EqualTo("SUF"));
                Assert.That(clone.Prefix_gen, Is.True);
                Assert.That(clone.Infix_gen, Is.False);
                Assert.That(clone.Suffix_gen, Is.True);
                Assert.That(clone.ReloadText, Is.True);
                Assert.That(clone.WaitTime, Is.EqualTo(1.0));
                Assert.That(clone.DelayBetweenKeys, Is.EqualTo(0.02));
                Assert.That(clone.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000002")));
                Assert.That(clone.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000004")));
                Assert.That(clone.IsNeed, Is.False);
                Assert.That(clone.EnableLog, Is.True);
            }

            [Test]
            public void Reload_ResetsToOriginalValues()
            {
                var step = new TypeTextMoreStep();
                step.Prefix = "ABC";
                step.Infix = "123";
                step.Suffix = "XYZ";

                // 模拟修改内部值后 Reload
                step.Reload();

                Assert.That(step.TextToType, Is.EqualTo("ABC123XYZ"));
            }
        }

        #endregion

        #region EmptyStep

        [TestFixture]
        public class EmptyStepTests
        {
            [Test]
            public void DefaultConstructor_SetsTypeAndIsTrue()
            {
                var step = new EmptyStep();
                Assert.That(step.Type, Is.EqualTo(StepType.Empty));
                Assert.That(step.IsTrue, Is.True);
            }

            [Test]
            public void ConstructorWithName_SetsName()
            {
                var step = new EmptyStep("空步骤");
                Assert.That(step.Name, Is.EqualTo("空步骤"));
                Assert.That(step.IsTrue, Is.True);
            }

            [Test]
            public void Clone_CreatesCopy()
            {
                var original = new EmptyStep("Empty", "Desc")
                {
                    WaitTime = 2.0,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000001"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000002"),
                    IsNeed = false
                };

                var clone = (EmptyStep)original.Clone();

                Assert.That(clone.Name, Is.EqualTo("Empty"));
                Assert.That(clone.Description, Is.EqualTo("Desc"));
                Assert.That(clone.WaitTime, Is.EqualTo(2.0));
                Assert.That(clone.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000001")));
                Assert.That(clone.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000002")));
                Assert.That(clone.IsNeed, Is.False);
                Assert.That(clone.IsTrue, Is.True);
            }
        }

        #endregion

        #region PopupStep

        [TestFixture]
        public class PopupStepTests
        {
            [Test]
            public void DefaultConstructor_SetsType()
            {
                var step = new PopupStep();
                Assert.That(step.Type, Is.EqualTo(StepType.Popup));
            }

            [Test]
            public void ConstructorWithName_SetsNameAndTitle()
            {
                var step = new PopupStep("确认框");
                Assert.That(step.Name, Is.EqualTo("确认框"));
                Assert.That(step.Title, Is.EqualTo("确认框"));
            }

            [Test]
            public void DefaultProperties()
            {
                var step = new PopupStep();
                Assert.That(step.PopupType, Is.EqualTo("Information"));
                Assert.That(step.PopupTypes, Does.Contain("None"));
                Assert.That(step.PopupTypes, Does.Contain("Information"));
                Assert.That(step.PopupTypes, Does.Contain("Warning"));
                Assert.That(step.PopupTypes, Does.Contain("Error"));
                Assert.That(step.PopupTypes, Does.Contain("Question"));
                Assert.That(step.PopupTypes.Count, Is.EqualTo(5));
            }

            [Test]
            public void Clone_CopiesProperties()
            {
                var original = new PopupStep("Popup", "Desc")
                {
                    Title = "标题",
                    PopupText = "内容",
                    PopupType = "Warning",
                    WaitTime = 0.5,
                    IsNeed = true,
                    TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000010"),
                    FalseGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000020")
                };

                var clone = (PopupStep)original.Clone();

                Assert.That(clone.Name, Is.EqualTo("Popup"));
                Assert.That(clone.Title, Is.EqualTo("标题"));
                Assert.That(clone.PopupText, Is.EqualTo("内容"));
                Assert.That(clone.PopupType, Is.EqualTo("Warning"));
                Assert.That(clone.WaitTime, Is.EqualTo(0.5));
                Assert.That(clone.IsNeed, Is.True);
                Assert.That(clone.TrueGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000010")));
                Assert.That(clone.FalseGotoUid, Is.EqualTo(Guid.Parse("00000000-0000-0000-0000-000000000020")));
            }
        }

        #endregion

        #region AutomationStepBase 通用属性

        [TestFixture]
        public class AutomationStepBaseTests
        {
            [Test]
            public void Conditions_DefaultEmpty()
            {
                var step = new TypeTextStep();
                Assert.That(step.Conditions, Is.Not.Null);
                Assert.That(step.Conditions.Count, Is.EqualTo(0));
            }

            [Test]
            public void AddConditionCommand_AddsCondition()
            {
                var step = new TypeTextStep();
                step.AddConditionCommand.Execute(null);

                Assert.That(step.Conditions.Count, Is.EqualTo(1));
            }

            [Test]
            public void RemoveConditionCommand_RemovesLastCondition()
            {
                var step = new TypeTextStep();
                step.AddConditionCommand.Execute(null);
                step.AddConditionCommand.Execute(null);
                Assert.That(step.Conditions.Count, Is.EqualTo(2));

                step.RemoveConditionCommand.Execute(null);
                Assert.That(step.Conditions.Count, Is.EqualTo(1));
            }

            [Test]
            public void RemoveConditionCommand_OnEmpty_DoesNotThrow()
            {
                var step = new TypeTextStep();
                Assert.DoesNotThrow((TestDelegate)(() => step.RemoveConditionCommand.Execute(null)));
                Assert.That(step.Conditions.Count, Is.EqualTo(0));
            }

            [Test]
            public void ErrorType_DefaultIsNone()
            {
                var step = new TypeTextStep();
                Assert.That(step.ErrorType, Is.EqualTo(StepErrorType.None));
            }

            [Test]
            public void SelfReferenceCount_DefaultIsZero()
            {
                var step = new TypeTextStep();
                Assert.That(step.SelfReferenceCount, Is.EqualTo(0));
            }

            [Test]
            public void LastResult_DefaultIsNull()
            {
                var step = new TypeTextStep();
                Assert.That(step.LastResult, Is.Null);
            }
        }

        #endregion

        #region MouseActionItem / DragPathPoint

        [TestFixture]
        public class MouseActionItemTests
        {
            [Test]
            public void AllTypes_ContainsDrag()
            {
                Assert.That(MouseActionItem.AllTypes, Does.Contain(MouseActionType.Drag));
            }

            [Test]
            public void DragPath_DefaultEmpty()
            {
                var item = new MouseActionItem();
                Assert.That(item.DragPath, Is.Not.Null);
                Assert.That(item.DragPath.Count, Is.EqualTo(0));
                Assert.That(item.ClickDragTrajectories, Is.Not.Null);
                Assert.That(item.ClickDragTrajectories.Count, Is.EqualTo(0));
            }

            [Test]
            public void Clone_CopiesDragPath_Independent()
            {
                var item = new MouseActionItem { ActionType = MouseActionType.Drag, Count = 2, Interval = 0.5 };
                item.DragPath.Add(new Point(100, 200));
                item.DragPath.Add(new Point(150, 250));
                item.ClickDragTrajectories.Add(new List<Point> { new Point(5, 6), new Point(7, 8) });

                var clone = item.Clone();

                Assert.That(clone.ActionType, Is.EqualTo(MouseActionType.Drag));
                Assert.That(clone.Count, Is.EqualTo(2));
                Assert.That(clone.Interval, Is.EqualTo(0.5));
                Assert.That(clone.DragPath.Count, Is.EqualTo(2));
                Assert.That(clone.DragPath[0].X, Is.EqualTo(100));
                Assert.That(clone.DragPath[1].Y, Is.EqualTo(250));
                Assert.That(clone.ClickDragTrajectories.Count, Is.EqualTo(1));
                Assert.That(clone.ClickDragTrajectories[0].Count, Is.EqualTo(2));
                Assert.That(clone.ClickDragTrajectories[0][0].X, Is.EqualTo(5));

                // 修改克隆不影响原始对象
                clone.DragPath[0].X = 999;
                clone.ClickDragTrajectories[0][0].X = 999;
                Assert.That(item.DragPath[0].X, Is.EqualTo(100));
                Assert.That(item.ClickDragTrajectories[0][0].X, Is.EqualTo(5));
            }
        }

        #endregion

        #region FocusWindowStep

        [TestFixture]
        public class FocusWindowStepTests
        {
            [Test]
            public void DefaultConstructor_SetsType()
            {
                var step = new FocusWindowStep();
                Assert.That(step.Type, Is.EqualTo(StepType.FocusWindow));
            }

            [Test]
            public void Defaults_AreCorrect()
            {
                var step = new FocusWindowStep();
                Assert.That(step.WindowTitleKeyword, Is.EqualTo(string.Empty));
                Assert.That(step.SelectedWindowTitle, Is.EqualTo(string.Empty));
                Assert.That(step.WindowTitles, Is.Not.Null);
            }

            [Test]
            public void Clone_CopiesProperties()
            {
                var step = new FocusWindowStep("Test")
                {
                    WindowTitleKeyword = "notepad",
                    SelectedWindowTitle = "Untitled - Notepad",
                };
                var clone = (FocusWindowStep)step.Clone();

                Assert.That(clone.Name, Is.EqualTo("Test"));
                Assert.That(clone.WindowTitleKeyword, Is.EqualTo("notepad"));
                Assert.That(clone.SelectedWindowTitle, Is.EqualTo("Untitled - Notepad"));
                Assert.That(clone.Type, Is.EqualTo(StepType.FocusWindow));
            }
        }

        #endregion
    }
}
