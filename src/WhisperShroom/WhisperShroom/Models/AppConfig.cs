using System.Text.Json.Serialization;

namespace WhisperShroom.Models;

public sealed class AppConfig
{
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; set; }

    [JsonPropertyName("hotkey")]
    public string Hotkey { get; set; } = "ctrl+shift+e";

    [JsonPropertyName("device_name")]
    public string? DeviceName { get; set; }
}
