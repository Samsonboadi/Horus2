using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using Test.UI;

namespace Test.UI
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            try
            {
                InitializeComponent();

                // Subscribe to DataContext changed event to handle password initialization
                DataContextChanged += OnDataContextChanged;
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsView constructor error: {ex}");
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Initialize password field when view is loaded
                if (DataContext is SphericalViewerViewModel viewModel &&
                    !string.IsNullOrEmpty(viewModel.DatabasePassword))
                {
                    DatabasePasswordBox.Password = viewModel.DatabasePassword;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsView OnLoaded error: {ex}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Clean up event handlers
                DataContextChanged -= OnDataContextChanged;
                Loaded -= OnLoaded;
                Unloaded -= OnUnloaded;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsView OnUnloaded error: {ex}");
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            try
            {
                // Safe casting with null check and proper type validation
                if (e.NewValue is SphericalViewerViewModel viewModel)
                {
                    // Set the initial password if available when DataContext changes
                    if (!string.IsNullOrEmpty(viewModel.DatabasePassword))
                    {
                        DatabasePasswordBox.Password = viewModel.DatabasePassword;
                    }

                    Debug.WriteLine("SettingsView DataContext successfully set to SphericalViewerViewModel");
                }
                else if (e.NewValue != null)
                {
                    Debug.WriteLine($"SettingsView DataContext set to unexpected type: {e.NewValue.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsView OnDataContextChanged error: {ex}");
            }
        }

        private void DatabasePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            try
            {
                // Safe casting with null checks and proper type validation
                if (DataContext is SphericalViewerViewModel viewModel &&
                    sender is PasswordBox passwordBox)
                {
                    viewModel.DatabasePassword = passwordBox.Password;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SettingsView DatabasePasswordBox_PasswordChanged error: {ex}");
            }
        }
    }
}