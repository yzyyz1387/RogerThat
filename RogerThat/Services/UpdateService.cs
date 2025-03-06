using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.IO;
using System.IO.Compression;
using System.Net.Http.Handlers;
using System.Text;
using System.Diagnostics;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using System.Collections.Generic;

namespace RogerThat.Services
{
    public class SemVersion : IComparable<SemVersion>
    {
        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }
        public string PreRelease { get; }
        public int PreReleaseNumber { get; }

        private static readonly Regex VersionRegex = new Regex(
            @"^(\d+)\.(\d+)\.(\d+)(?:-?(beta|alpha|rc)(\d+))?$",
            RegexOptions.IgnoreCase
        );

        public SemVersion(string version)
        {
            var match = VersionRegex.Match(version.Trim());
            if (!match.Success)
            {
                throw new ArgumentException($"Invalid version format: {version}", nameof(version));
            }

            Major = int.Parse(match.Groups[1].Value);
            Minor = int.Parse(match.Groups[2].Value);
            Patch = int.Parse(match.Groups[3].Value);
            
            if (match.Groups[4].Success)
            {
                PreRelease = match.Groups[4].Value.ToLower();
                PreReleaseNumber = match.Groups[5].Success ? int.Parse(match.Groups[5].Value) : 1;
            }
        }

        public int CompareTo(SemVersion? other)
        {
            if (other == null) return 1;

            // 比较主版本号
            var majorComparison = Major.CompareTo(other.Major);
            if (majorComparison != 0) return majorComparison;

            // 比较次版本号
            var minorComparison = Minor.CompareTo(other.Minor);
            if (minorComparison != 0) return minorComparison;

            // 比较修订号
            var patchComparison = Patch.CompareTo(other.Patch);
            if (patchComparison != 0) return patchComparison;

            // 如果一个是预发布版本而另一个不是
            if (string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
                return 1;
            if (!string.IsNullOrEmpty(PreRelease) && string.IsNullOrEmpty(other.PreRelease))
                return -1;

            // 如果都是预发布版本
            if (!string.IsNullOrEmpty(PreRelease) && !string.IsNullOrEmpty(other.PreRelease))
            {
                // 比较预发布类型 (alpha < beta < rc)
                var preReleaseComparison = ComparePreReleaseType(PreRelease, other.PreRelease);
                if (preReleaseComparison != 0) return preReleaseComparison;

                // 比较预发布版本号
                return PreReleaseNumber.CompareTo(other.PreReleaseNumber);
            }

            return 0;
        }

        private int ComparePreReleaseType(string type1, string type2)
        {
            var getWeight = (string type) => type switch
            {
                "alpha" => 0,
                "beta" => 1,
                "rc" => 2,
                _ => 1 // 默认当作beta
            };

            return getWeight(type1).CompareTo(getWeight(type2));
        }

        public override string ToString()
        {
            return string.IsNullOrEmpty(PreRelease)
                ? $"{Major}.{Minor}.{Patch}"
                : $"{Major}.{Minor}.{Patch}-{PreRelease}{PreReleaseNumber}";
        }
    }

    public class UpdateInfo
    {
        public string Version { get; set; } = string.Empty;
        public string ReleaseDate { get; set; } = string.Empty;
        public string DownloadUrl { get; set; } = string.Empty;  // 保持兼容性
        public Dictionary<string, string>? DownloadUrls { get; set; } 
        public string[] Changelog { get; set; } = Array.Empty<string>();
        public string Sha256 { get; set; } = string.Empty;
    }

    public class UpdateService
    {
        private readonly HttpClient _httpClient;
        private const string UPDATE_API_URL = "https://rt.yzyyz.top/latest.json";
        
        // 定义日志事件
        public event Action<string, LogLevel>? LogMessage;

        private readonly SettingsService _settingsService;

        public enum LogLevel
        {
            Info,
            Warning,
            Error
        }

        public UpdateService()
        {
            // 注册编码提供程序
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(10); // 设置超时时间
            _settingsService = new SettingsService();
        }

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            LogMessage?.Invoke(message, level);
        }

        public async Task<UpdateInfo> CheckForUpdates()
        {
            try
            {
                var response = await _httpClient.GetStringAsync(UPDATE_API_URL);
                Log($"已获取到服务器响应");

                var options = new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    AllowTrailingCommas = true
                };

                var updateInfo = JsonSerializer.Deserialize<UpdateInfo>(response, options);
                if (updateInfo == null)
                {
                    throw new Exception("无法解析更新信息");
                }

                if (string.IsNullOrWhiteSpace(updateInfo.Version))
                {
                    throw new Exception("服务器返回的版本号为空");
                }

                Log($"解析到的版本号: {updateInfo.Version}");
                return updateInfo;
            }
            catch (HttpRequestException ex)
            {
                var error = $"无法连接到更新服务器: {ex.Message}";
                Log(error, LogLevel.Error);
                throw new Exception(error);
            }
            catch (TaskCanceledException)
            {
                var error = "连接更新服务器超时";
                Log(error, LogLevel.Error);
                throw new Exception(error);
            }
            catch (JsonException ex)
            {
                var error = $"解析服务器响应失败: {ex.Message}";
                Log(error, LogLevel.Error);
                throw new Exception(error);
            }
            catch (Exception ex)
            {
                var error = $"检查更新时发生错误: {ex.Message}";
                Log(error, LogLevel.Error);
                throw new Exception(error);
            }
        }

        public bool IsNewVersionAvailable(string currentVersion, string latestVersion)
        {
            try
            {
                Log($"正在比较版本 - 当前版本: {currentVersion}, 最新版本: {latestVersion}");
                var current = new SemVersion(currentVersion);
                var latest = new SemVersion(latestVersion);
                
                var result = latest.CompareTo(current) > 0;
                Log($"版本比较结果: {(result ? "有新版本" : "已是最新")}");
                return result;
            }
            catch (Exception ex)
            {
                Log($"版本比较失败: {ex.Message}", LogLevel.Error);
                return false;
            }
        }

        public async Task<bool> VerifyDownloadedFile(string filePath, string expectedHash)
        {
            try
            {
                using var sha256 = System.Security.Cryptography.SHA256.Create();
                using var stream = File.OpenRead(filePath);
                var hash = await Task.Run(() => sha256.ComputeHash(stream));
                var actualHash = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
                
                var isValid = string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase);
                if (!isValid)
                {
                    Log($"文件校验失败 - 预期: {expectedHash}, 实际: {actualHash}", LogLevel.Error);
                }
                return isValid;
            }
            catch (Exception ex)
            {
                Log($"计算文件哈希值时出错: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        public async Task<string> GetDownloadUrl(UpdateInfo updateInfo, string? preferredSource = null)
        {
            try
            {
                if (updateInfo.DownloadUrls != null)
                {
                    if (!string.IsNullOrEmpty(preferredSource) && 
                        updateInfo.DownloadUrls.TryGetValue(preferredSource, out var preferredUrl))
                    {
                        Log($"使用指定的{preferredSource}下载源");
                        return preferredUrl;
                    }

                    // 默认使用 GitHub
                    if (updateInfo.DownloadUrls.TryGetValue("github", out var githubUrl))
                    {
                        try
                        {
                            using var response = await _httpClient.GetAsync(githubUrl, HttpCompletionOption.ResponseHeadersRead);
                            if (response.IsSuccessStatusCode)
                            {
                                Log("使用 GitHub 下载源");
                                return githubUrl;
                            }
                        }
                        catch
                        {
                            // GitHub 访问失败，继续使用 Gitee
                        }
                    }

                    // 如果 GitHub 失败或不可用，使用 Gitee
                    if (updateInfo.DownloadUrls.TryGetValue("gitee", out var giteeUrl))
                    {
                        Log("使用 Gitee 下载源");
                        return giteeUrl;
                    }
                }

                // 如果新格式不可用，使用旧格式
                Log("使用默认下载源");
                return updateInfo.DownloadUrl;
            }
            catch (Exception ex)
            {
                Log($"获取下载链接失败: {ex.Message}", LogLevel.Error);
                return updateInfo.DownloadUrl;  // 出错时返回原始链接
            }
        }

        public async Task DownloadAndVerifyUpdate(UpdateInfo updateInfo, string downloadPath, IProgress<double>? progress = null, string? preferredSource = null)
        {
            try
            {
                // 获取实际下载链接
                var downloadUrl = await GetDownloadUrl(updateInfo, preferredSource);
                Log($"开始下载更新文件: {downloadUrl}", LogLevel.Info);
                
                using var handler = new ProgressMessageHandler(new HttpClientHandler());
                using var client = new HttpClient(handler);
                
                handler.HttpReceiveProgress += (_, e) => 
                {
                    if (e.TotalBytes.HasValue)
                    {
                        var progressValue = (double)e.BytesTransferred / e.TotalBytes.Value;
                        progress?.Report(progressValue);
                    }
                };

                using var response = await client.GetAsync(downloadUrl);
                response.EnsureSuccessStatusCode();
                
                using var fileStream = File.Create(downloadPath);
                await response.Content.CopyToAsync(fileStream);
                fileStream.Close();

                // 验证文件
                Log("正在验证文件完整性...", LogLevel.Info);
                if (await VerifyDownloadedFile(downloadPath, updateInfo.Sha256))
                {
                    Log("文件验证成功", LogLevel.Info);
                }
                else
                {
                    File.Delete(downloadPath);
                    throw new Exception("下载文件校验失败，可能已损坏");
                }
            }
            catch (Exception ex)
            {
                if (File.Exists(downloadPath))
                {
                    File.Delete(downloadPath);
                }
                Log($"下载更新失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        public async Task<string> ExtractAndUpdate(string zipPath, string targetDir)
        {
            try
            {
                Log("准备更新...", LogLevel.Info);
                
                // 创建更新目录
                var updateDir = Path.Combine(Path.GetTempPath(), "RogerThatUpdate_" + DateTime.Now.ToString("yyyyMMddHHmmss"));
                Directory.CreateDirectory(updateDir);
                Log($"创建更新目录: {updateDir}", LogLevel.Info);

                // 使用 utf-8 编码解压文件
                Log("解压更新文件...", LogLevel.Info);
                using (var fileStream = new FileStream(zipPath, FileMode.Open))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Read, false, Encoding.GetEncoding("utf-8")))
                {
                    foreach (var entry in archive.Entries)
                    {
                        try 
                        {
                            // 获取完整路径
                            var destinationPath = Path.Combine(updateDir, entry.FullName);
                            
                            // 确保目录存在
                            var directoryPath = Path.GetDirectoryName(destinationPath);
                            if (!string.IsNullOrEmpty(directoryPath))
                            {
                                Directory.CreateDirectory(directoryPath);
                            }

                            // 如果不是目录则解压文件
                            if (!string.IsNullOrEmpty(entry.Name))
                            {
                                entry.ExtractToFile(destinationPath, true);
                                Log($"解压文件: {entry.FullName}", LogLevel.Info);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"解压文件 {entry.FullName} 失败: {ex.Message}", LogLevel.Error);
                            throw;
                        }
                    }
                }
                Log($"更新文件已解压到: {updateDir}", LogLevel.Info);

                // 创建更新批处理脚本
                var scriptPath = Path.Combine(Path.GetTempPath(), "RogerThatUpdate.bat");
                var logPath = Path.Combine(Path.GetTempPath(), "RogerThat_update.log");
                var exePath = Process.GetCurrentProcess().MainModule!.FileName;
                
                // 更新脚本
                var script = new StringBuilder();
                script.AppendLine("@echo off");
                script.AppendLine("chcp 65001 > nul");

                // 使用 PowerShell 写入日志
                script.AppendLine($"powershell -Command \"[System.IO.File]::WriteAllText('{logPath}', '[%date% %time%] 开始更新 RogerThat...' + [Environment]::NewLine, [System.Text.Encoding]::UTF8)\"");

                // 等待原程序退出
                script.AppendLine("timeout /t 2 /nobreak > nul");

                // 先尝试删除旧文件
                script.AppendLine($"powershell -Command \"Add-Content -Path '{logPath}' -Value '[%date% %time%] 删除旧文件...' -Encoding UTF8\"");
                script.AppendLine($"del /F /S /Q \"{targetDir}\\*\" > nul 2>&1");

                // 复制新文件
                script.AppendLine($"powershell -Command \"Add-Content -Path '{logPath}' -Value '[%date% %time%] 复制新文件...' -Encoding UTF8\"");
                script.AppendLine($"xcopy /Y /E /I \"{updateDir}\\*\" \"{targetDir}\" > nul 2>&1");

                // 检查复制结果
                script.AppendLine("if errorlevel 1 (");
                script.AppendLine($"    powershell -Command \"Add-Content -Path '{logPath}' -Value '[%date% %time%] 复制文件失败' -Encoding UTF8\"");
                script.AppendLine("    exit /b 1");
                script.AppendLine(")");

                // 清理临时文件
                script.AppendLine($"powershell -Command \"Add-Content -Path '{logPath}' -Value '[%date% %time%] 清理临时文件...' -Encoding UTF8\"");
                script.AppendLine($"rmdir /S /Q \"{updateDir}\" > nul 2>&1");
                script.AppendLine($"del /F /Q \"{zipPath}\" > nul 2>&1");

                // 启动
                script.AppendLine($"powershell -Command \"Add-Content -Path '{logPath}' -Value '[%date% %time%] 启动更新后的程序...' -Encoding UTF8\"");
                script.AppendLine($"start \"\" \"{exePath}\"");

                // 删除脚本
                script.AppendLine("ping 127.0.0.1 -n 2 > nul");
                script.AppendLine("del \"%~f0\"");
                
                // 保存脚本 - 使用 UTF-8 编码（带 BOM）
                await File.WriteAllTextAsync(scriptPath, script.ToString(), new UTF8Encoding(true));
                Log($"更新脚本已创建: {scriptPath}", LogLevel.Info);
                Log($"更新日志将保存到: {logPath}", LogLevel.Info);

                return scriptPath;
            }
            catch (Exception ex)
            {
                Log($"准备更新失败: {ex.Message}", LogLevel.Error);
                throw;
            }
        }

        public bool ShouldRemindUpdate(string version)
        {
            var ignoredVersion = _settingsService.IgnoredVersion;
            return version != ignoredVersion;
        }

        public void IgnoreVersion(string version)
        {
            _settingsService.IgnoredVersion = version;
        }

        private async Task DownloadUpdateAsync(string url, string filePath)
        {
            try
            {
                using var client = new HttpClient
                {
                    
                    Timeout = Timeout.InfiniteTimeSpan
                };

                // 使用流式下载，避免内存问题
                using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);

                var buffer = new byte[8192];
                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                var bytesRead = 0L;

                while (true)
                {
                    var read = await contentStream.ReadAsync(buffer);
                    if (read == 0)
                        break;

                    await fileStream.WriteAsync(buffer.AsMemory(0, read));
                    bytesRead += read;

                    // 更新进度
                    if (totalBytes > 0)
                    {
                        var progress = (double)bytesRead / totalBytes;
                        // 后期这里可以添加进度报告
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"下载更新失败: {ex.Message}", ex);
            }
        }
    }
} 