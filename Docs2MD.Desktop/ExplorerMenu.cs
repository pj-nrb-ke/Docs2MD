namespace Docs2MD;

using Microsoft.Win32;

/// <summary>
/// Adds/removes a per-user "Send To" shortcut ("Convert to MD") that runs this
/// exe with the selected file(s). Send To is used instead of a registry context
/// verb because static HKCU shell verbs don't reliably surface on heavily
/// customised shells; the shortcut always appears under right-click → Send to,
/// works for multi-select (every selected file arrives as an argument), carries
/// the app icon, and needs no admin.
/// </summary>
internal static class ExplorerMenu
{
    private const string ShortcutName = "Convert to MD.lnk";

    private static string ShortcutPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.SendTo), ShortcutName);

    public static bool IsInstalled() => File.Exists(ShortcutPath);

    public static void Install()
    {
        RemoveLegacyVerbs();

        string exe = Environment.ProcessPath!;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var sc = shell.CreateShortcut(ShortcutPath);
        sc.TargetPath   = exe;
        sc.IconLocation = exe + ",0";
        sc.Description  = "Convert PDF / DOCX / XLSX to Markdown";
        sc.Save();
    }

    public static void Uninstall()
    {
        if (File.Exists(ShortcutPath)) File.Delete(ShortcutPath);
        RemoveLegacyVerbs();
    }

    // Earlier builds registered HKCU context-menu verbs that this machine's shell
    // never showed; remove them so upgraders aren't left with dead keys.
    private static void RemoveLegacyVerbs()
    {
        foreach (var ext in DocumentToMarkdown.SupportedExtensions)
            Registry.CurrentUser.DeleteSubKeyTree(
                $@"Software\Classes\SystemFileAssociations\{ext}\shell\Docs2MDConvert",
                throwOnMissingSubKey: false);
    }
}
