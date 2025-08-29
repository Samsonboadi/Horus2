using System.Windows.Controls;
using ArcGIS.Desktop.Framework;

namespace Test.UI
{
    /// <summary>
    /// Interaction logic for SphericalViewerView.xaml
    /// </summary>
    public partial class SphericalViewerView : UserControl
    {
        public SphericalViewerView()
        {
            InitializeComponent();
            // Explicitly set the DataContext to the SphericalViewerViewModel
            DataContext = new SphericalViewerViewModel();
        }

        // Optional: Handle cleanup when the UserControl is unloaded
        private void UserControl_Unloaded(object sender, System.Windows.RoutedEventArgs e)
        {
            // Ensure the settings window is closed when the view is unloaded
            var viewModel = DataContext as SphericalViewerViewModel;
            if (viewModel != null && viewModel.IsSettingsOpen)
            {
                viewModel.CloseSettingsCommand.Execute(null);
            }
        }
    }
}