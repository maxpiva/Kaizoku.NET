using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using KaizokuBackend.Services.Jobs;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using static System.Net.Mime.MediaTypeNames;
using Action = KaizokuBackend.Models.Database.Action;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Search;
using KaizokuBackend.Services.Series;

namespace KaizokuBackend.Services.Import;

public class ImportQueryService
{
    private readonly AppDbContext _db;
    private readonly ContextProvider _baseUrl;
    private readonly SearchCommandService _searchCommand;

    public ImportQueryService(AppDbContext db, ContextProvider baseUrl, SearchCommandService searchCommand)
    {
        _db = db;
        _baseUrl = baseUrl;
        _searchCommand = searchCommand;
    }

    public async Task<ImportTotals> GetImportsTotalsAsync(CancellationToken token = default)
    {
        ImportTotals totals = new ImportTotals();
        List<KaizokuBackend.Models.Database.Import> imports = await _db.Imports
            .Where(a => a.Status != ImportStatus.DoNotChange && a.Action == Action.Add)
            .AsNoTracking()
            .ToListAsync(token).ConfigureAwait(false);
        HashSet<string> providers = [];
        foreach (KaizokuBackend.Models.Database.Import imp in imports)
        {
            totals.TotalSeries++;
            if (imp.Series == null)
                continue;
            HashSet<decimal?> chapters = new HashSet<decimal?>();
            foreach (FullSeries s in imp.Series.Where(a=>a.IsSelected && a.IsStorage))
            {
                if (!providers.Contains(s.Provider))
                    providers.Add(s.Provider);
                List<decimal?> chapterNumbers = s.Chapters.Select(a=>a.Number).Where(a => a > imp.ContinueAfterChapter).ToList();
                foreach (decimal? p in chapterNumbers)
                {
                    if (p.HasValue && !chapters.Contains(p))
                        chapters.Add(p);
                }
                totals.TotalDownloads += chapterNumbers.Count;
            }
            foreach (FullSeries s in imp.Series.Where(a => a.IsSelected && !a.IsStorage))
            {
                if (!providers.Contains(s.Provider))
                    providers.Add(s.Provider);
                List<decimal?> chapterNumbers = s.Chapters.Select(a => a.Number).Where(a => a > imp.ContinueAfterChapter).ToList();
                foreach (decimal? p in chapterNumbers.ToList())
                {
                    if (p.HasValue && chapters.Contains(p))
                        chapterNumbers.Remove(p);
                }
                totals.TotalDownloads += chapterNumbers.Count;
            }
        }
        totals.TotalProviders = providers.Count;
        return totals;
    }

    public async Task<List<ImportInfo>> GetImportsAsync(CancellationToken token = default)
    {
        var imports = await _db.Imports.ToListAsync(token).ConfigureAwait(false);
        return imports.Select(import => import.ToImportInfo(_baseUrl.BaseUrl)).ToList();
    }

    public async Task<ImportInfo?> AugmentAsync(string path, List<LinkedSeries> linked, CancellationToken token = default)
    {
        KaizokuBackend.Models.Database.Import? import = await _db.Imports.FirstOrDefaultAsync(a => a.Path == path, token).ConfigureAwait(false);
        if (import == null)
            return null;
        AugmentedResponse augmented = await _searchCommand.AugmentSeriesAsync(linked, token).ConfigureAwait(false);
        if (augmented.Series.Count > 0)
        {
            import.Series = augmented.Series;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        return import.ToImportInfo(_baseUrl.BaseUrl);
    }
}
