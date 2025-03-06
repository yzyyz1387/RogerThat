using System;

namespace RogerThat.Models
{
    public class ImportedSoundInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsPrefix { get; set; }
        public bool IsSuffix { get; set; }
        public DateTime ImportDate { get; set; }
    }
}