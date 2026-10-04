namespace ComicPlatform.Infrastructure.Configuration;

/// <summary>
/// Minimal .env reader for local development. Real environment variables always win;
/// in containers the file is absent and configuration comes from the environment only.
/// </summary>
public static class DotEnv
{
    /// <summary>Walks up from <paramref name="startDirectory"/> looking for a ".env" file.</summary>
    public static string? Find(string startDirectory, int maxDepth = 10)
    {
        var dir = new DirectoryInfo(startDirectory);
        for (var i = 0; i < maxDepth && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static Dictionary<string, string?> Parse(string path)
    {
        var values = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in File.ReadAllLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
            {
                value = value[1..^1];
            }

            values[key] = value;
        }

        return values;
    }

    /// <summary>Loads the nearest .env, dropping keys already present in the process environment.</summary>
    public static Dictionary<string, string?> LoadWithoutOverridingEnvironment(string startDirectory)
    {
        var path = Find(startDirectory);
        if (path is null)
        {
            return new Dictionary<string, string?>();
        }

        return Parse(path)
            .Where(kv => Environment.GetEnvironmentVariable(kv.Key) is null)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
    }
}
