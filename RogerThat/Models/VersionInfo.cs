namespace RogerThat.Models
{
    public static class VersionInfo
    {
        public const string Version = "1.0.0-rc2";
        public const string ReleaseDate = "2025-03-06";
        
        public static string GetVersionString()
        {
            return $"v{Version}";
        }
    }
} 