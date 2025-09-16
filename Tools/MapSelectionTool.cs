using System;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using Test;

namespace Test.Tools
{
    internal class MapSelectionTool : MapTool
    {
        public MapSelectionTool()
        {
            IsSketchTool = false;
            UseSnapping = true;
            SketchType = SketchGeometryType.None;
            SketchOutputMode = SketchOutputMode.Map;
        }

        protected override void OnToolMouseDown(MapViewMouseButtonEventArgs e)
        {
            try
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    e.Handled = true;
                    QueuedTask.Run(async () =>
                    {
                        try
                        {
                            var mapView = MapView.Active;
                            if (mapView == null) return;

                            var mapPoint = mapView.ClientToMap(e.ClientPoint);
                            if (mapPoint == null) return;

                            if (mapPoint.SpatialReference?.Wkid != 4326)
                            {
                                var wgs84 = SpatialReferenceBuilder.CreateSpatialReference(4326);
                                mapPoint = (MapPoint)GeometryEngine.Instance.Project(mapPoint, wgs84);
                            }

                            var vm = Module1.Current?.GetViewModel();
                            if (vm == null) return;

                            await vm.OnMapPointClickedAsync(mapPoint);
                        }
                        catch (Exception ex)
                        {
                            System.Diagnostics.Debug.WriteLine($"MapSelectionTool error: {ex}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnToolMouseDown error: {ex}");
            }
        }
    }
}
