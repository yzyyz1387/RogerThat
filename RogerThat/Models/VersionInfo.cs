namespace RogerThat.Models
{
    public static class VersionInfo
    {
        public const string Version = "1.0.1-beta2";
        public const string ReleaseDate = "2025-03-07";
        
        public static string GetVersionString()
        {
            return $"v{Version}";
        }
    }
} 