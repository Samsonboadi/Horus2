using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Test.Models;

namespace Test.Services
{
    public class HorusMediaService : IDisposable
    {
        private readonly HttpClient _httpClient;
        private string _bridgeUrl = "http://localhost:5001";
        private bool _isConnected = false;
        private bool _disposed = false;

        public HorusMediaService()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "SphericalImageViewer-HorusBridge/1.0");
        }

        public bool IsConnected => _isConnected && !_disposed;

        public void UpdateBridgeUrl(string bridgeUrl)
        {
            if (!_disposed)
            {
                _bridgeUrl = bridgeUrl.TrimEnd('/');
            }
        }

        public async Task<ApiResponse<bool>> ConnectAsync(HorusConnectionConfig config)
        {
            if (_disposed)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var connectionRequest = new
                {
                    horus = new
                    {
                        host = config.HorusHost,
                        port = config.HorusPort,
                        username = config.HorusUsername,
                        password = config.HorusPassword,
                        url = $"http://{config.HorusHost}:{config.HorusPort}/web/"
                    },
                    database = new
                    {
                        host = config.DatabaseHost,
                        port = config.DatabasePort,
                        database = config.DatabaseName,
                        user = config.DatabaseUser,
                        password = config.DatabasePassword
                    }
                };

                var jsonContent = JsonConvert.SerializeObject(connectionRequest);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_bridgeUrl}/connect", httpContent);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<dynamic>(content);
                    _isConnected = result.success == true;

                    return new ApiResponse<bool>
                    {
                        Success = true,
                        Data = _isConnected,
                        Message = result.message?.ToString()
                    };
                }
                else
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {content}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                _isConnected = false;
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = $"Connection failed: {ex.Message}"
                };
            }
        }

        public async Task<ApiResponse<List<HorusRecording>>> GetRecordingsAsync()
        {
            if (_disposed)
            {
                return new ApiResponse<List<HorusRecording>>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var response = await _httpClient.GetAsync($"{_bridgeUrl}/recordings");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<HorusRecording>>>(content);
                    return result;
                }
                else
                {
                    return new ApiResponse<List<HorusRecording>>
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {content}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<List<HorusRecording>>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<HorusRecording>>
                {
                    Success = false,
                    Error = $"Failed to get recordings: {ex.Message}"
                };
            }
        }

        public async Task<ApiResponse<List<HorusImage>>> GetImagesAsync(HorusImageRequest request)
        {
            if (_disposed)
            {
                return new ApiResponse<List<HorusImage>>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var requestData = new
                {
                    recording_endpoint = request.RecordingEndpoint,
                    count = request.Count,
                    width = request.Width,
                    height = request.Height
                };

                var jsonContent = JsonConvert.SerializeObject(requestData);
                var httpContent = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_bridgeUrl}/images", httpContent);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ApiResponse<List<HorusImage>>>(content);
                    return result;
                }
                else
                {
                    return new ApiResponse<List<HorusImage>>
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {content}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<List<HorusImage>>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<List<HorusImage>>
                {
                    Success = false,
                    Error = $"Failed to get images: {ex.Message}"
                };
            }
        }

        public async Task<ApiResponse<HorusImage>> GetImageByTimestampAsync(string recordingEndpoint, string timestamp, int width = 600, int height = 600)
        {
            if (_disposed)
            {
                return new ApiResponse<HorusImage>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var url = $"{_bridgeUrl}/image/{Uri.EscapeDataString(recordingEndpoint)}/{Uri.EscapeDataString(timestamp)}?width={width}&height={height}";

                var response = await _httpClient.GetAsync(url);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<ApiResponse<HorusImage>>(content);
                    return result;
                }
                else
                {
                    return new ApiResponse<HorusImage>
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {content}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<HorusImage>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<HorusImage>
                {
                    Success = false,
                    Error = $"Failed to get image: {ex.Message}"
                };
            }
        }

        public async Task<ApiResponse<bool>> CheckHealthAsync()
        {
            if (_disposed)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var response = await _httpClient.GetAsync($"{_bridgeUrl}/health");
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonConvert.DeserializeObject<dynamic>(content);
                    _isConnected = result.horus_connected == true;

                    return new ApiResponse<bool>
                    {
                        Success = true,
                        Data = _isConnected,
                        Message = result.status?.ToString()
                    };
                }
                else
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Error = $"Health check failed: HTTP {response.StatusCode}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = $"Health check failed: {ex.Message}"
                };
            }
        }

        public async Task<ApiResponse<bool>> DisconnectAsync()
        {
            if (_disposed)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "Service has been disposed"
                };
            }

            try
            {
                var response = await _httpClient.PostAsync($"{_bridgeUrl}/disconnect", null);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    _isConnected = false;
                    return new ApiResponse<bool>
                    {
                        Success = true,
                        Data = true,
                        Message = "Disconnected successfully"
                    };
                }
                else
                {
                    return new ApiResponse<bool>
                    {
                        Success = false,
                        Error = $"HTTP {response.StatusCode}: {content}"
                    };
                }
            }
            catch (ObjectDisposedException)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = "HTTP client has been disposed"
                };
            }
            catch (Exception ex)
            {
                return new ApiResponse<bool>
                {
                    Success = false,
                    Error = $"Disconnect failed: {ex.Message}"
                };
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
                        System.Diagnostics.Debug.WriteLine($"Error disposing HttpClient in PythonApiService: {ex.Message}");
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