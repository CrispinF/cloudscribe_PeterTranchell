using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public static class TextChunker
    {
        private static readonly Regex SentenceSplit = new Regex(@"(?<=[.!?;:])\s+", RegexOptions.Compiled);

        public static List<string> Split(string text, int maxChars, int overlapChars)
        {
            var results = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return results;
            if (maxChars < 200) maxChars = 200;
            if (overlapChars < 0) overlapChars = 0;
            if (overlapChars >= maxChars) overlapChars = maxChars / 4;

            var sentences = SentenceSplit.Split(text);
            var current = new StringBuilder();

            foreach (var raw in sentences)
            {
                var sentence = raw.Trim();
                if (sentence.Length == 0) continue;

                foreach (var piece in SplitOversize(sentence, maxChars))
                {
                    if (current.Length + piece.Length + 1 > maxChars && current.Length > 0)
                    {
                        results.Add(current.ToString().Trim());
                        StartWithOverlap(current, results[results.Count - 1], overlapChars);
                    }

                    if (current.Length > 0) current.Append(' ');
                    current.Append(piece);
                }
            }

            if (current.Length > 0) results.Add(current.ToString().Trim());
            return results;
        }

        private static IEnumerable<string> SplitOversize(string sentence, int maxChars)
        {
            if (sentence.Length <= maxChars)
            {
                yield return sentence;
                yield break;
            }

            var start = 0;
            while (start < sentence.Length)
            {
                var len = Math.Min(maxChars - 1, sentence.Length - start);
                if (start + len < sentence.Length)
                {
                    var lastSpace = sentence.LastIndexOf(' ', start + len - 1, len);
                    if (lastSpace > start) len = lastSpace - start;
                }
                yield return sentence.Substring(start, len).Trim();
                start += len;
            }
        }

        private static void StartWithOverlap(StringBuilder current, string previous, int overlapChars)
        {
            current.Clear();
            if (overlapChars == 0 || previous.Length == 0) return;

            var tailStart = Math.Max(0, previous.Length - overlapChars);
            var tail = previous.Substring(tailStart);
            if (tailStart > 0)
            {
                var spaceIdx = tail.IndexOf(' ');
                if (spaceIdx >= 0 && spaceIdx < tail.Length - 1) tail = tail.Substring(spaceIdx + 1);
            }
            current.Append(tail.Trim()).Append(' ');
        }
    }
}
