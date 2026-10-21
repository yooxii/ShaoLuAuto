using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Views;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace ShaoLu.Viewmodels
{
    /// <summary>
    /// 烧录统计窗口 ViewModel：承载工令汇总、明细列表、配置、导出
    /// </summary>
    public class BurnInViewModel : ObservableObject
    {
        private int _selectedTabIndex;
        private string _filterOrderNo;
        private DateTime? _filterFrom;
        private DateTime? _filterTo;
        private BurnResult? _filterResult;
        private string _statusText;

        /// <summary>明细每页条数（默认批次，滚动/加载更多时按页追加）</summary>
        private const int RecordPageSize = 200;

        /// <summary>明细当前已加载到的页码</summary>
        private int _recordPage = 1;

        /// <summary>明细过滤条件下的总条数（由 QueryCount 得到，不受已加载条数限制）</summary>
        private long _recordTotal;

        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
        public string FilterOrderNo { get => _filterOrderNo; set => SetProperty(ref _filterOrderNo, value); }
        public DateTime? FilterFrom { get => _filterFrom; set => SetProperty(ref _filterFrom, value); }
        public DateTime? FilterTo { get => _filterTo; set => SetProperty(ref _filterTo, value); }
        public BurnResult? FilterResult { get => _filterResult; set => SetProperty(ref _filterResult, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        /// <summary>明细过滤条件下的总条数</summary>
        public long RecordTotal
        {
            get => _recordTotal;
            private set
            {
                if (SetProperty(ref _recordTotal, value))
                {
                    OnPropertyChanged(nameof(HasMoreRecords));
                    LoadMoreRecordsCommand?.RaiseCanExecuteChanged();
                }
            }
        }

        /// <summary>是否还有未加载的明细（供"加载更多"使用）</summary>
        public bool HasMoreRecords => Records.Count < RecordTotal;

        /// <summary>
        /// 结束日期过滤的上界：半开区间 [from, to) 需要 +1 天，
        /// 这样汇总与明细口径一致，且能包含结束日当天的数据。
        /// </summary>
        private DateTime? FilterToExclusive => FilterTo?.Date.AddDays(1);

        /// <summary>工令汇总</summary>
        public ObservableCollection<BurnInService.OrderSummary> OrderSummaries { get; } = new();

        /// <summary>明细列表</summary>
        public ObservableCollection<BurnInRecord> Records { get; } = new();

        /// <summary>工令下拉列表（供汇总页双击跳转与明细页过滤）</summary>
        public ObservableCollection<string> OrderNoOptions { get; } = new();

        /// <summary>结果过滤选项（全部/良品/不良品/烧录失败）</summary>
        public ObservableCollection<string> ResultOptions { get; } = new()
        {
            LanguageService.GetLocalizedString("BurnIn_All"),
            LanguageService.GetLocalizedString("BurnIn_Good"),
            LanguageService.GetLocalizedString("BurnIn_Bad"),
            LanguageService.GetLocalizedString("BurnIn_BurnFailed"),
        };
        private string _selectedResult;
        public string SelectedResult
        {
            get => _selectedResult;
            set
            {
                if (SetProperty(ref _selectedResult, value))
                {
                    if (value == ResultOptions[1]) FilterResult = BurnResult.Good;
                    else if (value == ResultOptions[2]) FilterResult = BurnResult.Bad;
                    else if (value == ResultOptions[3]) FilterResult = BurnResult.BurnFailed;
                    else FilterResult = null;
                }
            }
        }

        #region 命令

        public ICommand RefreshSummariesCommand { get; }
        public ICommand RefreshRecordsCommand { get; }
        public ICommand ResetFilterCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportExcelCommand { get; }

        /// <summary>加载下一批明细（增量分页），已加载/总数见 StatusText</summary>
        public RelayCommand LoadMoreRecordsCommand { get; }

        #endregion

        public BurnInViewModel()
        {
            RefreshSummariesCommand = new RelayCommand(RefreshSummaries);
            RefreshRecordsCommand = new RelayCommand(RefreshRecords);
            ResetFilterCommand = new RelayCommand(ResetFilter);
            ExportCsvCommand = new RelayCommand(ExportCsv);
            ExportExcelCommand = new RelayCommand(ExportExcel);
            LoadMoreRecordsCommand = new RelayCommand(LoadMoreRecords, () => HasMoreRecords);

            _selectedResult = ResultOptions[0];
            OnPropertyChanged(nameof(SelectedResult));

            // 初始加载
            RefreshSummaries();
            RefreshOrderNoOptions();
        }

        public void RefreshSummaries()
        {
            try
            {
                OrderSummaries.Clear();
                // 半开区间 [from, to+1)：与明细口径一致，可包含结束日当天
                foreach (var s in BurnInService.GetOrderSummaries(FilterFrom, FilterToExclusive))
                    OrderSummaries.Add(s);
                StatusText = $"{LanguageService.GetLocalizedString("BurnIn_TotalOrders")}: {OrderSummaries.Count}";
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        /// <summary>
        /// 重新查询明细：从第 1 页开始，重新统计总数并加载第一批
        /// </summary>
        public void RefreshRecords()
        {
            try
            {
                Records.Clear();
                _recordPage = 1;

                _recordTotal = BurnInService.QueryCount(
                    orderNo: string.IsNullOrWhiteSpace(FilterOrderNo) ? null : FilterOrderNo,
                    from: FilterFrom,
                    to: FilterToExclusive,
                    result: FilterResult);

                AppendPage(_recordPage);
                UpdateRecordsStatus();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        /// <summary>
        /// 增量加载下一批明细（真正的分页，不再一次性取固定上限）
        /// </summary>
        private void LoadMoreRecords()
        {
            try
            {
                if (!HasMoreRecords) return;

                _recordPage++;
                AppendPage(_recordPage);
                UpdateRecordsStatus();
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        /// <summary>追加指定页的明细到列表</summary>
        private void AppendPage(int page)
        {
            var rows = BurnInService.Query(
                orderNo: string.IsNullOrWhiteSpace(FilterOrderNo) ? null : FilterOrderNo,
                from: FilterFrom,
                to: FilterToExclusive,
                result: FilterResult,
                page: page,
                pageSize: RecordPageSize);
            foreach (var r in rows)
                Records.Add(r);
        }

        /// <summary>状态栏显示"已加载 / 总数"，避免把已取条数当成总数</summary>
        private void UpdateRecordsStatus()
        {
            OnPropertyChanged(nameof(HasMoreRecords));
            LoadMoreRecordsCommand?.RaiseCanExecuteChanged();
            StatusText = $"{LanguageService.GetLocalizedString("BurnIn_TotalRecords")}: {Records.Count} / {RecordTotal}";
        }

        public void RefreshOrderNoOptions()
        {
            try
            {
                OrderNoOptions.Clear();
                OrderNoOptions.Add(string.Empty);
                foreach (var n in BurnInService.GetDistinctOrderNos())
                    OrderNoOptions.Add(n);
            }
            catch { }
        }

        private void ResetFilter()
        {
            FilterOrderNo = string.Empty;
            FilterFrom = null;
            FilterTo = null;
            SelectedResult = ResultOptions[0];
            RefreshSummaries();
            RefreshRecords();
        }

        /// <summary>从汇总页双击某工令，跳明细页签并自动过滤</summary>
        public void JumpToDetail(string orderNo)
        {
            FilterOrderNo = orderNo;
            SelectedTabIndex = 1;
            RefreshRecords();
        }

        private void ExportCsv()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"burn_in_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // 全量流式导出：按过滤条件逐页从库里读，不做条数截断
                    int count = BurnInService.ExportCsv(dialog.FileName, GetExportRecords());
                    StatusText = $"{LanguageService.GetLocalizedString("BurnIn_Exported")}: {dialog.FileName} ({count})";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export CSV", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportExcel()
        {
            var dialog = new SaveFileDialog
            {
                Filter = "Excel (*.xlsx)|*.xlsx",
                FileName = $"burn_in_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
            };
            if (dialog.ShowDialog() == true)
            {
                try
                {
                    // 全量导出：不做条数截断（数据量大时耗时较长，但不会静默丢记录）
                    int count = BurnInService.ExportExcel(dialog.FileName, GetExportRecords());
                    StatusText = $"{LanguageService.GetLocalizedString("BurnIn_Exported")}: {dialog.FileName} ({count})";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        /// <summary>
        /// 导出用的全量数据源：与当前过滤条件一致，惰性按页读取（不静默截断条数）
        /// </summary>
        private System.Collections.Generic.IEnumerable<BurnInRecord> GetExportRecords()
        {
            return BurnInService.QueryAll(
                orderNo: string.IsNullOrWhiteSpace(FilterOrderNo) ? null : FilterOrderNo,
                from: FilterFrom,
                to: FilterToExclusive,
                result: FilterResult);
        }
    }
}
