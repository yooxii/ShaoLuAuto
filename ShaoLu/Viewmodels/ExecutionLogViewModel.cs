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

                TotalCount = ExecutionLogService.QueryCount(
                    keyword: SearchKeyword,
                    stepFileName: fileName,
                    from: DateFrom,
                    to: DateTo);

                var results = ExecutionLogService.Query(
                    keyword: SearchKeyword,
                    stepFileName: fileName,
                    from: DateFrom,
                    to: DateTo,
                    orderBy: SortColumn,
                    descending: SortDescending,
                    page: CurrentPage,
                    pageSize: PageSize);

                Logs.Clear();
                foreach (var log in results)
                {
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
