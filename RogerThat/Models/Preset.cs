using System;

namespace RogerThat.Models
{
    public class Preset
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsBuiltin { get; set; }
        public bool PrefixEnabled { get; set; }
        public bool SuffixEnabled { get; set; }
        public string PrefixSoundPath { get; set; } = string.Empty;
        public string SuffixSoundPath { get; set; } = string.Empty;
        public string SelectedHotkey { get; set; } = "K";  // 默认热键
    }
} 