
using System;
using System.Security.Cryptography;
using System.Text;

namespace CmdRunnerPro.Services
{
    public static class EncryptionService
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CmdRunnerPro|v1|entropy");

        public static string Encrypt(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            var data = Encoding.UTF8.GetBytes(plainText);
            var encrypted = ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(encrypted);
        }

        public static string Decrypt(string cipherBase64)
        {
            if (string.IsNullOrEmpty(cipherBase64)) return "";
            var data = Convert.FromBase64String(cipherBase64);
            var decrypted = ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
    }
}
