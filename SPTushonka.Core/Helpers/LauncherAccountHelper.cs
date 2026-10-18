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
    private static readonly string TempPath = Path.Combine(AppContext.BaseDirectory, "user", "Launcher", "launcherTemp.json");

    public static void DeleteTemporarySessionMarker()
    {
        if (File.Exists(TempPath)) File.Delete(TempPath);
    }
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static void StartTemporarySession()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(TempPath)!);
        DeleteTemporarySessionMarker();
        File.WriteAllText(TempPath, "{}");
    }


    private sealed class SessionEntry
    {
        public string username { get; set; } = "";
        public string password { get; set; } = "";
        public string sessionToken { get; set; } = "";
    }
    private static Dictionary<string, SessionEntry> ReadSessions()
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, SessionEntry>>(File.ReadAllText(TempPath)) ?? new(); }
        catch (IOException) { return new(); }
        catch (JsonException) { return new(); }
    }
    private static void SaveSession(string server, string username, string token)
    {
        var sessions = ReadSessions();
        sessions[server] = new SessionEntry { username = username, sessionToken = token };
        Directory.CreateDirectory(Path.GetDirectoryName(TempPath)!);
        File.WriteAllText(TempPath, JsonSerializer.Serialize(sessions, JsonOptions));
    }
    private sealed record CachedLogin(string Username, string Password, string Token);
    private readonly Dictionary<string, CachedLogin> _logins = new(StringComparer.OrdinalIgnoreCase);

    public async Task<bool> TryAutoLoginAsync(CancellationToken ct = default)
    {
        var server = state.SelectedServer?.IpAddress;
        if (string.IsNullOrWhiteSpace(server)) return false;
        if (!_logins.TryGetValue(server, out var cached))
        {
            if (!ReadSessions().TryGetValue(server, out var saved)) return false;
            cached = new CachedLogin(saved.username, "", saved.sessionToken);
        }
        try
        {
            var session = await http.GameServerPut<LauncherAccountResponse>(Urls.AccountSession,
                new LauncherAccountRequest { Token = cached.Token }, ct);
            if (session?.Response == true)
            {
                state.LauncherToken = session.Token ?? cached.Token;
                state.LauncherUsername = session.Username ?? cached.Username;
                state.LauncherEditionAllowed = session.EditionAllowed;
                if (await RefreshProfilesAsync(ct)) return true;
            }
        }
        catch (HttpRequestException) { }
        return !string.IsNullOrEmpty(cached.Password) && await LoginAsync(cached.Username, cached.Password, ct) && await RefreshProfilesAsync(ct);
    }

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
        if (!string.IsNullOrWhiteSpace(state.SelectedServer?.IpAddress))
            _logins[state.SelectedServer.IpAddress] = new CachedLogin(username, password, result.Token);
        if (!string.IsNullOrWhiteSpace(state.SelectedServer?.IpAddress))
            SaveSession(state.SelectedServer.IpAddress, result.Username ?? username, result.Token);
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
        if (!string.IsNullOrWhiteSpace(state.SelectedServer?.IpAddress))
            _logins.Remove(state.SelectedServer.IpAddress);
        var token = state.LauncherToken;
        state.LauncherToken = null;
        state.LauncherUsername = null;
        state.LauncherEditionAllowed = null;
        if (!string.IsNullOrWhiteSpace(state.SelectedServer?.IpAddress))
        {
            var sessions = ReadSessions();
            sessions.Remove(state.SelectedServer.IpAddress);
            File.WriteAllText(TempPath, JsonSerializer.Serialize(sessions, JsonOptions));
        }
        if (!string.IsNullOrWhiteSpace(token))
            await http.GameServerPut<LauncherAccountResponse>(Urls.AccountLogout,
                new LauncherAccountRequest { Token = token }, ct);
    }
}
