namespace Docs2MD;

/// <summary>Routes converter warnings/progress to whatever UI is listening.</summary>
public static class Log
{
    public static Action<string> Sink { get; set; } = _ => { };

    public static void Warn(string message) => Sink(message);
}
