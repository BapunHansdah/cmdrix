using System.IO;
using System.Text.Json;
using cmdrix.Models;

namespace cmdrix.Services
{
    public class ConfigService
    {
        private readonly string _configFilePath;

        public ConfigService()
        {
            var appDataPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "cmdrix"
            );
            Directory.CreateDirectory(appDataPath);
            _configFilePath = Path.Combine(appDataPath, "config.json");
        }

        public AppConfig LoadConfig()
        {
            try
            {
                if (File.Exists(_configFilePath))
                {
                    var json = File.ReadAllText(_configFilePath);
                    var configDict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json);
                    
                    var config = new AppConfig();
                    
                    if (configDict != null)
                    {
                        if (configDict.TryGetValue("geminiApiKey", out var apiKey))
                        {
                            config.GeminiApiKey = apiKey.GetString() ?? string.Empty;
                        }
                        
                        if (configDict.TryGetValue("backgroundOpacity", out var opacity))
                        {
                            config.BackgroundOpacity = (byte)opacity.GetInt32();
                        }
                        
                        if (configDict.TryGetValue("currentDirectory", out var dir))
                        {
                            config.CurrentDirectory = dir.GetString() ?? Environment.CurrentDirectory;
                        }
                    }
                    
                    return config;
                }
                else
                {
                    // Create default config
                    var defaultConfig = new AppConfig
                    {
                        GeminiApiKey = "YOUR_API_KEY_HERE",
                        BackgroundOpacity = 204,
                        BackgroundColor = System.Drawing.Color.Black,
                        CurrentDirectory = Environment.CurrentDirectory
                    };
                    SaveConfig(defaultConfig);
                    return defaultConfig;
                }
            }
            catch (Exception)
            {
                return new AppConfig();
            }
        }

        public void SaveConfig(AppConfig config)
        {
            try
            {
                var configDict = new Dictionary<string, object>
                {
                    { "geminiApiKey", config.GeminiApiKey },
                    { "backgroundOpacity", config.BackgroundOpacity },
                    { "currentDirectory", config.CurrentDirectory }
                };

                var json = JsonSerializer.Serialize(configDict, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                File.WriteAllText(_configFilePath, json);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to save config: {ex.Message}");
            }
        }

        public void UpdateApiKey(string apiKey)
        {
            var config = LoadConfig();
            config.GeminiApiKey = apiKey;
            SaveConfig(config);
        }

        public void UpdateBackgroundOpacity(byte opacity)
        {
            var config = LoadConfig();
            config.BackgroundOpacity = opacity;
            SaveConfig(config);
        }

        public void UpdateCurrentDirectory(string directory)
        {
            var config = LoadConfig();
            config.CurrentDirectory = directory;
            SaveConfig(config);
        }
    }
}