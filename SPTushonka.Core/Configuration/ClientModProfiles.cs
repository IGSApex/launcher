using System.Text.Json;

namespace SPTarkov.Core.Configuration;

/// <summary>Moves BepInEx plugins between server-specific inactive stores.</summary>
public static class ClientModProfiles
{
    private static readonly object Gate = new();
    private const string StateFile = ".active-server.json";
    private sealed class ActiveState { public string Key { get; set; } = ""; }

    public static void Activate(string gamePath, Server server)
    {
        lock (Gate)
        {
            var bep = Path.Combine(gamePath, "BepInEx");
            var disabled = Path.Combine(bep, "disabled-plugins");
            Directory.CreateDirectory(disabled);
            var key = GetKey(server);
            var statePath = Path.Combine(disabled, StateFile);
            var oldKey = File.Exists(statePath)
                ? JsonSerializer.Deserialize<ActiveState>(File.ReadAllText(statePath))?.Key
                : null;

            // On first activation the existing active mods belong to the selected server.
            var previous = string.IsNullOrWhiteSpace(oldKey) ? key : ValidateKey(oldKey);
            var oldStore = Path.Combine(disabled, previous);
            var newStore = Path.Combine(disabled, key);
            foreach (var profile in new[] { oldStore, newStore })
                foreach (var kind in new[] { "plugins", "patchers" })
                    Directory.CreateDirectory(Path.Combine(profile, kind));

            // Upgrade the old flat plugin-only profile layout without moving active files.
            foreach (var profile in new[] { oldStore, newStore }.Distinct())
            {
                var legacy = Directory.EnumerateFileSystemEntries(profile)
                    .Where(p => !new[] { "plugins", "patchers" }.Contains(
                        Path.GetFileName(p), StringComparer.OrdinalIgnoreCase)).ToList();
                foreach (var entry in legacy)
                {
                    var destination = Path.Combine(profile, "plugins", Path.GetFileName(entry));
                    if (File.Exists(destination) || Directory.Exists(destination))
                        throw new IOException("Legacy profile migration conflict: " + destination);
                }
                foreach (var entry in legacy)
                    Move(entry, Path.Combine(profile, "plugins", Path.GetFileName(entry)), null);
            }

            var moves = new List<(string Source, string Destination)>();
            var outgoing = new List<(string Source, string Destination)>();
            var incoming = new List<(string Source, string Destination)>();

            foreach (var kind in new[] { "plugins", "patchers" })
            {
                var active = Path.Combine(bep, kind);
                Directory.CreateDirectory(active);
                var oldFolder = Path.Combine(oldStore, kind);
                var newFolder = Path.Combine(newStore, kind);

                bool IsShared(string name) =>
                    kind == "plugins"
                        ? name.Equals("spt", StringComparison.OrdinalIgnoreCase)
                        : name.Equals("spt-prepatch.dll", StringComparison.OrdinalIgnoreCase);

                foreach (var entry in Directory.EnumerateFileSystemEntries(active))
                {
                    if (IsShared(Path.GetFileName(entry))) continue;
                    outgoing.Add((entry, Path.Combine(oldFolder, Path.GetFileName(entry))));
                }

                if (oldKey == key) continue;
                foreach (var entry in Directory.EnumerateFileSystemEntries(newFolder))
                {
                    if (IsShared(Path.GetFileName(entry)))
                        throw new IOException("Shared SPT component must not be stored in a profile: " + entry);
                    incoming.Add((entry, Path.Combine(active, Path.GetFileName(entry))));
                }
            }

            // Do not move anything when selecting the already-active server.
            if (oldKey == key) return;

            foreach (var (source, destination) in outgoing.Concat(incoming))
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("Mod profile destination already exists: " + destination);

            try
            {
                foreach (var (source, destination) in outgoing)
                    Move(source, destination, moves);
                foreach (var (source, destination) in incoming)
                    Move(source, destination, moves);
                File.WriteAllText(statePath, JsonSerializer.Serialize(new ActiveState { Key = key }));
            }
            catch
            {
                for (var i = moves.Count - 1; i >= 0; i--)
                    Move(moves[i].Destination, moves[i].Source, null);
                throw;
            }
        }
    }

    private static void Move(string source, string destination, List<(string Source, string Destination)>? journal)
    {
        if (Directory.Exists(source)) Directory.Move(source, destination);
        else File.Move(source, destination);
        journal?.Add((source, destination));
    }

    private static string ValidateKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key is "." or ".." ||
            key.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            key.Contains('/') || key.Contains('\\'))
            throw new InvalidDataException("Invalid mod profile identifier.");
        return key;
    }

    public static string GetKey(Server server)
    {
        if (server.Locked || server.IpAddress.StartsWith("127.0.0.1:", StringComparison.OrdinalIgnoreCase) ||
            server.IpAddress.StartsWith("localhost:", StringComparison.OrdinalIgnoreCase))
            return "localhost";
        var raw = server.Name + "-" + server.IpAddress;
        var safe = new string(raw.Select(c =>
            char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_').ToArray());
        return ValidateKey(safe);
    }
}
