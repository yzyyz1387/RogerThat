using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Management;
using System.Security.Cryptography;
using System.Web;

namespace RogerThat.Services
{
    public class LeanCloudService
    {
        private readonly string _appId;
        private readonly string _appKey;
        private readonly string _serverUrl;
        private readonly HttpClient _client;
        private readonly HttpClient _ipClient;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public LeanCloudService()
        {
            // 用户统计（LeanCloud）
            // TODO: 如果您是二次开发，请替换为您的 AppID 和 AppKey
            _appId = "bb6CS4sly5CICxX5aDIhxHU8-gzGzoHsz";
            _appKey = "Xx3gT52rfYCDC6c2vNopPhZG"; 
            _serverUrl = "https://rtc.yzyyz.top";
            
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("X-LC-Id", _appId);
            _client.DefaultRequestHeaders.Add("X-LC-Key", $"{_appKey},master");

            _ipClient = new HttpClient();
        }

        private class LCDate
        {
            public string __type { get; init; } = "Date";
            public string iso { get; init; }

            public LCDate(DateTime dateTime)
            {
                iso = dateTime.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
            }

            [JsonConstructor]
            public LCDate(string __type, string iso)
            {
                this.__type = __type;
                this.iso = iso;
            }
        }

        private class IpInfo
        {
            public string query { get; set; }  // ip-api.com 返回的 IP 在 query 字段中
            public string country { get; set; }
            public string region { get; set; }
            public string city { get; set; }
        }

        private async Task<IpInfo> GetIpInfoAsync()
        {
            try
            {
                var response = await _ipClient.GetStringAsync("http://ip-api.com/json/?fields=status,message,country,region,city,query&lang=zh-CN");
                var ipInfo = JsonSerializer.Deserialize<IpInfo>(response, _jsonOptions);
                return ipInfo;
            }
            catch (Exception ex)
            {
                return new IpInfo { query = "unknown" };
            }
        }

        public async Task RecordUserAsync()
        {
            try
            {
                var machineId = GetMachineUniqueId();
                var ipInfo = await GetIpInfoAsync();

                // 构建查询参数
                var whereData = new { machineId };
                var whereJson = JsonSerializer.Serialize(whereData);
                var encodedWhere = HttpUtility.UrlEncode(whereJson);
                var query = $"/1.1/classes/Users?where={encodedWhere}";
                
                // 查询现有记录
                var response = await _client.GetAsync(_serverUrl + query);
                var result = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Query failed with status {response.StatusCode}. Response: {result}");
                }

                var queryResult = JsonSerializer.Deserialize<QueryResult>(result, _jsonOptions);

                // 准备更新数据
                if (queryResult?.Results?.Length > 0)
                {
                    // 更新现有记录
                    var objectId = queryResult.Results[0].objectId;
                    // 合并现有数据和新数据
                    var updateData = new
                    {
                        machineId,
                        lastVisit = new LCDate(DateTime.UtcNow),
                        version = Models.VersionInfo.Version,
                        ip = ipInfo.query,
                        country = ipInfo.country,
                        region = ipInfo.region,
                        city = ipInfo.city
                    };

                    var updateResponse = await _client.PutAsync(
                        $"{_serverUrl}/1.1/classes/Users/{objectId}",
                        new StringContent(JsonSerializer.Serialize(updateData), Encoding.UTF8, "application/json")
                    );
                    var updateResult = await updateResponse.Content.ReadAsStringAsync();
                    if (!updateResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            $"Update failed with status {updateResponse.StatusCode}. Response: {updateResult}");
                    }
                }
                else
                {
                    // 创建新记录
                    var data = new
                    {
                        machineId,
                        lastVisit = new LCDate(DateTime.UtcNow),
                        version = Models.VersionInfo.Version,
                        ip = ipInfo.query,
                        country = ipInfo.country,
                        region = ipInfo.region,
                        city = ipInfo.city
                    };
                    var json = JsonSerializer.Serialize(data);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var createResponse = await _client.PostAsync($"{_serverUrl}/1.1/classes/Users", content);
                    var createResult = await createResponse.Content.ReadAsStringAsync();
                    if (!createResponse.IsSuccessStatusCode)
                    {
                        throw new HttpRequestException(
                            $"Create failed with status {createResponse.StatusCode}. Response: {createResult}");
                    }
                }
            }
            catch (Exception ex)
            {
                var error = $"统计失败: {ex.Message}";
                if (ex is JsonException jsonEx)
                {
                    error += $"\nJSON Error: {jsonEx.Path}";
                }
                error += $"\n{ex.StackTrace}";
                throw;
            }
        }

        private string GetMachineUniqueId()
        {
            var ids = new[]
            {
                GetWMIValue("Win32_Processor", "ProcessorId"),
                GetWMIValue("Win32_BIOS", "SerialNumber"),
                GetWMIValue("Win32_DiskDrive", "Signature")
            };

            var combined = string.Join(":", ids);
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(combined));
            return BitConverter.ToString(hash).Replace("-", "");
        }

        private string GetWMIValue(string className, string propertyName)
        {
            try
            {
                using var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {className}");
                foreach (ManagementObject obj in searcher.Get())
                {
                    var value = obj[propertyName]?.ToString();
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch
            {
                // 忽略错误
            }
            return string.Empty;
        }

        private class QueryResult
        {
            public Result[] Results { get; set; }
        }

        private class Result
        {
            public string objectId { get; set; }
            public string machineId { get; set; }
            public string version { get; set; }
            public JsonElement lastVisit { get; set; }
            public string createdAt { get; set; }
            public string updatedAt { get; set; }
        }
    }
} 