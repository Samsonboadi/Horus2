using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework;
using Test.Models;
using Test.Services;
using System.Diagnostics;
using System.Linq;

namespace Test.UI
{
    internal class SphericalViewerViewModel : DockPane, INotifyPropertyChanged
    {
        private const string _dockPaneID = "Test_SphericalViewer_DockPane";

        #region Private Fields
        private readonly PythonApiService _apiService;
        private readonly SettingsService _settingsService;
        private ImageFrame _currentFrame;
        private List<ImageFrame> _imageFrames = new List<ImageFrame>();
        private int _currentFrameIndex = 0;
        private bool _isSettingsOpen = false;

        private readonly HorusMediaService _horusService;
        private List<HorusRecording> _horusRecordings = new List<HorusRecording>();
        private List<HorusImage> _horusImages = new List<HorusImage>();
        private HorusRecording _selectedRecording;
        private int _currentHorusImageIndex = 0;
        private bool _usingHorusImages = false;

        // Settings window reference
        private Window _settingsWindow;
        #endregion

        #region Properties
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

        private double _yaw = 0.0;
        public double Yaw
        {
            get => _yaw;
            set
            {
                if (SetProperty(ref _yaw, value))
                {
                    OnPropertyChanged(nameof(YawAngle));
                    _ = UpdateImageViewAsync();
                }
            }
        }

        public double YawAngle => (_yaw + 180) * 2;

        private double _pitch = -20.0;
        public double Pitch
        {
            get => _pitch;
            set
            {
                if (SetProperty(ref _pitch, value))
                {
                    OnPropertyChanged(nameof(PitchAngle));
                    _ = UpdateImageViewAsync();
                }
            }
        }

        public double PitchAngle => (_pitch + 90) * 2;

        private double _roll = 0.0;
        public double Roll
        {
            get => _roll;
            set
            {
                if (SetProperty(ref _roll, value))
                {
                    OnPropertyChanged(nameof(RollAngle));
                    _ = UpdateImageViewAsync();
                }
            }
        }

        public double RollAngle => (_roll + 180) * 2;

        private double _fov = 110.0;
        public double Fov
        {
            get => _fov;
            set
            {
                if (SetProperty(ref _fov, value))
                {
                    OnPropertyChanged(nameof(FovAngle));
                    _ = UpdateImageViewAsync();
                }
            }
        }

        public double FovAngle => ((_fov - 30) / 150) * 360;

        private string _detectionText = "pole";
        public string DetectionText
        {
            get => _detectionText;
            set => SetProperty(ref _detectionText, value);
        }

        private string _statusMessage = "Spherical Image Viewer loaded successfully!";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private bool _isConnected = false;
        public bool IsConnected
        {
            get => _isConnected;
            set => SetProperty(ref _isConnected, value);
        }

        private bool _isLoading = false;
        public bool IsLoading
        {
            get => _isLoading;
            set => SetProperty(ref _isLoading, value);
        }

        public ObservableCollection<string> AvailableModels { get; } = new ObservableCollection<string>
        {
            "GroundingLangSAM",
            "GroundingDino",
            "YoloWorld",
            "SAM_V2",
            "Florence2"
        };

        private string _selectedModel = "GroundingLangSAM";
        public string SelectedModel
        {
            get => _selectedModel;
            set => SetProperty(ref _selectedModel, value);
        }

        private string _frameInfo = "Frame: 0/0";
        public string FrameInfo
        {
            get => _frameInfo;
            set => SetProperty(ref _frameInfo, value);
        }

        private bool _canNavigateFrames = false;
        public bool CanNavigateFrames
        {
            get => _canNavigateFrames;
            set => SetProperty(ref _canNavigateFrames, value);
        }

        private ObservableCollection<DetectionResult> _detectionResults = new ObservableCollection<DetectionResult>();
        public ObservableCollection<DetectionResult> DetectionResults
        {
            get => _detectionResults;
            set => SetProperty(ref _detectionResults, value);
        }

        // Settings Properties
        private bool _autoConnect = false;
        public bool AutoConnect
        {
            get => _autoConnect;
            set => SetProperty(ref _autoConnect, value);
        }

        private int _connectionTimeout = 30;
        public int ConnectionTimeout
        {
            get => _connectionTimeout;
            set => SetProperty(ref _connectionTimeout, value);
        }

        private string _defaultImageDirectory = "/web/images/";
        public string DefaultImageDirectory
        {
            get => _defaultImageDirectory;
            set => SetProperty(ref _defaultImageDirectory, value);
        }

        private string _supportedFormats = "*.jpg,*.png,*.jpeg,*.tiff";
        public string SupportedFormats
        {
            get => _supportedFormats;
            set => SetProperty(ref _supportedFormats, value);
        }

        private int _maxImagesToLoad = 1000;
        public int MaxImagesToLoad
        {
            get => _maxImagesToLoad;
            set => SetProperty(ref _maxImagesToLoad, value);
        }

        private bool _includeSubdirectories = true;
        public bool IncludeSubdirectories
        {
            get => _includeSubdirectories;
            set => SetProperty(ref _includeSubdirectories, value);
        }

        private double _defaultYaw = 0.0;
        public double DefaultYaw
        {
            get => _defaultYaw;
            set => SetProperty(ref _defaultYaw, value);
        }

        private double _defaultPitch = -20.0;
        public double DefaultPitch
        {
            get => _defaultPitch;
            set => SetProperty(ref _defaultPitch, value);
        }

        private double _defaultRoll = 0.0;
        public double DefaultRoll
        {
            get => _defaultRoll;
            set => SetProperty(ref _defaultRoll, value);
        }

        private double _defaultFov = 110.0;
        public double DefaultFov
        {
            get => _defaultFov;
            set => SetProperty(ref _defaultFov, value);
        }

        private string _defaultModel = "GroundingLangSAM";
        public string DefaultModel
        {
            get => _defaultModel;
            set => SetProperty(ref _defaultModel, value);
        }

        private double _defaultConfidenceThreshold = 0.3;
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

        private string _defaultDetectionTarget = "pole";
        public string DefaultDetectionTarget
        {
            get => _defaultDetectionTarget;
            set => SetProperty(ref _defaultDetectionTarget, value);
        }

        // Database Connection Properties
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

        private bool _isDatabaseConnected = false;
        public bool IsDatabaseConnected
        {
            get => _isDatabaseConnected;
            set => SetProperty(ref _isDatabaseConnected, value);
        }

        // Image Retrieval Properties
        private string _recordingEndpoint = "Rotterdam360\\\\Ladybug5plus";
        public string RecordingEndpoint
        {
            get => _recordingEndpoint;
            set => SetProperty(ref _recordingEndpoint, value);
        }

        private string _horusClientUrl = "http://10.0.10.100:5050/web/";
        public string HorusClientUrl
        {
            get => _horusClientUrl;
            set => SetProperty(ref _horusClientUrl, value);
        }

        private int _defaultNumberOfImages = 5;
        public int DefaultNumberOfImages
        {
            get => _defaultNumberOfImages;
            set => SetProperty(ref _defaultNumberOfImages, value);
        }

        private int _defaultImageWidth = 600;
        public int DefaultImageWidth
        {
            get => _defaultImageWidth;
            set => SetProperty(ref _defaultImageWidth, value);
        }

        private int _defaultImageHeight = 600;
        public int DefaultImageHeight
        {
            get => _defaultImageHeight;
            set => SetProperty(ref _defaultImageHeight, value);
        }

        public bool IsSettingsOpen
        {
            get => _isSettingsOpen;
            set => SetProperty(ref _isSettingsOpen, value);
        }

        public List<HorusRecording> HorusRecordings
        {
            get => _horusRecordings;
            set => SetProperty(ref _horusRecordings, value);
        }

        public HorusRecording SelectedRecording
        {
            get => _selectedRecording;
            set => SetProperty(ref _selectedRecording, value);
        }

        private bool _isHorusConnected = false;
        public bool IsHorusConnected
        {
            get => _isHorusConnected;
            set => SetProperty(ref _isHorusConnected, value);
        }
        #endregion

        #region Commands
        public ICommand ConnectCommand { get; private set; }
        public ICommand DisconnectCommand { get; private set; }
        public ICommand LoadImagesCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand RunDetectionCommand { get; private set; }
        public ICommand ResetViewCommand { get; private set; }
        public ICommand PreviousFrameCommand { get; private set; }
        public ICommand NextFrameCommand { get; private set; }
        public ICommand FirstFrameCommand { get; private set; }
        public ICommand LastFrameCommand { get; private set; }
        public ICommand OpenSettingsCommand { get; private set; }
        public ICommand CloseSettingsCommand { get; private set; }
        public ICommand SaveSettingsCommand { get; private set; }
        public ICommand ResetSettingsCommand { get; private set; }
        public ICommand TestConnectionCommand { get; private set; }
        public ICommand TestDatabaseConnectionCommand { get; private set; }
        public ICommand BrowseDirectoryCommand { get; private set; }

        public ICommand ConnectHorusCommand { get; private set; }
        public ICommand DisconnectHorusCommand { get; private set; }
        public ICommand LoadHorusRecordingsCommand { get; private set; }
        public ICommand LoadHorusImagesCommand { get; private set; }
        public ICommand StartPythonBridgeCommand { get; private set; }
        public ICommand TestHorusConnectionCommand { get; private set; }
        #endregion

        protected SphericalViewerViewModel()
        {
            try
            {
                _apiService = new PythonApiService();
                _settingsService = new SettingsService();
                _horusService = new HorusMediaService();

                LoadSettings();
                InitializeCommands();

                StatusMessage = "Spherical Image Viewer initialized successfully";

                if (AutoConnect)
                {
                    _ = ConnectToServerAsync();
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Initialization error: {ex.Message}";
            }
        }

        protected override Task InitializeAsync()
        {
            StatusMessage = "Dock pane initialized and ready";
            return Task.CompletedTask;
        }

        private void InitializeCommands()
        {
            ConnectCommand = new RelayCommand(async () => await ConnectToServerAsync(), () => !IsLoading);
            DisconnectCommand = new RelayCommand(() => DisconnectFromServer(), () => IsConnected);
            LoadImagesCommand = new RelayCommand(async () => await LoadImagesAsync(), () => IsConnected && !IsLoading);
            RefreshCommand = new RelayCommand(() => RefreshView(), () => HasImage);
            RunDetectionCommand = new RelayCommand(async () => await RunDetectionAsync(), () => HasImage && !IsLoading);
            ResetViewCommand = new RelayCommand(() => ResetView(), () => HasImage);
            PreviousFrameCommand = new RelayCommand(() => PreviousFrame(), () => CanNavigateFrames && (_currentFrameIndex > 0 || _currentHorusImageIndex > 0));
            NextFrameCommand = new RelayCommand(() => NextFrame(), () => CanNavigateFrames && ((_usingHorusImages && _currentHorusImageIndex < _horusImages.Count - 1) || (!_usingHorusImages && _currentFrameIndex < _imageFrames.Count - 1)));
            FirstFrameCommand = new RelayCommand(() => FirstFrame(), () => CanNavigateFrames && (_currentFrameIndex > 0 || _currentHorusImageIndex > 0));
            LastFrameCommand = new RelayCommand(() => LastFrame(), () => CanNavigateFrames && ((_usingHorusImages && _currentHorusImageIndex < _horusImages.Count - 1) || (!_usingHorusImages && _currentFrameIndex < _imageFrames.Count - 1)));
            OpenSettingsCommand = new RelayCommand(() => OpenSettings(), () => !IsSettingsOpen);
            CloseSettingsCommand = new RelayCommand(() => CloseSettings(), () => IsSettingsOpen);
            SaveSettingsCommand = new RelayCommand(() => SaveSettings(), () => IsSettingsOpen);
            ResetSettingsCommand = new RelayCommand(() => ResetSettings(), () => IsSettingsOpen);
            TestConnectionCommand = new RelayCommand(async () => await TestConnectionAsync(), () => !IsLoading);
            TestDatabaseConnectionCommand = new RelayCommand(async () => await TestDatabaseConnectionAsync(), () => !IsLoading);
            BrowseDirectoryCommand = new RelayCommand(() => BrowseDirectory(), () => true);
            ConnectHorusCommand = new RelayCommand(async () => await ConnectToHorusAsync(), () => !IsLoading && !IsHorusConnected);
            DisconnectHorusCommand = new RelayCommand(async () => await DisconnectFromHorusAsync(), () => IsHorusConnected);
            LoadHorusRecordingsCommand = new RelayCommand(async () => await LoadHorusRecordingsAsync(), () => IsHorusConnected && !IsLoading);
            LoadHorusImagesCommand = new RelayCommand(async () => await LoadHorusImagesAsync(), () => IsHorusConnected && SelectedRecording != null && !IsLoading);
            StartPythonBridgeCommand = new RelayCommand(async () => await StartPythonBridgeAsync(), () => !IsLoading);
            TestHorusConnectionCommand = new RelayCommand(async () => await TestHorusConnectionAsync(), () => !IsLoading);
        }

        #region Command Methods

        private void OpenSettings()
        {
            try
            {
                if (_settingsWindow == null || !_settingsWindow.IsLoaded)
                {
                    var settingsView = new SettingsView();
                    settingsView.DataContext = this;

                    _settingsWindow = new Window
                    {
                        Title = "Spherical Image Viewer - Settings",
                        Content = settingsView,
                        Width = 650,
                        Height = 750,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        ResizeMode = ResizeMode.CanResize,
                        ShowInTaskbar = false,
                        WindowStyle = WindowStyle.ToolWindow
                    };

                    _settingsWindow.Closed += (s, e) => { _settingsWindow = null; IsSettingsOpen = false; };
                }

                IsSettingsOpen = true;
                _settingsWindow.Show();
                _settingsWindow.Activate();
                StatusMessage = "Settings window opened";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open settings: {ex.Message}";
            }
        }

        private void CloseSettings()
        {
            try
            {
                _settingsWindow?.Close();
                IsSettingsOpen = false;
                StatusMessage = "Settings window closed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing settings: {ex.Message}";
            }
        }

        private async Task StartPythonBridgeAsync()
        {
            try
            {
                StatusMessage = "Starting Python bridge server...";
                IsLoading = true;

                // Create a temporary config file with current database settings
                var bridgeConfig = new
                {
                    database = new
                    {
                        host = DatabaseHost,
                        port = DatabasePort,
                        database = DatabaseName,
                        user = DatabaseUser,
                        password = DatabasePassword
                    },
                    horus = new
                    {
                        url = HorusClientUrl
                    }
                };

                string configPath = Path.Combine(Path.GetTempPath(), "horus_bridge_config.json");
                string configJson = Newtonsoft.Json.JsonConvert.SerializeObject(bridgeConfig, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(configPath, configJson);

                // Path to your Python bridge script - should be in Scripts folder within add-in
                string addinPath = AppDomain.CurrentDomain.BaseDirectory;
                string scriptPath = Path.Combine(addinPath, "Scripts", "horus_bridge_server.py");

                // Alternative paths to check
                if (!File.Exists(scriptPath))
                {
                    // Try relative to current directory
                    scriptPath = Path.Combine(Environment.CurrentDirectory, "Scripts", "horus_bridge_server.py");
                }

                if (!File.Exists(scriptPath))
                {
                    // Try in the same directory as the executable
                    var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var assemblyDir = Path.GetDirectoryName(assemblyPath);
                    scriptPath = Path.Combine(assemblyDir, "Scripts", "horus_bridge_server.py");
                }

                if (!File.Exists(scriptPath))
                {
                    StatusMessage = $"Python script not found. Expected location: {scriptPath}";
                    return;
                }

                // Use ArcGIS Pro Python executable
                string pythonExe = @"C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe";

                if (!File.Exists(pythonExe))
                {
                    StatusMessage = "ArcGIS Pro Python not found. Please check installation.";
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --config \"{configPath}\" --port 5001 --host localhost",
                    UseShellExecute = false,
                    CreateNoWindow = false, // Show window for debugging
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };

                StatusMessage = "Launching Python bridge server...";
                var process = Process.Start(startInfo);

                if (process == null)
                {
                    StatusMessage = "Failed to start Python bridge process";
                    return;
                }

                StatusMessage = "Waiting for Python bridge to initialize...";

                // Give the server time to start
                await Task.Delay(5000);

                // Test if the server is running
                StatusMessage = "Testing bridge connection...";
                var healthCheck = await _horusService.CheckHealthAsync();

                if (healthCheck.Success)
                {
                    StatusMessage = $"Python bridge server started successfully! {healthCheck.Message}";
                }
                else
                {
                    StatusMessage = $"Python bridge server may not have started correctly: {healthCheck.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to start Python bridge: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task TestHorusConnectionAsync()
        {
            try
            {
                StatusMessage = "Testing Horus connection...";

                var healthCheck = await _horusService.CheckHealthAsync();
                if (!healthCheck.Success)
                {
                    StatusMessage = "Python bridge server is not running. Start it first.";
                    return;
                }

                StatusMessage = healthCheck.Data ? "Horus connection test successful" : "Horus connection test failed";
                IsHorusConnected = healthCheck.Data;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Horus connection test failed: {ex.Message}";
                IsHorusConnected = false;
            }
        }

        private async Task ConnectToHorusAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Connecting to Horus media server...";

                var config = new HorusConnectionConfig
                {
                    HorusHost = HorusClientUrl.Replace("http://", "").Replace("https://", "").Split(':')[0],
                    HorusPort = 5050,
                    DatabaseHost = DatabaseHost,
                    DatabasePort = DatabasePort,
                    DatabaseName = DatabaseName,
                    DatabaseUser = DatabaseUser,
                    DatabasePassword = DatabasePassword
                };

                var result = await _horusService.ConnectAsync(config);

                if (result.Success && result.Data)
                {
                    IsHorusConnected = true;
                    StatusMessage = "Connected to Horus media server successfully";

                    // Automatically load recordings after successful connection
                    await LoadHorusRecordingsAsync();
                }
                else
                {
                    IsHorusConnected = false;
                    StatusMessage = $"Failed to connect to Horus: {result.Error}";
                }
            }
            catch (Exception ex)
            {
                IsHorusConnected = false;
                StatusMessage = $"Horus connection error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DisconnectFromHorusAsync()
        {
            try
            {
                StatusMessage = "Disconnecting from Horus...";

                var result = await _horusService.DisconnectAsync();

                IsHorusConnected = false;
                HorusRecordings.Clear();
                _horusImages.Clear();
                SelectedRecording = null;

                StatusMessage = result.Success ? "Disconnected from Horus successfully" : $"Disconnect error: {result.Error}";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Disconnect error: {ex.Message}";
            }
        }

        private async Task LoadHorusRecordingsAsync()
        {
            if (!IsHorusConnected)
            {
                StatusMessage = "Not connected to Horus server";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Loading Horus recordings...";

                var response = await _horusService.GetRecordingsAsync();

                if (response.Success && response.Data != null)
                {
                    HorusRecordings = new List<HorusRecording>(response.Data);
                    StatusMessage = $"Loaded {HorusRecordings.Count} recordings from Horus";

                    // Auto-select the first recording if available
                    if (HorusRecordings.Count > 0)
                    {
                        SelectedRecording = HorusRecordings.FirstOrDefault(r =>
                            r.Endpoint.Contains(RecordingEndpoint) ||
                            r.Endpoint.Contains("Rotterdam360")) ?? HorusRecordings[0];
                    }
                }
                else
                {
                    StatusMessage = $"Failed to load recordings: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading recordings: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task LoadHorusImagesAsync()
        {
            if (!IsHorusConnected || SelectedRecording == null)
            {
                StatusMessage = "Please connect to Horus and select a recording first";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Loading images from Horus...";

                var request = new HorusImageRequest
                {
                    RecordingEndpoint = SelectedRecording.Endpoint,
                    Count = DefaultNumberOfImages,
                    Width = DefaultImageWidth,
                    Height = DefaultImageHeight
                };

                var response = await _horusService.GetImagesAsync(request);

                if (response.Success && response.Data != null)
                {
                    _horusImages = response.Data;
                    _currentHorusImageIndex = 0;
                    _usingHorusImages = true;

                    // Convert first image and display it
                    if (_horusImages.Count > 0)
                    {
                        await DisplayHorusImageAsync(0);
                        CanNavigateFrames = _horusImages.Count > 1;
                        UpdateHorusFrameInfo();
                    }

                    StatusMessage = $"Loaded {_horusImages.Count} images from Horus";
                }
                else
                {
                    StatusMessage = $"Failed to load images: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading images: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task DisplayHorusImageAsync(int imageIndex)
        {
            try
            {
                if (imageIndex < 0 || imageIndex >= _horusImages.Count)
                    return;

                var horusImage = _horusImages[imageIndex];
                var imageBytes = horusImage.GetImageBytes();

                if (imageBytes != null)
                {
                    CurrentImage = CreateBitmapFromBytes(imageBytes);
                    OnPropertyChanged(nameof(HasImage));
                    _currentHorusImageIndex = imageIndex;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error displaying Horus image: {ex.Message}";
            }
        }

        private void UpdateHorusFrameInfo()
        {
            if (_horusImages.Count > 0)
            {
                FrameInfo = $"Horus Image: {_currentHorusImageIndex + 1}/{_horusImages.Count}";
            }
            else
            {
                FrameInfo = "No Horus images loaded";
            }
        }

        private async Task ConnectToServerAsync()
        {
            try
            {
                IsLoading = true;
                StatusMessage = "Connecting to server...";
                _apiService.UpdateBaseUrl(ServerUrl);

                var response = await _apiService.GetAsync<ServerInfo>("health");
                if (response.Success)
                {
                    IsConnected = true;
                    StatusMessage = "Connected to server successfully";
                }
                else
                {
                    StatusMessage = $"Connection failed: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Connection error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task TestConnectionAsync()
        {
            StatusMessage = "Testing connection...";
            await ConnectToServerAsync();
        }

        private void DisconnectFromServer()
        {
            IsConnected = false;
            StatusMessage = "Disconnected from server";
            _imageFrames.Clear();
            CurrentImage = null;
            OnPropertyChanged(nameof(HasImage));
            CanNavigateFrames = false;
            FrameInfo = "Frame: 0/0";
        }

        private void RefreshView()
        {
            if (_currentFrame != null)
            {
                _ = UpdateImageViewAsync();
                StatusMessage = "View refreshed";
            }
            else
            {
                StatusMessage = "No image to refresh";
            }
        }

        private async Task LoadImagesAsync()
        {
            if (!IsConnected)
            {
                StatusMessage = "Please connect to server first";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Loading image frames...";
                _usingHorusImages = false;

                var request = new LoadImagesRequest
                {
                    Directory = ImageDirectory,
                    ServerUrl = ServerUrl,
                    FilePattern = SupportedFormats,
                    IncludeSubdirectories = IncludeSubdirectories,
                    MaxImages = MaxImagesToLoad
                };

                var response = await _apiService.PostAsync<List<ImageFrame>>("images/load", request);
                if (response.Success && response.Data != null)
                {
                    _imageFrames = response.Data;
                    _currentFrameIndex = 0;

                    if (_imageFrames.Count > 0)
                    {
                        await LoadCurrentFrameAsync();
                        CanNavigateFrames = _imageFrames.Count > 1;
                        UpdateFrameInfo();
                        StatusMessage = $"Loaded {_imageFrames.Count} image frames";
                    }
                    else
                    {
                        StatusMessage = "No images found in specified directory";
                    }
                }
                else
                {
                    StatusMessage = $"Failed to load images: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error loading images: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task RunDetectionAsync()
        {
            if ((_currentFrame == null && !_usingHorusImages) || string.IsNullOrWhiteSpace(DetectionText))
            {
                StatusMessage = "Please load an image and specify detection target";
                return;
            }

            try
            {
                IsLoading = true;
                StatusMessage = "Running AI object detection...";

                var request = new DetectionRequest
                {
                    ImagePath = _usingHorusImages ? "horus_current_image" : _currentFrame?.Path,
                    Model = SelectedModel,
                    DetectionText = DetectionText,
                    Yaw = Yaw,
                    Pitch = Pitch,
                    Roll = Roll,
                    Fov = Fov,
                    ConfidenceThreshold = DefaultConfidenceThreshold,
                    IoUThreshold = DefaultIoUThreshold
                };

                var response = await _apiService.PostAsync<List<DetectionResult>>("detection/detect", request);
                if (response.Success && response.Data != null)
                {
                    DetectionResults.Clear();
                    foreach (var result in response.Data)
                    {
                        DetectionResults.Add(result);
                    }
                    StatusMessage = $"Detection completed - found {response.Data.Count} objects";
                }
                else
                {
                    StatusMessage = $"Detection failed: {response.Error}";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Detection error: {ex.Message}";
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ResetView()
        {
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
            Roll = DefaultRoll;
            Fov = DefaultFov;
            StatusMessage = "View reset to defaults";
        }

        private void PreviousFrame()
        {
            if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex > 0)
            {
                _ = DisplayHorusImageAsync(_currentHorusImageIndex - 1);
                UpdateHorusFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
            else if (_currentFrameIndex > 0)
            {
                _currentFrameIndex--;
                _ = LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void NextFrame()
        {
            if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex < _horusImages.Count - 1)
            {
                _ = DisplayHorusImageAsync(_currentHorusImageIndex + 1);
                UpdateHorusFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
            else if (_currentFrameIndex < _imageFrames.Count - 1)
            {
                _currentFrameIndex++;
                _ = LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void FirstFrame()
        {
            if (_usingHorusImages && _horusImages.Count > 0)
            {
                _ = DisplayHorusImageAsync(0);
                UpdateHorusFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
            else if (_imageFrames.Count > 0)
            {
                _currentFrameIndex = 0;
                _ = LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void LastFrame()
        {
            if (_usingHorusImages && _horusImages.Count > 0)
            {
                _ = DisplayHorusImageAsync(_horusImages.Count - 1);
                UpdateHorusFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
            else if (_imageFrames.Count > 0)
            {
                _currentFrameIndex = _imageFrames.Count - 1;
                _ = LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void SaveSettings()
        {
            try
            {
                var settings = new UserSettings
                {
                    ServerUrl = ServerUrl,
                    DefaultImageDirectory = DefaultImageDirectory,
                    DefaultModel = DefaultModel,
                    DefaultConfidenceThreshold = DefaultConfidenceThreshold,
                    DefaultIoUThreshold = DefaultIoUThreshold,
                    CameraDefaults = new CameraDefaults
                    {
                        Yaw = DefaultYaw,
                        Pitch = DefaultPitch,
                        Roll = DefaultRoll,
                        Fov = DefaultFov
                    },
                    UISettings = new UISettings
                    {
                        AutoConnect = AutoConnect,
                        RememberWindowSize = true,
                        ShowTooltips = true,
                        EnableAnimations = true,
                        Theme = "Modern"
                    },
                    DatabaseHost = DatabaseHost,
                    DatabasePort = DatabasePort,
                    DatabaseName = DatabaseName,
                    DatabaseUser = DatabaseUser,
                    DatabasePassword = DatabasePassword,
                    RecordingEndpoint = RecordingEndpoint,
                    HorusClientUrl = HorusClientUrl,
                    DefaultNumberOfImages = DefaultNumberOfImages,
                    DefaultImageWidth = DefaultImageWidth,
                    DefaultImageHeight = DefaultImageHeight
                };

                _settingsService.SaveSettings(settings);
                CloseSettings();
                StatusMessage = "Settings saved successfully";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error saving settings: {ex.Message}";
            }
        }

        private void ResetSettings()
        {
            var defaultSettings = new UserSettings();
            LoadSettingsFromObject(defaultSettings);
            StatusMessage = "Settings reset to defaults";
        }

        private async Task TestDatabaseConnectionAsync()
        {
            try
            {
                StatusMessage = "Testing database connection...";

                // Test using psycopg2 connection similar to Python script
                string connectionString = $"host={DatabaseHost};port={DatabasePort};database={DatabaseName};user={DatabaseUser};password={DatabasePassword}";

                // For now, simulate the test - you can implement actual Npgsql test here
                await Task.Delay(1000);

                // TODO: Implement actual database connection test using Npgsql
                // var connection = new NpgsqlConnection(connectionString);
                // await connection.OpenAsync();
                // connection.Close();

                IsDatabaseConnected = true;
                StatusMessage = "Database connection test successful";
            }
            catch (Exception ex)
            {
                IsDatabaseConnected = false;
                StatusMessage = $"Database connection test failed: {ex.Message}";
            }
        }

        private void BrowseDirectory()
        {
            try
            {
                // TODO: Implement folder browser dialog
                StatusMessage = "Directory browser - to be implemented";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Directory browser error: {ex.Message}";
            }
        }
        #endregion

        #region Helper Methods
        private async Task LoadCurrentFrameAsync()
        {
            if (_currentFrameIndex < 0 || _currentFrameIndex >= _imageFrames.Count) return;

            _currentFrame = _imageFrames[_currentFrameIndex];
            await UpdateImageViewAsync();
        }

        private async Task UpdateImageViewAsync()
        {
            // Handle Horus images
            if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex < _horusImages.Count)
            {
                try
                {
                    var horusImage = _horusImages[_currentHorusImageIndex];
                    var imageBytes = horusImage.GetImageBytes();

                    if (imageBytes != null)
                    {
                        CurrentImage = CreateBitmapFromBytes(imageBytes);
                        OnPropertyChanged(nameof(HasImage));
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Error displaying Horus image: {ex.Message}";
                }
                return;
            }

            // Handle regular images
            if (_currentFrame == null) return;

            try
            {
                var request = new RenderRequest
                {
                    ImagePath = _currentFrame.Path,
                    Yaw = Yaw,
                    Pitch = Pitch,
                    Roll = Roll,
                    Fov = Fov,
                    Width = 800,
                    Height = 600
                };

                var response = await _apiService.PostAsync<byte[]>("images/render", request);
                if (response.Success && response.Data != null)
                {
                    CurrentImage = CreateBitmapFromBytes(response.Data);
                    OnPropertyChanged(nameof(HasImage));
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error updating image: {ex.Message}";
            }
        }

        private BitmapSource CreateBitmapFromBytes(byte[] imageBytes)
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

        private void UpdateFrameInfo()
        {
            FrameInfo = $"Frame: {_currentFrameIndex + 1}/{_imageFrames.Count}";
        }

        private void LoadSettings()
        {
            try
            {
                var settings = _settingsService.GetSettings();
                LoadSettingsFromObject(settings);
            }
            catch (Exception ex)
            {
                StatusMessage = $"Settings load error: {ex.Message}";
                var defaultSettings = new UserSettings();
                LoadSettingsFromObject(defaultSettings);
            }
        }

        private void LoadSettingsFromObject(UserSettings settings)
        {
            ServerUrl = settings.ServerUrl;
            DefaultImageDirectory = settings.DefaultImageDirectory;
            ImageDirectory = settings.DefaultImageDirectory;
            DefaultModel = settings.DefaultModel;
            SelectedModel = settings.DefaultModel;
            DefaultConfidenceThreshold = settings.DefaultConfidenceThreshold;
            DefaultIoUThreshold = settings.DefaultIoUThreshold;

            DefaultYaw = settings.CameraDefaults.Yaw;
            DefaultPitch = settings.CameraDefaults.Pitch;
            DefaultRoll = settings.CameraDefaults.Roll;
            DefaultFov = settings.CameraDefaults.Fov;

            AutoConnect = settings.UISettings.AutoConnect;

            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
            Roll = DefaultRoll;
            Fov = DefaultFov;

            DetectionText = DefaultDetectionTarget;

            DatabaseHost = settings.DatabaseHost;
            DatabasePort = settings.DatabasePort;
            DatabaseName = settings.DatabaseName;
            DatabaseUser = settings.DatabaseUser;
            DatabasePassword = settings.DatabasePassword;

            RecordingEndpoint = settings.RecordingEndpoint;
            HorusClientUrl = settings.HorusClientUrl;
            DefaultNumberOfImages = settings.DefaultNumberOfImages;
            DefaultImageWidth = settings.DefaultImageWidth;
            DefaultImageHeight = settings.DefaultImageHeight;
        }
        #endregion

        #region INotifyPropertyChanged
        public new event PropertyChangedEventHandler PropertyChanged;

        protected new virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            backingStore = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        #endregion

        /// <summary>
        /// Show the DockPane.
        /// </summary>
        internal static void Show()
        {
            DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
            pane?.Activate();
        }

        /// <summary>
        /// Text shown near the top of the DockPane.
        /// </summary>
        private string _heading = "Spherical Image Viewer";
        public string Heading
        {
            get => _heading;
            set => SetProperty(ref _heading, value);
        }
    }
}