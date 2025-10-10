using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace MMCore.Models
{
    public class InputPreset
    {
        public string Name { get; set; } = "";

        // Preferred template for this preset (optional)
        public string? TemplateName { get; set; }

        // Tokens saved with the preset
        public string? Com1 { get; set; }
        public string? Com2 { get; set; }
        public string? Username { get; set; }
        public string? Opco { get; set; }
        public string? Program { get; set; }
        public string? WorkingDirectory { get; set; }

        // ----------------------
        // Password (encrypted)
        // ----------------------
        // Encrypted payload that goes to disk (Base64 of DPAPI-protected bytes)
        public string? EncryptedPassword { get; set; }

        // Not serialized: use this property everywhere in code
        [JsonIgnore]
        public string? Password
        {
            get
            {
                if (_password != null) return _password;
                if (string.IsNullOrWhiteSpace(EncryptedPassword)) return null;
                _password = Decrypt(EncryptedPassword);
                return _password;
            }
            set
            {
                _password = value;
                EncryptedPassword = Encrypt(value);
            }
        }

        // Back-compat with older files that stored plain text as "PasswordPlain".
        // On deserialize, System.Text.Json will set this and we migrate it.
        [JsonPropertyName("PasswordPlain")]
        public string? LegacyPasswordPlain
        {
            get => null; // never emit this on write
            set
            {
                if (!string.IsNullOrEmpty(value))
                {
                    // Setting Password will encrypt and store in EncryptedPassword
                    Password = value;
                }
            }
        }

        [JsonIgnore]
        private string? _password;

        public override string ToString() => Name;

        // ===== DPAPI helpers =====
        // NOTE: CurrentUser scope means only this Windows user profile can decrypt it.
        // If you want the same across all users of the machine, switch to LocalMachine.
        private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("MMCore_DPAPI_v1");

        private static string? Encrypt(string? plain)
        {
            if (string.IsNullOrEmpty(plain)) return null;
            byte[] data = Encoding.UTF8.GetBytes(plain);
            byte[] cipher = ProtectedData.Protect(data, _entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(cipher);
        }

        private static string? Decrypt(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64)) return null;
            try
            {
                byte[] cipher = Convert.FromBase64String(base64);
                byte[] data = ProtectedData.Unprotect(cipher, _entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                // If the user profile changed or the blob is tampered/invalid,
                // fail gracefully by returning null.
                return null;
            }
        }
    }
}