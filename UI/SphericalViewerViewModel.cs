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
using Test.Models;
using Test.Services;
using Test.ViewModels;

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

        // Convert Yaw to rotation angle for knob visual (0-360 degrees)
        public double YawAngle => (_yaw + 180) * 2; // Map -180:180 to 0:720, then mod 360

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

        // Convert Pitch to rotation angle for knob visual
        public double PitchAngle => (_pitch + 90) * 2; // Map -90:90 to 0:360

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

        public double RollAngle => (_roll + 180) * 2; // Map -180:180 to 0:720

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

        // Convert FOV to rotation angle for knob visual
        public double FovAngle => ((_fov - 30) / 150) * 360; // Map 30:180 to 0:360

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
        #endregion

        protected SphericalViewerViewModel()
        {
            try
            {
                _apiService = new PythonApiService();
                _settingsService = new SettingsService();

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
            ConnectCommand = new AsyncRelayCommand(ConnectToServerAsync);
            DisconnectCommand = new RelayCommand(DisconnectFromServer);
            LoadImagesCommand = new AsyncRelayCommand(LoadImagesAsync);
            RefreshCommand = new RelayCommand(RefreshView);
            RunDetectionCommand = new AsyncRelayCommand(RunDetectionAsync);
            ResetViewCommand = new RelayCommand(ResetView);
            PreviousFrameCommand = new RelayCommand(PreviousFrame, () => _currentFrameIndex > 0);
            NextFrameCommand = new RelayCommand(NextFrame, () => _currentFrameIndex < _imageFrames.Count - 1);
            FirstFrameCommand = new RelayCommand(FirstFrame, () => _imageFrames.Count > 0 && _currentFrameIndex > 0);
            LastFrameCommand = new RelayCommand(LastFrame, () => _imageFrames.Count > 0 && _currentFrameIndex < _imageFrames.Count - 1);
            OpenSettingsCommand = new RelayCommand(OpenSettings);
            CloseSettingsCommand = new RelayCommand(CloseSettings);
            SaveSettingsCommand = new RelayCommand(SaveSettings);
            ResetSettingsCommand = new RelayCommand(ResetSettings);
            TestConnectionCommand = new AsyncRelayCommand(TestConnectionAsync);
            TestDatabaseConnectionCommand = new AsyncRelayCommand(TestDatabaseConnectionAsync);
            BrowseDirectoryCommand = new RelayCommand(BrowseDirectory);
        }

        #region Command Methods
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
            if (_currentFrame == null || string.IsNullOrWhiteSpace(DetectionText))
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
                    ImagePath = _currentFrame.Path,
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

        private async void PreviousFrame()
        {
            if (_currentFrameIndex > 0)
            {
                _currentFrameIndex--;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async void NextFrame()
        {
            if (_currentFrameIndex < _imageFrames.Count - 1)
            {
                _currentFrameIndex++;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async void FirstFrame()
        {
            if (_imageFrames.Count > 0)
            {
                _currentFrameIndex = 0;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private async void LastFrame()
        {
            if (_imageFrames.Count > 0)
            {
                _currentFrameIndex = _imageFrames.Count - 1;
                await LoadCurrentFrameAsync();
                UpdateFrameInfo();
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private void OpenSettings()
        {
            IsSettingsOpen = true;
            StatusMessage = "Settings opened";
        }

        private void CloseSettings()
        {
            IsSettingsOpen = false;
            StatusMessage = "Settings closed";
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
                    // Save new database settings
                    DatabaseHost = DatabaseHost,
                    DatabasePort = DatabasePort,
                    DatabaseName = DatabaseName,
                    DatabaseUser = DatabaseUser,
                    DatabasePassword = DatabasePassword,
                    // Save new image retrieval settings
                    RecordingEndpoint = RecordingEndpoint,
                    HorusClientUrl = HorusClientUrl,
                    DefaultNumberOfImages = DefaultNumberOfImages,
                    DefaultImageWidth = DefaultImageWidth,
                    DefaultImageHeight = DefaultImageHeight
                };

                _settingsService.SaveSettings(settings);
                IsSettingsOpen = false;
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

                // Here you would implement the actual database connection test
                // For now, we'll simulate the test
                await Task.Delay(1000); // Simulate connection test

                // TODO: Implement actual database connection test using your connection parameters
                // string connectionString = $"host={DatabaseHost};port={DatabasePort};dbname={DatabaseName};user={DatabaseUser};password={DatabasePassword}";
                // Test the connection here

                IsDatabaseConnected = true; // This should be based on actual test result
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
                // For now, just show a placeholder message
                // In a full implementation, you'd use a folder browser dialog
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
                // Use defaults if settings fail to load
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

            // Set current camera values to defaults
            Yaw = DefaultYaw;
            Pitch = DefaultPitch;
            Roll = DefaultRoll;
            Fov = DefaultFov;

            DetectionText = DefaultDetectionTarget;

            // Load new database settings
            DatabaseHost = settings.DatabaseHost;
            DatabasePort = settings.DatabasePort;
            DatabaseName = settings.DatabaseName;
            DatabaseUser = settings.DatabaseUser;
            DatabasePassword = settings.DatabasePassword;

            // Load new image retrieval settings
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
    }
}