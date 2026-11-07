namespace Docs2MD;

using Microsoft.Win32;

/// <summary>
/// Registers a per-user Explorer right-click verb ("Convert to MD here") on
/// PDF / DOCX / XLSX files. Per-user (HKCU) so no admin rights are needed; the
/// verb runs this exe with the clicked file, which converts it headlessly.
/// On Windows 11 the entry appears under "Show more options".
/// </summary>
internal static class ExplorerMenu
{
    private const string Verb  = "Docs2MDConvert";
    private const string Label = "Convert to MD here";

    private static string KeyPath(string ext) =>
        $@"Software\Classes\SystemFileAssociations\{ext}\shell\{Verb}";

    public static bool IsInstalled() =>
        Registry.CurrentUser.OpenSubKey(KeyPath(DocumentToMarkdown.SupportedExtensions[0])) is not null;

    public static void Install()
    {
        string exe = Environment.ProcessPath!;
        foreach (var ext in DocumentToMarkdown.SupportedExtensions)
        {
            using var verb = Registry.CurrentUser.CreateSubKey(KeyPath(ext));
            verb.SetValue(null, Label);
            verb.SetValue("Icon", exe);
            using var cmd = verb.CreateSubKey("command");
            cmd.SetValue(null, $"\"{exe}\" \"%1\"");
        }
    }

    public static void Uninstall()
    {
        foreach (var ext in DocumentToMarkdown.SupportedExtensions)
            Registry.CurrentUser.DeleteSubKeyTree(KeyPath(ext), throwOnMissingSubKey: false);
    }
}
