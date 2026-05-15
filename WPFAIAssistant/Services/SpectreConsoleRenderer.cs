using System.Text;
using System.Text.RegularExpressions;

namespace WPFAIAssistant.Services
{
    /// <summary>
    /// Converts ANSI escape sequences and plain text / Markdown to HTML
    /// suitable for rendering inside the WebView2 console panel.
    /// </summary>
    public class SpectreConsoleRenderer
    {
        // Basic ANSI SGR colour map
        private static readonly Dictionary<int, string> FgColours = new()
        {
            [30] = "#000000", [31] = "#e74c3c", [32] = "#2ecc71",
            [33] = "#f1c40f", [34] = "#3498db", [35] = "#9b59b6",
            [36] = "#1abc9c", [37] = "#ecf0f1", [90] = "#95a5a6",
            [91] = "#e74c3c", [92] = "#2ecc71", [93] = "#f39c12",
            [94] = "#3498db", [95] = "#9b59b6", [96] = "#1abc9c",
            [97] = "#ffffff",
        };

        private static readonly Dictionary<int, string> BgColours = new()
        {
            [40] = "#000000", [41] = "#e74c3c", [42] = "#2ecc71",
            [43] = "#f1c40f", [44] = "#3498db", [45] = "#9b59b6",
            [46] = "#1abc9c", [47] = "#ecf0f1",
        };

        private static readonly Regex AnsiRegex = new(
            @"\x1B\[([0-9;]*)m", RegexOptions.Compiled);

        /// <summary>
        /// Converts a text string that may contain ANSI escape codes to an HTML fragment.
        /// </summary>
        public string AnsiToHtml(string input)
        {
            var sb = new StringBuilder();
            int lastIndex = 0;
            bool spanOpen = false;

            foreach (Match m in AnsiRegex.Matches(input))
            {
                // Append plain text before this escape
                if (m.Index > lastIndex)
                    sb.Append(EscapeHtml(input[lastIndex..m.Index]));

                if (spanOpen) { sb.Append("</span>"); spanOpen = false; }

                var codes = m.Groups[1].Value.Split(';')
                    .Select(s => int.TryParse(s, out int v) ? v : 0)
                    .ToList();

                if (codes.Count == 1 && codes[0] == 0)
                {
                    // Reset — span already closed above
                }
                else
                {
                    var styles = new List<string>();
                    foreach (var code in codes)
                    {
                        if (FgColours.TryGetValue(code, out var fg)) styles.Add($"color:{fg}");
                        if (BgColours.TryGetValue(code, out var bg)) styles.Add($"background:{bg}");
                        if (code == 1) styles.Add("font-weight:bold");
                        if (code == 3) styles.Add("font-style:italic");
                        if (code == 4) styles.Add("text-decoration:underline");
                    }
                    if (styles.Count > 0)
                    {
                        sb.Append($"<span style=\"{string.Join(";", styles)}\">");
                        spanOpen = true;
                    }
                }

                lastIndex = m.Index + m.Length;
            }

            if (lastIndex < input.Length)
                sb.Append(EscapeHtml(input[lastIndex..]));

            if (spanOpen) sb.Append("</span>");

            return sb.ToString();
        }

        /// <summary>
        /// Converts plain Markdown text to basic HTML.
        /// </summary>
        public string MarkdownToHtml(string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return string.Empty;

            var lines = markdown.Split('\n');
            var sb = new StringBuilder();
            bool inCode = false;
            bool inUl = false;

            foreach (var rawLine in lines)
            {
                var line = rawLine.TrimEnd('\r');

                // Fenced code block
                if (line.StartsWith("```"))
                {
                    if (!inCode) { sb.Append("<pre><code>"); inCode = true; }
                    else { sb.Append("</code></pre>"); inCode = false; }
                    continue;
                }
                if (inCode) { sb.AppendLine(EscapeHtml(line)); continue; }

                // Close ul if needed
                if (!line.StartsWith("- ") && !line.StartsWith("* ") && inUl)
                {
                    sb.Append("</ul>"); inUl = false;
                }

                if (line.StartsWith("### "))
                    sb.Append($"<h3>{EscapeHtml(line[4..])}</h3>");
                else if (line.StartsWith("## "))
                    sb.Append($"<h2>{EscapeHtml(line[3..])}</h2>");
                else if (line.StartsWith("# "))
                    sb.Append($"<h1>{EscapeHtml(line[2..])}</h1>");
                else if (line.StartsWith("- ") || line.StartsWith("* "))
                {
                    if (!inUl) { sb.Append("<ul>"); inUl = true; }
                    sb.Append($"<li>{InlineFormat(line[2..])}</li>");
                }
                else if (string.IsNullOrWhiteSpace(line))
                    sb.Append("<br/>");
                else
                    sb.Append($"<p>{InlineFormat(line)}</p>");
            }

            if (inUl) sb.Append("</ul>");
            if (inCode) sb.Append("</code></pre>");

            return sb.ToString();
        }

        private static string InlineFormat(string text)
        {
            // Bold **text**
            text = Regex.Replace(text, @"\*\*(.+?)\*\*", "<strong>$1</strong>");
            // Italic *text*
            text = Regex.Replace(text, @"\*(.+?)\*", "<em>$1</em>");
            // Inline code `text`
            text = Regex.Replace(text, @"`(.+?)`", "<code>$1</code>");
            return EscapeHtmlPreserveFormatted(text);
        }

        private static string EscapeHtml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;")
                       .Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        private static string EscapeHtmlPreserveFormatted(string text)
        {
            // Only escape ampersands not already part of an entity
            return Regex.Replace(text, @"&(?![a-zA-Z]+;|#\d+;)", "&amp;");
        }
    }
}
