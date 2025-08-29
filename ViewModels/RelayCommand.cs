using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace Test.ViewModels
{
    public class RelayCommand : ICommand
    {
        private readonly Action _execute;
        private readonly Func<bool> _canExecute;

        public RelayCommand(Action execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            try
            {
                return _canExecute?.Invoke() ?? true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RelayCommand.CanExecute error: {ex.Message}");
                return false;
            }
        }

        public void Execute(object parameter)
        {
            try
            {
                if (CanExecute(parameter))
                {
                    _execute();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RelayCommand.Execute error: {ex.Message}");

                // Show error to user in a safe way
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            MessageBox.Show($"Command execution failed: {ex.Message}",
                                          "Spherical Image Viewer",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Warning);
                        }
                        catch
                        {
                            // If even MessageBox fails, just log it
                            Debug.WriteLine("Failed to show error message box");
                        }
                    }));
                }
            }
        }
    }

    public class AsyncRelayCommand : ICommand
    {
        private readonly Func<Task> _execute;
        private readonly Func<bool> _canExecute;
        private bool _isExecuting;

        public AsyncRelayCommand(Func<Task> execute, Func<bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            try
            {
                return !_isExecuting && (_canExecute?.Invoke() ?? true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AsyncRelayCommand.CanExecute error: {ex.Message}");
                return false;
            }
        }

        public async void Execute(object parameter)
        {
            if (!CanExecute(parameter))
                return;

            try
            {
                _isExecuting = true;

                // Update command states
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        CommandManager.InvalidateRequerySuggested()));
                }

                await _execute();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"AsyncRelayCommand.Execute error: {ex.Message}");

                // Show error to user in a safe way
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            MessageBox.Show($"Operation failed: {ex.Message}",
                                          "Spherical Image Viewer",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Warning);
                        }
                        catch
                        {
                            Debug.WriteLine("Failed to show async error message box");
                        }
                    }));
                }
            }
            finally
            {
                _isExecuting = false;

                // Update command states
                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        CommandManager.InvalidateRequerySuggested()));
                }
            }
        }
    }

    // Generic versions for parameterized commands
    public class RelayCommand<T> : ICommand
    {
        private readonly Action<T> _execute;
        private readonly Func<T, bool> _canExecute;

        public RelayCommand(Action<T> execute, Func<T, bool> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public event EventHandler CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public bool CanExecute(object parameter)
        {
            try
            {
                if (parameter is T typedParameter)
                {
                    return _canExecute?.Invoke(typedParameter) ?? true;
                }
                return _canExecute?.Invoke(default(T)) ?? true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RelayCommand<T>.CanExecute error: {ex.Message}");
                return false;
            }
        }

        public void Execute(object parameter)
        {
            try
            {
                if (CanExecute(parameter))
                {
                    if (parameter is T typedParameter)
                    {
                        _execute(typedParameter);
                    }
                    else
                    {
                        _execute(default(T));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RelayCommand<T>.Execute error: {ex.Message}");

                if (Application.Current?.Dispatcher != null)
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            MessageBox.Show($"Command execution failed: {ex.Message}",
                                          "Spherical Image Viewer",
                                          MessageBoxButton.OK,
                                          MessageBoxImage.Warning);
                        }
                        catch
                        {
                            Debug.WriteLine("Failed to show generic error message box");
                        }
                    }));
                }
            }
        }
    }
}