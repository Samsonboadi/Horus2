using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Catalog;
using ArcGIS.Desktop.Core;
using ArcGIS.Desktop.Editing;
using ArcGIS.Desktop.Extensions;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.KnowledgeGraph;
using ArcGIS.Desktop.Layouts;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using Test.UI;

namespace Test
{
    internal class Module1 : Module
    {
        private static Module1 _this = null;

        /// <summary>
        /// Retrieve the singleton instance to this module here
        /// </summary>
        public static Module1 Current => _this ??= (Module1)FrameworkApplication.FindModule("Test_Module");

        #region Overrides
        /// <summary>
        /// Called by Framework when ArcGIS Pro is closing
        /// </summary>
        /// <returns>False to prevent Pro from closing, otherwise True</returns>
        protected override bool CanUnload()
        {
            try
            {
                // Clean up resources when the module is unloading
                CleanupServices();

                //TODO - add your business logic
                //return false to ~cancel~ Application close
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module1.CanUnload error: {ex}");
                return true; // Don't prevent shutdown due to cleanup errors
            }
        }

        /// <summary>
        /// Called when the module is initialized
        /// </summary>
        /// <returns></returns>
        protected override bool Initialize()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Module1 initializing...");
                return base.Initialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module1.Initialize error: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Called when the module is being unloaded
        /// </summary>
        protected override void Uninitialize()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Module1 uninitializing...");
                CleanupServices();
                base.Uninitialize();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module1.Uninitialize error: {ex}");
            }
        }

        /// <summary>
        /// Clean up any resources used by the module
        /// </summary>
        private void CleanupServices()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("Module1: Starting cleanup process...");

                // Find and cleanup the dock pane if it exists
                var dockPane = FrameworkApplication.DockPaneManager.Find("Test_SphericalViewer_DockPane");
                if (dockPane != null)
                {
                    System.Diagnostics.Debug.WriteLine("Module1: Found dock pane, performing cleanup");
                    // The dock pane will handle its own cleanup in OnHidden()
                }

                System.Diagnostics.Debug.WriteLine("Module1: Cleanup completed successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module1.CleanupServices error: {ex}");
            }
        }

        /// <summary>
        /// Get reference to the SphericalViewerViewModel for cross-component communication
        /// </summary>
        public SphericalViewerViewModel GetViewModel()
        {
            try
            {
                var dockPane = FrameworkApplication.DockPaneManager.Find("Test_SphericalViewer_DockPane");
                return dockPane as SphericalViewerViewModel;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Module1.GetViewModel error: {ex}");
                return null;
            }
        }

        #endregion Overrides
    }
}