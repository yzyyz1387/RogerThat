using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RogerThat.Models;

namespace RogerThat.Services
{
    public class ImportedSoundService
    {
        private readonly string _soundsPath;
        private List<ImportedSoundInfo> _importedSounds;

        public ImportedSoundService()
        {
            _soundsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "RogerThat",
                "imported_sounds.json"
            );
            
            LoadImportedSounds();
        }

        public IEnumerable<ImportedSoundInfo> GetAllSounds()
        {
            return _importedSounds;
        }

        public void AddSound(ImportedSoundInfo sound)
        {
            _importedSounds.Add(sound);
            SaveSounds();
        }

        public void UpdateSound(ImportedSoundInfo sound)
        {
            var existing = _importedSounds.FirstOrDefault(s => s.FilePath == sound.FilePath);
            if (existing != null)
            {
                existing.IsPrefix = sound.IsPrefix;
                existing.IsSuffix = sound.IsSuffix;
                SaveSounds();
            }
        }

        public void RemoveSound(string filePath)
        {
            _importedSounds.RemoveAll(s => s.FilePath == filePath);
            SaveSounds();
        }

        private void LoadImportedSounds()
        {
            try
            {
                if (File.Exists(_soundsPath))
                {
                    string json = File.ReadAllText(_soundsPath);
                    _importedSounds = JsonSerializer.Deserialize<List<ImportedSoundInfo>>(json) ?? new List<ImportedSoundInfo>();
                    
                    // 检查文件是否存在
                    _importedSounds = _importedSounds.Where(s => File.Exists(s.FilePath)).ToList();
                }
                else
                {
                    _importedSounds = new List<ImportedSoundInfo>();
                }
            }
            catch (Exception)
            {
                _importedSounds = new List<ImportedSoundInfo>();
            }
        }

        private void SaveSounds()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_soundsPath)!);
                string json = JsonSerializer.Serialize(_importedSounds, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_soundsPath, json);
            }
            catch (Exception)
            {
                // 保存失败处理
            }
        }
    }
}