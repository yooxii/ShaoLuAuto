using NUnit.Framework;
using ShaoLu.Utils;

namespace NUnitTest
{
    /// <summary>
    /// 扫码窗口的「扫完自动确认」节奏判定：只要字符够多且节奏够快才认定是扫码枪，
    /// 人手逐字输入不能被误判，否则窗口会在人还没输完时自己关掉。
    /// </summary>
    [TestFixture]
    public class ScanBurstTrackerTests
    {
        [Test]
        public void 扫码枪节奏_快速成串_判定为扫码枪()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < 10; i++)
                tracker.OnChar(i * 10L);

            Assert.That(tracker.LooksLikeScanner(), Is.True);
        }

        [Test]
        public void 人手输入_节奏慢_不判定为扫码枪()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < 8; i++)
                tracker.OnChar(i * 220L);

            Assert.That(tracker.LooksLikeScanner(), Is.False);
        }

        [Test]
        public void 字符太少_不自动确认()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < ScanBurstTracker.MinChars - 1; i++)
                tracker.OnChar(i * 5L);

            Assert.That(tracker.LooksLikeScanner(), Is.False);
        }

        [Test]
        public void 中途长时间停顿_重新起串并按新串判断()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < 3; i++)
                tracker.OnChar(i * 10L);

            // 停顿超过起串阈值后再来两个字符：算新的一串，字符数不够就不自动确认
            tracker.OnChar(1000L);
            tracker.OnChar(1010L);

            Assert.That(tracker.CharCount, Is.EqualTo(2));
            Assert.That(tracker.LooksLikeScanner(), Is.False);
        }

        [Test]
        public void 同一毫秒内打完_判定为扫码枪()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < 6; i++)
                tracker.OnChar(42L);

            Assert.That(tracker.LooksLikeScanner(), Is.True);
        }

        [Test]
        public void 重置后不沿用上一串统计()
        {
            var tracker = new ScanBurstTracker();
            for (int i = 0; i < 10; i++)
                tracker.OnChar(i * 5L);

            tracker.Reset();

            Assert.That(tracker.CharCount, Is.EqualTo(0));
            Assert.That(tracker.LooksLikeScanner(), Is.False);
        }
    }
}
