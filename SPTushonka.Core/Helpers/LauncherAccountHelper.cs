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

/// <summary>Per-server remembered login preferences and sessions.</summary>
public sealed class LauncherAccountHelper(HttpHelper http, StateHelper state)
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "IGSApex", "SPTLauncher", "remembered-logins.json");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private sealed class SavedLogin
    {
        public bool RememberLogin { get; set; }
        public string Username { get; set; } = "";
        public string Token { get; set; } = "";
    }

    private static Dictionary<string, SavedLogin> ReadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, SavedLogin>>(File.ReadAllText(SettingsPath))
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch (IOException) { return new(StringComparer.OrdinalIgnoreCase); }
        catch (UnauthorizedAccessException) { return new(StringComparer.OrdinalIgnoreCase); }
        catch (JsonException) { return new(StringComparer.OrdinalIgnoreCase); }
    }

    private static void WriteSettings(Dictionary<string, SavedLogin> settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
        var temp = SettingsPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions));
        File.Move(temp, SettingsPath, true);
    }

    public bool GetRememberLogin(string server)
    {
        return ReadSettings().TryGetValue(server, out var entry) && entry.RememberLogin;
    }

    public void SetRememberLogin(string server, bool remember)
    {
        if (string.IsNullOrWhiteSpace(server)) return;
        var settings = ReadSettings();
        if (!settings.TryGetValue(server, out var entry))
            settings[server] = entry = new SavedLogin();
        entry.RememberLogin = remember;
        if (!remember) entry.Token = "";
        WriteSettings(settings);
    }

    public async Task<bool> TryAutoLoginAsync(string? serverAddress = null, CancellationToken ct = default)
    {
        var server = serverAddress ?? state.SelectedServer?.IpAddress;
        if (string.IsNullOrWhiteSpace(server)) return false;
        if (!ReadSettings().TryGetValue(server, out var saved) ||
            !saved.RememberLogin || string.IsNullOrWhiteSpace(saved.Token)) return false;
        try
        {
            var session = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountSession,
                new LauncherAccountRequest { Token = saved.Token }, ct);
            if (session?.Response != true) return false;
            state.LauncherToken = session.Token ?? saved.Token;
            state.LauncherUsername = session.Username ?? saved.Username;
            state.LauncherEditionAllowed = session.EditionAllowed;
            if (!await RefreshProfilesAsync(ct)) return false;
            if (!string.IsNullOrWhiteSpace(session.Token) && session.Token != saved.Token)
            {
                saved.Token = session.Token;
                var settings = ReadSettings();
                settings[server] = saved;
                WriteSettings(settings);
            }
            return true;
        }
        catch (HttpRequestException) { return false; }
    }

    public async Task<bool> RegisterAsync(string username, string password, CancellationToken ct = default)
    {
        var result = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountRegister,
            new LauncherAccountRequest { Username = username, Password = password }, ct);
        return result?.Response == true;
    }

    public async Task<bool> LoginAsync(string username, string password, bool rememberLogin = false, CancellationToken ct = default)
    {
        var result = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountLogin,
            new LauncherAccountRequest { Username = username, Password = password }, ct);
        if (result?.Response != true || string.IsNullOrWhiteSpace(result.Token)) return false;
        state.LauncherToken = result.Token;
        state.LauncherUsername = result.Username ?? username;
        state.LauncherEditionAllowed = result.EditionAllowed;
        var server = state.SelectedServer?.IpAddress;
        if (!string.IsNullOrWhiteSpace(server))
        {
            var settings = ReadSettings();
            settings[server] = new SavedLogin
            {
                RememberLogin = rememberLogin,
                Username = result.Username ?? username,
                Token = rememberLogin ? result.Token : ""
            };
            WriteSettings(settings);
        }
        return true;
    }

    public async Task<bool> RefreshProfilesAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(state.LauncherToken)) return false;
        var response = await http.GameServerPut<SPTProfilesResponse>(Urls.AccountProfiles,
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
        var server = state.SelectedServer?.IpAddress;
        if (!string.IsNullOrWhiteSpace(server))
            SetRememberLogin(server, false);
        if (!string.IsNullOrWhiteSpace(token))
            await http.GameServerPut<LauncherAccountResponse>(Urls.AccountLogout,
                new LauncherAccountRequest { Token = token }, ct);
    }
}
