using System;
using System.IO;
using Newtonsoft.Json;
using Test.Models;

namespace Test.Services
{
    public class SettingsService
    {
        private readonly string _settingsFilePath;
        private UserSettings _settings;

        public SettingsService()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var appFolder = Path.Combine(appDataPath, "SphericalImageViewer");
            Directory.CreateDirectory(appFolder);
            _settingsFilePath = Path.Combine(appFolder, "settings.json");
            LoadSettings();
        }

        public UserSettings GetSettings()
        {
            return _settings ?? new UserSettings();
        }

        public void SaveSettings(UserSettings settings)
        {
            try
            {
                _settings = settings;
                var json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    _settings = JsonConvert.DeserializeObject<UserSettings>(json);
                }
                else
                {
                    _settings = new UserSettings();
                    SaveSettings(_settings);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
                _settings = new UserSettings();
            }
        }
    }
}