namespace ComicPlatform.Infrastructure.Processing;

/// <summary>An expected processing failure with a stable code and a Vietnamese message for the house UI.</summary>
public sealed class PipelineException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;

    public static string DisplayName(string name)
    {
        var clean = new string(name.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 80 ? "…" + clean[^79..] : clean;
    }
}
