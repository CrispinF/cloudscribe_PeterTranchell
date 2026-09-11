using System;
using System.Security.Cryptography;
using System.Text;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public static class CorpusText
    {
        public static string ComputeHash(string title, string body)
        {
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(title + "\n" + body));
            var sb = new StringBuilder(bytes.Length * 2);
            for (var i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
            return sb.ToString();
        }

        public static string ComputeStampHash(params string[] parts)
        {
            using var sha = SHA256.Create();
            var sb = new StringBuilder();
            foreach (var part in parts)
            {
                sb.Append(part ?? string.Empty);
                sb.Append('\n');
            }
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var hex = new StringBuilder(hash.Length * 2);
            for (var i = 0; i < hash.Length; i++) hex.Append(hash[i].ToString("x2"));
            return hex.ToString();
        }
    }
}
