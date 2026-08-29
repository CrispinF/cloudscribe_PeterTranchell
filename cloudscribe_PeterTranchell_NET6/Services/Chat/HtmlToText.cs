using System;
using System.Text;
using System.Text.RegularExpressions;
using cloudscribe.HtmlAgilityPack;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public static class HtmlToText
    {
        private static readonly Regex Whitespace = new Regex(@"\s+", RegexOptions.Compiled);

        private static readonly System.Collections.Generic.HashSet<string> BlockElements =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "p","div","section","article","header","footer","aside","main","nav",
                "h1","h2","h3","h4","h5","h6","ul","ol","li","table","tr","td","th",
                "blockquote","pre","figure","figcaption","br","hr","address","dd","dt",
                "tbody","thead","caption","form","fieldset"
            };

        private static readonly System.Collections.Generic.HashSet<string> SkipElements =
            new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "script","style","noscript","template","svg","canvas","iframe","object","embed","video","audio","source","picture"
            };

        public static string Convert(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var sb = new StringBuilder(html.Length);
            AppendText(doc.DocumentNode, sb);
            return Whitespace.Replace(sb.ToString(), " ").Trim();
        }

        private static void AppendText(HtmlNode node, StringBuilder sb)
        {
            foreach (var child in node.ChildNodes)
            {
                if (child.NodeType == HtmlNodeType.Text)
                {
                    var text = System.Net.WebUtility.HtmlDecode(child.InnerText ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        AppendSeparatorIfNeeded(sb);
                        sb.Append(text);
                    }
                    continue;
                }

                if (child.NodeType != HtmlNodeType.Element) continue;

                var name = child.Name;
                if (SkipElements.Contains(name)) continue;

                if (BlockElements.Contains(name))
                {
                    AppendSeparatorIfNeeded(sb);
                }

                AppendText(child, sb);

                if (BlockElements.Contains(name))
                {
                    AppendSeparatorIfNeeded(sb);
                }
            }
        }

        private static void AppendSeparatorIfNeeded(StringBuilder sb)
        {
            if (sb.Length > 0 && sb[sb.Length - 1] != ' ' && sb[sb.Length - 1] != '\n')
            {
                sb.Append(' ');
            }
        }
    }
}
