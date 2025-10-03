// Utilities/RelayCommand.cs
using System;
using System.Windows.Input;

namespace CmdRunnerPro.ViewModels
{
    /// <summary>
    /// Simple non-generic RelayCommand for parameterless execute/canExecute.
    /// </summary>
    public sealed class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter) => _canExecute?.Invoke() ?? true;

        public void Execute(object parameter) => _execute();

        // Wire into CommandManager so WPF re-queries automatically,
        // and also allow manual invalidation via RaiseCanExecuteChanged().
        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Generic RelayCommand for commands that accept a parameter of type T.
    /// </summary>
    public sealed class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Predicate<T> _canExecute;

        public RelayCommand(Action<T> execute, Predicate<T> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            if (_canExecute is null) return true;
            return _canExecute(CastParameter(parameter));
        }

        public void Execute(object parameter) => _execute(CastParameter(parameter));

        private static T CastParameter(object parameter)
        {
            // Null -> default(T); safe for reference and value types
            if (parameter is null) return default;
            if (parameter is T t) return t;

            // Try to handle simple convertible cases (optional)
            try
            {
                return (T)Convert.ChangeType(parameter, typeof(T));
            }
            catch
            {
                return default;
            }
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
    }
}