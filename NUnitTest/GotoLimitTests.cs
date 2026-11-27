using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShaoLu.Viewmodels;
using ShaoLu.Viewmodels.AutomationStep;

namespace NUnitTest
{
    /// <summary>
    /// 跳转解析：跳转次数 / 超时到达上限后的处理（「上限后跳到」与兼容老行为的改走另一分支），
    /// 以及自引用（跳回自己）不再单独设上限、同样受跳转次数保护。
    /// </summary>
    [TestFixture]
    public class GotoLimitTests
    {
        private static List<AutomationStepBase> Steps(params AutomationStepBase[] steps) => new List<AutomationStepBase>(steps);

        private static EmptyStep NewStep(string name) => new EmptyStep { Name = name };

        [Test]
        public void Goto_WithoutLimit_JumpsToTarget()
        {
            var first = NewStep("1");
            var target = NewStep("2");
            var steps = Steps(first, target);
            first.TrueGotoUid = target.Uid;
            first.IsTrue = true;

            int next = StepsViewModel.ResolveNextIndex(first, 0, steps);

            Assert.That(next, Is.EqualTo(1));
        }

        [Test]
        public void LimitReached_WithLimitTarget_JumpsThere()
        {
            var first = NewStep("1");
            var loopTarget = NewStep("2");
            var escape = NewStep("3");
            var steps = Steps(first, loopTarget, escape);

            first.TrueGotoUid = loopTarget.Uid;
            first.TrueGotoMaxCount = 1;
            first.GotoLimitGotoUid = escape.Uid;
            first.IsTrue = true;

            Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(1), "第一次还在上限内，照常跳");

            int next = StepsViewModel.ResolveNextIndex(first, 0, steps);

            Assert.That(next, Is.EqualTo(2), "到达跳转上限后应该跳到「上限后跳到」指定的步骤");
            Assert.That(first.TrueGotoCount, Is.EqualTo(0), "跳出后计数重新开始");
        }

        [Test]
        public void LimitReached_WithoutLimitTarget_FallsBackToOtherBranch()
        {
            var first = NewStep("1");
            var trueTarget = NewStep("2");
            var falseTarget = NewStep("3");
            var steps = Steps(first, trueTarget, falseTarget);

            first.TrueGotoUid = trueTarget.Uid;
            first.FalseGotoUid = falseTarget.Uid;
            first.TrueGotoMaxCount = 0;      // 一开始就到上限
            first.IsTrue = true;

            int next = StepsViewModel.ResolveNextIndex(first, 0, steps);

            Assert.That(next, Is.EqualTo(2), "没配「上限后跳到」时保持老行为：改走另一分支");
            Assert.That(first.IsTrue, Is.False, "走另一分支后自身结果按该分支记");
        }

        [Test]
        public void LimitReached_NoTargetAndNoOtherBranch_StopsJumping()
        {
            var first = NewStep("1");
            var trueTarget = NewStep("2");
            var steps = Steps(first, trueTarget);

            first.TrueGotoUid = trueTarget.Uid;
            first.TrueGotoMaxCount = 0;
            first.IsTrue = true;

            int next = StepsViewModel.ResolveNextIndex(first, 0, steps);

            Assert.That(next, Is.EqualTo(-1), "两边都跳不出去就顺序执行下一步");
            Assert.That(first.ErrorMessage, Is.Not.Empty);
        }

        [Test]
        public void SelfJump_IsLimitedByGotoCountOnly()
        {
            var first = NewStep("1");
            var escape = NewStep("2");
            var steps = Steps(first, escape);

            first.TrueGotoUid = first.Uid;    // 跳回自己
            first.TrueGotoMaxCount = 2;
            first.GotoLimitGotoUid = escape.Uid;
            first.IsTrue = true;

            Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(0), "第一次自引用照常跳回自己");
            Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(0), "第二次仍在次数上限内");
            Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(1),
                "自引用不再单独设上限，到达跳转次数上限后按「上限后跳到」处理");
        }

        [Test]
        public void SelfJump_UnlimitedCount_KeepsLooping()
        {
            var first = NewStep("1");
            var steps = Steps(first);

            first.TrueGotoUid = first.Uid;
            first.TrueGotoMaxCount = -1;      // 不限制次数
            first.TrueGotoTimeoutSeconds = 0; // 也不限时
            first.IsTrue = true;

            for (int i = 0; i < 20; i++)
                Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(0));
            Assert.That(first.TrueGotoCount, Is.EqualTo(20), "自引用也记进跳转次数");
        }

        [Test]
        public void TimeoutReached_WithLimitTarget_JumpsThere()
        {
            var first = NewStep("1");
            var loopTarget = NewStep("2");
            var escape = NewStep("3");
            var steps = Steps(first, loopTarget, escape);

            first.TrueGotoUid = loopTarget.Uid;
            first.TrueGotoTimeoutSeconds = 0.001;
            first.GotoLimitGotoUid = escape.Uid;
            first.IsTrue = true;

            StepsViewModel.ResolveNextIndex(first, 0, steps);   // 开始计时
            System.Threading.Thread.Sleep(30);

            Assert.That(StepsViewModel.ResolveNextIndex(first, 0, steps), Is.EqualTo(2),
                "跳转超时后同样按「上限后跳到」处理");
        }
    }
}
