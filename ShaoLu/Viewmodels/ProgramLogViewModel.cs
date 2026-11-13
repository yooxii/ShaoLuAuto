using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Services;
using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShaoLu.Viewmodels
{
    /// <summary>
    /// 程序日志页签：读取 NLog 写出的 <c>logs\{日期}.log</c>，按时间从新到旧以文本方式展示，
    /// 支持按等级（该等级及以上）与关键字筛选，运行中自动刷新。
    /// </summary>
    public class ProgramLogViewModel : ObservableObject
    {
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
        private List<ProgramLogEntry> _entries = [];
        private DateTime _lastWriteTime;
        private long _lastLength;
        private bool _isActive;

        private string _selectedFile;
        private string _selectedLevel;
        private string _keyword;
        private string _logText = string.Empty;
        private long _totalCount;

        public ProgramLogViewModel()
        {
            Files = [];
            Levels = [];

            AllLevelsLabel = LanguageService.GetLocalizedString("ProgramLog_AllLevels", "全部等级");
            Levels.Add(AllLevelsLabel);
            foreach (var level in ProgramLogService.Levels)
                Levels.Add(level);
            _selectedLevel = AllLevelsLabel;

            ReloadCommand = new RelayCommand(() => Reload(true));

            // 页签可见时自动刷新：文件没变化就不重读，避免空转
            _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            _refreshTimer.Tick += (_, __) => Reload(false);
        }

        #region 属性

        /// <summary>日志文件（按日期，新的在前）</summary>
        public ObservableCollection<string> Files { get; }

        /// <summary>等级选项：全部 + 各级别</summary>
        public ObservableCollection<string> Levels { get; }

        /// <summary>「全部等级」的显示文本（用于判断当前是否不筛选）</summary>
        public string AllLevelsLabel { get; }

        /// <summary>当前选中的日志文件（文件名，如 2026-11-13.log）</summary>
        public string SelectedFile
        {
            get => _selectedFile;
            set
            {
                if (SetProperty(ref _selectedFile, value))
                    Reload(true);
            }
        }

        /// <summary>当前选中的等级（选中项表示「该等级及以上」）</summary>
        public string SelectedLevel
        {
            get => _selectedLevel;
            set
            {
                if (SetProperty(ref _selectedLevel, value))
                    Render();
            }
        }

        /// <summary>关键字（匹配记录器与日志内容）</summary>
        public string Keyword
        {
            get => _keyword;
            set
            {
                if (SetProperty(ref _keyword, value))
                    Render();
            }
        }

        /// <summary>文本视图内容：一行一条程序日志</summary>
        public string LogText
        {
            get => _logText;
            set => SetProperty(ref _logText, value);
        }

        /// <summary>当前筛选出的条数</summary>
        public long TotalCount
        {
            get => _totalCount;
            set => SetProperty(ref _totalCount, value);
        }

        public RelayCommand ReloadCommand { get; }

        #endregion

        #region 页签激活 / 刷新

        /// <summary>页签被切到前台：刷新并开始自动刷新</summary>
        public void Activate()
        {
            _isActive = true;
            LoadFiles();
            Reload(true);
            if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
        }

        /// <summary>页签被切走：停止自动刷新</summary>
        public void Deactivate()
        {
            _isActive = false;
            _refreshTimer?.Stop();
        }

        /// <summary>刷新文件列表并尽量保留当前选择</summary>
        private void LoadFiles()
        {
            var files = ProgramLogService.GetLogFiles()
                .Select(System.IO.Path.GetFileName)
                .ToList();

            bool same = files.Count == Files.Count
                        && files.SequenceEqual(Files, StringComparer.OrdinalIgnoreCase);
            if (!same)
            {
                string keep = SelectedFile;
                Files.Clear();
                foreach (var name in files)
                    Files.Add(name);

                if (!string.IsNullOrEmpty(keep) && Files.Contains(keep))
                    SetProperty(ref _selectedFile, keep, nameof(SelectedFile));
                else if (Files.Count > 0)
                    SetProperty(ref _selectedFile, Files[0], nameof(SelectedFile));
            }
        }

        /// <summary>读取当前文件并渲染（<paramref name="forceRead"/> 为 false 时文件没变化就跳过）</summary>
        private void Reload(bool forceRead)
        {
            LoadFiles();

            if (string.IsNullOrEmpty(SelectedFile))
            {
                _entries = [];
                Render();
                return;
            }

            string path = System.IO.Path.Combine(ProgramLogService.LogDirectory, SelectedFile);

            try
            {
                var info = new System.IO.FileInfo(path);
                if (!info.Exists)
                {
                    _entries = [];
                    Render();
                    return;
                }
                if (!forceRead && info.LastWriteTimeUtc == _lastWriteTime && info.Length == _lastLength)
                    return;

                _lastWriteTime = info.LastWriteTimeUtc;
                _lastLength = info.Length;
            }
            catch
            {
                // 取文件信息失败时照常读取（读取内部还有兜底）
            }

            _entries = ProgramLogService.Read(path);
            Render();
        }

        /// <summary>按等级与关键字过滤后生成文本</summary>
        private void Render()
        {
            IEnumerable<ProgramLogEntry> query = _entries ?? [];

            if (!string.IsNullOrEmpty(SelectedLevel) && SelectedLevel != AllLevelsLabel)
            {
                int minRank = ProgramLogService.LevelRank(SelectedLevel);
                query = query.Where(e => ProgramLogService.LevelRank(e.Level) >= minRank);
            }

            string keyword = Keyword?.Trim();
            if (!string.IsNullOrEmpty(keyword))
            {
                query = query.Where(e =>
                    (e.Message ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || (e.Logger ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0
                    || (e.Level ?? string.Empty).IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            var list = query.ToList();
            TotalCount = list.Count;

            if (list.Count == 0)
            {
                LogText = _entries == null || _entries.Count == 0
                    ? LanguageService.GetLocalizedString("ProgramLog_Empty", "暂无程序日志")
                    : LanguageService.GetLocalizedString("ProgramLog_NoMatch", "没有符合条件的日志");
                return;
            }

            var builder = new System.Text.StringBuilder();
            foreach (var entry in list)
            {
                if (builder.Length > 0) builder.AppendLine();
                builder.Append(ProgramLogService.FormatLine(entry));
            }
            LogText = builder.ToString();
        }

        #endregion
    }
}
