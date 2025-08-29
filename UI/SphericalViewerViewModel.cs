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
using ArcGIS.Desktop.Framework.Threading.Tasks;
using Test.Models;
using Test.Services;
using Test.ViewModels;
using RelayCommand = Test.ViewModels.RelayCommand;
using AsyncRelayCommand = Test.ViewModels.AsyncRelayCommand;
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
        private readonly HorusMediaService _horusService;

        private ImageFrame _currentFrame;
        private List<ImageFrame> _imageFrames = new List<ImageFrame>();
        private int _currentFrameIndex = 0;
        private bool _isSettingsOpen = false;

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
                    QueuedTask.Run(async () => await UpdateImageViewAsync());
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
                    QueuedTask.Run(async () => await UpdateImageViewAsync());
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
                    QueuedTask.Run(async () => await UpdateImageViewAsync());
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
                    QueuedTask.Run(async () => await UpdateImageViewAsync());
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

        public ObservableCollection<string> AvailableModels { get; private set; }

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

        private ObservableCollection<DetectionResult> _detectionResults;
        public ObservableCollection<DetectionResult> DetectionResults
        {
            get => _detectionResults;
            private set => SetProperty(ref _detectionResults, value);
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

        private string _heading = "Spherical Image Viewer";
        public string Heading
        {
            get => _heading;
            set => SetProperty(ref _heading, value);
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

        #region Constructor and Initialization
        public SphericalViewerViewModel()
        {
            try
            {
                // Initialize collections first
                AvailableModels = new ObservableCollection<string>
                {
                    "GroundingLangSAM",
                    "GroundingDino",
                    "YoloWorld",
                    "SAM_V2",
                    "Florence2"
                };

                DetectionResults = new ObservableCollection<DetectionResult>();

                // Initialize services - don't dispose these until module unload
                _apiService = new PythonApiService();
                _settingsService = new SettingsService();
                _horusService = new HorusMediaService();

                // Load settings before initializing commands
                LoadSettings();
                InitializeCommands();

                StatusMessage = "Spherical Image Viewer initialized successfully";

                // Auto-connect in background to avoid blocking UI
                if (AutoConnect)
                {
                    Task.Run(async () =>
                    {
                        try
                        {
                            await ConnectToServerAsync();
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                StatusMessage = $"Auto-connect failed: {ex.Message}";
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Initialization error: {ex.Message}";
                Debug.WriteLine($"SphericalViewerViewModel initialization failed: {ex}");
            }
        }

        protected override Task InitializeAsync()
        {
            try
            {
                StatusMessage = "Dock pane initialized and ready";
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"InitializeAsync error: {ex}");
                return Task.FromException(ex);
            }
        }

        private void InitializeCommands()
        {
            try
            {
                // Use AsyncRelayCommand for async operations
                ConnectCommand = new AsyncRelayCommand(ConnectToServerAsync, () => !IsLoading);
                LoadImagesCommand = new AsyncRelayCommand(LoadImagesAsync, () => IsConnected && !IsLoading);
                RunDetectionCommand = new AsyncRelayCommand(RunDetectionAsync, () => HasImage && !IsLoading);
                TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync, () => !IsLoading);
                TestDatabaseConnectionCommand = new AsyncRelayCommand(TestDatabaseConnectionAsync, () => !IsLoading);
                ConnectHorusCommand = new AsyncRelayCommand(ConnectToHorusAsync, () => !IsLoading && !IsHorusConnected);
                DisconnectHorusCommand = new AsyncRelayCommand(DisconnectFromHorusAsync, () => IsHorusConnected);
                LoadHorusRecordingsCommand = new AsyncRelayCommand(LoadHorusRecordingsAsync, () => IsHorusConnected && !IsLoading);
                LoadHorusImagesCommand = new AsyncRelayCommand(LoadHorusImagesAsync, () => IsHorusConnected && SelectedRecording != null && !IsLoading);
                StartPythonBridgeCommand = new AsyncRelayCommand(StartPythonBridgeAsync, () => !IsLoading);
                TestHorusConnectionCommand = new AsyncRelayCommand(TestHorusConnectionAsync, () => !IsLoading);

                // Synchronous commands with proper error handling
                DisconnectCommand = new RelayCommand(() => SafeExecute(DisconnectFromServer), () => IsConnected);
                RefreshCommand = new RelayCommand(() => SafeExecute(RefreshView), () => HasImage);
                ResetViewCommand = new RelayCommand(() => SafeExecute(ResetView), () => HasImage);
                PreviousFrameCommand = new RelayCommand(() => SafeExecute(PreviousFrame), () => CanNavigateFrames && (_currentFrameIndex > 0 || _currentHorusImageIndex > 0));
                NextFrameCommand = new RelayCommand(() => SafeExecute(NextFrame), () => CanNavigateFrames && ((_usingHorusImages && _currentHorusImageIndex < _horusImages.Count - 1) || (!_usingHorusImages && _currentFrameIndex < _imageFrames.Count - 1)));
                FirstFrameCommand = new RelayCommand(() => SafeExecute(FirstFrame), () => CanNavigateFrames && (_currentFrameIndex > 0 || _currentHorusImageIndex > 0));
                LastFrameCommand = new RelayCommand(() => SafeExecute(LastFrame), () => CanNavigateFrames && ((_usingHorusImages && _currentHorusImageIndex < _horusImages.Count - 1) || (!_usingHorusImages && _currentFrameIndex < _imageFrames.Count - 1)));

                OpenSettingsCommand = new RelayCommand(() => SafeExecute(OpenSettings), () => true);
                CloseSettingsCommand = new RelayCommand(() => SafeExecute(CloseSettings), () => true);
                SaveSettingsCommand = new RelayCommand(() => SafeExecute(SaveSettings), () => true);
                ResetSettingsCommand = new RelayCommand(() => SafeExecute(ResetSettings), () => true);
                BrowseDirectoryCommand = new RelayCommand(() => SafeExecute(BrowseDirectory), () => true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Command initialization failed: {ex}");
                StatusMessage = $"Command initialization error: {ex.Message}";
            }
        }

        private void SafeExecute(Action action)
        {
            try
            {
                action?.Invoke();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Command execution error: {ex}");
                StatusMessage = $"Operation failed: {ex.Message}";
            }
        }
        #endregion

        #region Command Methods
        private void OpenSettings()
        {
            try
            {
                if (!Application.Current.Dispatcher.CheckAccess())
                {
                    Application.Current.Dispatcher.Invoke(() => OpenSettings());
                    return;
                }

                if (_settingsWindow != null && _settingsWindow.IsLoaded)
                {
                    _settingsWindow.Activate();
                    _settingsWindow.Focus();
                    return;
                }

                var settingsView = new SettingsView();

                _settingsWindow = new Window
                {
                    Title = "Spherical Image Viewer - Settings",
                    Content = settingsView,
                    Width = 650,
                    Height = 750,
                    WindowStartupLocation = WindowStartupLocation.CenterScreen,
                    ResizeMode = ResizeMode.CanResize,
                    ShowInTaskbar = false,
                    WindowStyle = WindowStyle.ToolWindow
                };

                settingsView.DataContext = this;

                _settingsWindow.Closed += (s, e) =>
                {
                    try
                    {
                        _settingsWindow = null;
                        IsSettingsOpen = false;
                        StatusMessage = "Settings window closed";
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Settings window cleanup error: {ex}");
                    }
                };

                IsSettingsOpen = true;
                _settingsWindow.Show();
                StatusMessage = "Settings window opened";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Failed to open settings: {ex.Message}";
                Debug.WriteLine($"Settings window error: {ex}");
                IsSettingsOpen = false;
                _settingsWindow = null;
            }
        }

        private void CloseSettings()
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    _settingsWindow?.Close();
                }
                else
                {
                    Application.Current.Dispatcher.Invoke(() => _settingsWindow?.Close());
                }

                IsSettingsOpen = false;
                StatusMessage = "Settings window closed";
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error closing settings: {ex.Message}";
                Debug.WriteLine($"Close settings error: {ex}");
            }
        }

        private async Task StartPythonBridgeAsync()
        {
            try
            {
                UpdateStatusMessage("Starting Python bridge server...");
                IsLoading = true;

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

                string addinPath = AppDomain.CurrentDomain.BaseDirectory;
                string scriptPath = Path.Combine(addinPath, "Scripts", "horus_bridge_server.py");

                if (!File.Exists(scriptPath))
                {
                    scriptPath = Path.Combine(Environment.CurrentDirectory, "Scripts", "horus_bridge_server.py");
                }

                if (!File.Exists(scriptPath))
                {
                    var assemblyPath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var assemblyDir = Path.GetDirectoryName(assemblyPath);
                    scriptPath = Path.Combine(assemblyDir, "Scripts", "horus_bridge_server.py");
                }

                if (!File.Exists(scriptPath))
                {
                    UpdateStatusMessage($"Python script not found. Expected location: {scriptPath}");
                    return;
                }

                string pythonExe = @"C:\Program Files\ArcGIS\Pro\bin\Python\envs\arcgispro-py3\python.exe";

                if (!File.Exists(pythonExe))
                {
                    UpdateStatusMessage("ArcGIS Pro Python not found. Please check installation.");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = pythonExe,
                    Arguments = $"\"{scriptPath}\" --config \"{configPath}\" --port 5001 --host localhost",
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    RedirectStandardOutput = false,
                    RedirectStandardError = false,
                    WorkingDirectory = Path.GetDirectoryName(scriptPath)
                };

                UpdateStatusMessage("Launching Python bridge server...");
                var process = Process.Start(startInfo);

                if (process == null)
                {
                    UpdateStatusMessage("Failed to start Python bridge process");
                    return;
                }

                UpdateStatusMessage("Waiting for Python bridge to initialize...");

                // Give the server more time to start up properly
                await Task.Delay(8000);  // Increased from 5000ms to 8000ms

                UpdateStatusMessage("Testing bridge connection...");

                // Create a fresh HorusMediaService instance for testing to avoid disposal issues
                using (var testService = new HorusMediaService())
                {
                    var healthCheck = await testService.CheckHealthAsync();

                    if (healthCheck.Success)
                    {
                        UpdateStatusMessage($"Python bridge server started successfully! Status: {healthCheck.Message}");

                        // Update the bridge URL for the main service
                        _horusService.UpdateBridgeUrl("http://localhost:5001");

                        // Only auto-connect if we have database credentials
                        if (!string.IsNullOrWhiteSpace(DatabaseHost) && !string.IsNullOrWhiteSpace(DatabaseUser))
                        {
                            UpdateStatusMessage("Auto-connecting to Horus services...");
                            await Task.Delay(2000); // Give bridge a moment to settle
                            await ConnectToHorusAsync();
                        }
                        else
                        {
                            UpdateStatusMessage("Bridge ready. Configure database settings and click 'Connect Horus' to connect.");
                        }
                    }
                    else
                    {
                        UpdateStatusMessage($"Python bridge health check failed: {healthCheck.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Failed to start Python bridge: {ex.Message}");
                Debug.WriteLine($"Python bridge error: {ex}");
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
                UpdateStatusMessage("Testing Horus connection...");

                // Use the dedicated tester to avoid disposal issues
                var (success, message, details) = await BridgeConnectionTester.TestBridgeHealthAsync("http://localhost:5001");

                if (success)
                {
                    UpdateStatusMessage($"Bridge test successful: {details}");

                    // Parse the details to get connection status
                    IsHorusConnected = details.Contains("Horus: Connected");
                }
                else
                {
                    UpdateStatusMessage($"Bridge test failed: {message} - {details}");
                    IsHorusConnected = false;
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Horus connection test failed: {ex.Message}");
                IsHorusConnected = false;
                Debug.WriteLine($"Horus connection test error: {ex}");
            }
        }

        private async Task ConnectToHorusAsync()
        {
            try
            {
                IsLoading = true;
                UpdateStatusMessage("Connecting to Horus media server...");

                // Create connection config
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

                // Validate required fields first
                if (string.IsNullOrWhiteSpace(config.DatabaseHost) ||
                    string.IsNullOrWhiteSpace(config.DatabaseUser))
                {
                    UpdateStatusMessage("Please configure database credentials in settings first");
                    IsHorusConnected = false;
                    return;
                }

                // Use a fresh service instance to avoid disposal issues
                using (var connectionService = new HorusMediaService())
                {
                    connectionService.UpdateBridgeUrl("http://localhost:5001");
                    var result = await connectionService.ConnectAsync(config);

                    if (result.Success && result.Data)
                    {
                        IsHorusConnected = true;
                        UpdateStatusMessage("Connected to Horus media server successfully");

                        // Update main service bridge URL
                        _horusService.UpdateBridgeUrl("http://localhost:5001");

                        // Load recordings after successful connection
                        await LoadHorusRecordingsAsync();
                    }
                    else
                    {
                        IsHorusConnected = false;
                        UpdateStatusMessage($"Failed to connect to Horus: {result.Error}");

                        // Provide specific guidance based on the error
                        if (result.Error.Contains("database") || result.Error.Contains("Database"))
                        {
                            UpdateStatusMessage("Database connection failed. Check credentials and network connectivity.");
                        }
                        else if (result.Error.Contains("horus") || result.Error.Contains("Horus"))
                        {
                            UpdateStatusMessage("Horus server connection failed. Check if server is accessible.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                IsHorusConnected = false;
                UpdateStatusMessage($"Horus connection error: {ex.Message}");
                Debug.WriteLine($"Horus connection error: {ex}");
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
                UpdateStatusMessage("Disconnecting from Horus...");

                var result = await _horusService.DisconnectAsync();

                IsHorusConnected = false;
                HorusRecordings = new List<HorusRecording>();
                _horusImages.Clear();
                SelectedRecording = null;

                UpdateStatusMessage(result.Success ? "Disconnected from Horus successfully" : $"Disconnect error: {result.Error}");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Disconnect error: {ex.Message}");
                Debug.WriteLine($"Horus disconnect error: {ex}");
            }
        }

        private async Task LoadHorusRecordingsAsync()
        {
            if (!IsHorusConnected)
            {
                UpdateStatusMessage("Not connected to Horus server");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage("Loading Horus recordings...");

                // Use a fresh service instance to avoid disposal issues
                using (var recordingsService = new HorusMediaService())
                {
                    recordingsService.UpdateBridgeUrl("http://localhost:5001");
                    var response = await recordingsService.GetRecordingsAsync();

                    if (response.Success && response.Data != null)
                    {
                        HorusRecordings = new List<HorusRecording>(response.Data);
                        UpdateStatusMessage($"Loaded {HorusRecordings.Count} recordings from Horus");

                        if (HorusRecordings.Count > 0)
                        {
                            SelectedRecording = HorusRecordings.FirstOrDefault(r =>
                                r.Endpoint.Contains(RecordingEndpoint) ||
                                r.Endpoint.Contains("Rotterdam360")) ?? HorusRecordings[0];
                        }
                    }
                    else
                    {
                        UpdateStatusMessage($"Failed to load recordings: {response.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error loading recordings: {ex.Message}");
                Debug.WriteLine($"Load recordings error: {ex}");
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
                UpdateStatusMessage("Please connect to Horus and select a recording first");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage("Loading images from Horus...");

                var request = new HorusImageRequest
                {
                    RecordingEndpoint = SelectedRecording.Endpoint,
                    Count = DefaultNumberOfImages,
                    Width = DefaultImageWidth,
                    Height = DefaultImageHeight
                };

                // Use a fresh service instance to avoid disposal issues
                using (var imageService = new HorusMediaService())
                {
                    imageService.UpdateBridgeUrl("http://localhost:5001");
                    var response = await imageService.GetImagesAsync(request);

                    if (response.Success && response.Data != null)
                    {
                        _horusImages = response.Data;
                        _currentHorusImageIndex = 0;
                        _usingHorusImages = true;

                        if (_horusImages.Count > 0)
                        {
                            await DisplayHorusImageAsync(0);
                            CanNavigateFrames = _horusImages.Count > 1;
                            UpdateHorusFrameInfo();
                        }

                        UpdateStatusMessage($"Loaded {_horusImages.Count} images from Horus");
                    }
                    else
                    {
                        UpdateStatusMessage($"Failed to load images: {response.Error}");
                    }
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error loading images: {ex.Message}");
                Debug.WriteLine($"Load Horus images error: {ex}");
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
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CurrentImage = CreateBitmapFromBytes(imageBytes);
                        _currentHorusImageIndex = imageIndex;
                    });
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error displaying Horus image: {ex.Message}");
                Debug.WriteLine($"Display Horus image error: {ex}");
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
                UpdateStatusMessage("Connecting to server...");
                _apiService.UpdateBaseUrl(ServerUrl);

                var response = await _apiService.GetAsync<ServerInfo>("health");
                if (response.Success)
                {
                    IsConnected = true;
                    UpdateStatusMessage("Connected to server successfully");
                }
                else
                {
                    UpdateStatusMessage($"Connection failed: {response.Error}");
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Connection error: {ex.Message}");
                Debug.WriteLine($"Server connection error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task TestConnectionAsync()
        {
            UpdateStatusMessage("Testing connection...");
            await ConnectToServerAsync();
        }

        private void DisconnectFromServer()
        {
            try
            {
                IsConnected = false;
                UpdateStatusMessage("Disconnected from server");
                _imageFrames.Clear();

                Application.Current.Dispatcher.Invoke(() =>
                {
                    CurrentImage = null;
                });

                CanNavigateFrames = false;
                FrameInfo = "Frame: 0/0";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Disconnect error: {ex}");
                UpdateStatusMessage($"Disconnect error: {ex.Message}");
            }
        }

        private void RefreshView()
        {
            try
            {
                if (_currentFrame != null)
                {
                    Task.Run(async () => await UpdateImageViewAsync());
                    UpdateStatusMessage("View refreshed");
                }
                else
                {
                    UpdateStatusMessage("No image to refresh");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Refresh view error: {ex}");
                UpdateStatusMessage($"Refresh error: {ex.Message}");
            }
        }

        private async Task LoadImagesAsync()
        {
            if (!IsConnected)
            {
                UpdateStatusMessage("Please connect to server first");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage("Loading image frames...");
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
                        UpdateStatusMessage($"Loaded {_imageFrames.Count} image frames");
                    }
                    else
                    {
                        UpdateStatusMessage("No images found in specified directory");
                    }
                }
                else
                {
                    UpdateStatusMessage($"Failed to load images: {response.Error}");
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

        private async Task RunDetectionAsync()
        {
            if ((_currentFrame == null && !_usingHorusImages) || string.IsNullOrWhiteSpace(DetectionText))
            {
                UpdateStatusMessage("Please load an image and specify detection target");
                return;
            }

            try
            {
                IsLoading = true;
                UpdateStatusMessage("Running AI object detection...");

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
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        DetectionResults.Clear();
                        foreach (var result in response.Data)
                        {
                            DetectionResults.Add(result);
                        }
                    });

                    UpdateStatusMessage($"Detection completed - found {response.Data.Count} objects");
                }
                else
                {
                    UpdateStatusMessage($"Detection failed: {response.Error}");
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

        private void ResetView()
        {
            try
            {
                Yaw = DefaultYaw;
                Pitch = DefaultPitch;
                Roll = DefaultRoll;
                Fov = DefaultFov;
                UpdateStatusMessage("View reset to defaults");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Reset view error: {ex}");
                UpdateStatusMessage($"Reset error: {ex.Message}");
            }
        }

        private void PreviousFrame()
        {
            try
            {
                if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex > 0)
                {
                    Task.Run(async () => await DisplayHorusImageAsync(_currentHorusImageIndex - 1));
                    UpdateHorusFrameInfo();
                    InvalidateCommands();
                }
                else if (_currentFrameIndex > 0)
                {
                    _currentFrameIndex--;
                    Task.Run(async () => await LoadCurrentFrameAsync());
                    UpdateFrameInfo();
                    InvalidateCommands();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Previous frame error: {ex}");
                UpdateStatusMessage($"Navigation error: {ex.Message}");
            }
        }

        private void NextFrame()
        {
            try
            {
                if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex < _horusImages.Count - 1)
                {
                    Task.Run(async () => await DisplayHorusImageAsync(_currentHorusImageIndex + 1));
                    UpdateHorusFrameInfo();
                    InvalidateCommands();
                }
                else if (_currentFrameIndex < _imageFrames.Count - 1)
                {
                    _currentFrameIndex++;
                    Task.Run(async () => await LoadCurrentFrameAsync());
                    UpdateFrameInfo();
                    InvalidateCommands();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Next frame error: {ex}");
                UpdateStatusMessage($"Navigation error: {ex.Message}");
            }
        }

        private void FirstFrame()
        {
            try
            {
                if (_usingHorusImages && _horusImages.Count > 0)
                {
                    Task.Run(async () => await DisplayHorusImageAsync(0));
                    UpdateHorusFrameInfo();
                    InvalidateCommands();
                }
                else if (_imageFrames.Count > 0)
                {
                    _currentFrameIndex = 0;
                    Task.Run(async () => await LoadCurrentFrameAsync());
                    UpdateFrameInfo();
                    InvalidateCommands();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"First frame error: {ex}");
                UpdateStatusMessage($"Navigation error: {ex.Message}");
            }
        }

        private void LastFrame()
        {
            try
            {
                if (_usingHorusImages && _horusImages.Count > 0)
                {
                    Task.Run(async () => await DisplayHorusImageAsync(_horusImages.Count - 1));
                    UpdateHorusFrameInfo();
                    InvalidateCommands();
                }
                else if (_imageFrames.Count > 0)
                {
                    _currentFrameIndex = _imageFrames.Count - 1;
                    Task.Run(async () => await LoadCurrentFrameAsync());
                    UpdateFrameInfo();
                    InvalidateCommands();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Last frame error: {ex}");
                UpdateStatusMessage($"Navigation error: {ex.Message}");
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
                UpdateStatusMessage("Settings saved successfully");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error saving settings: {ex.Message}");
                Debug.WriteLine($"Save settings error: {ex}");
            }
        }

        private void ResetSettings()
        {
            try
            {
                var defaultSettings = new UserSettings();
                LoadSettingsFromObject(defaultSettings);
                UpdateStatusMessage("Settings reset to defaults");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error resetting settings: {ex.Message}");
                Debug.WriteLine($"Reset settings error: {ex}");
            }
        }

        private async Task TestDatabaseConnectionAsync()
        {
            try
            {
                UpdateStatusMessage("Testing database connection...");

                var config = new HorusConnectionConfig
                {
                    DatabaseHost = DatabaseHost,
                    DatabasePort = DatabasePort,
                    DatabaseName = DatabaseName,
                    DatabaseUser = DatabaseUser,
                    DatabasePassword = DatabasePassword
                };

                if (string.IsNullOrWhiteSpace(DatabaseHost) || string.IsNullOrWhiteSpace(DatabaseUser))
                {
                    UpdateStatusMessage("Please provide database host and username");
                    return;
                }

                await Task.Delay(500);

                IsDatabaseConnected = !string.IsNullOrWhiteSpace(DatabaseHost) &&
                                    !string.IsNullOrWhiteSpace(DatabaseUser);

                UpdateStatusMessage(IsDatabaseConnected ?
                    "Database connection parameters validated" :
                    "Database connection validation failed");
            }
            catch (Exception ex)
            {
                IsDatabaseConnected = false;
                UpdateStatusMessage($"Database connection test failed: {ex.Message}");
                Debug.WriteLine($"Database test error: {ex}");
            }
        }

        private void BrowseDirectory()
        {
            try
            {
                UpdateStatusMessage("Directory browser - please enter path manually for now");
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Directory browser error: {ex.Message}");
                Debug.WriteLine($"Browse directory error: {ex}");
            }
        }
        #endregion

        #region Helper Methods
        private async Task LoadCurrentFrameAsync()
        {
            try
            {
                if (_currentFrameIndex < 0 || _currentFrameIndex >= _imageFrames.Count)
                    return;

                _currentFrame = _imageFrames[_currentFrameIndex];
                await UpdateImageViewAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load current frame error: {ex}");
                UpdateStatusMessage($"Frame load error: {ex.Message}");
            }
        }

        private async Task UpdateImageViewAsync()
        {
            try
            {
                if (_usingHorusImages && _horusImages.Count > 0 && _currentHorusImageIndex < _horusImages.Count)
                {
                    var horusImage = _horusImages[_currentHorusImageIndex];
                    var imageBytes = horusImage.GetImageBytes();

                    if (imageBytes != null)
                    {
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            CurrentImage = CreateBitmapFromBytes(imageBytes);
                        });
                    }
                    return;
                }

                if (_currentFrame == null) return;

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
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        CurrentImage = CreateBitmapFromBytes(response.Data);
                    });
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Error updating image: {ex.Message}");
                Debug.WriteLine($"Update image view error: {ex}");
            }
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
                Debug.WriteLine($"Create bitmap error: {ex}");
                return null;
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
                if (settings != null)
                {
                    LoadSettingsFromObject(settings);
                }
                else
                {
                    Debug.WriteLine("Settings service returned null, using defaults");
                    LoadSettingsFromObject(new UserSettings());
                }
            }
            catch (Exception ex)
            {
                UpdateStatusMessage($"Settings load error: {ex.Message}");
                Debug.WriteLine($"Load settings error: {ex}");
                LoadSettingsFromObject(new UserSettings());
            }
        }

        private void LoadSettingsFromObject(UserSettings settings)
        {
            try
            {
                if (settings == null)
                {
                    Debug.WriteLine("LoadSettingsFromObject received null settings");
                    return;
                }

                ServerUrl = settings.ServerUrl ?? "http://192.168.6.100:5050";
                DefaultImageDirectory = settings.DefaultImageDirectory ?? "/web/images/";
                ImageDirectory = settings.DefaultImageDirectory ?? "/web/images/";
                DefaultModel = settings.DefaultModel ?? "GroundingLangSAM";
                SelectedModel = settings.DefaultModel ?? "GroundingLangSAM";
                DefaultConfidenceThreshold = settings.DefaultConfidenceThreshold;
                DefaultIoUThreshold = settings.DefaultIoUThreshold;

                if (settings.CameraDefaults != null)
                {
                    DefaultYaw = settings.CameraDefaults.Yaw;
                    DefaultPitch = settings.CameraDefaults.Pitch;
                    DefaultRoll = settings.CameraDefaults.Roll;
                    DefaultFov = settings.CameraDefaults.Fov;
                }

                if (settings.UISettings != null)
                {
                    AutoConnect = settings.UISettings.AutoConnect;
                }

                Yaw = DefaultYaw;
                Pitch = DefaultPitch;
                Roll = DefaultRoll;
                Fov = DefaultFov;

                DetectionText = DefaultDetectionTarget;

                DatabaseHost = settings.DatabaseHost ?? "";
                DatabasePort = settings.DatabasePort ?? "5432";
                DatabaseName = settings.DatabaseName ?? "HorusWebMoviePlayer";
                DatabaseUser = settings.DatabaseUser ?? "";
                DatabasePassword = settings.DatabasePassword ?? "";

                RecordingEndpoint = settings.RecordingEndpoint ?? "Rotterdam360\\\\Ladybug5plus";
                HorusClientUrl = settings.HorusClientUrl ?? "http://10.0.10.100:5050/web/";
                DefaultNumberOfImages = settings.DefaultNumberOfImages;
                DefaultImageWidth = settings.DefaultImageWidth;
                DefaultImageHeight = settings.DefaultImageHeight;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Load settings from object error: {ex}");
                UpdateStatusMessage($"Settings load error: {ex.Message}");
            }
        }

        private void UpdateStatusMessage(string message)
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    StatusMessage = message;
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() => StatusMessage = message));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Update status message error: {ex}");
            }
        }

        private void InvalidateCommands()
        {
            try
            {
                if (Application.Current.Dispatcher.CheckAccess())
                {
                    CommandManager.InvalidateRequerySuggested();
                }
                else
                {
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                        CommandManager.InvalidateRequerySuggested()));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Invalidate commands error: {ex}");
            }
        }
        #endregion

        #region INotifyPropertyChanged
        public new event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PropertyChanged error for {propertyName}: {ex}");
            }
        }

        protected new bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            try
            {
                if (EqualityComparer<T>.Default.Equals(backingStore, value))
                    return false;

                backingStore = value;
                OnPropertyChanged(propertyName);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"SetProperty error for {propertyName}: {ex}");
                return false;
            }
        }
        #endregion

        #region Static Methods and Cleanup
        /// <summary>
        /// Show the DockPane.
        /// </summary>
        internal static void Show()
        {
            try
            {
                DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
                pane?.Activate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Show dock pane error: {ex}");
            }
        }

        /// <summary>
        /// Cleanup resources when the dock pane is closed
        /// </summary>
        protected override void OnHidden()
        {
            try
            {
                CloseSettings();
                _apiService?.Dispose();
                _horusService?.Dispose();
                base.OnHidden();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"OnHidden cleanup error: {ex}");
            }
        }
        #endregion
    }
}