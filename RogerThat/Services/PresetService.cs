using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using RogerThat.Models;

namespace RogerThat.Services
{
    public class PresetService
    {
        private const string PresetsFileName = "presets.json";
        private const string LastSelectedPresetKey = "LastSelectedPreset";
        private static string PresetsFilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RogerThat",
            PresetsFileName
        );

        // 预设数据结构
        private class PresetData
        {
            public List<Preset> Presets { get; set; } = new();
            public string LastSelectedPreset { get; set; } = string.Empty;
        }

        private List<Preset> _builtinPresets;
        private List<Preset> _userPresets;
        private string _lastSelectedPreset = string.Empty;

        public PresetService()
        {
            _builtinPresets = CreateBuiltinPresets();
            _userPresets = LoadUserPresets();
            LoadPresets();
        }

        public string LastSelectedPreset => _lastSelectedPreset;

        private List<Preset> CreateBuiltinPresets()
        {
            // 默认音频路径
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string prefixPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]前置.wav");
            string prefixMinPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]前置_短.wav");
            string suffixPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]后置.wav");
            string suffixMinPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]后置_短.wav");
            string silentPrefixesPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]前置_普通噪声.wav");
            string silentSuffixPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]后置_普通噪声.wav");
            string kunMusicPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]坤坤_music.wav");
            string kunNgmPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]坤坤_你干嘛啊哎哟.wav");
            string kunChickenPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]坤坤_鸡.wav");
            string manBoWowPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]曼波_wow.mp3");
            string manBoPath = Path.Combine(baseDir, "Assets", "sounds", "[默认]曼波.mp3");



            return new List<Preset>
            {
                new Preset
                {
                    Name = "标准集群",
                    Description = "带集群接入+TPT前置和蛙叫信令后置",
                    IsBuiltin = true,
                    PrefixEnabled = true,
                    SuffixEnabled = true,
                    PrefixSoundPath = prefixPath,
                    SuffixSoundPath = suffixPath,
                    SelectedHotkey = "K"
                },
                new Preset
                {
                    Name = "简短",
                    Description = "不带接入集群时的Tone音，简短TPT前置和蛙叫后置",
                    IsBuiltin = true,
                    PrefixEnabled = true,
                    SuffixEnabled = true,
                    PrefixSoundPath = prefixMinPath,
                    SuffixSoundPath = suffixMinPath,
                    SelectedHotkey = "K"
                },
                new Preset
                {
                    Name = "纯净",
                    Description = "纯噪声前后置",
                    IsBuiltin = true,
                    PrefixEnabled = true,
                    SuffixEnabled = true,
                    PrefixSoundPath = silentPrefixesPath,
                    SuffixSoundPath = silentSuffixPath,
                    SelectedHotkey = "K"
                },
                new Preset
                {
                    Name = "坤坤",
                    Description = "小鸡子露出黑脚了吧",
                    IsBuiltin = true,
                    PrefixEnabled = true,
                    SuffixEnabled = true,
                    PrefixSoundPath = kunChickenPath,
                    SuffixSoundPath = kunNgmPath,
                    SelectedHotkey = "K"
                },
                new Preset
                {
                    Name = "无",
                    Description = "无音效",
                    IsBuiltin = true,
                    PrefixEnabled = false,
                    SuffixEnabled = false,
                    PrefixSoundPath = string.Empty,
                    SuffixSoundPath = string.Empty,
                },
                new Preset
                {
                    Name = "曼波",
                    Description = "曼波",
                    IsBuiltin = true,
                    PrefixEnabled = true,
                    SuffixEnabled = true,
                    PrefixSoundPath = manBoWowPath,
                    SuffixSoundPath = manBoPath,
                    SelectedHotkey = "K"
                }

            };
        }

        public IEnumerable<Preset> GetAllPresets()
        {
            return _builtinPresets.Concat(_userPresets);
        }

        public void SaveUserPreset(Preset preset)
        {
            preset.IsBuiltin = false;
            _userPresets.Add(preset);
            SavePresets();
        }

        public void DeleteUserPreset(Preset preset)
        {
            if (!preset.IsBuiltin)
            {
                _userPresets.Remove(preset);
                SavePresets();
            }
        }

        private List<Preset> LoadUserPresets()
        {
            try
            {
                if (File.Exists(PresetsFilePath))
                {
                    string json = File.ReadAllText(PresetsFilePath);
                    return JsonSerializer.Deserialize<List<Preset>>(json) ?? new List<Preset>();
                }
                else
                {
                    return new List<Preset>();
                }
            }
            catch (Exception)
            {
                return new List<Preset>();
            }
        }

        private void LoadPresets()
        {
            try
            {
                if (File.Exists(PresetsFilePath))
                {
                    string json = File.ReadAllText(PresetsFilePath);
                    var data = JsonSerializer.Deserialize<PresetData>(json);
                    if (data != null)
                    {
                        _userPresets = data.Presets ?? new List<Preset>();
                        _lastSelectedPreset = data.LastSelectedPreset ?? string.Empty;
                        return;
                    }
                }
                _userPresets = new List<Preset>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载预设失败: {ex.Message}");
                _userPresets = new List<Preset>();
            }
        }

        private void SavePresets()
        {
            try
            {
                string directoryPath = Path.GetDirectoryName(PresetsFilePath)!;
                Directory.CreateDirectory(directoryPath);

                var data = new PresetData
                {
                    Presets = _userPresets,
                    LastSelectedPreset = _lastSelectedPreset
                };

                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(data, options);
                File.WriteAllText(PresetsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"保存预设失败: {ex.Message}");
                throw;
            }
        }

        public void UpdateLastSelectedPreset(string presetName)
        {
            _lastSelectedPreset = presetName;
            SavePresets();
            System.Diagnostics.Debug.WriteLine($"已更新最后选择的预设: {presetName}");
        }
    }
} 