using System.Windows;
using System.Windows.Controls;

namespace Test.UI
{
    /// <summary>
    /// Interaction logic for SettingsView.xaml
    /// </summary>
    public partial class SettingsView : UserControl
    {
        public SettingsView()
        {
            InitializeComponent();

            // Set the initial password if available
            if (DataContext is SphericalViewerViewModel viewModel && !string.IsNullOrEmpty(viewModel.DatabasePassword))
            {
                DatabasePasswordBox.Password = viewModel.DatabasePassword;
            }
        }

        private void DatabasePasswordBox_PasswordChanged(object sender, RoutedEventArgs e)
        {
            if (DataContext is SphericalViewerViewModel viewModel && sender is PasswordBox passwordBox)
            {
                viewModel.DatabasePassword = passwordBox.Password;
            }
        }
    }
}