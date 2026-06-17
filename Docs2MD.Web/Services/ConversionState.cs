namespace Docs2MD.Web.Services;

/// <summary>
/// Circuit-scoped holder for the most recent conversion result.
/// Registered as Scoped so it persists across in-circuit navigation
/// (Convert → Viewer) within the same SignalR connection.
/// </summary>
public sealed class ConversionState
{
    public string? Markdown { get; private set; }
    public string? FileName { get; private set; }

    public bool HasResult => Markdown is not null;

    public void Set(string markdown, string? fileName)
    {
        Markdown = markdown;
        FileName = fileName;
    }

    public void Clear()
    {
        Markdown = null;
        FileName = null;
    }
}
