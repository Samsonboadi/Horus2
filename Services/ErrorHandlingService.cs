using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace Test.Services
{
    /// <summary>
    /// Centralized error handling and logging service for the add-in
    /// </summary>
    public class ErrorHandlingService
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SphericalImageViewer",
            "Logs");

        private static readonly string LogFilePath = Path.Combine(LogDirectory,
            $"SphericalViewer_{DateTime.Now:yyyyMMdd}.log");

        static ErrorHandlingService()
        {
            try
            {
                // Ensure log directory exists
                Directory.CreateDirectory(LogDirectory);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create log directory: {ex}");
            }
        }

        /// <summary>
        /// Log an error with context information
        /// </summary>
        public static void LogError(Exception ex, string context = "", string additionalInfo = "")
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] ERROR in {context}: {ex.Message}\n" +
                               $"Additional Info: {additionalInfo}\n" +
                               $"Stack Trace: {ex.StackTrace}\n" +
                               $"Inner Exception: {ex.InnerException?.Message ?? "None"}\n" +
                               new string('-', 80) + "\n";

                // Write to debug output
                Debug.WriteLine(logMessage);

                // Write to file asynchronously
                Task.Run(async () =>
                {
                    try
                    {
                        await File.AppendAllTextAsync(LogFilePath, logMessage);
                    }
                    catch
                    {
                        // If file logging fails, at least we have debug output
                    }
                });
            }
            catch
            {
                // Last resort - just write to debug if everything else fails
                Debug.WriteLine($"CRITICAL ERROR in {context}: {ex?.Message}");
            }
        }

        /// <summary>
        /// Log an informational message
        /// </summary>
        public static void LogInfo(string message, string context = "")
        {
            try
            {
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                var logMessage = $"[{timestamp}] INFO in {context}: {message}\n";

                Debug.WriteLine(logMessage);

                Task.Run(async () =>
                {
                    try
                    {
                        await File.AppendAllTextAsync(LogFilePath, logMessage);
                    }
                    catch
                    {
                        // Ignore file logging failures for info messages
                    }
                });
            }
            catch
            {
                Debug.WriteLine($"INFO {context}: {message}");
            }
        }

        /// <summary>
        /// Show a user-friendly error message
        /// </summary>
        public static void ShowErrorToUser(string message, string title = "Spherical Image Viewer")
        {
            try
            {
                if (Application.Current?.Dispatcher != null)
                {
                    if (Application.Current.Dispatcher.CheckAccess())
                    {
                        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    else
                    {
                        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning)));
                    }
                }
                else
                {
                    Debug.WriteLine($"Cannot show message to user - no dispatcher available: {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to show error message to user: {ex.Message}");
            }
        }

        /// <summary>
        /// Execute an action with automatic error handling and logging
        /// </summary>
        public static T ExecuteSafely<T>(Func<T> action, string context, T defaultValue = default(T))
        {
            try
            {
                return action();
            }
            catch (Exception ex)
            {
                LogError(ex, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Execute an async action with automatic error handling and logging
        /// </summary>
        public static async Task<T> ExecuteSafelyAsync<T>(Func<Task<T>> action, string context, T defaultValue = default(T))
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                LogError(ex, context);
                return defaultValue;
            }
        }

        /// <summary>
        /// Execute an action with automatic error handling, logging, and user notification
        /// </summary>
        public static void ExecuteSafelyWithUserFeedback(Action action, string context, string userErrorMessage = null)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                LogError(ex, context);

                var message = userErrorMessage ?? $"An error occurred: {ex.Message}";
                ShowErrorToUser(message);
            }
        }

        /// <summary>
        /// Execute an async action with automatic error handling, logging, and user notification
        /// </summary>
        public static async Task ExecuteSafelyWithUserFeedbackAsync(Func<Task> action, string context, string userErrorMessage = null)
        {
            try
            {
                await action();
            }
            catch (Exception ex)
            {
                LogError(ex, context);

                var message = userErrorMessage ?? $"An error occurred: {ex.Message}";
                ShowErrorToUser(message);
            }
        }

        /// <summary>
        /// Validate that required services are available
        /// </summary>
        public static bool ValidateArcGISProEnvironment()
        {
            try
            {
                // Basic validation that we're in a WPF context
                if (Application.Current == null)
                {
                    LogError(new InvalidOperationException("Not running in WPF application context"), "Environment Validation");
                    return false;
                }

                LogInfo("Application environment validation successful", "Environment Validation");
                return true;
            }
            catch (Exception ex)
            {
                LogError(ex, "Environment Validation");
                return false;
            }
        }

        /// <summary>
        /// Get the current log file path for debugging purposes
        /// </summary>
        public static string GetCurrentLogPath()
        {
            return LogFilePath;
        }

        /// <summary>
        /// Clear old log files (keep only last 7 days)
        /// </summary>
        public static void CleanupOldLogs()
        {
            try
            {
                if (!Directory.Exists(LogDirectory))
                    return;

                var files = Directory.GetFiles(LogDirectory, "SphericalViewer_*.log");
                var cutoffDate = DateTime.Now.AddDays(-7);

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.CreationTime < cutoffDate)
                        {
                            File.Delete(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete old log file {file}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to cleanup old logs: {ex.Message}");
            }
        }
    }
}