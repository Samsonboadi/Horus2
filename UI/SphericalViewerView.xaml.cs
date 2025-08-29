using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using ArcGIS.Desktop.Framework;

namespace Test.UI
{
    /// <summary>
    /// Interaction logic for SphericalViewerView.xaml
    /// </summary>
    public partial class SphericalViewerView : UserControl
    {
        private SphericalViewerViewModel _viewModel;

        public SphericalViewerView()
        {
            try
            {
                InitializeComponent();

                // Create and set the view model
                _viewModel = new SphericalViewerViewModel();
                DataContext = _viewModel;

                // Handle cleanup events
                Loaded += OnLoaded;
                Unloaded += OnUnloaded;

                Debug.WriteLine("SphericalViewerView initialized successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerView constructor error: {ex}");

                // Try to show a meaningful error message
                try
                {
                    MessageBox.Show($"Failed to initialize Spherical Viewer: {ex.Message}",
                                  "Initialization Error",
                                  MessageBoxButton.OK,
                                  MessageBoxImage.Error);
                }
                catch
                {
                    // If even MessageBox fails, we're in trouble
                    Debug.WriteLine("Critical: Failed to show initialization error message");
                }
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("SphericalViewerView loaded successfully");

                // Ensure DataContext is properly set
                if (DataContext == null && _viewModel != null)
                {
                    DataContext = _viewModel;
                    Debug.WriteLine("DataContext reassigned to view model");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerView OnLoaded error: {ex}");
            }
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("SphericalViewerView unloading - performing cleanup");

                // Ensure the settings window is closed when the view is unloaded
                if (_viewModel != null && _viewModel.IsSettingsOpen)
                {
                    _viewModel.CloseSettingsCommand?.Execute(null);
                }

                // Clean up event handlers
                Loaded -= OnLoaded;
                Unloaded -= OnUnloaded;

                Debug.WriteLine("SphericalViewerView cleanup completed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerView OnUnloaded error: {ex}");
            }
        }

        /// <summary>
        /// Handle any view-specific cleanup or special cases
        /// </summary>
        private void UserControl_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            try
            {
                // Handle any size-dependent layout adjustments if needed
                Debug.WriteLine($"SphericalViewerView size changed to: {e.NewSize}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerView SizeChanged error: {ex}");
            }
        }

        /// <summary>
        /// Handle focus events to ensure proper input handling
        /// </summary>
        private void UserControl_GotFocus(object sender, RoutedEventArgs e)
        {
            try
            {
                Debug.WriteLine("SphericalViewerView got focus");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerView GotFocus error: {ex}");
            }
        }
    }
}