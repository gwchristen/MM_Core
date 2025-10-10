using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MMCore.Models;

namespace MMCore.Services
{
    public interface ITemplateRepository
    {
        string StorageRoot { get; }

        Task<IReadOnlyList<Template>> LoadAllAsync(CancellationToken ct = default);

        /// <summary>
        /// Save or rename a template. If originalName is provided and differs from template.Name,
        /// the old file is deleted (rename semantics).
        /// </summary>
        Task SaveAsync(Template template, string? originalName = null, CancellationToken ct = default);

        Task DeleteByNameAsync(string name, CancellationToken ct = default);

        Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
    }
}