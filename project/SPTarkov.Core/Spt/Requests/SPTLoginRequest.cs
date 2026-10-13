using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace SPTarkov.Core.SPT.Requests;

public record SPTLoginRequest
{
    [Required]
    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("launcherToken")]
    public string? LauncherToken { get; set; }

    // Legacy SPT wire compatibility only; game profiles have no password.
    [JsonPropertyName("password")]
    public string Password { get; set; } = "";
}
