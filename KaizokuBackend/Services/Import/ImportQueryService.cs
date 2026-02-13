using KaizokuBackend.Data;
using KaizokuBackend.Extensions;
using KaizokuBackend.Models;
using KaizokuBackend.Services.Jobs;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using static System.Net.Mime.MediaTypeNames;
using Action = KaizokuBackend.Models.Action;
using KaizokuBackend.Services.Jobs.Models;
using KaizokuBackend.Services.Jobs.Report;
using KaizokuBackend.Services.Helpers;
using KaizokuBackend.Services.Import;
using KaizokuBackend.Services.Providers;
using KaizokuBackend.Services.Search;
using KaizokuBackend.Services.Series;
using KaizokuBackend.Models.Dto;
using KaizokuBackend.Models.Enums;

namespace KaizokuBackend.Services.Import;

public class ImportQueryService
{
    private readonly AppDbContext _db;

    private readonly SearchCommandService _searchCommand;

    public ImportQueryService(AppDbContext db, SearchCommandService searchCommand)
    {
        _db = db;
        _searchCommand = searchCommand;
    }

    public async Task<ImportTotalsDto> GetImportsTotalsAsync(CancellationToken token = default)
    {
        ImportTotalsDto totals = new ImportTotalsDto();
        List<KaizokuBackend.Models.Database.ImportEntity> imports = await _db.Imports
            .Where(a => a.Status != ImportStatus.DoNotChange && a.Action == Action.Add)
            .AsNoTracking()
            .ToListAsync(token).ConfigureAwait(false);
        HashSet<string> providers = [];
        foreach (KaizokuBackend.Models.Database.ImportEntity imp in imports)
        {
            totals.TotalSeries++;
            ImportChapterMetrics metrics = imp.CalculateSeriesMetrics();
            totals.TotalDownloads += metrics.TotalDownloads;
            foreach (string provider in metrics.Providers)
            {
                providers.Add(provider);
            }
        }
        totals.TotalProviders = providers.Count;
        return totals;
    }

    public async Task<List<ImportSeriesEntry>> GetImportsAsync(CancellationToken token = default)
    {
        var imports = await _db.Imports.ToListAsync(token).ConfigureAwait(false);
        return imports.Select(import => import.ToImportSeriesEntry()).ToList();
    }

    public async Task<ImportSeriesEntry?> AugmentAsync(string path, List<LinkedSeriesDto> linked, CancellationToken token = default)
    {
        KaizokuBackend.Models.Database.ImportEntity? import = await _db.Imports.FirstOrDefaultAsync(a => a.Path == path, token).ConfigureAwait(false);
        if (import == null)
            return null;
        AugmentedResponseDto augmented = await _searchCommand.AugmentSeriesAsync(linked, token).ConfigureAwait(false);
        if (augmented.Series.Count > 0)
        {
            import.Series = augmented.Series;
            await _db.SaveChangesAsync(token).ConfigureAwait(false);
        }
        return import.ToImportSeriesEntry();
    }
}

