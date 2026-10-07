using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Controllers;
using RensaioBackend.Data;
using RensaioBackend.Extensions;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Helpers;
using ParsedChapter = Mihon.ExtensionsBridge.Models.Extensions.ParsedChapter;
using RensaioBackend.Services.Import;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RensaioBackend.SeriesCreation.Tests;

public sealed class SeriesCreationTests : IDisposable
{
    private readonly string _storage = Directory.CreateTempSubdirectory("rensaio-series-creation-").FullName;
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly ServiceProvider _services = new ServiceCollection().BuildServiceProvider();
    private readonly SeriesCommandService _command;
    private readonly SeriesStateService _state;
    private readonly object? _previousSettings;
    private static readonly FieldInfo SettingsField = typeof(SettingsService).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!;

    public SeriesCreationTests()
    {
        _connection.Open();
        _db = CreateDb(_connection);
        _db.Database.EnsureCreated();
        _previousSettings = SettingsField.GetValue(null);
        SettingsField.SetValue(null, new SettingsDto { StorageFolder = _storage });
        (_command, _state) = CreateCommand(_db);
    }

    private static AppDbContext CreateDb(SqliteConnection connection) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);

    private (SeriesCommandService, SeriesStateService) CreateCommand(AppDbContext db)
    {
        var factory = _services.GetRequiredService<IServiceScopeFactory>();
        var settings = new SettingsService(new ConfigurationBuilder().Build(), factory, db, null!);
        var json = new RensaioJsonService(settings, NullLogger<RensaioJsonService>.Instance);
        var state = new SeriesStateService(db, settings, json, NullLogger<SeriesStateService>.Instance);
        var archive = new ArchiveHelperService(db, NullLogger<ArchiveHelperService>.Instance, null!, settings, null!, state);
        var providers = new SeriesProviderService(db, settings, null!, null!, NullLogger<SeriesProviderService>.Instance, state);
        return (new SeriesCommandService(db, settings, archive, providers,
            NullLogger<SeriesCommandService>.Instance, null!, null!, null!, null!, null!, state, null!, null!, factory), state);
    }

    private static AugmentedResponseDto Request(string path = "Manga", bool separate = false, string? name = null, string language = "en") => new()
    {
        StorageFolderPath = path,
        CreateSeparateInstance = separate,
        DisplayName = name,
        StartChapter = 3.5m,
        Series = [new ProviderSeriesDetails
        {
            Title = "Original Manga", Provider = "Source", Lang = language,
            IsLocal = true, IsStorage = true, UseTitle = true,
            Author = "Author", Description = "Source description", Genre = ["Action"],
            Chapters = [new Chapter { Number = 1 }, new Chapter { Number = 3.5m }, new Chapter { Number = 4 }]
        }]
    };

    [Fact]
    public async Task DefaultCreationStillDeduplicatesByProviderTitleWithoutMovingStorage()
    {
        Guid first = await _command.AddSeriesAsync(Request());
        Guid second = await _command.AddSeriesAsync(Request("DifferentFolder"));
        Assert.Equal(first, second);
        var series = Assert.Single(await _db.Series.ToListAsync());
        Assert.Equal("Manga", series.StoragePath);
        Assert.Equal("Original Manga", series.Title);
        Assert.True(Assert.Single(await _db.SeriesProviders.ToListAsync()).IsTitle);
        Assert.False(Directory.Exists(Path.Combine(_storage, "DifferentFolder")));
    }

    [Fact]
    public async Task SeparateInstancesPersistAliasOriginalMetadataAndChosenStart()
    {
        Guid first = await _command.AddSeriesAsync(Request());
        Guid second = await _command.AddSeriesAsync(Request("Manga-FR", true, "  Manga [FR / Source]  ", "fr"));
        Assert.NotEqual(first, second);
        _db.ChangeTracker.Clear();
        var series = await _db.Series.Include(s => s.Sources).SingleAsync(s => s.Id == second);
        Assert.Equal("Manga [FR / Source]", series.Title);
        Assert.Equal("Manga-FR", series.StoragePath);
        Assert.Equal(3.5m, series.StartFromChapter);
        Assert.Equal("Author", series.Author);
        var provider = Assert.Single(series.Sources);
        Assert.Equal("Original Manga", provider.Title);
        Assert.Equal("fr", provider.Language);
        Assert.False(provider.IsTitle);
        Assert.Equal(2.5m, provider.ContinueAfterChapter);
        Assert.Empty(await _db.Queues.ToListAsync()); // Local fixture: no remote/download jobs run.
        Assert.True(File.Exists(Path.Combine(_storage, "Manga-FR", "rensaio.json")));
    }

    [Fact]
    public async Task AddingAnotherSourceDoesNotOverwriteManualAlias()
    {
        Guid id = await _command.AddSeriesAsync(Request(name: "Manga [EN]"));
        await _command.AddSeriesAsync(Request("AnotherPath", language: "fr"));
        _db.ChangeTracker.Clear();
        var series = await _db.Series.Include(s => s.Sources).SingleAsync();
        Assert.Equal(id, series.Id);
        Assert.Equal("Manga [EN]", series.Title);
        Assert.Equal(2, series.Sources.Count);
        Assert.All(series.Sources, p => Assert.False(p.IsTitle));
    }

    [Fact]
    public async Task ConsolidationPreservesManualTitleUntilExplicitSourceSelection()
    {
        Guid id = await _command.AddSeriesAsync(Request(name: "Manga [EN]"));
        var series = await _db.Series.Include(s => s.Sources).SingleAsync(s => s.Id == id);
        var consolidate = typeof(SeriesCommandService).GetMethod("ConsolidateDBSeriesFromProvidersAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var task = (Task<SeriesEntity>)consolidate.Invoke(_command, [series, series.Sources.ToList(), series.StoragePath, false, 3.5m, CancellationToken.None])!;
        Assert.Equal("Manga [EN]", (await task).Title);
        Assert.Single(series.Sources).IsTitle = true; // Same persistent flag as explicit PATCH UseTitle.
        task = (Task<SeriesEntity>)consolidate.Invoke(_command, [series, series.Sources.ToList(), series.StoragePath, false, 3.5m, CancellationToken.None])!;
        Assert.Equal("Original Manga", (await task).Title);
    }

    [Fact]
    public async Task ImportMetadataReconciliationKeepsAliasAndStartAndExportsThem()
    {
        Guid id = await _command.AddSeriesAsync(Request(name: "Manga [EN]"));
        var series = await _db.Series.Include(s => s.Sources).SingleAsync(s => s.Id == id);
        Assert.Single(series.Sources).Description = "Refreshed source description";
        // The same helper/cursor path used by import reconciliation for an existing instance.
        series.FillSeriesFromProviderSeriesDetails(series.Sources.ToProviderSeriesDetails(), series.StartFromChapter);
        series.Sources.CalculateContinueAfterChapter(series.StartFromChapter);
        await _db.SaveChangesAsync();
        await _state.SyncToRensaioJsonAsync(id);
        _db.ChangeTracker.Clear();
        var persisted = await _db.Series.SingleAsync();
        Assert.Equal("Manga [EN]", persisted.Title);
        Assert.Equal("Refreshed source description", persisted.Description);
        Assert.Equal(3.5m, persisted.StartFromChapter);
        var snapshot = (await Path.Combine(_storage, "Manga").LoadImportSeriesSnapshotFromDirectoryAsync())!;
        Assert.Equal("Manga [EN]", snapshot.DisplayName);
        Assert.Equal(3.5m, snapshot.StartChapter);
    }

    [Fact]
    public async Task RealExportAndRecoveryKeepTwoInstancesAndManualAlias()
    {
        Guid first = await _command.AddSeriesAsync(Request());
        Guid second = await _command.AddSeriesAsync(Request("Manga-FR", true, "Manga [FR]", "fr"));
        // Read the snapshots produced by AddSeriesAsync -> SeriesStateService -> RensaioJsonService,
        // not seeded JSON pretending to be an export.
        var en = (await Path.Combine(_storage, "Manga").LoadImportSeriesSnapshotFromDirectoryAsync())!;
        var fr = (await Path.Combine(_storage, "Manga-FR").LoadImportSeriesSnapshotFromDirectoryAsync())!;
        Assert.Equal(first, en.InstanceId);
        Assert.Null(en.DisplayName);
        Assert.Equal(second, fr.InstanceId);
        Assert.Equal("Manga [FR]", fr.DisplayName);
        Assert.Equal(3.5m, fr.StartChapter);
        using var recoveryConnection = new SqliteConnection("Data Source=:memory:");
        recoveryConnection.Open();
        using var recoveryDb = CreateDb(recoveryConnection);
        recoveryDb.Database.EnsureCreated();
        var (recovery, _) = CreateCommand(recoveryDb);
        foreach (var snapshot in new[] { en, fr })
        {
            var request = Request(snapshot.Path, language: snapshot == fr ? "fr" : "en");
            request.StartChapter = null;
            request.LocalInfo = snapshot;
            Assert.Equal(snapshot.InstanceId, await recovery.AddSeriesAsync(request));
        }
        var recovered = await recoveryDb.Series.Include(s => s.Sources).ToListAsync();
        Assert.Equal(2, recovered.Count);
        var french = recovered.Single(s => s.Id == second);
        Assert.Equal("Manga [FR]", french.Title);
        Assert.Equal(3.5m, french.StartFromChapter);
        Assert.False(Assert.Single(french.Sources).IsTitle);
        Assert.Empty(new SeriesComparer().FindMatchingSeries(recovered.Where(s => s.Id == first), fr));
        Assert.Equal(second, Assert.Single(new SeriesComparer().FindMatchingSeries(recovered, fr)).Id);
    }

    [Fact]
    public void LegacySnapshotMatchingRemainsTitleBased()
    {
        var existing = new SeriesEntity { Id = Guid.NewGuid(), StoragePath = "Other", Sources = [new SeriesProviderEntity { Title = "Original Manga" }] };
        var old = JsonSerializer.Deserialize<ImportSeriesSnapshot>("{\"Title\":\"Original Manga\",\"Path\":\"Manga\"}")!;
        Assert.Null(old.InstanceId);
        Assert.Equal(existing.Id, Assert.Single(new SeriesComparer().FindMatchingSeries([existing], old)).Id);
    }

    [Theory]
    [InlineData("Manga")]
    [InlineData("manga")]
    [InlineData("Manga/Subfolder")]
    public async Task SeparateInstanceRejectsOwnedPathsWithoutChangingExistingSeries(string path)
    {
        Guid id = await _command.AddSeriesAsync(Request());
        await Assert.ThrowsAsync<SeriesStorageConflictException>(() => _command.AddSeriesAsync(Request(path, true, "FR")));
        Assert.Equal(id, Assert.Single(await _db.Series.ToListAsync()).Id);
    }

    [Fact]
    public async Task SeparateInstanceRejectsParentOfAnotherSeries()
    {
        await _command.AddSeriesAsync(Request("Category/Manga"));
        await Assert.ThrowsAsync<SeriesStorageConflictException>(() => _command.AddSeriesAsync(Request("Category", true)));
    }

    [Fact]
    public async Task SeparateInstanceRejectsExistingFolderFileAndLink()
    {
        Directory.CreateDirectory(Path.Combine(_storage, "Occupied"));
        await Assert.ThrowsAsync<SeriesStorageConflictException>(() => _command.AddSeriesAsync(Request("Occupied", true)));
        await File.WriteAllTextAsync(Path.Combine(_storage, "File"), "owned test file");
        await Assert.ThrowsAsync<SeriesStorageConflictException>(() => _command.AddSeriesAsync(Request("File/Child", true)));
        Directory.CreateSymbolicLink(Path.Combine(_storage, "Link"), Path.Combine(_storage, "Occupied"));
        await Assert.ThrowsAsync<ArgumentException>(() => _command.AddSeriesAsync(Request("Link/Child", true)));
        Assert.Empty(await _db.Series.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("../escape")]
    [InlineData("Category/../escape")]
    [InlineData("C:\\escape")]
    [InlineData("/escape")]
    [InlineData("\\server\\share")]
    public async Task SeparateInstanceRejectsUnsafePaths(string path)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _command.AddSeriesAsync(Request(path, true)));
        Assert.Empty(await _db.Series.ToListAsync());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Name\nControl")]
    public async Task InvalidNamesAreRejected(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _command.AddSeriesAsync(Request(name: name)));
        Assert.Empty(await _db.Series.ToListAsync());
    }

    [Fact]
    public async Task TooLongNameAndSeparateExistingIdAreRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _command.AddSeriesAsync(Request(name: new string('x', 251))));
        var request = Request(separate: true);
        request.ExistingSeriesId = Guid.NewGuid();
        await Assert.ThrowsAsync<ArgumentException>(() => _command.AddSeriesAsync(request));
    }

    [Fact]
    public async Task RecoveryRefusesAPathOwnedByAnotherInstance()
    {
        await _command.AddSeriesAsync(Request());
        var request = Request();
        request.LocalInfo.InstanceId = Guid.NewGuid();
        await Assert.ThrowsAsync<SeriesStorageConflictException>(() => _command.AddSeriesAsync(request));
        Assert.Single(await _db.Series.ToListAsync());
    }

    [Fact]
    public async Task ApiReturnsValidationAndConflictStatusCodes()
    {
        var controller = new SeriesController(NullLogger<SeriesController>.Instance, null!, _command, null!, null!, null!, null!, _db, null!, null!);
        Assert.IsType<BadRequestObjectResult>(await controller.AddSeriesAsync(Request(name: " ")));
        await _command.AddSeriesAsync(Request());
        Assert.IsType<ConflictObjectResult>(await controller.AddSeriesAsync(Request("Manga", true)));
        Assert.IsType<ConflictObjectResult>(await controller.AddSeriesAsync(Request("Different", name: "Alias")));
    }

    [Theory]
    [InlineData(3.5)]
    [InlineData(10.0)]
    [InlineData(12.0)]
    [InlineData(12.5)]
    public async Task AutomaticDownloadsRespectStartFromActualCreationState(double chosenStart)
    {
        var request = Request(separate: true, name: "Manga [EN]");
        request.StartChapter = (decimal)chosenStart;
        Guid id = await _command.AddSeriesAsync(request);
        _db.ChangeTracker.Clear();
        var series = await _db.Series.Include(s => s.Sources).SingleAsync(s => s.Id == id);
        var provider = Assert.Single(series.Sources);
        var online = new decimal[] { 1, 3, 3.5m, 4, 5, 10, 12, 12.5m, 13 }.Select(n => new ParsedChapter
        {
            ParsedNumber = n, ParsedName = $"Chapter {n}", RealUrl = $"/chapter/{n}",
            DateUpload = DateTimeOffset.UtcNow, Scanlator = provider.Provider
        }).ToList();
        var downloads = series.GenerateDownloadsFromChapterData(provider, online);
        Assert.Equal(online.Where(c => c.ParsedNumber >= request.StartChapter).Select(c => c.ParsedNumber),
            downloads.Select(d => d.Chapter.ParsedNumber));
        Assert.All(downloads, d =>
        {
            Assert.Equal("Manga [EN]", d.Title);
            Assert.Equal("Original Manga", d.SeriesTitle);
            Assert.Equal(series.StoragePath, d.StoragePath);
        });
    }

    [Fact]
    public async Task UnspecifiedStartKeepsDefaultAutomaticDownloadSelection()
    {
        var request = Request();
        request.StartChapter = null;
        Guid id = await _command.AddSeriesAsync(request);
        _db.ChangeTracker.Clear();
        var series = await _db.Series.Include(s => s.Sources).SingleAsync(s => s.Id == id);
        var provider = Assert.Single(series.Sources);
        Assert.Null(series.StartFromChapter);
        var online = new decimal[] { 1, 12, 12.5m, 13 }.Select(n => new ParsedChapter
        {
            ParsedNumber = n, ParsedName = $"Chapter {n}", RealUrl = $"/chapter/{n}",
            DateUpload = DateTimeOffset.UtcNow, Scanlator = provider.Provider
        }).ToList();
        Assert.Equal(online.Select(c => c.ParsedNumber),
            series.GenerateDownloadsFromChapterData(provider, online).Select(d => d.Chapter.ParsedNumber));
    }

    [Fact]
    public async Task ConcurrentSeparateAddsCannotClaimOnePath()
    {
        using var firstDb = CreateDb(_connection);
        using var secondDb = CreateDb(_connection);
        var (firstCommand, _) = CreateCommand(firstDb);
        var (secondCommand, _) = CreateCommand(secondDb);
        async Task<bool> TryAdd(SeriesCommandService command)
        {
            try
            {
                await command.AddSeriesAsync(Request("Concurrent", true));
                return true;
            }
            catch (SeriesStorageConflictException)
            {
                return false;
            }
        }
        var outcomes = await Task.WhenAll(Task.Run(() => TryAdd(firstCommand)), Task.Run(() => TryAdd(secondCommand)));
        Assert.Single(outcomes, success => success);
        Assert.Single(await _db.Series.ToListAsync());
    }

    public void Dispose()
    {
        SettingsField.SetValue(null, _previousSettings);
        _db.Dispose();
        _connection.Dispose();
        _services.Dispose();
        Directory.Delete(_storage, recursive: true);
    }
}
