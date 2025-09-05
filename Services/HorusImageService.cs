// Services/HorusImageService.cs
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using System.Linq;
using Test.Models;

namespace Test.Services
{
    public class HorusImageService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly Dictionary<string, BitmapSource> _imageCache;
        private bool _disposed = false;

        public HorusImageService(string baseUrl = "https://his-staging.horus.nu")
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(60);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "ArcGIS-SphericalImageViewer/1.0");

            // Add headers similar to the curl example
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
            _httpClient.DefaultRequestHeaders.Add("Accept-Language", "en-US,en;q=0.9");
            _httpClient.DefaultRequestHeaders.Add("DNT", "1");

            _imageCache = new Dictionary<string, BitmapSource>();
        }

        /// <summary>
        /// Get spherical image for a WFS feature
        /// </summary>
        public async Task<ApiResponse<BitmapSource>> GetFeatureImageAsync(WfsFeature feature, double? yaw = null, double? pitch = null, double? roll = null, double? fov = null)
        {
            if (feature?.Properties == null)
            {
                return new ApiResponse<BitmapSource>
                {
                    Success = false,
                    Error = "Invalid feature data"
                };
            }

            var imageRequest = new HorusImageRequest
            {
                RecordingId = feature.Properties.RecordingId,
                Guid = feature.Properties.Guid,
                Scale = feature.Properties.Scale,
                Section = feature.Properties.Section,
                Yaw = yaw,
                Pitch = pitch,
                Roll = roll,
                Fov = fov,
                Mode = "spherical"
            };

            return await GetImageAsync(imageRequest);
        }

        /// <summary>
        /// Get image with specific camera parameters
        /// </summary>
        public async Task<ApiResponse<BitmapSource>> GetImageAsync(HorusImageRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.RecordingId) || string.IsNullOrEmpty(request.Guid))
                {
                    return new ApiResponse<BitmapSource>
                    {
                        Success = false,
                        Error = "Missing recording ID or GUID"
                    };
                }

                var imageUrl = request.BuildImageUrl(_baseUrl);
                var cacheKey = GenerateCacheKey(request);

                // Check cache first
                if (_imageCache.TryGetValue(cacheKey, out var cachedImage))
                {
                    Debug.WriteLine($"Returning cached image for {request.RecordingId}/{request.Guid}");
                    return new ApiResponse<BitmapSource>
                    {
                        Success = true,
                        Data = cachedImage,
                        Message = "Image retrieved from cache"
                    };
                }

                Debug.WriteLine($"Fetching image from: {imageUrl}");

                var response = await _httpClient.GetAsync(imageUrl);

                if (response.IsSuccessStatusCode)
                {
                    var imageBytes = await response.Content.ReadAsByteArrayAsync();
                    var bitmapImage = CreateBitmapFromBytes(imageBytes);

                    if (bitmapImage != null)
                    {
                        // Cache the image (limit cache size)
                        if (_imageCache.Count > 50) // Limit cache size
                        {
                            var oldestKey = GetOldestCacheKey();
                            if (oldestKey != null)
                            {
                                _imageCache.Remove(oldestKey);
                            }
                        }

                        _imageCache[cacheKey] = bitmapImage;

                        Debug.WriteLine($"Successfully retrieved image for {request.RecordingId}/{request.Guid}");
                        return new ApiResponse<BitmapSource>
                        {
                            Success = true,
                            Data = bitmapImage,
                            Message = "Image retrieved successfully"
                        };
                    }
                    else
                    {
                        return new ApiResponse<BitmapSource>
                        {
                            Success = false,
                            Error = "Failed to create bitmap from response data"
                        };
                    }
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    Debug.WriteLine($"Image request failed with status {response.StatusCode}: {errorContent}");

                    return new ApiResponse<BitmapSource>
                    {
                        Success = false,
                        Error = $"Server returned {response.StatusCode}: {response.ReasonPhrase}",
                        StatusCode = (int)response.StatusCode
                    };
                }
            }
            catch (HttpRequestException httpEx)
            {
                Debug.WriteLine($"HTTP error getting image: {httpEx}");
                return new ApiResponse<BitmapSource>
                {
                    Success = false,
                    Error = $"Network error: {httpEx.Message}"
                };
            }
            catch (TaskCanceledException tcEx)
            {
                Debug.WriteLine($"Image request timeout: {tcEx}");
                return new ApiResponse<BitmapSource>
                {
                    Success = false,
                    Error = "Request timeout - image server may be slow"
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error getting image: {ex}");
                return new ApiResponse<BitmapSource>
                {
                    Success = false,
                    Error = $"Unexpected error: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Get multiple images for batch processing
        /// </summary>
        public async Task<List<ApiResponse<BitmapSource>>> GetMultipleImagesAsync(List<WfsFeature> features, double? yaw = null, double? pitch = null, double? roll = null, double? fov = null)
        {
            var results = new List<ApiResponse<BitmapSource>>();

            foreach (var feature in features)
            {
                try
                {
                    var imageResponse = await GetFeatureImageAsync(feature, yaw, pitch, roll, fov);
                    results.Add(imageResponse);

                    // Small delay to avoid overwhelming the server
                    await Task.Delay(100);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error getting image for feature {feature.Id}: {ex}");
                    results.Add(new ApiResponse<BitmapSource>
                    {
                        Success = false,
                        Error = $"Failed to get image: {ex.Message}"
                    });
                }
            }

            Debug.WriteLine($"Retrieved {results.Count} images, {results.Count(r => r.Success)} successful");
            return results;
        }

        /// <summary>
        /// Test if the Horus image server is accessible
        /// </summary>
        public async Task<ApiResponse<bool>> TestImageServerAsync()
        {
            try
            {
                // Try to access the base images endpoint
                var testUrl = $"{_baseUrl}/images/";
                var response = await _httpClient.GetAsync(testUrl);

                return new ApiResponse<bool>
                {
                    Success = response.IsSuccessStatusCode,
                    Data = response.IsSuccessStatusCode,
                    Message = response.IsSuccessStatusCode ?
                        "Image server is accessible" :
                        $"Image server returned {response.StatusCode}",
                    StatusCode = (int)response.StatusCode
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Image server test failed: {ex}");
                return new ApiResponse<bool>
                {
                    Success = false,
                    Data = false,
                    Error = $"Connection failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Clear the image cache
        /// </summary>
        public void ClearCache()
        {
            _imageCache.Clear();
            Debug.WriteLine("Image cache cleared");
        }

        /// <summary>
        /// Get cache statistics
        /// </summary>
        public (int Count, long EstimatedSizeBytes) GetCacheStats()
        {
            var count = _imageCache.Count;
            var estimatedSize = count * 1024 * 1024; // Rough estimate: 1MB per image
            return (count, estimatedSize);
        }

        #region Private Helper Methods

        private string GenerateCacheKey(HorusImageRequest request)
        {
            return $"{request.RecordingId}_{request.Guid}_{request.Scale}_{request.Section}_{request.Yaw}_{request.Pitch}_{request.Roll}_{request.Fov}_{request.Mode}";
        }

        private string GetOldestCacheKey()
        {
            // Simple implementation - return first key
            // In a real implementation, you might track access times
            return _imageCache.Keys.FirstOrDefault();
        }

        private BitmapSource CreateBitmapFromBytes(byte[] imageBytes)
        {
            try
            {
                using (var stream = new MemoryStream(imageBytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create bitmap from bytes: {ex}");
                return null;
            }
        }

        #endregion

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    try
                    {
                        _httpClient?.Dispose();
                        _imageCache?.Clear();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing HorusImageService: {ex.Message}");
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