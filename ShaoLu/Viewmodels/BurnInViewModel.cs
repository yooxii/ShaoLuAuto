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
        private bool? _filterIsGood;
        private string _statusText;

        public int SelectedTabIndex { get => _selectedTabIndex; set => SetProperty(ref _selectedTabIndex, value); }
        public string FilterOrderNo { get => _filterOrderNo; set => SetProperty(ref _filterOrderNo, value); }
        public DateTime? FilterFrom { get => _filterFrom; set => SetProperty(ref _filterFrom, value); }
        public DateTime? FilterTo { get => _filterTo; set => SetProperty(ref _filterTo, value); }
        public bool? FilterIsGood { get => _filterIsGood; set => SetProperty(ref _filterIsGood, value); }
        public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

        /// <summary>工令汇总</summary>
        public ObservableCollection<BurnInService.OrderSummary> OrderSummaries { get; } = new();

        /// <summary>明细列表</summary>
        public ObservableCollection<BurnInRecord> Records { get; } = new();

        /// <summary>工令下拉列表（供汇总页双击跳转与明细页过滤）</summary>
        public ObservableCollection<string> OrderNoOptions { get; } = new();

        /// <summary>良/不良过滤选项</summary>
        public ObservableCollection<string> GoodBadOptions { get; } = new()
        {
            LanguageService.GetLocalizedString("BurnIn_All"),
            LanguageService.GetLocalizedString("BurnIn_Good"),
            LanguageService.GetLocalizedString("BurnIn_Bad"),
        };
        private string _selectedGoodBad;
        public string SelectedGoodBad
        {
            get => _selectedGoodBad;
            set
            {
                if (SetProperty(ref _selectedGoodBad, value))
                {
                    if (value == GoodBadOptions[1]) FilterIsGood = true;
                    else if (value == GoodBadOptions[2]) FilterIsGood = false;
                    else FilterIsGood = null;
                }
            }
        }

        /// <summary>当前配置（绑定到 Config 页签）</summary>
        public BurnInConfig Config { get; private set; } = BurnInService.Config ?? new BurnInConfig();

        /// <summary>所有步骤集合（供 ComboBox 绑定选择判定步骤）</summary>
        public ObservableCollection<AutomationStep.AutomationStepBase> AllSteps
        {
            get
            {
                try { return SingletonLocator.Steps.AutomationStepBases; }
                catch { return null; }
            }
        }

        #region 命令

        public ICommand RefreshSummariesCommand { get; }
        public ICommand RefreshRecordsCommand { get; }
        public ICommand ResetFilterCommand { get; }
        public ICommand ExportCsvCommand { get; }
        public ICommand ExportExcelCommand { get; }
        public ICommand SaveConfigCommand { get; }
        public ICommand SelectRegionCommand { get; }

        #endregion

        public BurnInViewModel()
        {
            RefreshSummariesCommand = new RelayCommand(RefreshSummaries);
            RefreshRecordsCommand = new RelayCommand(RefreshRecords);
            ResetFilterCommand = new RelayCommand(ResetFilter);
            ExportCsvCommand = new RelayCommand(ExportCsv);
            ExportExcelCommand = new RelayCommand(ExportExcel);
            SaveConfigCommand = new RelayCommand(SaveConfig);
            SelectRegionCommand = new RelayCommand(SelectRegion);

            _selectedGoodBad = GoodBadOptions[0];
            OnPropertyChanged(nameof(SelectedGoodBad));

            // 初始加载
            RefreshSummaries();
            RefreshOrderNoOptions();
        }

        public void RefreshSummaries()
        {
            try
            {
                OrderSummaries.Clear();
                foreach (var s in BurnInService.GetOrderSummaries(FilterFrom, FilterTo))
                    OrderSummaries.Add(s);
                StatusText = $"{LanguageService.GetLocalizedString("BurnIn_TotalOrders")}: {OrderSummaries.Count}";
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
        }

        public void RefreshRecords()
        {
            try
            {
                Records.Clear();
                foreach (var r in BurnInService.Query(
                    orderNo: string.IsNullOrWhiteSpace(FilterOrderNo) ? null : FilterOrderNo,
                    from: FilterFrom,
                    to: FilterTo?.AddDays(1),
                    isGood: FilterIsGood,
                    pageSize: 1000))
                    Records.Add(r);
                StatusText = $"{LanguageService.GetLocalizedString("BurnIn_TotalRecords")}: {Records.Count}";
            }
            catch (Exception ex)
            {
                StatusText = ex.Message;
            }
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
            SelectedGoodBad = GoodBadOptions[0];
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
                    BurnInService.ExportCsv(dialog.FileName, GetCurrentRecordList());
                    StatusText = $"{LanguageService.GetLocalizedString("BurnIn_Exported")}: {dialog.FileName}";
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
                    BurnInService.ExportExcel(dialog.FileName, GetCurrentRecordList());
                    StatusText = $"{LanguageService.GetLocalizedString("BurnIn_Exported")}: {dialog.FileName}";
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message, "Export Excel", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private System.Collections.Generic.List<BurnInRecord> GetCurrentRecordList()
        {
            return BurnInService.Query(
                orderNo: string.IsNullOrWhiteSpace(FilterOrderNo) ? null : FilterOrderNo,
                from: FilterFrom,
                to: FilterTo?.AddDays(1),
                isGood: FilterIsGood,
                pageSize: 10000);
        }

        private void SaveConfig()
        {
            BurnInService.SaveConfig(Config);
            StatusText = LanguageService.GetLocalizedString("BurnIn_ConfigSaved");
        }

        private void SelectRegion()
        {
            var win = new WindowSelectRegion();
            if (win.ShowDialog() == true && win.SelectedRegion != Rect.Empty)
            {
                Config.RegionX = win.SelectedRegion.X;
                Config.RegionY = win.SelectedRegion.Y;
                Config.RegionW = win.SelectedRegion.Width;
                Config.RegionH = win.SelectedRegion.Height;
                OnPropertyChanged(nameof(Config));
            }
        }
    }
}
