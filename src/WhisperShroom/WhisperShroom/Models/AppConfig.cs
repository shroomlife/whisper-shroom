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

    [JsonPropertyName("auto_copy")]
    public bool AutoCopyEnabled { get; set; } = true;

    [JsonPropertyName("notifications")]
    public bool NotificationsEnabled { get; set; } = true;

    [JsonPropertyName("language")]
    public string? Language { get; set; } = "de";

    [JsonPropertyName("model")]
    public string? Model { get; set; } = "whisper-1";

    [JsonPropertyName("prompt_prefix")]
    public string? PromptPrefix { get; set; }

    [JsonPropertyName("prompt_suffix")]
    public string? PromptSuffix { get; set; }
}
