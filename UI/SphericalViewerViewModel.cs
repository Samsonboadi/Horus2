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
            set
            {
                if (SetProperty(ref _aiApiBaseUrl, value))
                {
                    // Update HTTP client when API URL changes
                    UpdateHttpClientConfiguration();
                }
            }
        }



        private string _serverUrl = "http://localhost:5001";
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (SetProperty(ref _serverUrl, value))
                {
                    // Apply changes immediately when server URL changes
                    UpdateServiceConfiguration();
                }
            }
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



        private string _imageDirectory = "";
        public string ImageDirectory
        {
            get => _imageDirectory;
            set => SetProperty(ref _imageDirectory, value);
        }

        // === Selection Properties ===

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

        public string SelectionInfo
        {
            get
            {
                return _selectedFeatures?.Count > 0 ?
                    $"Selected: {_selectedFeatures.Count} feature(s)"
                    : "No selection";
            }
        }

        // === WFS Properties ===

        private string _wfsStatus = "Not connected";
        public string WfsStatus
        {
            get => _wfsStatus;
            set => SetProperty(ref _wfsStatus, value);
        }

        private int _wfsMaxFeatures = 100;
        public int WfsMaxFeatures
        {
            get => _wfsMaxFeatures;
            set => SetProperty(ref _wfsMaxFeatures, value);
        }

        // === Database Connection Properties ===

        private string _databaseHost = "";
        public string DatabaseHost
        {
            get => _databaseHost;
            set => SetProperty(ref _databaseHost, value);
        }

        private string _databasePort = "5432";
        public string DatabasePort
        {
            get => _databasePort;
            set => SetProperty(ref _databasePort, value);
        }

        private string _databaseName = "HorusWebMoviePlayer";
        public string DatabaseName
        {
            get => _databaseName;
            set => SetProperty(ref _databaseName, value);
        }

        private string _databaseUser = "";
        public string DatabaseUser
        {
            get => _databaseUser;
            set => SetProperty(ref _databaseUser, value);
        }

        private string _databasePassword = "";
        public string DatabasePassword
        {
            get => _databasePassword;
            set => SetProperty(ref _databasePassword, value);
        }

        // === Section Navigation Properties ===

        private int _currentSection = 20;
        public int CurrentSection
        {
            get => _currentSection;
            set
            {
                var clampedValue = Math.Max(0, Math.Min(value, SectionCount - 1));
                if (SetProperty(ref _currentSection, clampedValue))
                {
                    OnSectionChanged();
                }
            }
        }

        private int _sectionCount = 32;
        public int SectionCount
        {
            get => _sectionCount;
            set => SetProperty(ref _sectionCount, value);
        }

        private void UpdateHttpClientConfiguration()
        {
            try
            {
                if (_httpClient != null && !string.IsNullOrWhiteSpace(AiApiBaseUrl))
                {
                    // Update base address for HTTP client
                    if (Uri.IsWellFormedUriString(AiApiBaseUrl, UriKind.Absolute))
                    {
                        Debug.WriteLine($"HTTP client configured for AI API: {AiApiBaseUrl}");
                        UpdateStatusMessage($"AI API endpoint updated: {AiApiBaseUrl}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateHttpClientConfiguration error: {ex}");
            }
        }

        private void UpdateServiceConfiguration()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(ServerUrl))
                {
                    // Update service configurations when server URL changes
                    if (Uri.IsWellFormedUriString(ServerUrl, UriKind.Absolute))
                    {
                        Debug.WriteLine($"Service configuration updated: {ServerUrl}");
                        UpdateStatusMessage($"Server URL updated: {ServerUrl}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateServiceConfiguration error: {ex}");
            }
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

        // Settings Commands
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ApplySettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }

        #endregion

        #region Constructor

        protected SphericalViewerViewModel()
        {
            try
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
            catch (Exception ex)
            {
                Debug.WriteLine($"SphericalViewerViewModel constructor error: {ex}");
                UpdateStatusMessage($"Initialization error: {ex.Message}");
            }
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

            // Settings Commands
            SaveSettingsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => SaveSettings());
            ApplySettingsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ApplySettings());
            ResetSettingsCommand = new ArcGIS.Desktop.Framework.RelayCommand(() => ResetSettings());
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

                if (_imageFrames.Any())
                {
                    await LoadCurrentFrameAsync();
                    CanNavigateFrames = _imageFrames.Count > 1;
                    UpdateFrameInfo();
                    UpdateStatusMessage($"Loaded {_imageFrames.Count} image frames");
                }
                else
                {
                    UpdateStatusMessage("No image frames found");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Failed to load images: {ex.Message}");
                Debug.WriteLine($"LoadImagesAsync error: {ex}");
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
                UpdateStatusMessage("Loading Horus image frames...");
                _usingHorusImages = true;

                // This would normally connect to Horus API
                _imageFrames = new List<ImageFrame>
                {
                    new ImageFrame { Path = "horus_image_1.jpg" },
                    new ImageFrame { Path = "horus_image_2.jpg" }
                };

                _currentFrameIndex = 0;

                if (_imageFrames.Any())
                {
                    await LoadCurrentFrameAsync();
                    CanNavigateFrames = _imageFrames.Count > 1;
                    UpdateFrameInfo();
                    UpdateStatusMessage($"Loaded {_imageFrames.Count} Horus image frames");
                }
                else
                {
                    UpdateStatusMessage("No Horus image frames found");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Failed to load Horus images: {ex.Message}");
                Debug.WriteLine($"LoadHorusImagesAsync error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadCurrentFrameAsync()
        {
            try
            {
                if (_imageFrames == null || _currentFrameIndex < 0 || _currentFrameIndex >= _imageFrames.Count)
                    return;

                _currentFrame = _imageFrames[_currentFrameIndex];

                // For demo purposes, create a placeholder image
                // In a real implementation, you might load the actual image asynchronously
                await Task.Run(() =>
                {
                    // Simulate some async work
                    System.Threading.Thread.Sleep(50);
                });

                CurrentImage = CreatePlaceholderImage();

                if (_currentFrame != null)
                {
                    UpdateStatusMessage($"Loaded frame: {_currentFrame.Path}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Failed to load current frame: {ex.Message}");
                Debug.WriteLine($"LoadCurrentFrameAsync error: {ex}");
            }
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
            var bitmap = new WriteableBitmap(400, 300, 96, 96, System.Windows.Media.PixelFormats.Bgr32, null);
            return bitmap;
        }

        private void UpdateFrameInfo()
        {
            FrameInfo = $"Frame: {_currentFrameIndex + 1}/{_imageFrames?.Count ?? 0}";
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
                else if (!string.IsNullOrWhiteSpace(_currentFrame?.Path))
                {
                    request.ImageData = await GetImageAsBase64Async(_currentFrame.Path);
                }

                // Make HTTP API call instead of using _detectionService
                var json = JsonConvert.SerializeObject(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _httpClient.PostAsync($"{AiApiBaseUrl}/detect", content);
                response.EnsureSuccessStatusCode();

                var responseJson = await response.Content.ReadAsStringAsync();
                var apiResults = JsonConvert.DeserializeObject<List<AiDetectionResult>>(responseJson);

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DetectionResults.Clear();
                    foreach (var apiResult in apiResults)
                    {
                        var bbox = apiResult.Bbox;
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
                        HorusCurrentImage = feature.Id,
                        DetectionText = DetectionText,
                        Model = "langsam",
                        Yaw = Yaw,
                        Pitch = Pitch,
                        Roll = Roll,
                        Fov = Fov,
                        ConfidenceThreshold = DefaultConfidenceThreshold,
                        IoUThreshold = DefaultIoUThreshold
                    };

                    try
                    {
                        var json = JsonConvert.SerializeObject(request);
                        var content = new StringContent(json, Encoding.UTF8, "application/json");
                        var response = await _httpClient.PostAsync($"{AiApiBaseUrl}/detect", content);
                        response.EnsureSuccessStatusCode();

                        var responseJson = await response.Content.ReadAsStringAsync();
                        var apiResults = JsonConvert.DeserializeObject<List<AiDetectionResult>>(responseJson);

                        foreach (var apiResult in apiResults)
                        {
                            var bbox = apiResult.Bbox;
                            var result = new DetectionResult
                            {
                                ObjectName = apiResult.Label,
                                Confidence = apiResult.Confidence,
                                ModelUsed = "AI API Selection",
                                ProcessingTime = 0.0,
                                BoundingBox = new BoundingBox
                                {
                                    X = bbox?.X1 ?? 0,
                                    Y = bbox?.Y1 ?? 0,
                                    Width = (bbox != null) ? bbox.X2 - bbox.X1 : 0,
                                    Height = (bbox != null) ? bbox.Y2 - bbox.Y1 : 0
                                }
                            };
                            allResults.Add(result);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Detection failed for feature {feature.Id}: {ex}");
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    DetectionResults.Clear();
                    foreach (var result in allResults)
                    {
                        DetectionResults.Add(result);
                    }
                });

                UpdateStatusMessage($"Selection detection completed - found {allResults.Count} total objects");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Selection detection error: {ex.Message}");
                Debug.WriteLine($"RunDetectionOnSelectionAsync error: {ex}");
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

        private async Task<string> GetImageAsBase64Async(string imagePath)
        {
            try
            {
                if (File.Exists(imagePath))
                {
                    byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
                    return Convert.ToBase64String(imageBytes);
                }
                return string.Empty;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetImageAsBase64Async error: {ex}");
                return string.Empty;
            }
        }

        #endregion

        #region View Management

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
                Debug.WriteLine($"ResetView error: {ex}");
            }
        }

        #endregion

        #region Status Management

        private void UpdateStatusMessage(string message)
        {
            StatusMessage = message;
        }

        #endregion

        #region Settings Management

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.GetSettings();

                ServerUrl = settings.ServerUrl ?? "http://localhost:5001";
                ImageDirectory = settings.DefaultImageDirectory ?? "";
                DefaultConfidenceThreshold = settings.DefaultConfidenceThreshold;
                DefaultIoUThreshold = settings.DefaultIoUThreshold;

                // Load database settings
                DatabaseHost = settings.DatabaseHost ?? "";
                DatabasePort = settings.DatabasePort ?? "5432";
                DatabaseName = settings.DatabaseName ?? "HorusWebMoviePlayer";
                DatabaseUser = settings.DatabaseUser ?? "";
                DatabasePassword = settings.DatabasePassword ?? "";

                // Load view settings from CameraDefaults
                if (settings.CameraDefaults != null)
                {
                    Yaw = settings.CameraDefaults.Yaw;
                    Pitch = settings.CameraDefaults.Pitch;
                    Roll = settings.CameraDefaults.Roll;
                    Fov = settings.CameraDefaults.Fov;
                }

                // Load UI settings
                if (settings.UISettings != null)
                {
                    SectionCount = settings.UISettings.ImageSectionCount;
                    CurrentSection = settings.UISettings.PreferredSectionIndex;
                }

                UpdateStatusMessage("Settings loaded successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load settings error: {ex}");
                UpdateStatusMessage($"Settings load error: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                // Create UserSettings object from current properties
                var settings = new UserSettings
                {
                    ServerUrl = ServerUrl,
                    DefaultImageDirectory = ImageDirectory,
                    DefaultConfidenceThreshold = DefaultConfidenceThreshold,
                    DefaultIoUThreshold = DefaultIoUThreshold,

                    // Database settings
                    DatabaseHost = DatabaseHost,
                    DatabasePort = DatabasePort,
                    DatabaseName = DatabaseName,
                    DatabaseUser = DatabaseUser,
                    DatabasePassword = DatabasePassword,

                    // Camera defaults
                    CameraDefaults = new CameraDefaults
                    {
                        Yaw = Yaw,
                        Pitch = Pitch,
                        Roll = Roll,
                        Fov = Fov
                    },

                    // UI settings
                    UISettings = new UISettings
                    {
                        ImageSectionCount = SectionCount,
                        PreferredSectionIndex = CurrentSection,
                        ShowTooltips = true,
                        EnableAnimations = true,
                        Theme = "Light",
                        RememberWindowSize = true,
                        AutoConnect = false,
                        AutoRefreshWfsOnViewChange = true,
                        ClearWfsBeforeLoad = true,
                        ForceWebMercator = true,
                        WfsMaxFeatures = WfsMaxFeatures,
                        RequireMinZoomForWfs = true,
                        MinWfsViewWidthDegrees = 0.5,
                        DefaultImageScale = 2
                    }
                };

                // Save to file
                _settingsService.SaveSettings(settings);

                // Apply the settings to operations
                ApplySettings();

                UpdateStatusMessage("Settings saved and applied successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Save settings error: {ex}");
                UpdateStatusMessage($"Settings save error: {ex.Message}");
            }
        }

        private void ApplySettings()
        {
            try
            {
                // Apply AI API settings
                if (!string.IsNullOrWhiteSpace(ServerUrl) && Uri.IsWellFormedUriString(ServerUrl, UriKind.Absolute))
                {
                    AiApiBaseUrl = ServerUrl;
                }

                // Apply HTTP client settings
                if (_httpClient != null)
                {
                    _httpClient.Timeout = TimeSpan.FromMinutes(5);
                    _httpClient.DefaultRequestHeaders.Clear();
                    _httpClient.DefaultRequestHeaders.Add("User-Agent", "SphericalViewer/1.0");
                }

                // Apply WFS settings
                if (_wfsService != null && !string.IsNullOrWhiteSpace(ServerUrl))
                {
                    // Update WFS service configuration if needed
                    Debug.WriteLine($"WFS service configured with base URL: {ServerUrl}");
                }

                // Validate and apply camera settings
                if (Yaw < 0 || Yaw >= 360) Yaw = 0;
                if (Pitch < -90 || Pitch > 90) Pitch = -20;
                if (Fov < 10 || Fov > 180) Fov = 110;

                // Validate section settings
                if (CurrentSection < 0) CurrentSection = 0;
                if (CurrentSection >= SectionCount) CurrentSection = SectionCount - 1;

                UpdateStatusMessage("Settings applied successfully");
                Debug.WriteLine("Settings applied to all operations");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Apply settings error: {ex}");
                UpdateStatusMessage($"Apply settings error: {ex.Message}");
            }
        }

        private void ResetSettings()
        {
            try
            {
                // Reset to default values
                var defaultSettings = new UserSettings();

                ServerUrl = defaultSettings.ServerUrl;
                ImageDirectory = defaultSettings.DefaultImageDirectory;
                DefaultConfidenceThreshold = defaultSettings.DefaultConfidenceThreshold;
                DefaultIoUThreshold = defaultSettings.DefaultIoUThreshold;

                // Reset database settings
                DatabaseHost = defaultSettings.DatabaseHost;
                DatabasePort = defaultSettings.DatabasePort;
                DatabaseName = defaultSettings.DatabaseName;
                DatabaseUser = defaultSettings.DatabaseUser;
                DatabasePassword = defaultSettings.DatabasePassword;

                // Reset camera settings
                Yaw = defaultSettings.CameraDefaults.Yaw;
                Pitch = defaultSettings.CameraDefaults.Pitch;
                Roll = defaultSettings.CameraDefaults.Roll;
                Fov = defaultSettings.CameraDefaults.Fov;

                // Reset UI settings
                SectionCount = defaultSettings.UISettings.ImageSectionCount;
                CurrentSection = defaultSettings.UISettings.PreferredSectionIndex;
                WfsMaxFeatures = defaultSettings.UISettings.WfsMaxFeatures;

                UpdateStatusMessage("Settings reset to defaults");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reset settings error: {ex}");
                UpdateStatusMessage($"Reset settings error: {ex.Message}");
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
                WfsStatus = "Loading...";
                UpdateStatusMessage("Loading WFS points...");

                // Simulate WFS loading
                await Task.Delay(1000);

                _selectedFeatures = new List<WfsFeature>
                {
                    new WfsFeature { Id = "feature_1" },
                    new WfsFeature { Id = "feature_2" }
                };

                WfsStatus = $"Loaded {_selectedFeatures.Count} features";
                OnPropertyChanged(nameof(SelectionInfo));
                UpdateStatusMessage($"WFS points loaded - {_selectedFeatures.Count} features available");
            }
            catch (Exception ex)
            {
                WfsStatus = $"Error: {ex.Message}";
                UpdateStatusMessage($"WFS load error: {ex.Message}");
                Debug.WriteLine($"LoadWfsPointsAsync error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ClearSelection()
        {
            _selectedFeatures.Clear();
            OnPropertyChanged(nameof(SelectionInfo));
            UpdateStatusMessage("Selection cleared");
        }

        #endregion

        #region Map Interaction

        public async Task OnMapPointClickedAsync(MapPoint mapPoint)
        {
            try
            {
                if (mapPoint == null) return;

                UpdateStatusMessage($"Map point clicked: {mapPoint.X:F6}, {mapPoint.Y:F6}");

                // Here you would typically:
                // 1. Query the WFS service for features near this point
                // 2. Load images for the selected location
                // 3. Update the current view

                // For now, just simulate the operation
                await Task.Delay(100);

                Debug.WriteLine($"Map interaction at coordinates: {mapPoint.X}, {mapPoint.Y}");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Map interaction error: {ex.Message}");
                Debug.WriteLine($"OnMapPointClickedAsync error: {ex}");
            }
        }

        #endregion

        #region Section Navigation

        private void OnSectionChanged()
        {
            try
            {
                // Update status when section changes
                UpdateStatusMessage($"Section changed to: {CurrentSection + 1}/{SectionCount}");

                // Here you would typically:
                // 1. Load the image for the new section
                // 2. Update the current view
                // 3. Refresh any overlays or annotations

                Debug.WriteLine($"Section changed to: {CurrentSection}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnSectionChanged error: {ex}");
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

        #region INotifyPropertyChanged

        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected new bool SetProperty<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
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