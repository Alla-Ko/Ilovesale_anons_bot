using System.Text;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;

namespace Announcement.Services;

public interface ICaptionPublishFormatter
{
    /// <summary>HTML для Telegram (ParseMode.Html): екранування, посилання як &lt;a href&gt;, переноси рядків — символ \n (тег &lt;br&gt; API не підтримує).</summary>
    string FormatCaptionForTelegramHtml(string? raw);

    /// <summary>Вузли Telegraf для підпису: кожен непорожній рядок — &lt;p&gt; з дочірніми текстами та &lt;a&gt;.</summary>
    IReadOnlyList<object> BuildTelegraphCaptionParagraphs(string? raw);
}

public class CaptionPublishFormatter : ICaptionPublishFormatter
{
    private static readonly Regex UrlRegex = new(@"(https?://\S+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public string FormatCaptionForTelegramHtml(string? raw)
    {
        var prepared = PrepareForPublish(raw);
        if (string.IsNullOrEmpty(prepared))
            return string.Empty;

        var lines = prepared.Replace("\r\n", "\n").Split('\n');
        var parts = new List<string>();
        foreach (var line in lines)
            parts.Add(FormatSingleLineTelegramHtml(line));

        return string.Join("\n", parts);
    }

    public IReadOnlyList<object> BuildTelegraphCaptionParagraphs(string? raw)
    {
        var prepared = PrepareForPublish(raw);
        if (string.IsNullOrEmpty(prepared))
            return Array.Empty<object>();

        var lines = prepared.Replace("\r\n", "\n").Split('\n');
        var nodes = new List<object>();
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var children = BuildTelegraphParagraphChildren(line);
            nodes.Add(new { tag = "p", children });
        }

        return nodes;
    }

    internal static string PrepareForPublish(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        var s = raw.Replace("\r\n", "\n");
        // Telegram Bot API (ParseMode.Html) не підтримує тег <br> — лише дозволені теги + переноси \n.
        s = Regex.Replace(s, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\(heart\)", "❤️", RegexOptions.IgnoreCase);

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if (ch is '*' or '~')
                continue;
            sb.Append(ch);
        }

        s = sb.ToString();
        while (s.Contains("++", StringComparison.Ordinal))
            s = s.Replace("++", string.Empty, StringComparison.Ordinal);

        return s;
    }

    private static string FormatSingleLineTelegramHtml(string line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        var enc = HtmlEncoder.Default;
        var sb = new StringBuilder();
        var idx = 0;
        foreach (Match m in UrlRegex.Matches(line))
        {
            if (m.Index > idx)
                sb.Append(enc.Encode(line.Substring(idx, m.Index - idx)));

            var urlRaw = m.Value;
            var url = TrimTrailingUrlJunk(urlRaw);
            if (string.IsNullOrEmpty(url))
            {
                sb.Append(enc.Encode(urlRaw));
                idx = m.Index + m.Length;
                continue;
            }

            var href = enc.Encode(url);
            sb.Append("<a href=\"");
            sb.Append(href);
            sb.Append("\">");
            sb.Append(enc.Encode(url));
            sb.Append("</a>");
            idx = m.Index + m.Length;
        }

        if (idx < line.Length)
            sb.Append(enc.Encode(line.Substring(idx)));

        return sb.ToString();
    }

    private static List<object> BuildTelegraphParagraphChildren(string line)
    {
        var list = new List<object>();
        var idx = 0;
        foreach (Match m in UrlRegex.Matches(line))
        {
            if (m.Index > idx)
                list.Add(line[idx..m.Index]);

            var url = TrimTrailingUrlJunk(m.Value);
            list.Add(new
            {
                tag = "a",
                attrs = new { href = url },
                children = new[] { url }
            });
            idx = m.Index + m.Length;
        }

        if (idx < line.Length)
            list.Add(line[idx..]);

        if (list.Count == 0)
            list.Add(string.Empty);

        return list;
    }

    private static string TrimTrailingUrlJunk(string url)
    {
        var t = url.TrimEnd();
        while (t.Length > 0 && IsTrailingJunk(t[^1]))
            t = t[..^1];
        return t;
    }

    private static bool IsTrailingJunk(char c) =>
        c is '.' or ',' or ';' or ':' or '!' or '?' or ')' or ']' or '»' or '"' or '\'';
}
