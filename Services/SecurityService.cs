
using System.Collections.Generic;

namespace CmdRunnerPro.Services
{
    public static class SecurityService
    {
        public static string Redact(string text, IEnumerable<string?> secrets, string mask = "******")
        {
            if (string.IsNullOrEmpty(text)) return text;
            string result = text;
            foreach (var s in secrets)
            {
                if (string.IsNullOrEmpty(s)) continue;
                result = result.Replace(s, mask, System.StringComparison.Ordinal);
            }
            return result;
        }
    }
}
