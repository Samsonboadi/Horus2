// Services/WfsService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json;
using Test.Models;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;

namespace Test.Services
{
    public class WfsService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private bool _disposed = false;

        public WfsService(string baseUrl = "https://his-staging.horus.nu")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ArcGIS-SphericalImageViewer/1.0");

            // Add headers similar to the curl example
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");
        }

        /// <summary>
        /// Query WFS features within a bounding box
        /// </summary>
        public async Task<ApiResponse<WfsResponse>> QueryFeaturesAsync(BoundingBox boundingBox, int? maxFeatures = null)
        {
            try
            {
                var queryParams = new WfsQueryParameters
                {
                    BoundingBox = boundingBox,
                    MaxFeatures = maxFeatures
                };

                return await QueryFeaturesAsync(queryParams);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WFS query by bounding box failed: {ex}");
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = $"WFS query failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Query WFS features with custom parameters
        /// </summary>
        public async Task<ApiResponse<WfsResponse>> QueryFeaturesAsync(WfsQueryParameters parameters)
        {
            try
            {
                Debug.WriteLine($"Querying WFS with parameters: {parameters.BoundingBox}");

                var queryUrl = parameters.BuildQueryUrl(_baseUrl);
                Debug.WriteLine($"WFS Query URL: {queryUrl}");

                var response = await _httpClient.GetAsync(queryUrl);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var wfsResponse = JsonConvert.DeserializeObject<WfsResponse>(content);

                        // Post-process features to add computed properties
                        ProcessFeatures(wfsResponse.Features);

                        Debug.WriteLine($"WFS query successful: {wfsResponse.Features?.Count ?? 0} features returned");

                        return new ApiResponse<WfsResponse>
                        {
                            Success = true,
                            Data = wfsResponse,
                            Message = $"Retrieved {wfsResponse.Features?.Count ?? 0} features"
                        };
                    }
                    catch (JsonException jsonEx)
                    {
                        Debug.WriteLine($"WFS JSON parsing failed: {jsonEx}");
                        Debug.WriteLine($"Response content: {content.Substring(0, Math.Min(500, content.Length))}...");

                        return new ApiResponse<WfsResponse>
                        {
                            Success = false,
                            Error = $"Failed to parse WFS response: {jsonEx.Message}",
                            StatusCode = (int)response.StatusCode
                        };
                    }
                }
                else
                {
                    Debug.WriteLine($"WFS query failed with status {response.StatusCode}: {content}");
                    return new ApiResponse<WfsResponse>
                    {
                        Success = false,
                        Error = $"WFS server returned {response.StatusCode}: {response.ReasonPhrase}",
                        StatusCode = (int)response.StatusCode
                    };
                }
            }
            catch (HttpRequestException httpEx)
            {
                Debug.WriteLine($"WFS HTTP request failed: {httpEx}");
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = $"Network error: {httpEx.Message}"
                };
            }
            catch (TaskCanceledException tcEx)
            {
                Debug.WriteLine($"WFS request timeout: {tcEx}");
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = "Request timeout - WFS server may be slow or unavailable"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WFS query failed: {ex}");
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = $"Unexpected error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Query features around a specific point with a radius
        /// </summary>
        public async Task<ApiResponse<WfsResponse>> QueryFeaturesAroundPointAsync(double longitude, double latitude, double radiusMeters, int? maxFeatures = null)
        {
            try
            {
                // Convert radius from meters to approximate degrees (rough approximation)
                double radiusDegrees = radiusMeters / 111000.0; // 1 degree ≈ 111km

                var boundingBox = new BoundingBox(
                    longitude - radiusDegrees,
                    latitude - radiusDegrees,
                    longitude + radiusDegrees,
                    latitude + radiusDegrees
                );

                return await QueryFeaturesAsync(boundingBox, maxFeatures);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WFS point query failed: {ex}");
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = $"Point query failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Query features within the current map extent
        /// </summary>
        public async Task<ApiResponse<WfsResponse>> QueryFeaturesInMapExtentAsync(Envelope mapExtent, int? maxFeatures = 100)
        {
            if (mapExtent == null)
            {
                return new ApiResponse<WfsResponse>
                {
                    Success = false,
                    Error = "Map extent is null"
                };
            }

            var boundingBox = new BoundingBox(mapExtent);
            return await QueryFeaturesAsync(boundingBox, maxFeatures);
        }

        /// <summary>
        /// Test WFS server connectivity
        /// </summary>
        public async Task<ApiResponse<bool>> TestConnectionAsync()
        {
            try
            {
                var testUrl = $"{_baseUrl}/geoserver/wfs?service=WFS&request=GetCapabilities&version=2.0.0";
                Debug.WriteLine($"Testing WFS connection: {testUrl}");

                var response = await _httpClient.GetAsync(testUrl);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine("WFS connection test successful");
                    return new ApiResponse<bool>
                    {
                        Success = true,
                        Data = true,
                        Message = "WFS server is accessible"
                    };
                }
                else
                {
                    Debug.WriteLine($"WFS connection test failed: {response.StatusCode}");
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Data = false,
                        Error = $"WFS server returned {response.StatusCode}: {response.ReasonPhrase}"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WFS connection test failed: {ex}");
                return new ApiResponse<bool>
                {
                    Success = false,
                    Data = false,
                    Error = $"Connection failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get WFS server capabilities
        /// </summary>
        public async Task<ApiResponse<string>> GetCapabilitiesAsync()
        {
            try
            {
                var capabilitiesUrl = $"{_baseUrl}/geoserver/wfs?service=WFS&request=GetCapabilities&version=2.0.0";

                var response = await _httpClient.GetAsync(capabilitiesUrl);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    return new ApiResponse<string>
                    {
                        Success = true,
                        Data = content,
                        Message = "Capabilities retrieved successfully"
                    };
                }
                else
                {
                    return new ApiResponse<string>
                    {
                        Success = false,
                        Error = $"Failed to get capabilities: {response.StatusCode}"
                    };
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Get capabilities failed: {ex}");
                return new ApiResponse<string>
                {
                    Success = false,
                    Error = $"Failed to get capabilities: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Process features to add computed properties and validate data
        /// </summary>
        private void ProcessFeatures(List<WfsFeature> features)
        {
            if (features == null) return;

            foreach (var feature in features)
            {
                try
                {
                    // Create ArcGIS MapPoint from coordinates
                    if (feature.Geometry?.Coordinates?.Length >= 2)
                    {
                        feature.MapPoint = MapPointBuilderEx.CreateMapPoint(
                            feature.Geometry.Longitude,
                            feature.Geometry.Latitude,
                            SpatialReferenceBuilder.CreateSpatialReference(4326)
                        );
                    }

                    // Build image URL
                    if (feature.Properties != null &&
                        !string.IsNullOrEmpty(feature.Properties.RecordingId) &&
                        !string.IsNullOrEmpty(feature.Properties.Guid))
                    {
                        var imageRequest = new HorusImageRequest
                        {
                            RecordingId = feature.Properties.RecordingId,
                            Guid = feature.Properties.Guid,
                            Scale = feature.Properties.Scale,
                            Section = feature.Properties.Section
                        };
                        feature.ImageUrl = imageRequest.BuildImageUrl(_baseUrl);
                    }

                    Debug.WriteLine($"Processed feature {feature.Id}: {feature.Properties?.DisplayName} at ({feature.Geometry?.Longitude}, {feature.Geometry?.Latitude})");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to process feature {feature.Id}: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Build a sample query for testing
        /// </summary>
        public WfsQueryParameters CreateSampleQuery()
        {
            // Example bounding box around Groningen, Netherlands (from the curl example)
            return new WfsQueryParameters
            {
                BoundingBox = new BoundingBox(6.546728827431761, 53.18827977814843, 6.550322088568239, 53.19043276082607),
                MaxFeatures = 50
            };
        }

        /// <summary>
        /// Create query for current ArcGIS map view
        /// </summary>
        public async Task<WfsQueryParameters> CreateQueryForCurrentViewAsync()
        {
            try
            {
                return await QueuedTask.Run(() =>
                {
                    var mapView = MapView.Active;
                    if (mapView?.Extent != null)
                    {
                        var extent = mapView.Extent;

                        // Convert to WGS84 if needed
                        if (extent.SpatialReference.Wkid != 4326)
                        {
                            var wgs84SR = SpatialReferenceBuilder.CreateSpatialReference(4326);
                            extent = GeometryEngine.Instance.Project(extent, wgs84SR) as Envelope;
                        }

                        return new WfsQueryParameters
                        {
                            BoundingBox = new BoundingBox(extent),
                            MaxFeatures = 100
                        };
                    }
                    return null;
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create query for current view: {ex}");
                return CreateSampleQuery(); // Fallback to sample query
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _httpClient?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing HttpClient: {ex.Message}");
                    }
                }
                _disposed = true;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}