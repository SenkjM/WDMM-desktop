using System;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    /// <summary>
    /// A cloud/WebDAV source (product name: 源 / Source).
    /// Backed by Core <c>wdmm_account_*</c> FFI (credentials stored as UTF-8 JSON bytes in Core DB).
    /// </summary>
    public sealed class WebDavAccount
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("display_name")]
        public string DisplayNameField { get; set; } = "";

        [JsonPropertyName("base_url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("provider_type")]
        public string ProviderType { get; set; } = "webdav";

        [JsonIgnore]
        public string Username { get; set; } = "";

        [JsonIgnore]
        public string Password { get; set; } = "";

        [JsonIgnore]
        public string RemoteDir { get; set; } = "/";

        [JsonIgnore]
        public string Name
        {
            get => DisplayNameField;
            set => DisplayNameField = value;
        }

        [JsonIgnore]
        public string DisplayName =>
            string.IsNullOrWhiteSpace(DisplayNameField) ? (Url ?? "") : DisplayNameField;

        public static WebDavAccount? FromAccountJson(JsonElement el)
        {
            var a = new WebDavAccount
            {
                Id = el.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "",
                DisplayNameField = el.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? "" : "",
                Url = el.TryGetProperty("base_url", out var bu) ? bu.GetString() ?? "" : "",
                ProviderType = el.TryGetProperty("provider_type", out var pt) ? pt.GetString() ?? "webdav" : "webdav",
            };
            if (el.TryGetProperty("credentials_b64", out var b64El))
            {
                var b64 = b64El.GetString();
                if (!string.IsNullOrEmpty(b64))
                {
                    try
                    {
                        var bytes = Convert.FromBase64String(b64);
                        var json = Encoding.UTF8.GetString(bytes);
                        using var doc = JsonDocument.Parse(json);
                        var c = doc.RootElement;
                        if (c.TryGetProperty("username", out var u)) a.Username = u.GetString() ?? "";
                        if (c.TryGetProperty("password", out var p)) a.Password = p.GetString() ?? "";
                        if (c.TryGetProperty("remote_dir", out var r)) a.RemoteDir = string.IsNullOrWhiteSpace(r.GetString()) ? "/" : r.GetString()!;
                    }
                    catch { /* encrypted or non-JSON credentials — leave blank */ }
                }
            }
            return a;
        }

        /// <summary>
        /// Build upsert payload with credentials as nested JSON object (Phase 0.2 Wave C).
        /// Core will store credentials as UTF-8 JSON bytes (or encrypted if passphrase given).
        /// </summary>
        public string ToUpsertJson()
        {
            var payload = new
            {
                id = string.IsNullOrEmpty(Id) ? null : Id,
                provider_type = string.IsNullOrEmpty(ProviderType) ? "webdav" : ProviderType,
                display_name = DisplayNameField ?? "",
                base_url = Url ?? "",
                credentials = new
                {
                    username = Username ?? "",
                    password = Password ?? "",
                    remote_dir = string.IsNullOrWhiteSpace(RemoteDir) ? "/" : RemoteDir
                }
            };
            return JsonSerializer.Serialize(payload);
        }
    }

    public sealed class RemoteEntry
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsDir { get; set; }
        public ulong Size { get; set; }
        public string? Modified { get; set; }
        public string SizeText => IsDir ? "" : FormatSize(Size);
        public string Glyph => IsDir ? "\uE8B7" : "\uE8A5"; // Folder / Page

        static string FormatSize(ulong n)
        {
            if (n < 1024) return $"{n} B";
            if (n < 1024 * 1024) return $"{n / 1024.0:0.#} KB";
            if (n < 1024UL * 1024 * 1024) return $"{n / (1024.0 * 1024):0.#} MB";
            return $"{n / (1024.0 * 1024 * 1024):0.#} GB";
        }
    }
}
