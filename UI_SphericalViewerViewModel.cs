using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Newtonsoft.Json;
using Test.Models;
using Test.Services;

namespace Test.UI
{
    internal class SphericalViewerViewModel : DockPane, INotifyPropertyChanged
    {
        private const string _dockPaneID = "Test_SphericalViewer_DockPane";

        #region Private Fields

        private readonly SettingsService _settingsService;
        private readonly WfsService _wfsService;
        private HttpClient _httpClient;

        // Image Management
        private ImageFrame _currentFrame;
        private List<ImageFrame> _imageFrames = new List<ImageFrame>();
        private int _currentFrameIndex = 0;
        private bool _usingHorusImages = false;

        // UI State
        private bool _isLoading = false;
        private string _statusMessage = "Ready";
        private bool _isSettingsOpen = false;
        private Window _settingsWindow;

        // Selection state - simplified
        private List<WfsFeature> _selectedFeatures = new List<WfsFeature>();

        #endregion

        #region Properties

        // === AI Detection API Properties ===

        private string _aiApiBaseUrl = "http://localhost:8000";
        public string AiApiBaseUrl
        {
            get => _aiApiBaseUrl;
            set => SetProperty(ref _aiApiBaseUrl, value);
        }

        private string _detectionText = "pole";
        public string DetectionText
        {
            get => _detectionText;
            set => SetProperty(ref _detectionText, value);
        }

        private ObservableCollection<DetectionResult> _detectionResults;
        public ObservableCollection<DetectionResult> DetectionResults
        {
            get => _detectionResults ?? (_detectionResults = new ObservableCollection<DetectionResult>());
            set => SetProperty(ref _detectionResults, value);
        }

        // === Camera Parameters ===

        private double _yaw = 0.0;
        public double Yaw
        {
            get => _yaw;
            set => SetProperty(ref _yaw, value);
        }

        private double _pitch = -20.0;
        public double Pitch
        {
            get => _pitch;
            set => SetProperty(ref _pitch, value);
        }

        private double _roll = 0.0;
        public double Roll
        {
            get => _roll;
            set => SetProperty(ref _roll, value);
        }

        private double _fov = 110.0;
        public double Fov
        {
            get => _fov;
            set => SetProperty(ref _fov, value);
        }

        private double _defaultConfidenceThreshold = 0.22;
        public double DefaultConfidenceThreshold
        {
            get => _defaultConfidenceThreshold;
            set => SetProperty(ref _defaultConfidenceThreshold, value);
        }

        private double _defaultIoUThreshold = 0.5;
        public double DefaultIoUThreshold
        {
            get => _defaultIoUThreshold;
            set => SetProperty(ref _defaultIoUThreshold, value);
        }

        // === UI State Properties ===

        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetProperty(ref _isSettingsOpen, value);
        }

        // === Image Properties ===

        private BitmapSource _currentImage;
        public BitmapSource CurrentImage
        {
            get => _currentImage;
            set
            {
                if (SetProperty(ref _currentImage, value))
                {
                    OnPropertyChanged(nameof(HasImage));
                }
            }
        }

        public bool HasImage => CurrentImage != null;

        private bool _canNavigateFrames = false;
        public bool CanNavigateFrames
        {
            get => _canNavigateFrames;
            set => SetProperty(ref _canNavigateFrames, value);
        }

        private string _frameInfo = "Frame: 0/0";
        public string FrameInfo
        {
            get => _frameInfo;
            set => SetProperty(ref _frameInfo, value);
        }

        // === Settings Properties ===

        private string _serverUrl = "http://192.168.6.100:5050";
        public string ServerUrl
        {
            get => _serverUrl;
            set => SetProperty(ref _serverUrl, value);
        }

        private string _imageDirectory = "/web/images/";
        public string ImageDirectory
        {
            get => _imageDirectory;
            set => SetProperty(ref _imageDirectory, value);
        }

        // === WFS Properties ===

        private bool _isMultiSelectMode = false;
        public bool IsMultiSelectMode
        {
            get => _isMultiSelectMode;
            set
            {
                if (SetProperty(ref _isMultiSelectMode, value))
                {
                    OnPropertyChanged(nameof(SelectionInfo));
                }
            }
        }

        private string _wfsStatus = "WFS: idle";
        public string WfsStatus
        {
            get => _wfsStatus;
            set => SetProperty(ref _wfsStatus, value);
        }

        public string SelectionInfo
        {
            get
            {
                return _selectedFeatures?.Count > 0
                    ? $"Selected: {_selectedFeatures.Count} feature(s)"
                    : "No selection";
            }
        }

        private int _wfsMaxFeatures = 100;
        public int WfsMaxFeatures
        {
            get => _wfsMaxFeatures;
            set => SetProperty(ref _wfsMaxFeatures, value);
        }

        #endregion

        #region Commands

        public ICommand LoadImagesCommand { get; private set; }
        public ICommand LoadHorusImagesCommand { get; private set; }
        public ICommand PreviousFrameCommand { get; private set; }
        public ICommand NextFrameCommand { get; private set; }
        public ICommand RunDetectionCommand { get; private set; }
        public ICommand RunDetectionOnSelectionCommand { get; private set; }
        public ICommand ClearResultsCommand { get; private set; }
        public ICommand ResetViewCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand CloseSettingsCommand { get; private set; }
        public ICommand LoadWfsPointsCommand { get; private set; }
        public ICommand ClearSelectionCommand { get; private set; }

        #endregion

        #region Constructor

        protected SphericalViewerViewModel()
        {
            // Initialize services
            _settingsService = new SettingsService();
            _wfsService = new WfsService("https://his-staging.horus.nu");

            // Initialize HTTP client for AI API
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromMinutes(5);

            InitializeCommands();
            LoadSettings();

            UpdateStatusMessage("Spherical Viewer initialized - AI Detection API ready");
        }

        #endregion

        #region Initialization

        private void InitializeCommands()
        {
            LoadImagesCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await LoadImagesAsync(), () => !IsLoading);
            LoadHorusImagesCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await LoadHorusImagesAsync(), () => !IsLoading);
            PreviousFrameCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await PreviousFrameAsync(), () => CanNavigateFrames);
            NextFrameCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await NextFrameAsync(), () => CanNavigateFrames);
            RunDetectionCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await RunDetectionAsync(), () => HasImage && !IsLoading);
            RunDetectionOnSelectionCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await RunDetectionOnSelectionAsync(), () => _selectedFeatures?.Count > 0 && !IsLoading);
            ClearResultsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ClearResults(), () => DetectionResults.Any());
            ResetViewCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ResetView());
            OpenSettingsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => OpenSettings());
            CloseSettingsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => CloseSettings());
            LoadWfsPointsCommand = new ArcGIS.Desktop.Framework.RelayCommand(async () => await LoadWfsPointsAsync(), () => !IsLoading);
            ClearSelectionCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ClearSelection(), () => _selectedFeatures?.Count > 0);
        }

        #endregion

        #region Image Management

        private async Task LoadImagesAsync()
        {
            try
            {
                IsLoading = true;
                UpdateStatusMessage("Loading image frames...");
                _usingHorusImages = false;

                // This would normally use your API service to load images
                // For now, we'll simulate loading some test images
                _imageFrames = new List<ImageFrame>
                {
                    new ImageFrame { Path = "test_image_1.jpg" },
                    new ImageFrame { Path = "test_image_2.jpg" }
                };

                _currentFrameIndex = 0;

                if (_imageFrames.Count > 0)
                {
                    await LoadCurrentFrameAsync();
                    CanNavigateFrames = _imageFrames.Count > 1;
                    UpdateFrameInfo();
                    UpdateStatusMessage($"Loaded {_imageFrames.Count} image frames");
                }
                else
                {
                    UpdateStatusMessage("No images found");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error loading images: {ex.Message}");
                Debug.WriteLine($"Load images error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadHorusImagesAsync()
        {
            try
            {
                IsLoading = true;
                UpdateStatusMessage("Loading Horus images...");
                _usingHorusImages = true;

                // This would integrate with your Horus system
                // For now, we'll simulate some Horus images
                _imageFrames = new List<ImageFrame>
                {
                    new ImageFrame { Path = "horus_image_1" },
                    new ImageFrame { Path = "horus_image_2" }
                };

                _currentFrameIndex = 0;

                if (_imageFrames.Count > 0)
                {
                    await LoadCurrentFrameAsync();
                    CanNavigateFrames = _imageFrames.Count > 1;
                    UpdateFrameInfo();
                    UpdateStatusMessage($"Loaded {_imageFrames.Count} Horus image frames");
                }
                else
                {
                    UpdateStatusMessage("No Horus images available");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error loading Horus images: {ex.Message}");
                Debug.WriteLine($"Load Horus images error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCurrentFrameAsync()
        {
            await Task.Run(() =>
            {
                try
                {
                    if (_imageFrames == null || _currentFrameIndex >= _imageFrames.Count)
                        return;

                    _currentFrame = _imageFrames[_currentFrameIndex];

                    // For now, create a placeholder image
                    // In your real implementation, you'd load the actual image data
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        CurrentImage = CreatePlaceholderImage();
                    });

                    UpdateStatusMessage($"Loaded frame: {_currentFrame.Path}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Load current frame error: {ex}");
                    UpdateStatusMessage($"Error loading frame: {ex.Message}");
                }
            });
        }

        private async Task PreviousFrameAsync()
        {
            if (_currentFrameIndex > 0)
            {
                _currentFrameIndex--;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
            }
        }

        private async Task NextFrameAsync()
        {
            if (_currentFrameIndex < _imageFrames.Count - 1)
            {
                _currentFrameIndex++;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
            }
        }

        private BitmapSource CreatePlaceholderImage()
        {
            // Create a simple placeholder image
            var bitmap = new WriteableBitmap(800, 600, 96, 96, System.Windows.Media.PixelFormats.Bgr24, null);
            return bitmap;
        }

        private void UpdateFrameInfo()
        {
            FrameInfo = $"Frame: {_currentFrameIndex + 1}/{_imageFrames.Count}";
        }

        #endregion

        #region AI Detection

        private async Task RunDetectionAsync()
        {
            if (_currentFrame == null || string.IsNullOrWhiteSpace(DetectionText))
            {
                UpdateStatusMessage("Please load an image and specify detection target");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage("Running AI object detection...");

                var request = new DetectionApiRequest
                {
                    DetectionText = DetectionText,
                    Model = "langsam",
                    Yaw = Yaw,
                    Pitch = Pitch,
                    Roll = Roll,
                    Fov = Fov,
                    ConfidenceThreshold = DefaultConfidenceThreshold,
                    IoUThreshold = DefaultIoUThreshold
                };

                if (_usingHorusImages)
                {
                    request.HorusCurrentImage = _currentFrame.Path;
                }
                else if (!string.IsNullOrWhiteSpace(_currentFrame?.Path))
                {
                    request.ImageData = await GetImageAsBase64Async(_currentFrame.Path);
                }

                var apiResults = await _detectionService.DetectAsync(request);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DetectionResults.Clear();
                    foreach (var apiResult in apiResults)
                    {
                        var bbox = apiResult.BoundingBox;
                        var result = new DetectionResult
                        {
                            ObjectName = apiResult.Label,
                            Confidence = apiResult.Confidence,
                            ModelUsed = "AI API",
                            ProcessingTime = 0.0,
                            BoundingBox = new BoundingBox
                            {
                                X = bbox?.X1 ?? 0,
                                Y = bbox?.Y1 ?? 0,
                                Width = (bbox != null) ? bbox.X2 - bbox.X1 : 0,
                                Height = (bbox != null) ? bbox.Y2 - bbox.Y1 : 0
                            }
                        };
                        DetectionResults.Add(result);
                    }
                });

                UpdateStatusMessage($"Detection completed - found {apiResults.Count} objects via AI API");
            }
            catch (HttpRequestException httpEx)
            {
                UpdateStatusMessage($"AI API call failed: {httpEx.Message}");
                Debug.WriteLine($"API Error: {httpEx}");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Detection error: {ex.Message}");
                Debug.WriteLine($"Detection error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RunDetectionOnSelectionAsync()
        {
            if (_selectedFeatures?.Count == 0)
            {
                UpdateStatusMessage("No selection available for detection");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage($"Running detection on {_selectedFeatures.Count} selected points...");

                var total = _selectedFeatures.Count;
                var processed = 0;
                var allResults = new List<DetectionResult>();

                foreach (var feature in _selectedFeatures)
                {
                    processed++;
                    UpdateStatusMessage($"Processing selection {processed}/{total}...");

                    var request = new AiDetectionRequest
                    {
                        HorusCurrentImage = $"{feature.Properties.RecordingId}/{feature.Properties.Guid}",
                        DetectionText = DetectionText,
                        Model = "langsam",
                        Yaw = Yaw,
                        Pitch = Pitch,
                        Roll = Roll,
                        Fov = Fov,
                        ConfidenceThreshold = DefaultConfidenceThreshold,
                        IoUThreshold = DefaultIoUThreshold
                    };

                    var jsonRequest = JsonConvert.SerializeObject(request);
                    var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                    var response = await _httpClient.PostAsync($"{AiApiBaseUrl}/detection/detect", content);

                    if (response.IsSuccessStatusCode)
                    {
                        var jsonResponse = await response.Content.ReadAsStringAsync();
                        var apiResults = JsonConvert.DeserializeObject<List<AiDetectionResult>>(jsonResponse);

                        foreach (var apiResult in apiResults)
                        {
                            var result = new DetectionResult
                            {
                                ObjectName = apiResult.Label,
                                Confidence = apiResult.Confidence,
                                ModelUsed = "AI API",
                                ProcessingTime = 0.0,
                                BoundingBox = new BoundingBox
                                {
                                    X = apiResult.Bbox.X1,
                                    Y = apiResult.Bbox.Y1,
                                    Width = apiResult.Bbox.X2 - apiResult.Bbox.X1,
                                    Height = apiResult.Bbox.Y2 - apiResult.Bbox.Y1
                                }
                            };
                            allResults.Add(result);
                        }

                        // Add results to the feature for map display
                        feature.DetectionResults.AddRange(allResults);
                    }
                    else
                    {
                        Debug.WriteLine($"Detection failed for feature {feature.Id}: {response.StatusCode}");
                    }
                }

                // Update UI with all results
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var result in allResults)
                    {
                        DetectionResults.Add(result);
                    }
                });

                UpdateStatusMessage($"Detection completed for {total} point(s) - found {allResults.Count} total objects");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"RunDetectionOnSelectionAsync error: {ex}");
                UpdateStatusMessage($"Detection error: {ex.Message}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ClearResults()
        {
            DetectionResults.Clear();
            UpdateStatusMessage("Detection results cleared");
        }

        #endregion

        #region Helper Methods

        private async Task<string> GetImageAsBase64Async(string imagePath)
        {
            if (string.IsNullOrEmpty(imagePath))
                return null;

            try
            {
                // For real implementation, you'd read the actual image file
                // For now, return null (API will handle Horus images differently)
                await Task.Delay(1); // Remove async warning
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error converting image to base64: {ex}");
                return null;
            }
        }

        private void ResetView()
        {
            try
            {
                Yaw = 0.0;
                Pitch = -20.0;
                Roll = 0.0;
                Fov = 110.0;
                UpdateStatusMessage("View reset to defaults");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Reset view error: {ex.Message}");
                Debug.WriteLine($"Reset view error: {ex}");
            }
        }

        private void UpdateStatusMessage(string message)
        {
            StatusMessage = $"{DateTime.Now:HH:mm:ss} - {message}";
            Debug.WriteLine($"Status: {message}");
        }

        #endregion

        #region Settings Management

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.GetSettings();
                if (settings != null)
                {
                    ServerUrl = settings.ServerUrl ?? "http://192.168.6.100:5050";
                    ImageDirectory = settings.DefaultImageDirectory ?? "/web/images/";
                    DefaultConfidenceThreshold = settings.DefaultConfidenceThreshold;
                    DefaultIoUThreshold = settings.DefaultIoUThreshold;

                    if (settings.CameraDefaults != null)
                    {
                        Yaw = settings.CameraDefaults.Yaw;
                        Pitch = settings.CameraDefaults.Pitch;
                        Roll = settings.CameraDefaults.Roll;
                        Fov = settings.CameraDefaults.Fov;
                    }

                    Debug.WriteLine("Settings loaded successfully");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Settings load error: {ex.Message}");
                Debug.WriteLine($"Load settings error: {ex}");
            }
        }

        private void OpenSettings()
        {
            try
            {
                // You would create a settings window here
                // For now, just show a message
                UpdateStatusMessage("Settings window would open here");
                IsSettingsOpen = true;
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error opening settings: {ex.Message}");
                Debug.WriteLine($"Open settings error: {ex}");
            }
        }

        private void CloseSettings()
        {
            try
            {
                _settingsWindow?.Close();
                IsSettingsOpen = false;
                UpdateStatusMessage("Settings window closed");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error closing settings: {ex.Message}");
                Debug.WriteLine($"Close settings error: {ex}");
            }
        }

        #endregion

        #region WFS Management

        private async Task LoadWfsPointsAsync()
        {
            try
            {
                IsLoading = true;
                WfsStatus = "WFS: querying...";
                UpdateStatusMessage("Loading WFS points for current view...");

                // Create a simple bounding box for testing
                var boundingBox = new GeoBoundingBox
                {
                    MinX = -180,
                    MinY = -90,
                    MaxX = 180,
                    MaxY = 90
                };

                var response = await _wfsService.QueryFeaturesAsync(boundingBox, WfsMaxFeatures);

                if (response.Success && response.Data?.Features?.Any() == true)
                {
                    _selectedFeatures = response.Data.Features;

                    UpdateStatusMessage($"Loaded {response.Data.Features.Count} WFS points");
                    WfsStatus = $"WFS: {response.Data.Features.Count} points";
                    OnPropertyChanged(nameof(SelectionInfo));
                }
                else
                {
                    UpdateStatusMessage("No WFS points found in current view");
                    WfsStatus = "WFS: no points";
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"WFS query error: {ex.Message}");
                WfsStatus = "WFS: error";
                Debug.WriteLine($"WFS query error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ClearSelection()
        {
            _selectedFeatures?.Clear();
            OnPropertyChanged(nameof(SelectionInfo));
            UpdateStatusMessage("Selection cleared");
        }

        #endregion

        #region Cleanup

        protected override void OnHidden()
        {
            try
            {
                _httpClient?.Dispose();
                _wfsService?.Dispose();
                CloseSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cleanup error: {ex}");
            }
            finally
            {
                base.OnHidden();
            }
        }

        #endregion

        #region INotifyPropertyChanged

        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        #endregion

        #region Static

        public static SphericalViewerViewModel Create()
        {
            return new SphericalViewerViewModel();
        }

        protected override Task InitializeAsync()
        {
            return base.InitializeAsync();
        }

        #endregion
    }

    #region AI API Data Models

    public class AiDetectionRequest
    {
        [JsonProperty("image_data")]
        public string ImageData { get; set; }

        [JsonProperty("horus_current_image")]
        public string HorusCurrentImage { get; set; }

        [JsonProperty("detection_text")]
        public string DetectionText { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("yaw")]
        public double Yaw { get; set; }

        [JsonProperty("pitch")]
        public double Pitch { get; set; }

        [JsonProperty("roll")]
        public double Roll { get; set; }

        [JsonProperty("fov")]
        public double Fov { get; set; }

        [JsonProperty("confidence_threshold")]
        public double ConfidenceThreshold { get; set; }

        [JsonProperty("iou_threshold")]
        public double IoUThreshold { get; set; }
    }

    public class AiDetectionResult
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("label")]
        public string Label { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("bbox")]
        public AiBoundingBox Bbox { get; set; }

        [JsonProperty("ground_point")]
        public AiGroundPoint GroundPoint { get; set; }

        [JsonProperty("geographic_location")]
        public AiGeographicLocation GeographicLocation { get; set; }
    }

    public class AiBoundingBox
    {
        [JsonProperty("x1")]
        public double X1 { get; set; }

        [JsonProperty("y1")]
        public double Y1 { get; set; }

        [JsonProperty("x2")]
        public double X2 { get; set; }

        [JsonProperty("y2")]
        public double Y2 { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }
    }

    public class AiGroundPoint
    {
        [JsonProperty("x")]
        public double X { get; set; }

        [JsonProperty("y")]
        public double Y { get; set; }
    }

    public class AiGeographicLocation
    {
        [JsonProperty("lat")]
        public double Lat { get; set; }

        [JsonProperty("lon")]
        public double Lon { get; set; }

        [JsonProperty("alt")]
        public double Alt { get; set; }
    }

    #endregion
}