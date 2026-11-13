using System;

namespace ShaoLu.Utils
{
    /// <summary>
    /// 扫码枪输入节奏判定：把逐字符到达的时间串成「一串」，
    /// 输入停下后判断这一串是不是扫码枪打进来的（字符够多、平均间隔够短）。
    /// 人手逐字输入的平均间隔远大于阈值，因此不会被误判成扫码枪。
    /// </summary>
    internal sealed class ScanBurstTracker
    {
        /// <summary>字符间隔超过该毫秒数，视为另一串输入（重新开始统计）。</summary>
        internal const int BurstSplitMs = 400;

        /// <summary>至少这么多字符才可能是扫码枪输入。</summary>
        internal const int MinChars = 5;

        /// <summary>这一串字符的平均间隔不超过该毫秒数，才认定为扫码枪。</summary>
        internal const double MaxAvgGapMs = 120;

        private long _startMs;
        private long _lastMs;
        private int _chars;

        /// <summary>当前这一串已经累计的字符数。</summary>
        public int CharCount => _chars;

        /// <summary>清空统计（粘贴、确认、取消后调用）。</summary>
        public void Reset()
        {
            _startMs = 0;
            _lastMs = 0;
            _chars = 0;
        }

        /// <summary>记录一次字符输入（nowMs 为单调递增的毫秒数）。</summary>
        public void OnChar(long nowMs, int length = 1)
        {
            if (length <= 0) return;

            // 第一字符，或间隔过大：从这次输入重新起一串
            if (_lastMs == 0 || nowMs - _lastMs > BurstSplitMs)
            {
                _startMs = nowMs;
                _chars = 0;
            }

            _chars += length;
            _lastMs = nowMs;
        }

        /// <summary>输入停下后判断：字符够多且平均间隔够短，才认定是扫码枪。</summary>
        public bool LooksLikeScanner()
        {
            if (_chars < MinChars) return false;

            long span = _lastMs - _startMs;
            // 同一毫秒内打完（span = 0）本就是机器节奏
            if (span <= 0) return true;

            return (double)span / (_chars - 1) <= MaxAvgGapMs;
        }
    }
}
