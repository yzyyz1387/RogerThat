using System.IO;
using System.Text.Json;

namespace RogerThat
{
    public class Settings
    {
        public bool PrefixEnabled { get; set; } = true;
        public bool SuffixEnabled { get; set; } = true;
        public string PrefixSoundPath { get; set; } = "";
        public string SuffixSoundPath { get; set; } = "";
        public string SelectedHotkey { get; set; } = "K";
        public int SelectedAudioDevice { get; set; } = 0;

        private static string SettingsPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RogerThat",
            "settings.json"
        );

        public static Settings Load()
        {
            try
            {
                string settingsDir = Path.GetDirectoryName(SettingsPath);
                if (!Directory.Exists(settingsDir))
                {
                    Directory.CreateDirectory(settingsDir);
                }

                if (File.Exists(SettingsPath))
                {
                    string json = File.ReadAllText(SettingsPath);
                    return JsonSerializer.Deserialize<Settings>(json) ?? new Settings();
                }
            }
            catch (Exception)
            {
                // 如果加载失败，返回默认设置
            }
            return new Settings();
        }

        public void Save()
        {
            try
            {
                string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch (Exception)
            {
                // 保存失败时的处理
            }
        }
    }
} 