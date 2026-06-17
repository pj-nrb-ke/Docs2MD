using System.Text.RegularExpressions;

namespace Docs2MD.Web;

internal static class WireframeRenderer
{
    internal static string NormalizeMarkdown(string md) =>
        Regex.Replace(md, @"([^\n])\n([ \t]*[-*+] )", "$1\n\n$2",
            RegexOptions.Multiline);

    internal static string WrapHtml(string body) =>
        "<!DOCTYPE html><html><head><meta charset=\"utf-8\"><style>" +
        "* { box-sizing: border-box; }" +
        "body { font-family: 'Segoe UI', Arial, sans-serif; font-size: 14px; max-width: 1060px; margin: 28px auto; padding: 0 32px 60px; color: #1a1a1a; background: #f4f6f9; line-height: 1.65; }" +
        "h1 { font-size: 22px; font-weight: 700; color: #fff; background: #0f62ae; padding: 12px 20px; margin: 0 -32px 24px; letter-spacing: 0.2px; }" +
        "h2 { font-size: 15px; font-weight: 700; text-transform: uppercase; letter-spacing: 0.8px; color: #0f62ae; border-bottom: 2px solid #0f62ae; padding-bottom: 4px; margin-top: 36px; }" +
        "h3 { font-size: 14px; font-weight: 600; color: #333; margin-top: 22px; }" +
        "h4 { font-size: 13px; font-weight: 600; color: #555; margin-top: 14px; }" +
        "p { margin: 6px 0 10px; color: #2c2c2c; }" +
        "hr { border: none; border-top: 2px solid #d0d8e4; margin: 24px 0; }" +
        "strong { font-weight: 700; color: #0a4d8c; }" +
        "em { font-style: italic; color: #444; }" +
        "ul, ol { padding-left: 22px; margin: 8px 0; }" +
        "li { margin: 4px 0; }" +
        "li::marker { color: #0f62ae; font-weight: bold; }" +
        "blockquote { border-left: 4px solid #0078d4; margin: 12px 0; padding: 8px 16px; background: #eaf3fb; color: #333; border-radius: 0 4px 4px 0; }" +
        "code { font-family: Consolas, 'Cascadia Code', 'Courier New', monospace; font-size: 12px; background: #dde5f0; padding: 1px 5px; border-radius: 3px; color: #0a4d8c; }" +
        "table { border-collapse: collapse; margin: 14px 0; width: auto; max-width: 100%; background: #fff; border-radius: 4px; overflow: hidden; box-shadow: 0 1px 4px rgba(0,0,0,0.08); }" +
        "th, td { border: 1px solid #cdd5e0; padding: 7px 14px; text-align: left; font-size: 13px; }" +
        "th { background: #e8eef6; font-weight: 700; color: #1a3a5c; }" +
        "tr:nth-child(even) { background: #f8fafc; }" +
        "pre { background: #fff; border: 1px solid #d0d7de; border-radius: 6px; padding: 14px 18px; margin: 16px 0; overflow-x: auto; line-height: 1.45; }" +
        "pre code { font-family: Consolas, 'Cascadia Code', 'Courier New', monospace; font-size: 12.5px; background: none; padding: 0; white-space: pre; color: #24292e; }" +
        "pre.wf-frame { background: #fff; border: 1.5px solid #8a9bb5; border-radius: 6px; box-shadow: 0 3px 16px rgba(0,0,0,0.13); padding: 16px 20px; }" +
        "pre.wf-frame code { font-size: 13px; line-height: 1.5; }" +
        ".wf-btn { display:inline; background:#e4ecf7; border:1px solid #4a72a8; border-radius:3px; padding:0 4px; color:#0a3d7a; font-weight:700; }" +
        ".wf-dd  { display:inline; background:#fff; border:1px solid #777; border-radius:2px; padding:0 4px; color:#333; }" +
        ".wf-r-on  { color:#0f62ae; font-weight:900; }" +
        ".wf-r-off { color:#aaa; }" +
        ".wf-s-draft     { color:#888; }" +
        ".wf-s-approved  { color:#1d6fd1; font-weight:700; }" +
        ".wf-s-running   { color:#d97706; font-weight:700; }" +
        ".wf-s-complete  { color:#16a34a; font-weight:700; }" +
        ".wf-s-cancelled { color:#dc2626; }" +
        ".wf-s-default   { color:#555; }" +
        "</style></head><body>" +
        body +
        @"<script>
document.querySelectorAll('pre code').forEach(function(el) {
    if (!/[┌┐└┘│─├┤┬┴┼╔╗╚╝║═]/.test(el.textContent)) return;
    el.parentElement.classList.add('wf-frame');
    var h = el.innerHTML;
    h = h.replace(/\[([^\]\n]{0,80}▼\s*)\]/g, '<span class=""wf-dd"">[$1]</span>');
    h = h.replace(/\[([^\]\n]{0,80})\]/g, '<span class=""wf-btn"">[$1]</span>');
    h = h.replace(/◉/g, '<span class=""wf-r-on"">◉</span>');
    h = h.replace(/○/g, '<span class=""wf-r-off"">○</span>');
    h = h.replace(/● ?(Draft|Open)[^\n<]*/gi, function(m) { return '<span class=""wf-s-draft"">' + m + '</span>'; });
    h = h.replace(/● ?(Approved)[^\n<]*/gi, function(m) { return '<span class=""wf-s-approved"">' + m + '</span>'; });
    h = h.replace(/● ?(Running|Active|In.?Progress)[^\n<]*/gi, function(m) { return '<span class=""wf-s-running"">' + m + '</span>'; });
    h = h.replace(/● ?(Complete|Done|Closed)[^\n<]*/gi, function(m) { return '<span class=""wf-s-complete"">' + m + '</span>'; });
    h = h.replace(/● ?(Cancelled|Rejected|Failed)[^\n<]*/gi, function(m) { return '<span class=""wf-s-cancelled"">' + m + '</span>'; });
    h = h.replace(/●([^\n<]*)/g, '<span class=""wf-s-default"">●$1</span>');
    el.innerHTML = h;
});
</script></body></html>";
}
