// Services/ArcGISMapService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics;
using ArcGIS.Core.CIM;
using ArcGIS.Core.Data;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using ArcGIS.Desktop.Framework.Dialogs;
using Test.Models;
using ArcGIS.Core.Data.DDL;
using ArcGIS.Desktop.Editing;

namespace Test.Services
{
    public class ArcGISMapService
    {
        private const string WFS_POINTS_LAYER_NAME = "Horus_WFS_Points";
        private const string SELECTED_POINTS_LAYER_NAME = "Horus_Selected_Points";
        private const string AI_RESULTS_LAYER_NAME = "Horus_AI_Results";

        private FeatureLayer _wfsPointsLayer;
        private FeatureLayer _selectedPointsLayer;
        private FeatureLayer _aiResultsLayer;
        private Map _activeMap;
        public bool ForceWebMercator { get; set; } = true;

        /// <summary>
        /// Initialize the map service and create necessary layers
        /// </summary>
        public async Task<bool> InitializeAsync()
        {
            try
            {
                return await QueuedTask.Run(async () =>
                {
                    _activeMap = MapView.Active?.Map;
                    if (_activeMap == null)
                    {
                        Debug.WriteLine("No active map found");
                        return false;
                    }

                    if (ForceWebMercator)
                    {
                        // Ensure map uses Web Mercator (to avoid distortion with common basemaps)
                        try
                        {
                            var wm = SpatialReferenceBuilder.CreateSpatialReference(3857);
                            if (_activeMap.SpatialReference == null || _activeMap.SpatialReference.Wkid != 3857)
                            {
                                _activeMap.SetSpatialReference(wm);
                                Debug.WriteLine("Map spatial reference set to Web Mercator (EPSG:3857)");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"Failed to set map spatial reference: {ex}");
                        }
                    }

                    // Create or get WFS points layer
                    _wfsPointsLayer = await CreateOrGetWfsPointsLayerAsync();

                    // Create or get selected points layer
                    _selectedPointsLayer = await CreateOrGetSelectedPointsLayerAsync();

                    // Create or get AI results layer
                    _aiResultsLayer = await CreateOrGetAiResultsLayerAsync();

                    Debug.WriteLine("ArcGIS Map Service initialized successfully");
                    return true;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Map service initialization failed: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Add WFS features to the map as points
        /// </summary>
        public async Task<bool> AddWfsFeaturesToMapAsync(List<WfsFeature> features)
        {
            if (features == null || features.Count == 0)
            {
                Debug.WriteLine("No features to add to map");
                return true;
            }

            try
            {
                return await QueuedTask.Run(async () =>
                {
                    if (_wfsPointsLayer == null)
                    {
                        await InitializeAsync();
                    }

                    if (_wfsPointsLayer?.GetTable() is not FeatureClass featureClass)
                    {
                        Debug.WriteLine("WFS points layer not available");
                        return false;
                    }

                    var editOperation = new EditOperation()
                    {
                        Name = "Add WFS Points",
                        SelectNewFeatures = false
                    };

                    foreach (var feature in features)
                    {
                        if (feature.MapPoint == null) continue;

                        editOperation.Callback(context =>
                        {
                            using (var rb = featureClass.CreateRowBuffer())
                            {
                                rb["Shape"] = feature.MapPoint;
                                SetAttributeValue(rb, "FeatureId", feature.Id);
                                SetAttributeValue(rb, "RecordingId", feature.Properties?.RecordingId);
                                SetAttributeValue(rb, "Guid", feature.Properties?.Guid);
                                SetAttributeValue(rb, "Name", feature.Properties?.DisplayName);
                                SetAttributeValue(rb, "ImageUrl", feature.ImageUrl);
                                SetAttributeValue(rb, "Timestamp", feature.Properties?.Timestamp);
                                SetAttributeValue(rb, "IsSelected", feature.IsSelected ? 1 : 0);

                                using (var row = featureClass.CreateRow(rb))
                                {
                                    context.Invalidate(row);
                                }
                            }
                        }, featureClass);
                    }

                    var result = await editOperation.ExecuteAsync();
                    if (result)
                    {
                        Debug.WriteLine($"Successfully added {features.Count} WFS features to map");
                        return true;
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to add WFS features: {editOperation.ErrorMessage}");
                        return false;
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding WFS features to map: {ex}");
                return false;
            }
        }

        /// <summary>
        /// Update the selection status of features on the map
        /// </summary>
        public async Task UpdateFeatureSelectionAsync(List<WfsFeature> selectedFeatures)
        {
            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (_selectedPointsLayer?.GetTable() is not FeatureClass selectedFeatureClass)
                        return;

                    // Clear existing selected points (delete all)
                    var clearOperation = new EditOperation() { Name = "Clear Selected Points" };
                    clearOperation.Callback(ctx =>
                    {
                        selectedFeatureClass.DeleteRows(new QueryFilter());
                        ctx.Invalidate(selectedFeatureClass);
                    }, selectedFeatureClass);
                    await clearOperation.ExecuteAsync();

                    // Add currently selected points
                    if (selectedFeatures?.Count > 0)
                    {
                        var addOperation = new EditOperation()
                        {
                            Name = "Update Selected Points"
                        };

                        foreach (var feature in selectedFeatures)
                        {
                            if (feature.MapPoint == null) continue;

                            addOperation.Callback(ctx =>
                            {
                                using (var rb = selectedFeatureClass.CreateRowBuffer())
                                {
                                    rb["Shape"] = feature.MapPoint;
                                    SetAttributeValue(rb, "FeatureId", feature.Id);
                                    SetAttributeValue(rb, "RecordingId", feature.Properties?.RecordingId);
                                    SetAttributeValue(rb, "Guid", feature.Properties?.Guid);
                                    SetAttributeValue(rb, "Name", feature.Properties?.DisplayName);
                                    SetAttributeValue(rb, "SelectionOrder", selectedFeatures.IndexOf(feature));

                                    using (var row = selectedFeatureClass.CreateRow(rb))
                                    {
                                        ctx.Invalidate(row);
                                    }
                                }
                            }, selectedFeatureClass);
                        }

                        await addOperation.ExecuteAsync();
                    }

                    Debug.WriteLine($"Updated selection display: {selectedFeatures?.Count ?? 0} features selected");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating feature selection: {ex}");
            }
        }

        /// <summary>
        /// Add AI detection results to the map
        /// </summary>
        public async Task AddAiDetectionResultsAsync(WfsFeature feature, List<DetectionResult> results)
        {
            if (feature?.MapPoint == null || results?.Count == 0)
                return;

            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (_aiResultsLayer?.GetTable() is not FeatureClass aiFeatureClass)
                        return;

                    var editOperation = new EditOperation()
                    {
                        Name = "Add AI Detection Results"
                    };

                    foreach (var det in results)
                    {
                        editOperation.Callback(ctx =>
                        {
                            using (var rb = aiFeatureClass.CreateRowBuffer())
                            {
                                rb["Shape"] = feature.MapPoint;
                                SetAttributeValue(rb, "FeatureId", feature.Id);
                                SetAttributeValue(rb, "ObjectName", det.ObjectName);
                                SetAttributeValue(rb, "Confidence", det.Confidence);
                                SetAttributeValue(rb, "ModelUsed", det.ModelUsed);
                                SetAttributeValue(rb, "ProcessingTime", det.ProcessingTime);
                                SetAttributeValue(rb, "DetectionTime", DateTime.Now);

                                using (var row = aiFeatureClass.CreateRow(rb))
                                {
                                    ctx.Invalidate(row);
                                }
                            }
                        }, aiFeatureClass);
                    }

                    var success = await editOperation.ExecuteAsync();
                    if (success)
                    {
                        Debug.WriteLine($"Added {results.Count} AI detection results for feature {feature.Id}");
                    }
                    else
                    {
                        Debug.WriteLine($"Failed to add AI results: {editOperation.ErrorMessage}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error adding AI detection results: {ex}");
            }
        }

        /// <summary>
        /// Zoom to selected features
        /// </summary>
        public async Task ZoomToSelectedFeaturesAsync(List<WfsFeature> features)
        {
            if (features?.Count == 0)
                return;

            try
            {
                await QueuedTask.Run(() =>
                {
                    var mapView = MapView.Active;
                    if (mapView == null) return;

                    if (features.Count == 1)
                    {
                        // Zoom to single point with appropriate scale
                        var point = features[0].MapPoint;
                        var camera = new Camera(point.X, point.Y, 1000, 0); // 1:1000 scale
                        mapView.ZoomToAsync(camera);
                    }
                    else
                    {
                        // Create envelope around all points using multipoint extent
                        var points = features.Where(f => f.MapPoint != null).Select(f => f.MapPoint).ToList();
                        if (points.Count > 0)
                        {
                            var multipoint = MultipointBuilderEx.CreateMultipoint(points);
                            var envelope = multipoint.Extent;
                            var expandedEnvelope = GeometryEngine.Instance.Expand(envelope, 1.2, 1.2, true);
                            mapView.ZoomToAsync(expandedEnvelope);
                        }
                    }

                    Debug.WriteLine($"Zoomed to {features.Count} selected features");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error zooming to features: {ex}");
            }
        }

        /// <summary>
        /// Clear all WFS features from the map
        /// </summary>
        public async Task ClearWfsFeaturesAsync()
        {
            try
            {
                await QueuedTask.Run(async () =>
                {
                    var layers = new[] { _wfsPointsLayer, _selectedPointsLayer, _aiResultsLayer };

                    foreach (var layer in layers.Where(l => l != null))
                    {
                        if (layer.GetTable() is FeatureClass fc)
                        {
                            var op = new EditOperation() { Name = $"Clear {layer.Name}" };
                            op.Callback(ctx =>
                            {
                                fc.DeleteRows(new QueryFilter());
                                ctx.Invalidate(fc);
                            }, fc);
                            await op.ExecuteAsync();
                        }
                    }

                    Debug.WriteLine("Cleared all WFS features from map");
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing WFS features: {ex}");
            }
        }

        /// <summary>
        /// Clear only the WFS points layer (leave selection/AI layers intact)
        /// </summary>
        public async Task ClearWfsPointsLayerAsync()
        {
            try
            {
                await QueuedTask.Run(async () =>
                {
                    if (_wfsPointsLayer?.GetTable() is FeatureClass fc)
                    {
                        var op = new EditOperation() { Name = "Clear WFS Points" };
                        op.Callback(ctx =>
                        {
                            fc.DeleteRows(new QueryFilter());
                            ctx.Invalidate(fc);
                        }, fc);
                        await op.ExecuteAsync();
                        Debug.WriteLine("Cleared WFS points layer");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error clearing WFS points layer: {ex}");
            }
        }

        /// <summary>
        /// Get the current map extent
        /// </summary>
        public async Task<Envelope> GetCurrentMapExtentAsync()
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var mapView = MapView.Active;
                    return mapView?.Extent;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting map extent: {ex}");
                return null;
            }
        }

        /// <summary>
        /// Find features near a click point
        /// </summary>
        public async Task<List<WfsFeature>> FindFeaturesNearPointAsync(MapPoint clickPoint, double toleranceMeters = 10)
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var results = new List<WfsFeature>();

                    if (_wfsPointsLayer?.GetTable() is not FeatureClass featureClass)
                        return results;

                    // Build a buffer of toleranceMeters using planar buffer in Web Mercator, then project back to WGS84
                    var wm = SpatialReferenceBuilder.CreateSpatialReference(3857);
                    var wgs84 = SpatialReferenceBuilder.CreateSpatialReference(4326);
                    var clickWm = (MapPoint)GeometryEngine.Instance.Project(clickPoint, wm);
                    var bufferWm = GeometryEngine.Instance.Buffer(clickWm, toleranceMeters);
                    var searchGeometry = GeometryEngine.Instance.Project(bufferWm, wgs84);
                    var spatialFilter = new SpatialQueryFilter()
                    {
                        FilterGeometry = searchGeometry,
                        SpatialRelationship = SpatialRelationship.Intersects
                    };

                    using (var cursor = featureClass.Search(spatialFilter))
                    {
                        while (cursor.MoveNext())
                        {
                            using (var row = cursor.Current)
                            {
                                var feature = CreateWfsFeatureFromRow(row);
                                if (feature != null)
                                {
                                    results.Add(feature);
                                }
                            }
                        }
                    }

                    // Sort by geodesic distance to click point so [0] is the nearest
                    try
                    {
                        results = results
                            .OrderBy(f =>
                            {
                                var fp = (MapPoint)GeometryEngine.Instance.Project(f.MapPoint, wm);
                                return GeometryEngine.Instance.Distance(clickWm, fp);
                            })
                            .ToList();
                    }
                    catch { }

                    Debug.WriteLine($"Found {results.Count} features near click point");
                    return results;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error finding features near point: {ex}");
                return new List<WfsFeature>();
            }
        }

        #region Private Helper Methods

        private async Task<FeatureLayer> CreateOrGetWfsPointsLayerAsync()
        {
            var existingLayer = _activeMap.FindLayers(WFS_POINTS_LAYER_NAME).FirstOrDefault() as FeatureLayer;
            if (existingLayer != null)
                return existingLayer;

            return await CreateInMemoryFeatureLayerAsync(WFS_POINTS_LAYER_NAME, CreateWfsPointsSchema(), GetWfsPointsSymbol());
        }

        private async Task<FeatureLayer> CreateOrGetSelectedPointsLayerAsync()
        {
            var existingLayer = _activeMap.FindLayers(SELECTED_POINTS_LAYER_NAME).FirstOrDefault() as FeatureLayer;
            if (existingLayer != null)
                return existingLayer;

            return await CreateInMemoryFeatureLayerAsync(SELECTED_POINTS_LAYER_NAME, CreateSelectedPointsSchema(), GetSelectedPointsSymbol());
        }

        private async Task<FeatureLayer> CreateOrGetAiResultsLayerAsync()
        {
            var existingLayer = _activeMap.FindLayers(AI_RESULTS_LAYER_NAME).FirstOrDefault() as FeatureLayer;
            if (existingLayer != null)
                return existingLayer;

            return await CreateInMemoryFeatureLayerAsync(AI_RESULTS_LAYER_NAME, CreateAiResultsSchema(), GetAiResultsSymbol());
        }

        private Task<FeatureLayer> CreateInMemoryFeatureLayerAsync(string layerName, System.Collections.Generic.IEnumerable<ArcGIS.Core.Data.DDL.FieldDescription> fields, CIMSymbolReference symbol)
        {
            try
            {
                var featureClassDescription = new FeatureClassDescription(layerName, fields,
                    new ShapeDescription(GeometryType.Point, SpatialReferenceBuilder.CreateSpatialReference(4326)));

                var memConn = new MemoryConnectionProperties();
                var geodatabase = new Geodatabase(memConn);
                var schemaBuilder = new SchemaBuilder(geodatabase);
                schemaBuilder.Create(featureClassDescription);
                schemaBuilder.Build();
                var featureClass = geodatabase.OpenDataset<FeatureClass>(layerName);

                var layerParams = new FeatureLayerCreationParams(featureClass)
                {
                    Name = layerName,
                    IsVisible = true
                };

                var featureLayer = LayerFactory.Instance.CreateLayer<FeatureLayer>(layerParams, _activeMap);

                // Apply symbol
                var renderer = featureLayer.GetRenderer() as CIMSimpleRenderer;
                if (renderer != null)
                {
                    renderer.Symbol = symbol;
                    featureLayer.SetRenderer(renderer);
                }

                Debug.WriteLine($"Created in-memory layer: {layerName}");
                return Task.FromResult(featureLayer);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create layer {layerName}: {ex}");
                return Task.FromResult<FeatureLayer>(null);
            }
        }

        private System.Collections.Generic.List<ArcGIS.Core.Data.DDL.FieldDescription> CreateWfsPointsSchema()
        {
            var fields = new List<ArcGIS.Core.Data.DDL.FieldDescription>();
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("FeatureId", FieldType.String) { Length = 100 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("RecordingId", FieldType.String) { Length = 50 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Guid", FieldType.String) { Length = 50 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Name", FieldType.String) { Length = 200 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("ImageUrl", FieldType.String) { Length = 500 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Timestamp", FieldType.Date));
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("IsSelected", FieldType.Integer));
            return fields;
        }

        private System.Collections.Generic.List<ArcGIS.Core.Data.DDL.FieldDescription> CreateSelectedPointsSchema()
        {
            var fields = new List<ArcGIS.Core.Data.DDL.FieldDescription>();
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("FeatureId", FieldType.String) { Length = 100 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("RecordingId", FieldType.String) { Length = 50 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Guid", FieldType.String) { Length = 50 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Name", FieldType.String) { Length = 200 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("SelectionOrder", FieldType.Integer));
            return fields;
        }

        private System.Collections.Generic.List<ArcGIS.Core.Data.DDL.FieldDescription> CreateAiResultsSchema()
        {
            var fields = new List<ArcGIS.Core.Data.DDL.FieldDescription>();
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("FeatureId", FieldType.String) { Length = 100 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("ObjectName", FieldType.String) { Length = 100 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("Confidence", FieldType.Double));
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("ModelUsed", FieldType.String) { Length = 100 });
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("ProcessingTime", FieldType.Double));
            fields.Add(new ArcGIS.Core.Data.DDL.FieldDescription("DetectionTime", FieldType.Date));
            return fields;
        }

        private CIMSymbolReference GetWfsPointsSymbol()
        {
            return SymbolFactory.Instance.ConstructPointSymbol(
                ColorFactory.Instance.BlueRGB, 8, SimpleMarkerStyle.Circle).MakeSymbolReference();
        }

        private CIMSymbolReference GetSelectedPointsSymbol()
        {
            return SymbolFactory.Instance.ConstructPointSymbol(
                ColorFactory.Instance.RedRGB, 12, SimpleMarkerStyle.Circle).MakeSymbolReference();
        }

        private CIMSymbolReference GetAiResultsSymbol()
        {
            return SymbolFactory.Instance.ConstructPointSymbol(
                ColorFactory.Instance.GreenRGB, 6, SimpleMarkerStyle.Diamond).MakeSymbolReference();
        }

        private void SetAttributeValue(RowBuffer rowBuffer, string fieldName, object value)
        {
            try
            {
                if (value != null)
                {
                    rowBuffer[fieldName] = value;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to set attribute {fieldName}: {ex.Message}");
            }
        }

        private WfsFeature CreateWfsFeatureFromRow(Row row)
        {
            try
            {
                var geometry = row["Shape"] as MapPoint;
                if (geometry == null) return null;

                var feature = new WfsFeature
                {
                    Id = GetFieldValue<string>(row, "FeatureId"),
                    Geometry = new WfsGeometry
                    {
                        Type = "Point",
                        Coordinates = new[] { geometry.X, geometry.Y }
                    },
                    Properties = new WfsProperties
                    {
                        RecordingId = GetFieldValue<string>(row, "RecordingId"),
                        Guid = GetFieldValue<string>(row, "Guid"),
                        Name = GetFieldValue<string>(row, "Name"),
                        Timestamp = GetFieldValue<DateTime?>(row, "Timestamp")
                    },
                    MapPoint = geometry,
                    ImageUrl = GetFieldValue<string>(row, "ImageUrl"),
                    IsSelected = GetFieldValue<int>(row, "IsSelected") > 0
                };

                return feature;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating WFS feature from row: {ex}");
                return null;
            }
        }

        private T GetFieldValue<T>(Row row, string fieldName)
        {
            try
            {
                var value = row[fieldName];
                if (value == null || value == DBNull.Value)
                    return default(T);

                return (T)value;
            }
            catch
            {
                return default(T);
            }
        }

        #endregion
    }
}
