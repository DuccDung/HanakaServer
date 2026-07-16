using System.Text.Json;
using mail_service.Internal;

namespace mail_service.service
{
    public sealed class AbenlaZaloOtpSender : IZaloOtpSender
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;

        public AbenlaZaloOtpSender(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;

            var timeoutSeconds = int.TryParse(_config["AbenlaOtp:TimeoutSeconds"], out var seconds)
                ? seconds
                : 15;

            if (timeoutSeconds < 1)
            {
                timeoutSeconds = 15;
            }

            _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        }

        public async Task<ZaloOtpSendResult> SendOtpAsync(string phoneNumber, string otp, CancellationToken ct = default)
        {
            var smsGuid = Guid.NewGuid().ToString();

            if (string.IsNullOrWhiteSpace(phoneNumber))
            {
                return new ZaloOtpSendResult
                {
                    Success = false,
                    SmsGuid = smsGuid,
                    Error = "Missing phone number."
                };
            }

            var baseUrl = _config["AbenlaOtp:BaseUrl"]?.Trim();
            if (string.IsNullOrWhiteSpace(baseUrl))
            {
                return new ZaloOtpSendResult
                {
                    Success = false,
                    SmsGuid = smsGuid,
                    Error = "AbenlaOtp:BaseUrl is missing."
                };
            }

            var requestUrl = BuildRequestUrl(baseUrl, new Dictionary<string, string?>
            {
                ["loginName"] = _config["AbenlaOtp:LoginName"],
                ["sign"] = _config["AbenlaOtp:Sign"],
                ["serviceTypeId"] = _config["AbenlaOtp:ServiceTypeId"],
                ["phoneNumber"] = phoneNumber.Trim(),
                ["message"] = otp,
                ["detectCode"] = _config["AbenlaOtp:DetectCode"] ?? "true",
                ["brandName"] = _config["AbenlaOtp:BrandName"] ?? "ZOTP",
                ["callBack"] = _config["AbenlaOtp:CallBack"] ?? "false",
                ["smsGuid"] = smsGuid
            });

            try
            {
                using var response = await _httpClient.GetAsync(requestUrl, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                if (!response.IsSuccessStatusCode)
                {
                    return new ZaloOtpSendResult
                    {
                        Success = false,
                        SmsGuid = smsGuid,
                        Error = $"HTTP {(int)response.StatusCode}: {raw}"
                    };
                }

                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                var smsPerMessage = ReadInt(root, "SmsPerMessage");
                var code = ReadInt(root, "Code");
                var message = ReadString(root, "Message");
                var success = smsPerMessage == 1
                              && code == 203
                              && string.Equals(message, "Success", StringComparison.Ordinal);

                return new ZaloOtpSendResult
                {
                    Success = success,
                    SmsGuid = smsGuid,
                    SmsPerMessage = smsPerMessage,
                    Code = code,
                    Message = message,
                    Error = success ? null : raw
                };
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new ZaloOtpSendResult
                {
                    Success = false,
                    SmsGuid = smsGuid,
                    Error = ex.Message
                };
            }
        }

        private static string BuildRequestUrl(string baseUrl, IReadOnlyDictionary<string, string?> query)
        {
            var pairs = query
                .Where(x => !string.IsNullOrWhiteSpace(x.Value))
                .Select(x => $"{Uri.EscapeDataString(x.Key)}={Uri.EscapeDataString(x.Value!)}");

            var separator = baseUrl.Contains('?', StringComparison.Ordinal) ? "&" : "?";
            return baseUrl + separator + string.Join("&", pairs);
        }

        private static int? ReadInt(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
            {
                return number;
            }

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
            {
                return number;
            }

            return null;
        }

        private static string? ReadString(JsonElement root, string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out var value))
            {
                return null;
            }

            return value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString();
        }
    }
}
