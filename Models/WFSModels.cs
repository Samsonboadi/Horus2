// Models/WfsModels.cs
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using ArcGIS.Core.Geometry;

namespace Test.Models
{
    /// <summary>
    /// Represents a WFS feature from the Horus geoserver
    /// </summary>
    public class WfsFeature
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("geometry")]
        public WfsGeometry Geometry { get; set; }

        [JsonProperty("properties")]
        public WfsProperties Properties { get; set; }

        // Additional properties for ArcGIS integration
        public bool IsSelected { get; set; }
        public MapPoint MapPoint { get; set; }
        public string ImageUrl { get; set; }
        public DateTime? LastImageUpdate { get; set; }
        public List<DetectionResult> DetectionResults { get; set; } = new List<DetectionResult>();
    }

    /// <summary>
    /// WFS geometry structure
    /// </summary>
    public class WfsGeometry
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("coordinates")]
        public double[] Coordinates { get; set; }

        // Helper properties
        public double Longitude => Coordinates?.Length > 0 ? Coordinates[0] : 0;
        public double Latitude => Coordinates?.Length > 1 ? Coordinates[1] : 0;
    }

    /// <summary>
    /// WFS feature properties containing recording and image information
    /// </summary>
    public class WfsProperties
    {
        [JsonProperty("recording_id")]
        public string RecordingId { get; set; }

        [JsonProperty("guid")]
        public string Guid { get; set; }

        [JsonProperty("timestamp")]
        public DateTime? Timestamp { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        // Additional metadata
        [JsonProperty("section")]
        public int Section { get; set; } = 0;

        [JsonProperty("scale")]
        public int Scale { get; set; } = 2;

        // Computed properties
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : $"Point {Guid?.Substring(0, 8)}";
        public string ImageBaseUrl => $"https://his-staging.horus.nu/images/{RecordingId}/{Guid}";
    }

    /// <summary>
    /// WFS response containing collection of features
    /// </summary>
    public class WfsResponse
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("features")]
        public List<WfsFeature> Features { get; set; } = new List<WfsFeature>();

        [JsonProperty("totalFeatures")]
        public int? TotalFeatures { get; set; }

        [JsonProperty("numberReturned")]
        public int? NumberReturned { get; set; }

        [JsonProperty("crs")]
        public WfsCrs Crs { get; set; }
    }

    /// <summary>
    /// Coordinate reference system information
    /// </summary>
    public class WfsCrs
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("properties")]
        public Dictionary<string, object> Properties { get; set; }
    }

    /// <summary>
    /// WFS query parameters for bounding box queries
    /// </summary>
    public class WfsQueryParameters
    {
        public string Service { get; set; } = "WFS";
        public string Request { get; set; } = "GetFeature";
        public string Version { get; set; } = "2.0.0";
        public string TypeName { get; set; } = "main_layer";
        public string OutputFormat { get; set; } = "application/json; subtype=geojson";
        public string SrsName { get; set; } = "EPSG:4326";
        public BoundingBox BoundingBox { get; set; }
        public int? MaxFeatures { get; set; }
        public string Filter { get; set; }

        /// <summary>
        /// Generate WFS query URL
        /// </summary>
        public string BuildQueryUrl(string baseUrl)
        {
            var url = $"{baseUrl.TrimEnd('/')}/geoserver/wfs";
            var parameters = new List<string>
            {
                $"service={Service}",
                $"request={Request}",
                $"version={Version}",
                $"typename={TypeName}",
                $"outputFormat={Uri.EscapeDataString(OutputFormat)}",
                $"srsname={SrsName}"
            };

            if (BoundingBox != null)
            {
                var bboxFilter = BuildBBoxFilter();
                parameters.Add($"filter={Uri.EscapeDataString(bboxFilter)}");
            }

            if (MaxFeatures.HasValue)
            {
                parameters.Add($"maxFeatures={MaxFeatures.Value}");
            }

            if (!string.IsNullOrEmpty(Filter))
            {
                parameters.Add($"filter={Uri.EscapeDataString(Filter)}");
            }

            return $"{url}?{string.Join("&", parameters)}";
        }

        /// <summary>
        /// Build BBOX filter for WFS query
        /// </summary>
        private string BuildBBoxFilter()
        {
            if (BoundingBox == null) return string.Empty;

            return $@"<Filter>
                <BBOX>
                    <PropertyName>geom</PropertyName>
                    <Box srsName=""{SrsName}"">
                        <coordinates>{BoundingBox.MinX},{BoundingBox.MinY} {BoundingBox.MaxX},{BoundingBox.MaxY}</coordinates>
                    </Box>
                </BBOX>
            </Filter>";
        }
    }

    /// <summary>
    /// Bounding box for spatial queries
    /// </summary>
    public class BoundingBox
    {
        public double MinX { get; set; }
        public double MinY { get; set; }
        public double MaxX { get; set; }
        public double MaxY { get; set; }

        public BoundingBox() { }

        public BoundingBox(double minX, double minY, double maxX, double maxY)
        {
            MinX = minX;
            MinY = minY;
            MaxX = maxX;
            MaxY = maxY;
        }

        public BoundingBox(Envelope envelope)
        {
            MinX = envelope.XMin;
            MinY = envelope.YMin;
            MaxX = envelope.XMax;
            MaxY = envelope.YMax;
        }

        public bool IsValid => MinX < MaxX && MinY < MaxY;

        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;

        public override string ToString()
        {
            return $"BBOX({MinX}, {MinY}, {MaxX}, {MaxY})";
        }
    }

    /// <summary>
    /// Image request parameters for Horus image server
    /// </summary>
    public class HorusImageRequest
    {
        public string RecordingId { get; set; }
        public string Guid { get; set; }
        public int Scale { get; set; } = 2;
        public int Section { get; set; } = 0;
        public double? Yaw { get; set; }
        public double? Pitch { get; set; }
        public double? Roll { get; set; }
        public double? Fov { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Mode { get; set; } = "spherical";

        /// <summary>
        /// Build image URL for Horus server
        /// </summary>
        public string BuildImageUrl(string baseUrl = "https://his-staging.horus.nu")
        {
            if (string.IsNullOrEmpty(RecordingId) || string.IsNullOrEmpty(Guid))
                return null;

            var url = $"{baseUrl.TrimEnd('/')}/images/{RecordingId}/{Guid}";
            var parameters = new List<string>();

            if (Scale > 0) parameters.Add($"scale={Scale}");
            if (Section >= 0) parameters.Add($"section={Section}");
            if (Yaw.HasValue) parameters.Add($"yaw={Yaw.Value}");
            if (Pitch.HasValue) parameters.Add($"pitch={Pitch.Value}");
            if (Roll.HasValue) parameters.Add($"roll={Roll.Value}");
            if (Fov.HasValue) parameters.Add($"fov={Fov.Value}");
            if (Width.HasValue) parameters.Add($"width={Width.Value}");
            if (Height.HasValue) parameters.Add($"height={Height.Value}");
            if (!string.IsNullOrEmpty(Mode)) parameters.Add($"mode={Mode}");

            return parameters.Count > 0 ? $"{url}?{string.Join("&", parameters)}" : url;
        }
    }

    /// <summary>
    /// Point selection state for map interaction
    /// </summary>
    public class PointSelectionState
    {
        public List<WfsFeature> SelectedFeatures { get; set; } = new List<WfsFeature>();
        public WfsFeature CurrentFeature { get; set; }
        public int CurrentIndex { get; set; } = 0;
        public bool IsMultiSelectMode { get; set; }
        public DateTime LastSelectionTime { get; set; }

        public bool HasSelection => SelectedFeatures.Count > 0;
        public bool HasMultipleSelections => SelectedFeatures.Count > 1;
        public int SelectionCount => SelectedFeatures.Count;

        public void AddFeature(WfsFeature feature)
        {
            if (feature == null) return;

            if (!IsMultiSelectMode)
            {
                ClearSelection();
            }

            if (!SelectedFeatures.Contains(feature))
            {
                feature.IsSelected = true;
                SelectedFeatures.Add(feature);
                CurrentFeature = feature;
                CurrentIndex = SelectedFeatures.Count - 1;
                LastSelectionTime = DateTime.Now;
            }
        }

        public void RemoveFeature(WfsFeature feature)
        {
            if (feature == null) return;

            feature.IsSelected = false;
            SelectedFeatures.Remove(feature);

            if (CurrentFeature == feature)
            {
                if (SelectedFeatures.Count > 0)
                {
                    CurrentIndex = Math.Min(CurrentIndex, SelectedFeatures.Count - 1);
                    CurrentFeature = SelectedFeatures[CurrentIndex];
                }
                else
                {
                    CurrentFeature = null;
                    CurrentIndex = 0;
                }
            }
        }

        public void ClearSelection()
        {
            foreach (var feature in SelectedFeatures)
            {
                feature.IsSelected = false;
            }
            SelectedFeatures.Clear();
            CurrentFeature = null;
            CurrentIndex = 0;
        }

        public WfsFeature GetNext()
        {
            if (!HasSelection) return null;

            CurrentIndex = (CurrentIndex + 1) % SelectedFeatures.Count;
            CurrentFeature = SelectedFeatures[CurrentIndex];
            return CurrentFeature;
        }

        public WfsFeature GetPrevious()
        {
            if (!HasSelection) return null;

            CurrentIndex = CurrentIndex > 0 ? CurrentIndex - 1 : SelectedFeatures.Count - 1;
            CurrentFeature = SelectedFeatures[CurrentIndex];
            return CurrentFeature;
        }

        public WfsFeature GetFirst()
        {
            if (!HasSelection) return null;

            CurrentIndex = 0;
            CurrentFeature = SelectedFeatures[CurrentIndex];
            return CurrentFeature;
        }

        public WfsFeature GetLast()
        {
            if (!HasSelection) return null;

            CurrentIndex = SelectedFeatures.Count - 1;
            CurrentFeature = SelectedFeatures[CurrentIndex];
            return CurrentFeature;
        }
    }
}