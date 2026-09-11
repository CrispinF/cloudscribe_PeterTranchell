using System;
using Microsoft.AspNetCore.DataProtection;

namespace cloudscribe_PeterTranchell_NET6.Services.Chat
{
    public class ChatNonceService
    {
        private const string TimestampSeparator = ":";

        private const int RefreshBackdateSeconds = 6;

        private readonly IDataProtector _protector;

        public ChatNonceService(IDataProtectionProvider provider)
        {
            _protector = provider.CreateProtector("ChatNonceProtection");
        }

        public string GenerateToken()
        {
            return GenerateToken(0, DateTime.UtcNow);
        }

        private string GenerateToken(int refreshCount, DateTime stampTime)
        {
            var random = new byte[8];
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            var payload = stampTime.Ticks.ToString() + TimestampSeparator + Convert.ToHexString(random)
                + TimestampSeparator + refreshCount.ToString();
            return _protector.Protect(payload);
        }

        public bool TryValidateToken(string token, int maxAgeSeconds, out string error)
        {
            if (!TryDecode(token, out var ticks, out _, out error)) return false;

            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);

            if (age.TotalSeconds < 2)
            {
                error = "Request too fast";
                return false;
            }

            if (age.TotalSeconds > maxAgeSeconds)
            {
                error = "Nonce expired";
                return false;
            }

            return true;
        }

        public bool TryRefreshToken(string token, int maxAgeSeconds, int graceSeconds, int maxRefreshCount, out string newToken, out string error)
        {
            newToken = null;

            if (!TryDecode(token, out var ticks, out var refreshCount, out error)) return false;

            var age = DateTime.UtcNow - new DateTime(ticks, DateTimeKind.Utc);

            if (age.TotalSeconds > maxAgeSeconds + graceSeconds)
            {
                error = "Nonce expired";
                return false;
            }

            if (refreshCount >= maxRefreshCount)
            {
                error = "Too many refreshes";
                return false;
            }

            newToken = GenerateToken(refreshCount + 1, DateTime.UtcNow.AddSeconds(-RefreshBackdateSeconds));
            return true;
        }

        private bool TryDecode(string token, out long ticks, out int refreshCount, out string error)
        {
            ticks = 0;
            refreshCount = 0;
            error = null;

            if (string.IsNullOrWhiteSpace(token))
            {
                error = "Missing nonce";
                return false;
            }

            string plaintext;
            try
            {
                plaintext = _protector.Unprotect(token);
            }
            catch (Exception)
            {
                error = "Invalid nonce";
                return false;
            }

            var parts = plaintext.Split(TimestampSeparator);
            if (parts.Length < 2 || parts.Length > 3)
            {
                error = "Invalid nonce payload";
                return false;
            }

            if (!long.TryParse(parts[0], out ticks))
            {
                error = "Invalid nonce payload";
                return false;
            }

            if (parts.Length == 3 && !int.TryParse(parts[2], out refreshCount))
            {
                error = "Invalid nonce payload";
                return false;
            }

            return true;
        }
    }
}