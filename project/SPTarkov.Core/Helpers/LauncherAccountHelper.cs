using System.Text.Json;
using System.Text.Json.Serialization;
using SPTarkov.Core.SPT;
using SPTarkov.Core.SPT.Responses;

namespace SPTarkov.Core.Helpers;

public sealed class LauncherAccountRequest
{
    [JsonPropertyName("username")] public string? Username { get; set; }
    [JsonPropertyName("password")] public string? Password { get; set; }
    [JsonPropertyName("token")] public string? Token { get; set; }
}

public sealed class LauncherAccountResponse
{
    public bool Response { get; set; }
    public string? Token { get; set; }
    public string? Username { get; set; }
    public string? EditionAllowed { get; set; }
    public List<string> SavedProfiles { get; set; } = [];
}

/// <summary>Launcher-lifetime session; local file is a convenience marker, never authentication authority.</summary>
public sealed class LauncherAccountHelper(HttpHelper http, StateHelper state)
{
    private static readonly string TempPath = Path.Combine(Path.GetTempPath(), "SPTarkov", "launcherTemp.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var result = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountRegister,
            new LauncherAccountRequest { Username = username, Password = password }, ct);
        return result?.Response == true;
    }

    public async Task<bool> LoginAsync(string username, string password, CancellationToken ct = default)
    {
        var result = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountLogin,
            new LauncherAccountRequest { Username = username, Password = password }, ct);
        if (result?.Response != true || string.IsNullOrWhiteSpace(result.Token)) return false;
        state.LauncherToken = result.Token;
        state.LauncherUsername = result.Username;
        state.LauncherEditionAllowed = result.EditionAllowed;
        Directory.CreateDirectory(Path.GetDirectoryName(TempPath)!);
        File.WriteAllText(TempPath, JsonSerializer.Serialize(new { isLoggedIn = true }, JsonOptions));
        return true;
    }

    public async Task<bool> RefreshProfilesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state.LauncherToken)) return false;
        var response = await http.GameServerPut<SPTProfilesResponse>(Urls.Profiles,
            new LauncherAccountRequest { Token = state.LauncherToken }, ct);
        if (response?.Response is null) return false;
        state.ProfileList = response.Response;
        return true;
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var token = state.LauncherToken;
        state.LauncherToken = null;
        state.LauncherUsername = null;
        state.LauncherEditionAllowed = null;
        if (File.Exists(TempPath)) File.Delete(TempPath);
        if (!string.IsNullOrWhiteSpace(token))
            await http.GameServerPut<LauncherAccountResponse>(Urls.AccountLogout,
                new LauncherAccountRequest { Token = token }, ct);
    }
}
