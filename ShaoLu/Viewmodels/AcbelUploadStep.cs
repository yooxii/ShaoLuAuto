using AcbelMes;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ShaoLu.Viewmodels.AutomationStep
{
    /// <summary>
    /// 字段值的引用种类：引用其他步骤本次运行的结果，或「用户输入」步骤填写的字段值。
    /// </summary>
    public enum AcbelReferenceKind
    {
        None = 0,

        /// <summary>执行结果（真 PASS / 假 FAIL）。</summary>
        ExecutionResult = 1,

        /// <summary>OCR 文本。</summary>
        OcrText = 2,

        /// <summary>直接获取文本。</summary>
        DirectText = 3,

        /// <summary>用户输入字段（按字段名读取「用户输入」步骤填写的值）。</summary>
        UserInput = 4
    }

    /// <summary>
    /// 桥接通道抽象（测试注入点用）。
    /// </summary>
    public interface IAcbelBridgeChannel : IDisposable
    {
        AcbelCallResult OpenDatabase();

        AcbelCallResult InsertData(string uploadData, string programFile);
    }

    /// <summary>
    /// 默认通道实现：包装真实的 x86 桥接客户端。
    /// </summary>
    public sealed class AcbelBridgeChannelAdapter : IAcbelBridgeChannel
    {
        private readonly AcbelBridgeClient _client;

        public AcbelBridgeChannelAdapter(AcbelBridgeClient client)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
        }

        public AcbelCallResult OpenDatabase() => _client.OpenDatabase();

        public AcbelCallResult InsertData(string uploadData, string programFile)
        {
            // 组装阶段已经按字段规范校验过，这里不再重复校验（校验失败时桥接根本不会被调用）
            return _client.InsertData(uploadData, programFile, validate: false);
        }

        public void Dispose() => _client.Dispose();
    }

    /// <summary>
    /// 「上传Acbel」的字段行（既参与序列化，也是界面行视图模型）。
    /// </summary>
    public sealed class AcbelUploadRow : ObservableObject
    {
        private static readonly NLog.Logger RowLogger = NLog.LogManager.GetCurrentClassLogger();

        private int _index;
        private string _text = string.Empty;
        private Guid? _refStepUid;
        private AcbelReferenceKind? _refKind;
        private string _refFieldName = string.Empty;
        private ICommand _clearReferenceCommand;

        /// <summary>段号（1..27）；0 表示镭雕程式文件名（insert 第 3 参数）。</summary>
        public int Index
        {
            get => _index;
            set
            {
                if (SetProperty(ref _index, value))
                {
                    OnPropertyChanged(nameof(Name));
                    OnPropertyChanged(nameof(IsRequired));
                }
            }
        }

        /// <summary>固定文本（被引用期间原值保留，清除引用后恢复）。</summary>
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        /// <summary>被引用的步骤 Uid（空表示使用固定文本）。</summary>
        public Guid? RefStepUid
        {
            get => _refStepUid;
            set
            {
                if (SetProperty(ref _refStepUid, value)) RaiseReferenceChanged();
            }
        }

        /// <summary>引用种类。</summary>
        public AcbelReferenceKind? RefKind
        {
            get => _refKind;
            set
            {
                if (SetProperty(ref _refKind, value)) RaiseReferenceChanged();
            }
        }

        /// <summary>
        /// 被引用的「用户输入」字段名（仅 RefKind = UserInput 时有意义）。
        /// 旧文件没有该属性时为空串，不影响其余三种引用。
        /// </summary>
        public string RefFieldName
        {
            get => _refFieldName;
            set
            {
                if (SetProperty(ref _refFieldName, value ?? string.Empty)) RaiseReferenceChanged();
            }
        }

        /// <summary>所属步骤 Uid（用于在引用菜单里排除本步骤自身）。</summary>
        [JsonIgnore]
        public Guid OwnerUid { get; set; }

        /// <summary>段名（按字段规范运行时取，程序文件名行为独立文案）。</summary>
        [JsonIgnore]
        public string Name
        {
            get
            {
                if (_index <= 0) return AcbelUploadStep.Loc("AcbelProgramFile", "镭雕程式文件名");
                var spec = AcbelInsertDataSpec.ByIndex(_index);
                return spec != null ? spec.Name : "#" + _index;
            }
        }

        [JsonIgnore]
        public bool IsRequired
        {
            get
            {
                if (_index <= 0) return false;
                var spec = AcbelInsertDataSpec.ByIndex(_index);
                return spec != null && spec.Required;
            }
        }

        /// <summary>已经引用了其他步骤的运行结果或「用户输入」字段。</summary>
        [JsonIgnore]
        public bool IsReferenced
            => _refStepUid.HasValue && _refStepUid.Value != Guid.Empty
               && _refKind.HasValue && _refKind.Value != AcbelReferenceKind.None;

        /// <summary>引用标签，例如「12. GetInput_1 · OCR 文本」或「用户输入 → 用户输入_1 / SN」。</summary>
        [JsonIgnore]
        public string ReferenceLabel
        {
            get
            {
                if (!IsReferenced) return string.Empty;
                int lineNo = 0;
                string stepName = null;
                foreach (var pair in AcbelUploadStep.OtherSteps(Guid.Empty))
                {
                    if (pair.Value.Uid != _refStepUid.Value) continue;
                    lineNo = pair.Key;
                    stepName = pair.Value.Name;
                    break;
                }

                // 用户输入：显示「用户输入 → 步骤名 / 字段名」；找不到步骤名时用字段名
                if (_refKind.Value == AcbelReferenceKind.UserInput)
                {
                    string userInputKind = AcbelUploadStep.Loc("AcbelRefUserInput", "用户输入");
                    string fieldName = _refFieldName ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(stepName))
                        return string.IsNullOrWhiteSpace(fieldName)
                            ? userInputKind + " → " + stepName
                            : userInputKind + " → " + stepName + " / " + fieldName;
                    if (!string.IsNullOrWhiteSpace(fieldName))
                        return userInputKind + " → " + fieldName;
                    return userInputKind;
                }

                string kind;
                switch (_refKind.Value)
                {
                    case AcbelReferenceKind.ExecutionResult:
                        kind = AcbelUploadStep.Loc("AcbelRefExecutionResult", "执行结果");
                        break;
                    case AcbelReferenceKind.OcrText:
                        kind = AcbelUploadStep.Loc("AcbelRefOcrText", "OCR 文本");
                        break;
                    case AcbelReferenceKind.DirectText:
                        kind = AcbelUploadStep.Loc("AcbelRefDirectText", "直接获取文本");
                        break;
                    default:
                        kind = string.Empty;
                        break;
                }

                stepName = stepName ?? "?";
                return lineNo > 0 ? lineNo + ". " + stepName + " · " + kind : stepName + " · " + kind;
            }
        }

        /// <summary>「▾」菜单的节点树（引用步骤 / 三种结果种类 / 用户输入字段 / 清除引用）。</summary>
        [JsonIgnore]
        public ObservableCollection<AcbelRefMenuNode> MenuNodes
        {
            get
            {
                var nodes = new ObservableCollection<AcbelRefMenuNode>();
                var root = new AcbelRefMenuNode { Header = AcbelUploadStep.Loc("AcbelRefSteps", "引用步骤") };
                foreach (var pair in AcbelUploadStep.OtherSteps(OwnerUid))
                {
                    var step = pair.Value;
                    var stepNode = new AcbelRefMenuNode { Header = pair.Key + ". " + step.Name };

                    // 「用户输入」步骤：步骤节点下直接列出它的每个字段，选中即按字段名引用
                    if (step is UserInputStep userInputStep)
                    {
                        foreach (var fieldName in CollectUserInputFieldNames(userInputStep))
                            stepNode.Children.Add(CreateUserInputFieldNode(step, fieldName));

                        if (stepNode.Children.Count == 0)
                        {
                            stepNode.Children.Add(new AcbelRefMenuNode
                            {
                                Header = AcbelUploadStep.Loc("AcbelRefNoFields", "（未定义字段）"),
                                StepUid = step.Uid,
                                Kind = AcbelReferenceKind.UserInput
                            });
                        }

                        root.Children.Add(stepNode);
                        continue;
                    }

                    stepNode.Children.Add(CreateKindNode("AcbelRefExecutionResult", "执行结果", step.Uid, AcbelReferenceKind.ExecutionResult));
                    stepNode.Children.Add(CreateKindNode("AcbelRefOcrText", "OCR 文本", step.Uid, AcbelReferenceKind.OcrText));
                    stepNode.Children.Add(CreateKindNode("AcbelRefDirectText", "直接获取文本", step.Uid, AcbelReferenceKind.DirectText));
                    root.Children.Add(stepNode);
                }

                nodes.Add(root);
                nodes.Add(new AcbelRefMenuNode
                {
                    Header = AcbelUploadStep.Loc("AcbelRefClear", "清除引用"),
                    Command = ClearReferenceCommand
                });
                return nodes;
            }
        }

        /// <summary>
        /// 收集某「用户输入」步骤可选字段名：设计期 Fields 与运行时已写入的字段名合并去重，
        /// 这样运行前后都能从菜单里选到字段。
        /// </summary>
        private static List<string> CollectUserInputFieldNames(UserInputStep step)
        {
            var names = new List<string>();
            try
            {
                if (step.Fields != null)
                {
                    foreach (var field in step.Fields)
                    {
                        if (field == null || string.IsNullOrWhiteSpace(field.Name)) continue;
                        string name = field.Name.Trim();
                        if (!names.Contains(name)) names.Add(name);
                    }
                }

                foreach (var name in StepExecutionContext.GetUserInputFieldNamesAll(step.Uid))
                {
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    string trimmed = name.Trim();
                    if (!names.Contains(trimmed)) names.Add(trimmed);
                }
            }
            catch (Exception ex)
            {
                RowLogger.Warn(ex, "AcbelUpload: 取用户输入字段名失败");
            }

            return names;
        }

        /// <summary>点击引用标签即可清除引用。</summary>
        [JsonIgnore]
        public ICommand ClearReferenceCommand => _clearReferenceCommand ??= new RelayCommand(ClearReference);

        /// <summary>
        /// 引用其他步骤本次运行的结果。
        /// kind = UserInput 时必须给出 fieldName（用户输入字段名）。
        /// </summary>
        public void SetReference(Guid stepUid, AcbelReferenceKind kind, string fieldName = null)
        {
            if (stepUid == Guid.Empty || kind == AcbelReferenceKind.None) return;
            if (kind == AcbelReferenceKind.UserInput && string.IsNullOrWhiteSpace(fieldName)) return;

            _refStepUid = stepUid;
            _refKind = kind;
            // 只有用户输入引用需要字段名，其余引用清空，避免残留旧字段名
            _refFieldName = kind == AcbelReferenceKind.UserInput ? fieldName.Trim() : string.Empty;
            RaiseReferenceChanged();
        }

        /// <summary>清除引用，恢复固定文本。</summary>
        public void ClearReference()
        {
            if (!IsReferenced) return;
            _refStepUid = null;
            _refKind = null;
            _refFieldName = string.Empty;
            RaiseReferenceChanged();
        }

        /// <summary>深拷贝（Uid 不复制，由构造函数重新生成）。</summary>
        public AcbelUploadRow Clone()
        {
            return new AcbelUploadRow
            {
                Index = _index,
                Text = _text,
                RefStepUid = _refStepUid,
                RefKind = _refKind,
                RefFieldName = _refFieldName,
                OwnerUid = OwnerUid
            };
        }

        private void RaiseReferenceChanged()
        {
            OnPropertyChanged(nameof(IsReferenced));
            OnPropertyChanged(nameof(ReferenceLabel));
        }

        /// <summary>「用户输入 → 字段」叶子节点：记录字段名，点击即按 UserInput 引用。</summary>
        private AcbelRefMenuNode CreateUserInputFieldNode(AutomationStepBase step, string fieldName)
        {
            return new AcbelRefMenuNode
            {
                Header = fieldName,
                StepUid = step.Uid,
                Kind = AcbelReferenceKind.UserInput,
                FieldName = fieldName,
                Command = new RelayCommand(() => SetReference(step.Uid, AcbelReferenceKind.UserInput, fieldName))
            };
        }

        private AcbelRefMenuNode CreateKindNode(string key, string fallback, Guid stepUid, AcbelReferenceKind kind)
        {
            return new AcbelRefMenuNode
            {
                Header = AcbelUploadStep.Loc(key, fallback),
                StepUid = stepUid,
                Kind = kind,
                Command = new RelayCommand(() => SetReference(stepUid, kind))
            };
        }
    }

    /// <summary>
    /// 「▾」菜单节点。ToString 供 MenuItem 直接显示（容器 Header 由生成器赋值，样式里的绑定会被覆盖）。
    /// StepUid / Kind / FieldName 为叶子节点的引用元数据（供「用户输入字段」这类需要额外参数的引用使用）。
    /// </summary>
    public sealed class AcbelRefMenuNode
    {
        public string Header { get; set; }

        public ObservableCollection<AcbelRefMenuNode> Children { get; } = new ObservableCollection<AcbelRefMenuNode>();

        public ICommand Command { get; set; }

        /// <summary>该叶子节点引用的步骤 Uid。</summary>
        public Guid? StepUid { get; set; }

        /// <summary>该叶子节点对应的引用种类。</summary>
        public AcbelReferenceKind? Kind { get; set; }

        /// <summary>「用户输入」引用时的字段名；其它引用为空。</summary>
        public string FieldName { get; set; }

        public override string ToString() => Header ?? string.Empty;
    }

    /// <summary>
    /// 「上传Acbel」步骤：按字段规范组装 27 段上传资料并调用桥接进程的 ACBEL_INSERT_DATA 写入 Acbel MES。
    /// 失败不抛异常，返回 false 并带上错误信息。
    /// </summary>
    public sealed class AcbelUploadStep : AutomationStepBase
    {
        private static readonly NLog.Logger Logger = NLog.LogManager.GetCurrentClassLogger();

        // 桥接通道与数据库连接：进程内缓存一次，首次上传时自动 open
        private static readonly object BridgeGate = new object();
        private static IAcbelBridgeChannel _channel;
        private static bool _dbOpened;

        /// <summary>测试注入点：桥接通道工厂；为 null 时使用真实桥接进程。</summary>
        public static Func<IAcbelBridgeChannel> ChannelFactory { get; set; }

        /// <summary>测试用：清空桥接通道缓存与注入点。</summary>
        public static void ResetChannelCacheForTests()
        {
            lock (BridgeGate)
            {
                if (_channel != null)
                {
                    try
                    {
                        _channel.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Logger.Warn(ex, "AcbelUpload: 释放桥接通道失败");
                    }
                }

                _channel = null;
                _dbOpened = false;
                ChannelFactory = null;
            }
        }

        private ObservableCollection<AcbelUploadRow> _requiredRows = new ObservableCollection<AcbelUploadRow>();
        private ObservableCollection<AcbelUploadRow> _optionalRows = new ObservableCollection<AcbelUploadRow>();
        private AcbelUploadRow _programFileRow;
        private bool _dryRun;
        private bool _rowsReady;

        public AcbelUploadStep() : base()
        {
            Type = StepType.AcbelUpload;
        }

        public AcbelUploadStep(string name) : base()
        {
            Type = StepType.AcbelUpload;
            Name = name;
        }

        public AcbelUploadStep(string name, string description) : base()
        {
            Type = StepType.AcbelUpload;
            Name = name;
            Description = description;
        }

        /// <summary>必填字段行（按字段规范运行时生成）。</summary>
        public ObservableCollection<AcbelUploadRow> RequiredRows
        {
            get
            {
                EnsureRows();
                return _requiredRows;
            }
            set
            {
                _requiredRows = value ?? new ObservableCollection<AcbelUploadRow>();
                _rowsReady = false;
            }
        }

        /// <summary>选填字段行（按字段规范运行时生成）。</summary>
        public ObservableCollection<AcbelUploadRow> OptionalRows
        {
            get
            {
                EnsureRows();
                return _optionalRows;
            }
            set
            {
                _optionalRows = value ?? new ObservableCollection<AcbelUploadRow>();
                _rowsReady = false;
            }
        }

        /// <summary>镭雕程式文件名行（insert 第 3 参数）。</summary>
        public AcbelUploadRow ProgramFileRow
        {
            get
            {
                EnsureRows();
                return _programFileRow;
            }
            set => _programFileRow = value;
        }

        /// <summary>仅校验、不写库。</summary>
        public bool DryRun
        {
            get => _dryRun;
            set => SetProperty(ref _dryRun, value);
        }

        /// <summary>全部字段行（不含镭雕程式文件名行）。</summary>
        public IEnumerable<AcbelUploadRow> FieldRows()
        {
            EnsureRows();
            return _requiredRows.Concat(_optionalRows)
                .Where(r => r != null && r.Index >= 1 && r.Index <= AcbelInsertDataSpec.FieldCount);
        }

        /// <summary>组装 27 段上传资料（insert 第 2 参数）。</summary>
        public string BuildUploadData()
        {
            var builder = new AcbelInsertDataBuilder();
            foreach (var row in FieldRows()) builder.Set(row.Index, ResolveRowValue(row));
            return builder.Build();
        }

        /// <summary>按字段规范校验当前组装结果。</summary>
        public AcbelPayloadValidationResult ValidateUploadData()
            => AcbelInsertDataValidator.Validate(BuildUploadData());

        /// <summary>
        /// 解析某个字段行的取值：未引用取固定文本；引用取其他步骤「本次运行」的结果，
        /// 取不到（未执行过/未初始化）时按空串处理，不报错。
        /// 「用户输入」引用按字段名从执行上下文取值，不依赖步骤的执行结果。
        /// </summary>
        public string ResolveRowValue(AcbelUploadRow row)
        {
            if (row == null) return string.Empty;
            if (!row.IsReferenced) return row.Text ?? string.Empty;

            try
            {
                // 用户输入字段：走执行上下文的字段值（未填写/未运行均按空串）
                if (row.RefKind.Value == AcbelReferenceKind.UserInput)
                {
                    return StepExecutionContext.TryGetUserInput(row.RefStepUid.Value, row.RefFieldName, out string userInput)
                        ? (userInput ?? string.Empty)
                        : string.Empty;
                }

                StepExecutionResult result;
                string threadName;
                if (!StepExecutionContext.TryGetLatestResult(row.RefStepUid.Value, out result, out threadName) || result == null)
                    return string.Empty;

                switch (row.RefKind.Value)
                {
                    case AcbelReferenceKind.ExecutionResult:
                        return result.IsTrue ? "PASS" : "FAIL";
                    case AcbelReferenceKind.OcrText:
                        return IsDirectTextSource(result.TextSource) ? string.Empty : (result.OCRText ?? string.Empty);
                    case AcbelReferenceKind.DirectText:
                        return IsDirectTextSource(result.TextSource) ? (result.OCRText ?? string.Empty) : string.Empty;
                    default:
                        return string.Empty;
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "AcbelUpload: 解析引用值失败");
                return string.Empty;
            }
        }

        public override async Task<bool> RunAsync(CancellationToken cancellationToken = default)
        {
            string prefix = Loc("AcbelUploadFailed", "上传Acbel失败：");
            try
            {
                if (WaitTime > 0) await Task.Delay((int)(WaitTime * 1000), cancellationToken);

                EnsureRows();

                var validation = ValidateUploadData();
                if (!validation.IsValid) return Fail(prefix + validation.ToErrorText());

                // 仅校验、不写库：不 open、不 insert，也不改变库连接状态
                if (DryRun) return Succeed();

                string programFile = ResolveRowValue(_programFileRow);
                string uploadData = BuildUploadData();

                IAcbelBridgeChannel channel;
                string openError;
                if (!EnsureDatabaseOpen(out channel, out openError)) return Fail(prefix + openError);

                var call = channel.InsertData(uploadData, programFile);

                // 内部异常（2}…）：重试一次
                if (IsRetryNeededCall(call))
                {
                    Logger.Warn("AcbelUpload: 需要重试一次，返回值 {0}", call == null ? "null" : call.Value);
                    call = channel.InsertData(uploadData, programFile);
                }

                string text = call == null ? null : (!string.IsNullOrEmpty(call.Value) ? call.Value : call.Error);
                if (call != null && call.TransportOk && !call.TimedOut)
                {
                    if (AcbelBridgeClient.IsOk(call.Value)) return Succeed();

                    // 业务卡控（0}…）：返回的说明写进错误信息
                    if (AcbelBridgeClient.IsBusinessFail(text)) return Fail(prefix + text);
                }

                return Fail(prefix + Describe(call));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "AcbelUpload: 执行失败");
                return Fail(prefix + ex.Message);
            }
        }

        public override AutomationStepBase Clone()
        {
            EnsureRows();

            var res = new AcbelUploadStep(Name, Description)
            {
                WaitTime = WaitTime,
                IsNeed = IsNeed,
                EnableLog = EnableLog,
                SelfReferenceLimit = SelfReferenceLimit,
                ConditionMode = ConditionMode,
                Conditions = new ObservableCollection<StepCondition>(Conditions),
                TrueGotoUid = TrueGotoUid,
                FalseGotoUid = FalseGotoUid,
                DryRun = DryRun,
                _programFileRow = _programFileRow == null ? null : _programFileRow.Clone(),
                _requiredRows = new ObservableCollection<AcbelUploadRow>(_requiredRows.Select(r => r.Clone())),
                _optionalRows = new ObservableCollection<AcbelUploadRow>(_optionalRows.Select(r => r.Clone())),
                _rowsReady = true
            };
            res.StampOwnerUid();
            return res;
        }

        /// <summary>其他步骤（按行号升序；ownerUid 非空时排除该步骤自身）。</summary>
        internal static List<KeyValuePair<int, AutomationStepBase>> OtherSteps(Guid ownerUid)
        {
            var list = new List<KeyValuePair<int, AutomationStepBase>>();
            try
            {
                var steps = SingletonLocator.Steps == null ? null : SingletonLocator.Steps.AutomationStepBases;
                if (steps == null) return list;

                for (int i = 0; i < steps.Count; i++)
                {
                    var step = steps[i];
                    if (step == null) continue;
                    if (ownerUid != Guid.Empty && step.Uid == ownerUid) continue;
                    list.Add(new KeyValuePair<int, AutomationStepBase>(i + 1, step));
                }
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "AcbelUpload: 取步骤列表失败");
            }

            return list;
        }

        /// <summary>取本地化文案；未初始化或取不到时回退。</summary>
        internal static string Loc(string key, string fallback = null)
        {
            try
            {
                var value = LanguageService.GetLocalizedString(key);
                if (string.IsNullOrEmpty(value) || string.Equals(value, key, StringComparison.Ordinal))
                    return fallback ?? key;
                return value;
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "AcbelUpload: 取本地化字符串 {0} 失败", key);
                return fallback ?? key;
            }
        }

        private static bool EnsureDatabaseOpen(out IAcbelBridgeChannel channel, out string error)
        {
            lock (BridgeGate)
            {
                channel = null;
                error = null;
                try
                {
                    if (_channel == null)
                    {
                        var factory = ChannelFactory;
                        _channel = factory != null ? factory() : new AcbelBridgeChannelAdapter(new AcbelBridgeClient());
                        _dbOpened = false;
                    }

                    if (_dbOpened)
                    {
                        channel = _channel;
                        return true;
                    }

                    var open = _channel.OpenDatabase();
                    if (open != null && open.TransportOk && !open.TimedOut && AcbelBridgeClient.IsOk(open.Value))
                    {
                        _dbOpened = true;
                        channel = _channel;
                        return true;
                    }

                    error = open == null ? "unknown" : Describe(open);
                    DisposeChannel();
                    return false;
                }
                catch (Exception ex)
                {
                    Logger.Error(ex, "AcbelUpload: 打开数据库失败");
                    error = ex.Message;
                    DisposeChannel();
                    return false;
                }
            }
        }

        /// <summary>必须在 BridgeGate 内调用。</summary>
        private static void DisposeChannel()
        {
            var channel = _channel;
            _channel = null;
            _dbOpened = false;
            if (channel == null) return;
            try
            {
                channel.Dispose();
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, "AcbelUpload: 释放桥接通道失败");
            }
        }

        private static bool IsRetryNeededCall(AcbelCallResult call)
        {
            if (call == null || !call.TransportOk || call.TimedOut) return false;
            return AcbelBridgeClient.IsRetryNeeded(call.Value) || AcbelBridgeClient.IsRetryNeeded(call.Error);
        }

        private static string Describe(AcbelCallResult call)
        {
            if (call == null) return "unknown";
            if (!string.IsNullOrEmpty(call.Error)) return call.Error;
            if (!string.IsNullOrEmpty(call.Value)) return call.Value;
            return "unknown";
        }

        /// <summary>TextSource 为 Direct 表示「直接获取文本」；其余（含旧数据的空值）按 OCR 处理。</summary>
        private static bool IsDirectTextSource(string textSource)
            => string.Equals(textSource, "Direct", StringComparison.OrdinalIgnoreCase);

        private bool Succeed()
        {
            IsTrue = true;
            IsError = false;
            ErrorType = StepErrorType.None;
            ErrorMessage = string.Empty;
            LastResult = new StepExecutionResult { IsTrue = true, ExecutedAt = DateTime.Now };
            return true;
        }

        private bool Fail(string message)
        {
            IsTrue = false;
            IsError = true;
            ErrorType = StepErrorType.Unknown;
            ErrorMessage = message;
            // 引擎会在成功路径清掉 step.IsError，但 LastResult 上的错误信息会保留下来
            LastResult = new StepExecutionResult { IsTrue = false, ErrorMessage = message, ExecutedAt = DateTime.Now };
            Logger.Warn("AcbelUpload: {0}", message);
            return false;
        }

        /// <summary>按字段规范补齐行集合（构造与反序列化两条路径都走这里）。</summary>
        private void EnsureRows()
        {
            if (_rowsReady) return;

            _requiredRows = MergeRows(_requiredRows, true);
            _optionalRows = MergeRows(_optionalRows, false);
            if (_programFileRow == null) _programFileRow = new AcbelUploadRow { Index = 0 };
            _rowsReady = true;
            StampOwnerUid();
        }

        private static ObservableCollection<AcbelUploadRow> MergeRows(ObservableCollection<AcbelUploadRow> existing, bool required)
        {
            var byIndex = new Dictionary<int, AcbelUploadRow>();
            if (existing != null)
            {
                foreach (var row in existing)
                {
                    var spec = row == null ? null : AcbelInsertDataSpec.ByIndex(row.Index);
                    if (spec == null || spec.Required != required) continue;
                    if (!byIndex.ContainsKey(row.Index)) byIndex[row.Index] = row;
                }
            }

            var result = new ObservableCollection<AcbelUploadRow>();
            foreach (var spec in AcbelInsertDataSpec.Fields)
            {
                if (spec.Required != required) continue;
                AcbelUploadRow row;
                if (!byIndex.TryGetValue(spec.Index, out row)) row = new AcbelUploadRow { Index = spec.Index };
                result.Add(row);
            }

            return result;
        }

        private void StampOwnerUid()
        {
            if (_programFileRow != null) _programFileRow.OwnerUid = Uid;
            foreach (var row in _requiredRows) row.OwnerUid = Uid;
            foreach (var row in _optionalRows) row.OwnerUid = Uid;
        }
    }
}
