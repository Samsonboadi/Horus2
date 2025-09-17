using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ArcGIS.Core.Geometry;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using Newtonsoft.Json;
using Test.Models;
using Test.Services;

namespace Test.UI
{
    internal class SphericalViewerViewModel : DockPane
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
        private Window _settingsWindow; // may be set by your settings UI

        // Selection state - simplified
        private List<WfsFeature> _selectedFeatures = new List<WfsFeature>();

        #endregion

        #region Sections / Navigation

        public enum ViewerSection
        {
            Images,
            HorusImages,
            Detection,
            Settings,
            Wfs
        }

        private ViewerSection _currentSection = ViewerSection.Images;
        public ViewerSection CurrentSection
        {
            get => _currentSection;
            set => SetProperty(ref _currentSection, value);
        }

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

        /// <summary>
        /// Added because SettingsView.xaml.cs references it.
        /// Keep as plain string or switch to SecureString as you prefer.
        /// </summary>
        private string _databasePassword = string.Empty;
        public string DatabasePassword
        {
            get => _databasePassword;
            set => SetProperty(ref _databasePassword, value);
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
            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(5)
            };

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
            ClearResultsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ClearResults(), () => (DetectionResults?.Any() ?? false));
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

                // Simulated test images
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
                    CurrentSection = ViewerSection.Images;
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

                // Simulated Horus images
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
                    CurrentSection = ViewerSection.HorusImages;
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

                    // Placeholder image (replace with actual loading in your impl)
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

                var request = new AiDetectionRequest
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
                else
                {
                    request.ImageData = await GetImageAsBase64Async(_currentFrame.Path);
                }

                var jsonRequest = JsonConvert.SerializeObject(request);
                var content = new StringContent(jsonRequest, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{AiApiBaseUrl}/detection/detect", content);

                if (response.IsSuccessStatusCode)
                {
                    var jsonResponse = await response.Content.ReadAsStringAsync();
                    var apiResults = JsonConvert.DeserializeObject<List<AiDetectionResult>>(jsonResponse) ?? new List<AiDetectionResult>();

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DetectionResults.Clear();
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
                                    X = apiResult.Bbox?.X1 ?? 0,
                                    Y = apiResult.Bbox?.Y1 ?? 0,
                                    Width = (apiResult.Bbox != null) ? (apiResult.Bbox.X2 - apiResult.Bbox.X1) : 0,
                                    Height = (apiResult.Bbox != null) ? (apiResult.Bbox.Y2 - apiResult.Bbox.Y1) : 0
                                }
                            };
                            DetectionResults.Add(result);
                        }
                    });

                    UpdateStatusMessage($"Detection completed - found {apiResults.Count} objects via AI API");
                    CurrentSection = ViewerSection.Detection;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    UpdateStatusMessage($"AI API call failed: {response.StatusCode} - {errorContent}");
                    Debug.WriteLine($"API Error: {errorContent}");
                }
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
                        var apiResults = JsonConvert.DeserializeObject<List<AiDetectionResult>>(jsonResponse) ?? new List<AiDetectionResult>();

                        var perFeatureResults = new List<DetectionResult>();
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
                                    X = apiResult.Bbox?.X1 ?? 0,
                                    Y = apiResult.Bbox?.Y1 ?? 0,
                                    Width = (apiResult.Bbox != null) ? (apiResult.Bbox.X2 - apiResult.Bbox.X1) : 0,
                                    Height = (apiResult.Bbox != null) ? (apiResult.Bbox.Y2 - apiResult.Bbox.Y1) : 0
                                }
                            };
                            perFeatureResults.Add(result);
                            allResults.Add(result);
                        }

                        // Attach to feature if your model supports it
                        feature.DetectionResults = feature.DetectionResults ?? new List<DetectionResult>();
                        feature.DetectionResults.AddRange(perFeatureResults);
                    }
                    else
                    {
                        Debug.WriteLine($"Detection failed for feature {feature.Id}: {response.StatusCode}");
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    foreach (var result in allResults)
                    {
                        DetectionResults.Add(result);
                    }
                });

                UpdateStatusMessage($"Detection completed for {total} point(s) - found {allResults.Count} total objects");
                CurrentSection = ViewerSection.Detection;
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
            DetectionResults?.Clear();
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
                // TODO: Read the real file bytes and convert to Base64
                await Task.Delay(1); // keep async signature noise-free
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

        /// <summary>
        /// Provide a local shim so we can keep using OnPropertyChanged in this VM.
        /// DockPane derives from BindableBase which has NotifyPropertyChanged.
        /// </summary>
        protected void OnPropertyChanged(string propertyName)
        {
            // Forward to the base implementation
            base.NotifyPropertyChanged(propertyName);
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

                    // NOTE: We intentionally do not reference settings.DatabasePassword here,
                    // because your SettingsService/Model may not expose it. If it does,
                    // feel free to read & assign: DatabasePassword = settings.DatabasePassword;

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
                UpdateStatusMessage("Opening settings...");

                // Create settings window if it doesn't exist
                if (_settingsWindow == null)
                {
                    _settingsWindow = new Window
                    {
                        Title = "Spherical Viewer Settings",
                        Width = 500,
                        Height = 600,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Content = new SettingsView { DataContext = this }
                    };

                    _settingsWindow.Closed += (s, e) => _settingsWindow = null;
                }

                _settingsWindow.Show();
                _settingsWindow.Focus();

                IsSettingsOpen = true;
                UpdateStatusMessage("Settings panel opened");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Open settings error: {ex.Message}");
                Debug.WriteLine($"OpenSettings error: {ex}");
            }
        }

        private void CloseSettings()
        {
            try
            {
                if (_settingsWindow != null)
                {
                    _settingsWindow.Close();
                }
                IsSettingsOpen = false;
                UpdateStatusMessage("Settings panel closed");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Close settings error: {ex.Message}");
                Debug.WriteLine($"CloseSettings error: {ex}");
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

                // Simple global bbox for testing
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
                    CurrentSection = ViewerSection.Wfs;
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

        #region Map interaction (called from MapSelectionTool)

        /// <summary>
        /// Handles a map click sent by MapSelectionTool.
        /// </summary>
        public async Task OnMapPointClickedAsync(MapPoint mapPoint)
        {
            if (mapPoint == null)
            {
                UpdateStatusMessage("Map click ignored (null point).");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage($"Map point clicked at: X={mapPoint.X:0.#####}, Y={mapPoint.Y:0.#####}");

                CurrentSection = ViewerSection.Wfs;

                // Tiny bbox around clicked point; adjust to your spatial ref/workflow
                var tol = 0.0005;
                var bbox = new GeoBoundingBox
                {
                    MinX = mapPoint.X - tol,
                    MinY = mapPoint.Y - tol,
                    MaxX = mapPoint.X + tol,
                    MaxY = mapPoint.Y + tol
                };

                var resp = await _wfsService.QueryFeaturesAsync(bbox, Math.Max(1, WfsMaxFeatures));
                if (resp.Success && resp.Data?.Features?.Any() == true)
                {
                    _selectedFeatures = resp.Data.Features;
                    OnPropertyChanged(nameof(SelectionInfo));
                    WfsStatus = $"WFS: {_selectedFeatures.Count} selected near click";
                    UpdateStatusMessage($"Selected {_selectedFeatures.Count} feature(s) near the clicked point.");
                }
                else
                {
                    _selectedFeatures?.Clear();
                    OnPropertyChanged(nameof(SelectionInfo));
                    WfsStatus = "WFS: no features near click";
                    UpdateStatusMessage("No features near the clicked point.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnMapPointClickedAsync error: {ex}");
                UpdateStatusMessage($"Map click error: {ex.Message}");
                WfsStatus = "WFS: error";
            }
            finally
            {
                IsLoading = false;
            }
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

        #region Static

        public static SphericalViewerViewModel Create()
        {
            return new SphericalViewerViewModel();
        }

        protected override Task InitializeAsync()
        {
            // Keep base behavior; extend if needed.
            return base.InitializeAsync();
        }

        /// <summary>
        /// Helper to get the active pane's view-model instance.
        /// </summary>
        public static SphericalViewerViewModel GetActive()
        {
            var pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID) as SphericalViewerViewModel;
            return pane;
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
