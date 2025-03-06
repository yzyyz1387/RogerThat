using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using RogerThat.Models;

namespace RogerThat.Services;

public class SettingsService
{
    private const string StorageConfigFileName = "storage_cfg.json";
    private static string StorageConfigFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RogerThat",
        StorageConfigFileName
    );

    private StorageConfig _storageConfig;

    // 添加主题色配置到 StorageConfig 类中 -> 主题色配置
    public class StorageConfig
    {
        public string AudioStoragePath { get; set; }
        public List<ImportedSoundConfig> ImportedSounds { get; set; } = new();
        public string ThemeColor { get; set; } = "#3898fc";  // 默认蓝
        public string ThemeDarkColor { get; set; } = "#276ab0";
        public string IgnoredVersion { get; set; } = ""; 

        public StorageConfig()
        {
            // 默认存储路径 -> 存储路径
            AudioStoragePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "RogerThat",
                "Sounds"
            );
            ImportedSounds = new List<ImportedSoundConfig>();
        }
    }

    // 用于存储导入音频信息的类 -> 音频配置
    public class ImportedSoundConfig
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsPrefix { get; set; }
        public bool IsSuffix { get; set; }
    }

    // 属性访问器 -> 主题属性
    public string ThemeColor 
    { 
        get => _storageConfig.ThemeColor;
        set
        {
            _storageConfig.ThemeColor = value;
            SaveStorageConfig();
        }
    }

    public string ThemeDarkColor
    {
        get => _storageConfig.ThemeDarkColor;
        set
        {
            _storageConfig.ThemeDarkColor = value;
            SaveStorageConfig();
        }
    }

    public string IgnoredVersion
    {
        get => _storageConfig.IgnoredVersion;
        set
        {
            _storageConfig.IgnoredVersion = value;
            SaveStorageConfig();
        }
    }

    public SettingsService()
    {
        _storageConfig = LoadStorageConfig();
        EnsureStoragePathExists();
    }

    public string AudioStoragePath => _storageConfig.AudioStoragePath;

    public void UpdateAudioStoragePath(string newPath)
    {
        try
        {
            if (string.IsNullOrEmpty(newPath))
            {
                throw new ArgumentException("存储路径不能为空");
            }

            if (_storageConfig.AudioStoragePath != newPath)
            {
                _storageConfig.AudioStoragePath = newPath;
                SaveStorageConfig();
                EnsureStoragePathExists();
                System.Diagnostics.Debug.WriteLine($"已更新存储路径: {newPath}");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"更新存储路径失败: {ex.Message}");
            throw;
        }
    }

    private StorageConfig LoadStorageConfig()
    {
        try
        {
            if (File.Exists(StorageConfigFilePath))
            {
                string json = File.ReadAllText(StorageConfigFilePath);
                var config = JsonSerializer.Deserialize<StorageConfig>(json);
                if (config != null && !string.IsNullOrEmpty(config.AudioStoragePath))
                {
                    return config;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"加载存储配置失败: {ex.Message}");
        }
        return new StorageConfig();
    }

    private void SaveStorageConfig()
    {
        try
        {
            string directoryPath = Path.GetDirectoryName(StorageConfigFilePath)!;
            Directory.CreateDirectory(directoryPath);

            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
            };

            string json = JsonSerializer.Serialize(_storageConfig, options);
            File.WriteAllText(StorageConfigFilePath, json);
            
            System.Diagnostics.Debug.WriteLine("存储配置已保存");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"保存存储配置失败: {ex.Message}");
            throw;
        }
    }

    private void EnsureStoragePathExists()
    {
        try
        {
            if (!Directory.Exists(_storageConfig.AudioStoragePath))
            {
                Directory.CreateDirectory(_storageConfig.AudioStoragePath);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"创建存储目录失败: {ex.Message}");
            // 如果创建目录失败，重置为默认路径 -> 重置为默认路径
            _storageConfig.AudioStoragePath = new StorageConfig().AudioStoragePath;
            SaveStorageConfig();
            Directory.CreateDirectory(_storageConfig.AudioStoragePath);
        }
    }

    public void SaveImportedSounds(IEnumerable<ImportedSound> sounds)
    {
        _storageConfig.ImportedSounds = sounds.Select(s => new ImportedSoundConfig
        {
            FilePath = s.FilePath,
            IsPrefix = s.IsPrefix,
            IsSuffix = s.IsSuffix
        }).ToList();
        SaveStorageConfig();
    }

    public List<ImportedSoundConfig> GetImportedSounds()
    {
        return _storageConfig.ImportedSounds;
    }
} 