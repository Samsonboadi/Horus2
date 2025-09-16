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
        private bool _isDragging = false;
        private Point _lastPos;
        private double _dragAccumX = 0.0;
        private DateTime _lastSectionChange = DateTime.MinValue;

        public SphericalViewerView()
        {
            try
            {
                InitializeComponent();

                // Use the DataContext provided by the DockPane framework (do not create a new VM)
                _viewModel = DataContext as SphericalViewerViewModel;
                this.DataContextChanged += (s, args) =>
                {
                    _viewModel = args.NewValue as SphericalViewerViewModel;
                };

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

    // Interaction handlers for 360° panning and zooming
    public partial class SphericalViewerView
    {
        private const double YawSensitivity = 0.15;   // deg per pixel
        private const double PitchSensitivity = 0.15; // deg per pixel
        private const double MinPitch = -85;
        private const double MaxPitch = 85;
        private const double MinFov = 30;
        private const double MaxFov = 120;

        private void MainImage_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;
                _isDragging = true;
                _lastPos = e.GetPosition((IInputElement)sender);
                _dragAccumX = 0.0;
                ((UIElement)sender).CaptureMouse();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainImage_MouseLeftButtonDown error: {ex}");
            }
        }

        private void MainImage_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                _isDragging = false;
                ((UIElement)sender).ReleaseMouseCapture();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainImage_MouseLeftButtonUp error: {ex}");
            }
        }

        private void MainImage_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
        {
            try
            {
                if (!_isDragging || _viewModel == null) return;
                var pos = e.GetPosition((IInputElement)sender);
                var dx = pos.X - _lastPos.X;
                _lastPos = pos;

                // Map horizontal drag to section changes; accumulate and step every N pixels
                const double stepPx = 8.0; // finer steps for smoother feel
                _dragAccumX += dx;
                if (Math.Abs(_dragAccumX) >= stepPx)
                {
                    // Throttle updates to avoid flooding image requests
                    var now = DateTime.UtcNow;
                    if ((now - _lastSectionChange).TotalMilliseconds >= 60)
                    {
                        var steps = (int)(_dragAccumX / stepPx);
                        _dragAccumX -= steps * stepPx;
                        if (steps != 0)
                        {
                            _viewModel.CurrentSection += steps;
                            _lastSectionChange = now;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainImage_MouseMove error: {ex}");
            }
        }

        private void MainImage_MouseWheel(object sender, System.Windows.Input.MouseWheelEventArgs e)
        {
            try
            {
                if (_viewModel == null) return;
                // Wheel adjusts section too: notch up => next, notch down => previous
                var delta = e.Delta / 120;
                if (delta != 0)
                {
                    var now = DateTime.UtcNow;
                    if ((now - _lastSectionChange).TotalMilliseconds >= 40)
                    {
                        _viewModel.CurrentSection += delta;
                        _lastSectionChange = now;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MainImage_MouseWheel error: {ex}");
            }
        }

        private double NormalizeAngle(double angle)
        {
            angle %= 360.0;
            if (angle < 0) angle += 360.0;
            return angle;
        }
    }
}
