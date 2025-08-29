// Create this file: UI/SphericalViewerShowButton.cs

using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;

namespace Test.UI
{
    internal class SphericalViewerShowButton : Button
    {
        protected override void OnClick()
        {
            DockPane pane = FrameworkApplication.DockPaneManager.Find("Test_SphericalViewer_DockPane");
            pane?.Activate();
        }
    }
}