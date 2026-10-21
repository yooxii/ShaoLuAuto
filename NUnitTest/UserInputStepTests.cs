using NUnit.Framework;
using ShaoLu.Models;
using ShaoLu.Services;
using ShaoLu.Utils;
using ShaoLu.Viewmodels.AutomationStep;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;

namespace NUnitTest
{
    /// <summary>
    /// 「用户输入」步骤的单元测试：
    /// Clone 字段深拷贝、JSON 往返（含 FieldType/AskOnFileOpen/AskOnRun/Fields）、
    /// 必填判定、执行上下文读写、条件判断按字段取值，
    /// 以及冻结字段的锁定语义（冻结+已确认+未登录只读，登录后可改）。
    /// </summary>
    [TestFixture]
    public class UserInputStepTests
    {
        private string _testDir;

        [SetUp]
        public void Setup()
        {
            _testDir = Path.Combine(Path.GetTempPath(), "ShaoLu_UserInput_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testDir);
            // 默认按「未登录」跑（锁定语义依赖登录态），每个用例结束再恢复一次
            SetLoggedInState(false);
        }

        [TearDown]
        public void TearDown()
        {
            SetLoggedInState(false);
            RestoreIocState();
            // 清理测试写入的执行上下文，避免影响其它用例
            StepExecutionContext.ClearChildContexts();
            StepExecutionContext.Instance.Clear();
            try
            {
                if (Directory.Exists(_testDir))
                    Directory.Delete(_testDir, true);
            }
            catch
            {
                // 清理失败不影响结论
            }
        }

        #region 登录态模拟（不修改生产代码）

        /// <summary>
        /// Ioc.Default 内部的 IServiceProvider 字段。
        /// CommunityToolkit 的 Ioc 只允许 ConfigureServices 一次（本进程里已被 StepsFileTests 配置过），
        /// 这里按 Lead 的允许用反射直接注入/还原登录态所需的服务提供者，不触发单次配置限制。
        /// </summary>
        private static readonly System.Reflection.FieldInfo IocProviderField =
            typeof(CommunityToolkit.Mvvm.DependencyInjection.Ioc)
                .GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(f => typeof(IServiceProvider).IsAssignableFrom(f.FieldType));

        private static object _originalIocProvider;
        private static bool _originalIocProviderCaptured;

        /// <summary>
        /// 模拟登录态：UserInputField.IsUserLoggedIn 走 SingletonLocator.UserService，
        /// 而测试进程没有 App.xaml.cs 的 DI 注册，这里注入一个只提供 IUserService 的最小 IServiceProvider。
        /// </summary>
        private static void SetLoggedInState(bool loggedIn)
        {
            if (IocProviderField == null) return;

            if (!_originalIocProviderCaptured)
            {
                _originalIocProvider = IocProviderField.GetValue(CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default);
                _originalIocProviderCaptured = true;
            }

            var user = loggedIn ? new User { Username = "unit_test_user", Role = UserRole.User } : null;
            IocProviderField.SetValue(
                CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default,
                new TestServices(new FakeUserService(user)));
        }

        /// <summary>还原用例开始前的 Ioc 状态，避免影响同进程的其它测试</summary>
        private static void RestoreIocState()
        {
            if (IocProviderField == null || !_originalIocProviderCaptured) return;
            IocProviderField.SetValue(CommunityToolkit.Mvvm.DependencyInjection.Ioc.Default, _originalIocProvider);
            _originalIocProvider = null;
            _originalIocProviderCaptured = false;
        }

        private sealed class TestServices : IServiceProvider
        {
            private readonly IUserService _userService;
            private ShaoLu.Viewmodels.MainViewModel _main;
            private FileServices _files;

            public TestServices(IUserService userService)
            {
                _userService = userService;
            }

            public object GetService(Type serviceType)
            {
                if (serviceType == typeof(IUserService)) return _userService;
                if (serviceType == typeof(ShaoLu.Viewmodels.MainViewModel))
                {
                    if (_main == null) _main = new ShaoLu.Viewmodels.MainViewModel();
                    return _main;
                }
                if (serviceType == typeof(FileServices))
                {
                    if (_files == null) _files = new FileServices();
                    return _files;
                }
                return null;
            }
        }

        /// <summary>只实现登录态相关的 IUserService 桩（其余成员不会被测试用到）</summary>
        private sealed class FakeUserService : IUserService
        {
            public FakeUserService(User currentUser)
            {
                CurrentUser = currentUser;
            }

            public User CurrentUser { get; set; }
            public bool IsAdmin => CurrentUser != null && CurrentUser.Role == UserRole.Admin;
            public bool Login(string username, string password) => false;
            public void Logout() => CurrentUser = null;
            public List<User> GetAllUsers() => new List<User>();
            public bool AddUser(string username, string password, UserRole role) => false;
            public bool DeleteUser(string username) => false;
            public bool ChangeRole(string username, UserRole role) => false;
            public bool ChangePassword(string username, string oldPassword, string newPassword) => false;
            public bool HasAnyAdmin() => CurrentUser != null;
            public bool Register(string username, string password, string adminUsername = null, string adminPassword = null) => false;
        }

        #endregion

        /// <summary>构造一个带两个字段（文本 + 下拉）的用户输入步骤</summary>
        private static UserInputStep CreateStep(string name = "用户输入")
        {
            var step = new UserInputStep(name)
            {
                PromptTitle = "请输入工令信息",
                AskOnFileOpen = false,
                AskOnRun = true,
            };
            step.Fields.Add(new UserInputField
            {
                Name = "工令",
                Value = "A12345",
                FieldType = UserInputFieldType.Text,
                IsRequired = true,
                Description = "今日工令号",
            });
            step.Fields.Add(new UserInputField
            {
                Name = "线体",
                Value = "3",
                FieldType = UserInputFieldType.Dropdown,
                Options = "1\r\n2\r\n3",
                IsRequired = false,
            });
            return step;
        }

        #region Clone 深拷贝

        [Test]
        public void Clone_CopiesFieldsDeeply()
        {
            var step = CreateStep();
            var clone = (UserInputStep)step.Clone();

            Assert.That(clone, Is.Not.SameAs(step));
            Assert.That(clone.Type, Is.EqualTo(StepType.UserInput));
            Assert.That(clone.Name, Is.EqualTo(step.Name));
            Assert.That(clone.PromptTitle, Is.EqualTo("请输入工令信息"));
            Assert.That(clone.AskOnFileOpen, Is.False);
            Assert.That(clone.AskOnRun, Is.True);

            Assert.That(clone.Fields.Count, Is.EqualTo(step.Fields.Count), "字段数量必须一致");
            Assert.That(clone.Fields, Is.Not.SameAs(step.Fields), "集合不能共享引用");
            for (int i = 0; i < step.Fields.Count; i++)
            {
                Assert.That(clone.Fields[i], Is.Not.SameAs(step.Fields[i]), $"第 {i} 个字段必须是新实例");
                Assert.That(clone.Fields[i].Name, Is.EqualTo(step.Fields[i].Name));
                Assert.That(clone.Fields[i].Value, Is.EqualTo(step.Fields[i].Value));
                Assert.That(clone.Fields[i].FieldType, Is.EqualTo(step.Fields[i].FieldType));
                Assert.That(clone.Fields[i].Options, Is.EqualTo(step.Fields[i].Options));
                Assert.That(clone.Fields[i].IsRequired, Is.EqualTo(step.Fields[i].IsRequired));
                Assert.That(clone.Fields[i].Description, Is.EqualTo(step.Fields[i].Description));
            }
        }

        [Test]
        public void Clone_ModifyingClone_DoesNotAffectSource()
        {
            var step = CreateStep();
            var clone = (UserInputStep)step.Clone();

            clone.Fields[0].Value = "B999";
            clone.Fields.Add(new UserInputField { Name = "新增", Value = "x" });

            Assert.That(step.Fields[0].Value, Is.EqualTo("A12345"), "克隆体修改不能回写源对象");
            Assert.That(step.Fields.Count, Is.EqualTo(2));
            Assert.That(clone.Fields.Count, Is.EqualTo(3));
        }

        #endregion

        #region JSON 往返

#pragma warning disable CS0618 // 测试沿用旧版 JSON API（兼容性覆盖）

        [Test]
        public void SaveAndLoad_UserInputStep_RoundTrip()
        {
            var step = CreateStep("用户输入步骤");
            step.TrueGotoUid = Guid.Parse("00000000-0000-0000-0000-000000000005");
            var steps = new ObservableCollection<AutomationStepBase> { step };
            string filePath = Path.Combine(_testDir, "user_input_steps.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            Assert.That(File.Exists(filePath), Is.True);

            var loaded = StepsFile.LoadStepsFromJson(filePath);
            Assert.That(loaded.Count, Is.EqualTo(1));

            var restored = loaded[0] as UserInputStep;
            Assert.That(restored, Is.Not.Null, "StepType.UserInput 必须能反序列化回 UserInputStep");
            Assert.That(restored.Type, Is.EqualTo(StepType.UserInput));
            Assert.That(restored.Uid, Is.EqualTo(step.Uid));
            Assert.That(restored.TrueGotoUid, Is.EqualTo(step.TrueGotoUid));
            Assert.That(restored.PromptTitle, Is.EqualTo("请输入工令信息"));
            Assert.That(restored.AskOnFileOpen, Is.False);
            Assert.That(restored.AskOnRun, Is.True, "AskOnRun 必须随工程持久化");

            Assert.That(restored.Fields.Count, Is.EqualTo(2));
            Assert.That(restored.Fields[0].Name, Is.EqualTo("工令"));
            Assert.That(restored.Fields[0].Value, Is.EqualTo("A12345"));
            Assert.That(restored.Fields[0].FieldType, Is.EqualTo(UserInputFieldType.Text));
            Assert.That(restored.Fields[0].IsRequired, Is.True);
            Assert.That(restored.Fields[0].Description, Is.EqualTo("今日工令号"));
            Assert.That(restored.Fields[1].FieldType, Is.EqualTo(UserInputFieldType.Dropdown));
            Assert.That(restored.Fields[1].Options, Is.EqualTo("1\r\n2\r\n3"));
            Assert.That(restored.Fields[1].IsRequired, Is.False);
        }

#pragma warning restore CS0618

        #endregion

        #region MissingRequiredFieldNames 必填判定

        [Test]
        public void MissingRequiredFieldNames_ReturnsOnlyEmptyRequiredFields()
        {
            var step = new UserInputStep("用户输入");
            step.Fields.Add(new UserInputField { Name = "工令", IsRequired = true, Value = "A1" });
            step.Fields.Add(new UserInputField { Name = "线体", IsRequired = true, Value = "" });
            step.Fields.Add(new UserInputField { Name = "备注", IsRequired = false, Value = "" });

            var missing = step.MissingRequiredFieldNames;

            Assert.That(missing.Count, Is.EqualTo(1), "只应报告「必填且为空」的字段");
            Assert.That(missing[0], Is.EqualTo("线体"));
        }

        [Test]
        public void MissingRequiredFieldNames_AllFilled_IsEmpty()
        {
            var step = new UserInputStep("用户输入");
            step.Fields.Add(new UserInputField { Name = "工令", IsRequired = true, Value = "A1" });
            step.Fields.Add(new UserInputField { Name = "线体", IsRequired = true, Value = "3" });

            Assert.That(step.MissingRequiredFieldNames, Is.Empty);
        }

        [Test]
        public void MissingRequiredFieldNames_UnnamedRequiredField_UsesPlaceholder()
        {
            var step = new UserInputStep("用户输入");
            step.Fields.Add(new UserInputField { Name = "", IsRequired = true, Value = "" });

            var missing = step.MissingRequiredFieldNames;

            Assert.That(missing.Count, Is.EqualTo(1));
            Assert.That(missing[0], Is.EqualTo("(未命名字段)"));
        }

        [Test]
        public void HasFields_ReflectsFieldCollection()
        {
            var step = new UserInputStep("用户输入");
            Assert.That(step.HasFields, Is.False);

            step.Fields.Add(new UserInputField { Name = "工令", Value = "A1" });
            Assert.That(step.HasFields, Is.True);
        }

        #endregion

        #region 执行上下文读写

        [Test]
        public void SetUserInput_ThenGetUserInput_ReturnsValue()
        {
            var context = new StepExecutionContext();
            Guid stepUid = Guid.NewGuid();

            context.SetUserInput(stepUid, new[]
            {
                new UserInputField { Name = "工令", Value = "A12345" },
                new UserInputField { Name = "线体", Value = "3" },
            });

            Assert.That(context.GetUserInput(stepUid, "工令"), Is.EqualTo("A12345"));
            Assert.That(context.GetUserInput(stepUid, " 工令 "), Is.EqualTo("A12345"), "字段名匹配时应忽略首尾空白");
            Assert.That(context.GetUserInput(stepUid, "不存在"), Is.Null);
        }

        [Test]
        public void SetUserInput_OverwriteSameField_KeepsLatestValue()
        {
            var context = new StepExecutionContext();
            Guid stepUid = Guid.NewGuid();

            context.SetUserInput(stepUid, "工令", "A1");
            context.SetUserInput(stepUid, "工令", "B2");

            Assert.That(context.GetUserInput(stepUid, "工令"), Is.EqualTo("B2"));
        }

        [Test]
        public void SetUserInput_EmptyUid_IsIgnored()
        {
            var context = new StepExecutionContext();
            context.SetUserInput(Guid.Empty, new[] { new UserInputField { Name = "工令", Value = "A1" } });

            Assert.That(context.GetUserInput(Guid.Empty, "工令"), Is.Null);
        }

        [Test]
        public void TryGetUserInput_FindsValueWrittenInChildContext()
        {
            var childContext = new StepExecutionContext();
            Guid stepUid = Guid.NewGuid();
            childContext.SetUserInput(stepUid, "工令", "C3");
            StepExecutionContext.RegisterContext(childContext, "测试子线程");

            bool found = StepExecutionContext.TryGetUserInput(stepUid, "工令", out string value);

            Assert.That(found, Is.True, "子线程上下文写入的字段值应能被跨上下文找到");
            Assert.That(value, Is.EqualTo("C3"));
        }

        [Test]
        public void TryGetUserInput_UnknownField_ReturnsFalse()
        {
            var childContext = new StepExecutionContext();
            Guid stepUid = Guid.NewGuid();
            childContext.SetUserInput(stepUid, "工令", "C3");
            StepExecutionContext.RegisterContext(childContext, "测试子线程");

            Assert.That(StepExecutionContext.TryGetUserInput(stepUid, "线体", out string value), Is.False);
            Assert.That(value, Is.Null);
        }

        #endregion

        #region 条件判断：用户输入字段

        private static StepCondition BuildUserInputCondition(Guid stepUid, string fieldName, ConditionOperator op, string value)
            => new StepCondition
            {
                Variable = ConditionVariable.Step_UserInputField,
                StepUid = stepUid,
                FieldName = fieldName,
                Operator = op,
                Value = value,
            };

        [Test]
        public void ConditionEvaluator_StepUserInputField_Equal_MatchesValue()
        {
            Guid stepUid = Guid.NewGuid();
            StepExecutionContext.Instance.SetUserInput(stepUid, new[]
            {
                new UserInputField { Name = "工令", Value = "A12345" },
            });

            var conditions = new List<StepCondition>
            {
                BuildUserInputCondition(stepUid, "工令", ConditionOperator.Equal, "A12345"),
            };
            var result = ConditionEvaluator.Evaluate(conditions, new StepExecutionContext(), new StepExecutionResult { IsTrue = true });

            Assert.That(result, Is.True, "条件应能读到运行前写入的用户输入字段值");
        }

        [Test]
        public void ConditionEvaluator_StepUserInputField_NotEqual_WhenValueDiffers()
        {
            Guid stepUid = Guid.NewGuid();
            StepExecutionContext.Instance.SetUserInput(stepUid, "工令", "A12345");

            var conditions = new List<StepCondition>
            {
                BuildUserInputCondition(stepUid, "工令", ConditionOperator.NotEqual, "B00000"),
            };
            var result = ConditionEvaluator.Evaluate(conditions, new StepExecutionContext(), new StepExecutionResult { IsTrue = false });

            Assert.That(result, Is.True);
        }

        [Test]
        public void ConditionEvaluator_StepUserInputField_Contains_MatchesPartOfValue()
        {
            Guid stepUid = Guid.NewGuid();
            StepExecutionContext.Instance.SetUserInput(stepUid, "工令", "A12345");

            var conditions = new List<StepCondition>
            {
                BuildUserInputCondition(stepUid, "工令", ConditionOperator.Contains, "123"),
            };
            var result = ConditionEvaluator.Evaluate(conditions, new StepExecutionContext(), new StepExecutionResult { IsTrue = true });

            Assert.That(result, Is.True);
        }

        [Test]
        public void ConditionEvaluator_StepUserInputField_MissingField_ComparesAsEmpty()
        {
            Guid stepUid = Guid.NewGuid();
            StepExecutionContext.Instance.SetUserInput(stepUid, "工令", "A12345");

            // 未写入的字段按空字符串参与比较，不能命中非空值
            var conditions = new List<StepCondition>
            {
                BuildUserInputCondition(stepUid, "线体", ConditionOperator.Equal, "3"),
            };
            var result = ConditionEvaluator.Evaluate(conditions, new StepExecutionContext(), new StepExecutionResult { IsTrue = true });

            Assert.That(result, Is.False);
        }

        #endregion

        #region 冻结字段锁定语义

        /// <summary>构造一个冻结字段（默认已确认填写过）</summary>
        private static UserInputField CreateFrozenField(string value = "A12345", bool confirmed = true) => new UserInputField
        {
            Name = "工令",
            Value = value,
            IsFrozen = true,
            IsValueConfirmed = confirmed,
        };

        [Test]
        public void FrozenField_ConfirmedAndNotLoggedIn_IsLockedAndRejectsEdit()
        {
            SetLoggedInState(false);
            var field = CreateFrozenField();

            Assert.That(field.IsFrozen, Is.True);
            Assert.That(field.IsValueConfirmed, Is.True);
            Assert.That(UserInputField.IsUserLoggedIn, Is.False, "测试前提：未登录");
            Assert.That(field.IsValueLocked, Is.True, "冻结+已确认+未登录 -> 锁定");
            Assert.That(field.CanEditValue, Is.False);
            Assert.That(field.LockHint, Is.Not.Empty, "锁定时应给出提示文案");

            field.Value = "B99999";

            Assert.That(field.Value, Is.EqualTo("A12345"), "锁定状态下的 Value setter 必须拒绝修改，值保持不变");
        }

        [Test]
        public void FrozenField_ConfirmedAndLoggedIn_IsEditable()
        {
            SetLoggedInState(true);
            var field = CreateFrozenField();

            Assert.That(UserInputField.IsUserLoggedIn, Is.True, "测试前提：已登录");
            Assert.That(field.IsValueLocked, Is.False, "已登录时冻结字段解锁");
            Assert.That(field.CanEditValue, Is.True);
            Assert.That(field.LockHint, Is.Empty);

            field.Value = "B99999";

            Assert.That(field.Value, Is.EqualTo("B99999"), "登录后应能修改冻结字段的值");
        }

        [Test]
        public void FrozenField_NotConfirmed_CanBeFilledWhileLoggedOut_ThenLocksAfterConfirm()
        {
            SetLoggedInState(false);
            var field = CreateFrozenField(value: "", confirmed: false);

            Assert.That(field.IsValueLocked, Is.False, "首次填写（未确认）时即使未登录也允许输入");
            Assert.That(field.CanEditValue, Is.True);

            field.Value = "A12345";
            Assert.That(field.Value, Is.EqualTo("A12345"));

            // 模拟窗口点「确定」：窗口对「冻结且有值」的字段置 IsValueConfirmed
            field.IsValueConfirmed = true;
            field.RefreshLockState();

            Assert.That(field.IsValueLocked, Is.True, "确认后应立刻锁定");
            Assert.That(field.CanEditValue, Is.False);

            field.Value = "B99999";
            Assert.That(field.Value, Is.EqualTo("A12345"), "确认后未登录不能再改");
        }

        [Test]
        public void NonFrozenField_IsEditableRegardlessOfConfirmAndLoginState()
        {
            SetLoggedInState(false);
            var field = new UserInputField { Name = "备注", Value = "旧值", IsFrozen = false, IsValueConfirmed = true };

            Assert.That(field.IsValueLocked, Is.False, "未冻结字段永远不会被锁定");
            Assert.That(field.CanEditValue, Is.True);

            field.Value = "新值";
            Assert.That(field.Value, Is.EqualTo("新值"));
        }

        [Test]
        public void RefreshLockState_AfterLoginChange_RecomputesEditability()
        {
            SetLoggedInState(false);
            var field = CreateFrozenField();
            Assert.That(field.IsValueLocked, Is.True);

            SetLoggedInState(true);
            field.RefreshLockState();
            Assert.That(field.IsValueLocked, Is.False, "登录后刷新应解锁");
            Assert.That(field.CanEditValue, Is.True);

            SetLoggedInState(false);
            field.RefreshLockState();
            Assert.That(field.IsValueLocked, Is.True, "回到未登录后应重新锁定");
        }

        [Test]
        public void LockedField_AttemptingEdit_RaisesLockedEditAttemptedEvent()
        {
            SetLoggedInState(false);
            var field = CreateFrozenField();
            UserInputField captured = null;
            void Handler(UserInputField f) => captured = f;

            UserInputField.LockedEditAttempted += Handler;
            try
            {
                field.Value = "B99999";
            }
            finally
            {
                UserInputField.LockedEditAttempted -= Handler;
            }

            Assert.That(captured, Is.SameAs(field), "锁定时应触发 LockedEditAttempted，便于界面提示登录");
            Assert.That(field.Value, Is.EqualTo("A12345"));
        }

        [Test]
        public void Clone_PreservesFrozenFlagsAndValue()
        {
            SetLoggedInState(false);
            var field = CreateFrozenField();   // 已锁定
            var step = new UserInputStep("用户输入");
            step.Fields.Add(field);

            var clone = (UserInputStep)step.Clone();
            var clonedField = clone.Fields[0];

            Assert.That(clonedField, Is.Not.SameAs(field));
            Assert.That(clonedField.IsFrozen, Is.True, "Clone 必须保留 IsFrozen");
            Assert.That(clonedField.IsValueConfirmed, Is.True, "Clone 必须保留 IsValueConfirmed");
            Assert.That(clonedField.Value, Is.EqualTo("A12345"), "锁定字段 Clone 时值不能丢");
            Assert.That(clonedField.IsValueLocked, Is.True, "克隆体在未登录时同样处于锁定状态");
        }

#pragma warning disable CS0618 // 测试沿用旧版 JSON API（兼容性覆盖）

        [Test]
        public void SaveAndLoad_FrozenField_KeepsFlagsAndValue()
        {
            SetLoggedInState(false);
            var step = new UserInputStep("用户输入");
            step.Fields.Add(CreateFrozenField());
            step.Fields.Add(new UserInputField { Name = "备注", Value = "x", IsFrozen = false, IsValueConfirmed = false });
            var steps = new ObservableCollection<AutomationStepBase> { step };
            string filePath = Path.Combine(_testDir, "frozen_fields.json");

            StepsFile.SaveStepsToJson(steps, filePath);
            var loaded = StepsFile.LoadStepsFromJson(filePath);
            var restored = loaded[0] as UserInputStep;

            Assert.That(restored, Is.Not.Null);
            Assert.That(restored.Fields.Count, Is.EqualTo(2));
            Assert.That(restored.Fields[0].IsFrozen, Is.True, "JSON 往返必须保留 IsFrozen");
            Assert.That(restored.Fields[0].IsValueConfirmed, Is.True, "JSON 往返必须保留 IsValueConfirmed");
            Assert.That(restored.Fields[0].Value, Is.EqualTo("A12345"), "锁定字段反序列化时值不能丢");
            Assert.That(restored.Fields[1].IsFrozen, Is.False);
            Assert.That(restored.Fields[1].IsValueConfirmed, Is.False);
        }

#pragma warning restore CS0618

        [Test]
        public void LockedField_ValueStillPublishedToExecutionContext()
        {
            // 运行前/打开工程弹窗把字段发布到执行上下文时，锁定字段的值必须照常写入（不能因锁定丢值）。
            // StepsViewModel.PublishUserInputs 只是逐条调用这里验证的 SetUserInput，无额外过滤。
            SetLoggedInState(false);
            var step = new UserInputStep("用户输入");
            step.Fields.Add(CreateFrozenField());
            Assert.That(step.Fields[0].IsValueLocked, Is.True, "测试前提：字段已锁定");

            StepExecutionContext.Instance.SetUserInput(step.Uid, step.Fields);

            Assert.That(StepExecutionContext.TryGetUserInput(step.Uid, "工令", out string value), Is.True);
            Assert.That(value, Is.EqualTo("A12345"), "锁定字段的值也要进入执行上下文");
        }

        #endregion
    }
}
