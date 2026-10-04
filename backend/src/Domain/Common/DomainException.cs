namespace ComicPlatform.Domain.Common;

/// <summary>
/// A business-rule violation. <see cref="Code"/> is a stable machine-readable code;
/// the message is user-facing Vietnamese text.
/// </summary>
public class DomainException : Exception
{
    public DomainException(string code, string message) : base(message) => Code = code;

    public string Code { get; }
}
