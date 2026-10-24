using System.Text.Json.Serialization;

namespace WdmmDesktop.Models
{
    public sealed class QueueTask
    {
        [JsonPropertyName("id")]           public string Id { get; set; } = "";
        [JsonPropertyName("kind")]         public string Kind { get; set; } = "";
        [JsonPropertyName("remote_path")]  public string RemotePath { get; set; } = "";
        [JsonPropertyName("total_bytes")]  public long TotalBytes { get; set; }
        [JsonPropertyName("bytes_done")]   public long BytesDone { get; set; }
        [JsonPropertyName("status")]       public string Status { get; set; } = "pending";
        [JsonPropertyName("error")]        public string? Error { get; set; }

        [JsonIgnore]
        public double ProgressPct => TotalBytes > 0
            ? (double)BytesDone / TotalBytes * 100.0
            : 0;

        [JsonIgnore]
        public string StatusDisplay => Status switch
        {
            "running" => $"Running ({ProgressPct:F0}%)",
            "done"    => "Done",
            "error"   => $"Error: {Error ?? "unknown"}",
            "pending" => "Pending",
            _         => Status.Length > 0
                            ? $"{char.ToUpperInvariant(Status[0])}{Status[1..]}"
                            : Status
        };

        [JsonIgnore]
        public bool CanCancel => Status is "pending" or "running";
    }
}
