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
            var plugins = Path.Combine(bep, "plugins");
            var disabled = Path.Combine(bep, "disabled-plugins");
            Directory.CreateDirectory(plugins);
            Directory.CreateDirectory(disabled);
            var key = GetKey(server);
            var statePath = Path.Combine(disabled, StateFile);
            var oldKey = File.Exists(statePath)
                ? JsonSerializer.Deserialize<ActiveState>(File.ReadAllText(statePath))?.Key
                : null;
            if (oldKey == key) return;

            // On first use, preserve existing installed mods under the selected server.
            // Core SPT plugins are shared by every server.
            var previous = string.IsNullOrWhiteSpace(oldKey) ? key : ValidateKey(oldKey);
            var oldStore = Path.Combine(disabled, previous);
            var newStore = Path.Combine(disabled, key);
            Directory.CreateDirectory(oldStore);
            Directory.CreateDirectory(newStore);

            var outgoing = Directory.EnumerateFileSystemEntries(plugins)
                .Where(p => !Path.GetFileName(p).Equals("spt", StringComparison.OrdinalIgnoreCase))
                .ToList();
            var incoming = Directory.EnumerateFileSystemEntries(newStore).ToList();

            // Never overwrite an existing mod: refuse the switch before moving anything.
            foreach (var entry in outgoing)
            {
                var destination = Path.Combine(oldStore, Path.GetFileName(entry));
                if (File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("Mod profile destination already exists: " + destination);
            }
            foreach (var entry in incoming)
            {
                var destination = Path.Combine(plugins, Path.GetFileName(entry));
                if (Path.GetFileName(entry).Equals("spt", StringComparison.OrdinalIgnoreCase) ||
                    File.Exists(destination) || Directory.Exists(destination))
                    throw new IOException("Cannot activate conflicting mod: " + destination);
            }

            var moves = new List<(string Source, string Destination)>();
            try
            {
                foreach (var entry in outgoing)
                    Move(entry, Path.Combine(oldStore, Path.GetFileName(entry)), moves);
                foreach (var entry in incoming)
                    Move(entry, Path.Combine(plugins, Path.GetFileName(entry)), moves);
                File.WriteAllText(statePath, JsonSerializer.Serialize(new ActiveState { Key = key }));
            }
            catch
            {
                for (var i = moves.Count - 1; i >= 0; i--)
                {
                    var move = moves[i];
                    Move(move.Destination, move.Source, null);
                }
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
