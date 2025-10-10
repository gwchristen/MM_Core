using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace MMCore.Services
{
    public static class TemplateEngine
    {
        // Explicit constructors to avoid language-version issues
        private static readonly Regex TokenRegex = new Regex("\\{(Q:)?([A-Za-z0-9_]+)\\}", RegexOptions.Compiled);

        // Case-insensitive alias map (explicit initialization)
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"COM1", "comport1"},
            {"COM2", "comport2"},
            {"FIELD3", "username"},
            {"FIELD4", "password"},
            {"FIELD5", "opco"},
            {"FIELD6", "program"},
            {"WD",    "wd"}
        };

        private static string Canonical(string token)
        {
            if (string.IsNullOrEmpty(token)) return token;
            string upper = token.ToUpperInvariant();
            if (Aliases.TryGetValue(upper, out var mapped)) return mapped;
            return token.ToLowerInvariant();
        }

        private static string QuoteIfNeeded(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;
            return value.IndexOf(' ') >= 0 ? "\"" + value + "\"" : value;
        }

        public static string Expand(string template, Dictionary<string, string?> tokens)
        {
            if (template == null) return string.Empty;
            if (tokens == null) tokens = new Dictionary<string, string?>();

            // Canonicalize lookup keys
            var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in tokens)
                map[Canonical(kv.Key)] = kv.Value;

            string result = TokenRegex.Replace(template, m =>
            {
                bool quoted = m.Groups[1].Success; // group 1 is "Q:"
                string rawKey = m.Groups[2].Value;
                string key = Canonical(rawKey);
                map.TryGetValue(key, out string value);
                return quoted ? QuoteIfNeeded(value ?? string.Empty) : (value ?? string.Empty);
            });

            // Collapse excessive whitespace
            result = Regex.Replace(result, @"\s+", " ").Trim();
            return result;
        }

        public static List<string> GetTokensUsed(string template)
        {
            var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(template)) return new List<string>();
            foreach (Match m in TokenRegex.Matches(template))
            {
                string key = Canonical(m.Groups[2].Value);
                if (!string.IsNullOrEmpty(key)) found.Add(key);
            }
            return found.ToList();
        }
    }
}