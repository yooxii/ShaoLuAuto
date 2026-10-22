using CommunityToolkit.Mvvm.ComponentModel;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShaoLu.Viewmodels
{
    public class ExecutionLogViewModel : ObservableObject
    {
        private string _searchKeyword;
        private string _selectedFileName;
        private DateTime? _dateFrom;
        private DateTime? _dateTo;
        private bool _isFilterExpanded;
        private string _sortColumn = "ExecutedAt";
        private bool _sortDescending = true;
        private int _currentPage = 1;
        private int _pageSize = 100;
        private long _totalCount;
        private readonly System.Windows.Threading.DispatcherTimer _refreshTimer;
        private bool _refreshPending;

        public ExecutionLogViewModel()
        {
            Logs = [];
            FileNames = [];

            SearchCommand = new RelayCommand(DoSearch);
            ResetCommand = new RelayCommand(DoReset);
            PrevPageCommand = new RelayCommand(DoPrevPage, () => CurrentPage > 1);
            NextPageCommand = new RelayCommand(DoNextPage, () => CurrentPage < TotalPages);

            LoadFileNames();
            DoSearch();

            // 运行中实时刷新：新日志写入后防抖重查（仅在用户停留第 1 页时刷新，避免打断翻历史）
            _refreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
            _refreshTimer.Tick += (s, e) =>
            {
                _refreshTimer.Stop();
                if (_refreshPending)
                {
                    _refreshPending = false;
                    DoSearch();
                }
            };
            ExecutionLogService.LogWritten += OnLogWritten;
        }

        private void OnLogWritten(StepExecutionLog log)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                if (CurrentPage != 1) return;
                _refreshPending = true;
                if (!_refreshTimer.IsEnabled) _refreshTimer.Start();
            }));
        }

        /// <summary>窗口关闭时取消订阅，避免静态事件持有 ViewModel。</summary>
        public void StopWatching()
        {
            try { ExecutionLogService.LogWritten -= OnLogWritten; } catch { }
            _refreshTimer?.Stop();
        }

        #region 属性

        public ObservableCollection<StepExecutionLog> Logs { get; }

        public ObservableCollection<string> FileNames { get; }

        /// <summary>
        /// 筛选区域是否展开
        /// </summary>
        public bool IsFilterExpanded { get => _isFilterExpanded; set => SetProperty(ref _isFilterExpanded, value); }

        /// <summary>
        /// 搜索关键字（匹配 InputText 或 StepName）
        /// </summary>
        public string SearchKeyword { get => _searchKeyword; set => SetProperty(ref _searchKeyword, value); }

        /// <summary>
        /// 筛选：步骤文件名
        /// </summary>
        public string SelectedFileName { get => _selectedFileName; set => SetProperty(ref _selectedFileName, value); }

        /// <summary>
        /// 筛选：起始日期
        /// </summary>
        public DateTime? DateFrom { get => _dateFrom; set => SetProperty(ref _dateFrom, value); }

        /// <summary>
        /// 筛选：结束日期
        /// </summary>
        public DateTime? DateTo { get => _dateTo; set => SetProperty(ref _dateTo, value); }

        /// <summary>
        /// 排序列名
        /// </summary>
        public string SortColumn { get => _sortColumn; set => SetProperty(ref _sortColumn, value); }

        /// <summary>
        /// 是否降序
        /// </summary>
        public bool SortDescending { get => _sortDescending; set => SetProperty(ref _sortDescending, value); }

        /// <summary>
        /// 当前页码
        /// </summary>
        public int CurrentPage
        {
            get => _currentPage;
            set
            {
                if (SetProperty(ref _currentPage, value))
                {
                    PrevPageCommand.RaiseCanExecuteChanged();
                    NextPageCommand.RaiseCanExecuteChanged();
                }
            }
        }

        public int PageSize { get => _pageSize; set => SetProperty(ref _pageSize, value); }

        /// <summary>
        /// 总记录数
        /// </summary>
        public long TotalCount { get => _totalCount; set => SetProperty(ref _totalCount, value); }

        /// <summary>
        /// 总页数
        /// </summary>
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

        #endregion

        #region 命令

        public RelayCommand SearchCommand { get; }
        public RelayCommand ResetCommand { get; }
        public RelayCommand PrevPageCommand { get; }
        public RelayCommand NextPageCommand { get; }

        #endregion

        #region 方法

        private void LoadFileNames()
        {
            FileNames.Clear();
            try
            {
                var names = ExecutionLogService.GetDistinctFileNames();
                foreach (var name in names)
                {
                    FileNames.Add(name);
                }

                // 日志按步骤文件分表保存：默认选中当前工程文件，避免窗口只显示主表里的历史数据
                try
                {
                    var current = System.IO.Path.GetFileNameWithoutExtension(
                        ShaoLu.Utils.SingletonLocator.Main?.StepFilePath ?? string.Empty);
                    if (!string.IsNullOrEmpty(current) && FileNames.Contains(current))
                        SelectedFileName = current;
                }
                catch { }
            }
            catch { /* 数据库尚未创建时忽略 */ }
        }

        private void DoSearch()
        {
            CurrentPage = 1;
            ExecuteQuery();
        }

        private void DoReset()
        {
            SearchKeyword = null;
            SelectedFileName = null;
            DateFrom = null;
            DateTo = null;
            SortColumn = "ExecutedAt";
            SortDescending = true;
            CurrentPage = 1;
            ExecuteQuery();
        }

        private void DoPrevPage()
        {
            if (CurrentPage > 1)
            {
                CurrentPage--;
                ExecuteQuery();
            }
        }

        private void DoNextPage()
        {
            if (CurrentPage < TotalPages)
            {
                CurrentPage++;
                ExecuteQuery();
            }
        }

        /// <summary>
        /// 切换排序（由 DataGrid 列头点击触发）
        /// </summary>
        public void ToggleSort(string columnName)
        {
            if (SortColumn == columnName)
            {
                SortDescending = !SortDescending;
            }
            else
            {
                SortColumn = columnName;
                SortDescending = true;
            }
            CurrentPage = 1;
            ExecuteQuery();
        }

        private void ExecuteQuery()
        {
            try
            {
                string fileName = string.IsNullOrWhiteSpace(SelectedFileName) ? null : SelectedFileName;

                // 时间区间统一为半开区间 [from, to+1)：结束日期当天的数据也要能看到
                DateTime? toExclusive = DateTo?.Date.AddDays(1);

                TotalCount = ExecutionLogService.QueryCount(
                    keyword: SearchKeyword,
                    stepFileName: fileName,
                    from: DateFrom,
                    to: toExclusive);

                var results = ExecutionLogService.Query(
                    keyword: SearchKeyword,
                    stepFileName: fileName,
                    from: DateFrom,
                    to: toExclusive,
                    orderBy: SortColumn,
                    descending: SortDescending,
                    page: CurrentPage,
                    pageSize: PageSize);

                // 生成展示列（结果 / 执行时间 / 线程）并归并 10 秒内的连续相同结果
                ExecutionLogDisplay.Build(results);

                Logs.Clear();
                foreach (var log in results)
                {
                    // 被归并进上一行的重复记录不再显示，重复次数由组首的「 ×N」体现
                    if (log.IsMergedAway)
                        continue;

                    Logs.Add(log);
                }

                // 通知 TotalPages 变更
                OnPropertyChanged(nameof(TotalPages));
                PrevPageCommand.RaiseCanExecuteChanged();
                NextPageCommand.RaiseCanExecuteChanged();
            }
            catch (Exception ex)
            {
                NLog.LogManager.GetCurrentClassLogger().Error(ex, "Failed to query execution logs");
            }
        }

        #endregion
    }
}
