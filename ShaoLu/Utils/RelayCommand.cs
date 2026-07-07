using System;
using System.Windows.Input;
using System.Windows.Threading;

namespace ShaoLu
{
    public class RelayCommand(Action execute, Func<bool> canExecute) : ICommand
    {
        private readonly Action _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<bool> _canExecute = canExecute;
        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action execute) : this(execute, null) { }

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute();
        }

        public void Execute(object parameter)
        {
            _execute?.Invoke();
        }

        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public class RelayCommand<T>(Action<T> execute, Func<T, bool> canExecute) : ICommand
    { 
        private readonly Action<T> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<T, bool> _canExecute = canExecute;
        public event EventHandler CanExecuteChanged;

        public bool CanExecute(object parameter)
        {
            return _canExecute == null || _canExecute((T)parameter);
        }
        public void Execute(object parameter)
        {
            _execute?.Invoke((T)parameter);
        }
        public void RaiseCanExecuteChanged()
        {
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }


    /// <summary>
    /// 一个支持参数且线程安全的 RelayCommand 实现，适用于 WPF (.NET Framework 4.8)。
    /// </summary>
    /// <remarks>
    /// 创建一个新的 RelayParameterCommand。
    /// </remarks>
    /// <param name="execute">执行逻辑。不能为 null。</param>
    /// <param name="canExecute">判断是否可执行的逻辑。可以为 null，默认为 true。</param>
    public class RelayParameterCommand(Action<object> execute, Func<bool> canExecute = null) : ICommand
    {
        private readonly Action<object> _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        private readonly Func<bool> _canExecute = canExecute;

        /// <summary>
        /// 发生当 CanExecute 状态改变时。
        /// </summary>
        public event EventHandler CanExecuteChanged;

        /// <summary>
        /// 确定命令是否可以在当前状态下执行。
        /// </summary>
        public bool CanExecute(object parameter)
        {
            // 如果未提供 canExecute 委托，则默认始终可执行
            return _canExecute == null || _canExecute();
        }

        /// <summary>
        /// 执行命令逻辑。
        /// </summary>
        public void Execute(object parameter)
        {
            // _execute 在构造函数中已保证非空，直接调用
            _execute(parameter);
        }

        /// <summary>
        /// 手动触发 CanExecuteChanged 事件。
        /// 此方法确保事件在 UI 线程上引发，以符合 WPF 绑定要求。
        /// </summary>
        public void RaiseCanExecuteChanged()
        {
            var handler = CanExecuteChanged;
            if (handler != null)
            {
                // 检查当前线程是否为 UI 线程
                var dispatcher = Dispatcher.CurrentDispatcher;

                if (dispatcher.CheckAccess())
                {
                    // 如果已经在 UI 线程，直接 invoke
                    handler(this, EventArgs.Empty);
                }
                else
                {
                    // 否则，异步 marshalling 到 UI 线程
                    // 使用 BeginInvoke 避免阻塞调用线程，并防止死锁
                    dispatcher.BeginInvoke(new Action(() => handler(this, EventArgs.Empty)));
                }
            }
        }
    }
}
