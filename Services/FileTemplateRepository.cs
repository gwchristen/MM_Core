using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using MMCore.Models;

namespace MMCore.Services
{
    public sealed class FileTemplateRepository : ITemplateRepository
    {
        private readonly JsonSerializerOptions _json = new() { WriteIndented = true };
        public string StorageRoot { get; }

        public FileTemplateRepository(string? storageRoot = null)
        {
            StorageRoot = storageRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MMCore_v2", "Templates");
            Directory.CreateDirectory(StorageRoot);
        }

        public async Task<IReadOnlyList<Template>> LoadAllAsync(CancellationToken ct = default)
        {
            Directory.CreateDirectory(StorageRoot);
            var list = new List<Template>();

            foreach (var file in Directory.EnumerateFiles(StorageRoot, "*.json"))
            {
                try
                {
                    await using var fs = File.OpenRead(file);
                    var t = await JsonSerializer.DeserializeAsync<Template>(fs, cancellationToken: ct);
                    if (t != null) list.Add(t);
                }
                catch
                {
                    // ignore malformed files
                }
            }

            return list.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task SaveAsync(Template template, string? originalName = null, CancellationToken ct = default)
        {
            if (template is null) throw new ArgumentNullException(nameof(template));
            if (string.IsNullOrWhiteSpace(template.Name))
                throw new ArgumentException("Template.Name is required.", nameof(template));

            Directory.CreateDirectory(StorageRoot);

            // If this is a rename (originalName provided and different), delete the old file
            if (!string.IsNullOrWhiteSpace(originalName) &&
                !originalName.Equals(template.Name, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = PathFor(originalName);
                if (File.Exists(oldPath))
                    File.Delete(oldPath);
            }

            var path = PathFor(template.Name);
            await using var fs = File.Create(path);
            await JsonSerializer.SerializeAsync(fs, template, _json, ct);
        }

        public Task DeleteByNameAsync(string name, CancellationToken ct = default)
        {
            var path = PathFor(name);
            if (File.Exists(path)) File.Delete(path);
            return Task.CompletedTask;
        }

        public Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default)
            => Task.FromResult(File.Exists(PathFor(name)));

        private string PathFor(string name)
        {
            var safe = ToSafeFileName(name);
            return Path.Combine(StorageRoot, $"{safe}.json");
        }

        private static string ToSafeFileName(string name)
        {
            // Replace invalid chars and collapse whitespace
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(name
                .Select(ch => invalid.Contains(ch) ? '_' : ch)
                .ToArray());

            // Trim and collapse spaces
            var normalized = string.Join(" ",
                cleaned.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));

            return string.IsNullOrWhiteSpace(normalized) ? "Template" : normalized;
        }
    }
}