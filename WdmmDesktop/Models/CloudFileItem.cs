using Newtonsoft.Json;

namespace WdmmDesktop.Models
{
    /// <summary>
    /// Directory/file entry returned by Core <c>wdmm_account_list_dir</c> /
    /// <c>wdmm_driver_list</c> / get_entry FFI (snake_case JSON).
    /// </summary>
    public sealed class CloudFileItem
    {
        [JsonProperty("name")]
        public string? Name { get; set; }

        [JsonProperty("path")]
        public string? Path { get; set; }

        [JsonProperty("is_dir")]
        public bool IsDir { get; set; }

        [JsonProperty("size")]
        public ulong Size { get; set; }

        [JsonProperty("modified")]
        public string? Modified { get; set; }

        [JsonProperty("raw_url")]
        public string? RawUrl { get; set; }

        [JsonProperty("delivery")]
        public string? Delivery { get; set; }

        [JsonProperty("local_path")]
        public string? LocalPath { get; set; }
    }
}
