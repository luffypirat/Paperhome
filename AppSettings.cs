using System;
using System.IO;
using System.Text.Json;

namespace Paperhome
{
    public class AppSettings
    {
        public string OllamaUrl   { get; set; } = "http://localhost:11434";
        public string LocalModel  { get; set; } = "qwen2.5:0.5b";
        public string PasswordSalt { get; set; } = "";
        public string PasswordHash { get; set; } = "";
        public string AboutText    { get; set; } = "";

        private static readonly string SettingsPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ArchiveData", "settings.json");

        public static AppSettings Load()
        {
            try
            {
                if (File.Exists(SettingsPath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath))
                           ?? new AppSettings();
            }
            catch { }
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
                File.WriteAllText(SettingsPath,
                    JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
