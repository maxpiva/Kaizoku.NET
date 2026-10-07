using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
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
using RensaioBackend.Models.Enums;
using RensaioBackend.Services.Helpers;
using RensaioBackend.Services.Opds;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using Xunit;

// One fixture/root also exercises the real SettingsService process-wide cache.
public sealed class CleanupFixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "rensaio-source-cleanup-tests-" + Guid.NewGuid());
    public DbContextOptions<AppDbContext> Options { get; }
    public IConfiguration Config { get; }

    public CleanupFixture()
    {
        Directory.CreateDirectory(Root);
        Options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=" + Path.Combine(Root, "tests.db")).Options;
        Config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["StorageFolder"] = Root, ["runtimeDirectory"] = Root }).Build();
        using var db = new AppDbContext(Options);
        db.Database.EnsureCreated();
        var defaults = new EditableSettingsDto { ReleaseCadenceMultiplierYellow = 2, ReleaseCadenceMultiplierRed = 5 };
        // Seed real settings rows, not the private static SettingsService cache.
        foreach (var property in typeof(EditableSettingsDto).GetProperties())
        {
            var value = property.GetValue(defaults);
            var text = value is string[] strings ? string.Join('|', strings) :
                value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value?.ToString() ?? "";
            db.Settings.Add(new SettingEntity { Name = property.Name, Value = text });
        }
        db.SaveChanges();
    }

    public (SeriesArchiveService Archive, SettingsService Settings) Services(AppDbContext db)
    {
        var settings = new SettingsService(Config, null!, db, null!);
        var json = new RensaioJsonService(settings, NullLogger<RensaioJsonService>.Instance);
        var state = new SeriesStateService(db, settings, json, NullLogger<SeriesStateService>.Instance);
        return (new SeriesArchiveService(db, settings, null!, null!, NullLogger<SeriesArchiveService>.Instance,
            state, new HashCacheService(Config)), settings);
    }

    public async Task<SeriesEntity> SeedAsync(AppDbContext db)
    {
        var series = new SeriesEntity { Id = Guid.NewGuid(), Title = "Test series", StoragePath = Guid.NewGuid().ToString("N") };
        series.Sources.Add(new SeriesProviderEntity { Id = Guid.NewGuid(), SeriesId = series.Id, Provider = "Fallback", Language = "en", Title = series.Title });
        series.Sources.Add(new SeriesProviderEntity { Id = Guid.NewGuid(), SeriesId = series.Id, Provider = "Permanent", Language = "en", Title = series.Title, IsStorage = true });
        Directory.CreateDirectory(Path.Combine(Root, series.StoragePath));
        db.Series.Add(series);
        await db.SaveChangesAsync();
        return series;
    }

    public async Task<Chapter> ProduceAsync(AppDbContext db, SeriesEntity series, SeriesProviderEntity source, decimal number = 1)
    {
        var chapter = new Chapter { Number = number, Name = "Chapter " + number, PageCount = 1, DownloadDate = DateTime.UtcNow };
        chapter.Filename = ArchiveHelperService.MakeFileNameSafe(source.Provider, source.Scanlator, series.Title, source.Language, number, chapter.Name, number) + ".cbz";
        // Use the production ComicInfo producer/serializer and the same ZIP container as downloads.
        using (var output = File.Create(Path.Combine(Root, series.StoragePath, chapter.Filename)))
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create))
        {
            using (var xml = ArchiveHelperService.CreateComicInfo(series, source, chapter, 1).ToStream())
            using (var entry = zip.CreateEntry("ComicInfo.xml").Open()) xml.CopyTo(entry);
            using var image = zip.CreateEntry("page.png", CompressionLevel.NoCompression).Open();
            image.Write(Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII="));
        }
        source.Chapters.Add(chapter);
        db.Touch(source, s => s.Chapters);
        await db.SaveChangesAsync();
        return chapter;
    }

    public void Dispose() { Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); Directory.Delete(Root, true); }
}

public sealed class SourceCleanupTests(CleanupFixture fixture) : IClassFixture<CleanupFixture>
{
    [Fact]
    public async Task RealProducerArchivesCleanOnlyFallbackAndRemainEligible()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db);
        var temporary = s.Sources.First(); var permanent = s.Sources.Last();
        var t = await fixture.ProduceAsync(db, s, temporary); var p = await fixture.ProduceAsync(db, s, permanent);
        string path = Path.Combine(fixture.Root, s.StoragePath, t.Filename!);
        var result = await fixture.Services(db).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.Equal(1, result.Deleted); Assert.Equal(0, result.Skipped);
        Assert.False(File.Exists(path)); Assert.True(File.Exists(Path.Combine(fixture.Root, s.StoragePath, p.Filename!)));
        db.ChangeTracker.Clear();
        var persisted = await db.SeriesProviders.SingleAsync(x => x.Id == temporary.Id);
        Assert.Null(persisted.Chapters.Single().Filename);
        Assert.False(persisted.Chapters.Single().IsDeleted);
        Assert.Null(persisted.Chapters.Single().DownloadDate);
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ImportSeriesSnapshot>(
            await File.ReadAllTextAsync(Path.Combine(fixture.Root, s.StoragePath, "rensaio.json")))!;
        var fallback = Assert.Single(snapshot.Providers, p => p.Provider == temporary.Provider);
        Assert.False(fallback.IsStorage);
        var repeat = await fixture.Services(db).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.Equal(0, repeat.Deleted); Assert.Equal(0, repeat.Skipped);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("corrupt")]
    [InlineData("crc")]
    [InlineData("language")]
    [InlineData("chapter")]
    [InlineData("edition")]
    [InlineData("ambiguous-metadata")]
    [InlineData("shared")]
    [InlineData("symlink")]
    [InlineData("escape")]
    [InlineData("disabled")]
    public async Task UnsafeOrUnrelatedCopiesAreSkipped(string kind)
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db);
        var temporary = s.Sources.First(); var permanent = s.Sources.Last();
        var t = await fixture.ProduceAsync(db, s, temporary);
        if (kind == "language") permanent.Language = "it";
        var p = await fixture.ProduceAsync(db, s, permanent, kind == "chapter" ? 2 : 1);
        var folder = Path.Combine(fixture.Root, s.StoragePath);
        var targetPath = Path.Combine(folder, t.Filename!);
        var permanentPath = Path.Combine(folder, p.Filename!);
        if (kind == "missing") File.Delete(permanentPath);
        if (kind == "corrupt") File.WriteAllText(permanentPath, "not an archive");
        if (kind == "crc")
        {
            var bytes = File.ReadAllBytes(permanentPath);
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII=");
            int offset = Enumerable.Range(0, bytes.Length - png.Length).First(i => bytes.AsSpan(i, png.Length).SequenceEqual(png));
            bytes[offset + 20] ^= 1; File.WriteAllBytes(permanentPath, bytes);
        }
        if (kind == "edition" || kind == "ambiguous-metadata")
        {
            using var zip = ZipFile.Open(permanentPath, ZipArchiveMode.Update);
            using var input = zip.GetEntry("ComicInfo.xml")!.Open();
            var doc = System.Xml.Linq.XDocument.Load(input); input.Close();
            if (kind == "edition") doc.Root!.SetElementValue("Volume", "2");
            else doc.Root!.Add(new System.Xml.Linq.XElement("Number", "2"));
            zip.GetEntry("ComicInfo.xml")!.Delete(); using var output = zip.CreateEntry("ComicInfo.xml").Open(); doc.Save(output);
        }
        if (kind == "shared") { p.Filename = t.Filename; db.Touch(permanent, x => x.Chapters); }
        if (kind == "symlink") { File.Delete(targetPath); File.CreateSymbolicLink(targetPath, permanentPath); }
        if (kind == "escape") { t.Filename = "../" + t.Filename; db.Touch(temporary, x => x.Chapters); }
        if (kind == "disabled") permanent.IsDisabled = true;
        await db.SaveChangesAsync();
        var result = await fixture.Services(db).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(targetPath)); Assert.NotNull(t.Filename);
    }

    [Fact]
    public async Task MissingAndAlreadyEmptySourcesSurviveRealVerification()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db);
        var temporary = s.Sources.First();
        var t = await fixture.ProduceAsync(db, s, temporary);
        File.Delete(Path.Combine(fixture.Root, s.StoragePath, t.Filename!));
        await fixture.Services(db).Archive.VerifyIntegrityAsync(s.Id);
        db.ChangeTracker.Clear();
        var sources = await db.SeriesProviders.Where(x => x.SeriesId == s.Id).ToListAsync();
        Assert.Equal(2, sources.Count); Assert.All(sources, source => Assert.Empty(source.Chapters));
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<ImportSeriesSnapshot>(
            await File.ReadAllTextAsync(Path.Combine(fixture.Root, s.StoragePath, "rensaio.json")))!;
        var fallback = Assert.Single(snapshot.Providers, p => p.Provider == temporary.Provider);
        Assert.False(fallback.IsStorage);
    }

    [Fact]
    public async Task FailedRealDatabaseUpdateRestoresOriginalFileAndChapter()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First();
        var t = await fixture.ProduceAsync(db, s, temporary); await fixture.ProduceAsync(db, s, s.Sources.Last());
        var filename = t.Filename!;
        await db.Database.ExecuteSqlRawAsync("CREATE TRIGGER cleanup_test_failure BEFORE UPDATE ON SeriesProviders BEGIN SELECT RAISE(ABORT, 'cleanup test failure'); END;");
        try { await Assert.ThrowsAsync<DbUpdateException>(() => fixture.Services(db).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id)); }
        finally { await db.Database.ExecuteSqlRawAsync("DROP TRIGGER cleanup_test_failure;"); }
        Assert.True(File.Exists(Path.Combine(fixture.Root, s.StoragePath, filename)));
        db.ChangeTracker.Clear();
        Assert.Equal(filename, (await db.SeriesProviders.SingleAsync(x => x.Id == temporary.Id)).Chapters.Single().Filename);
        Assert.Empty(Directory.GetFiles(Path.Combine(fixture.Root, s.StoragePath), "*.tmp"));
    }

    [Fact]
    public async Task ConcurrentCleanupSerializesAndUsesFreshDatabaseState()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First();
        await fixture.ProduceAsync(db, s, temporary); await fixture.ProduceAsync(db, s, s.Sources.Last());
        using var first = new AppDbContext(fixture.Options); using var second = new AppDbContext(fixture.Options);
        var results = await Task.WhenAll(fixture.Services(first).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id),
            fixture.Services(second).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id));
        Assert.Equal(1, results.Sum(r => r.Deleted)); Assert.Equal(0, results.Sum(r => r.Skipped));
    }

    [Fact]
    public async Task CleanupWaitsForPublisherOrDemotionThenRechecksPermanence()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First();
        await fixture.ProduceAsync(db, s, temporary); await fixture.ProduceAsync(db, s, s.Sources.Last());
        var held = await SeriesMutationLock.AcquireAsync(s.Id);
        using var cleanupDb = new AppDbContext(fixture.Options);
        var cleanup = fixture.Services(cleanupDb).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.False(cleanup.IsCompleted);
        temporary.IsStorage = true; await db.SaveChangesAsync(); held.Dispose();
        await Assert.ThrowsAsync<ArgumentException>(() => cleanup);
        Assert.True(File.Exists(Path.Combine(fixture.Root, s.StoragePath, temporary.Chapters.Single().Filename!)));
    }

    [Fact]
    public async Task CleanupWaitsForRealArchiveProducerAndRechecksPermanentDemotion()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First(); var permanent = s.Sources.Last();
        await fixture.ProduceAsync(db, s, temporary);
        using var cleanupDb = new AppDbContext(fixture.Options);
        var held = await SeriesMutationLock.AcquireAsync(s.Id);
        var cleanup = fixture.Services(cleanupDb).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.False(cleanup.IsCompleted);
        await fixture.ProduceAsync(db, s, permanent); held.Dispose();
        Assert.Equal(1, (await cleanup).Deleted);

        // Restore a real fallback copy, then demote its only permanent backup while
        // cleanup is queued. The cleanup must not use the previous permanence snapshot.
        db.ChangeTracker.Clear();
        s = await db.Series.Include(x => x.Sources).SingleAsync(x => x.Id == s.Id);
        temporary = s.Sources.Single(x => x.Id == temporary.Id); permanent = s.Sources.Single(x => x.Id == permanent.Id);
        temporary.Chapters.Clear(); db.Touch(temporary, x => x.Chapters);
        await fixture.ProduceAsync(db, s, temporary);
        using var demotionCleanupDb = new AppDbContext(fixture.Options);
        held = await SeriesMutationLock.AcquireAsync(s.Id);
        cleanup = fixture.Services(demotionCleanupDb).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.False(cleanup.IsCompleted);
        permanent.IsStorage = false; await db.SaveChangesAsync(); held.Dispose();
        var result = await cleanup;
        Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Skipped);
        Assert.True(File.Exists(Path.Combine(fixture.Root, s.StoragePath, temporary.Chapters.Single().Filename!)));
    }

    [Fact]
    public async Task StaleTrackedPermanentFlagIsReloadedBeforeDeleting()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First(); var permanent = s.Sources.Last();
        await fixture.ProduceAsync(db, s, temporary); await fixture.ProduceAsync(db, s, permanent);
        using (var updateDb = new AppDbContext(fixture.Options))
        {
            var current = await updateDb.SeriesProviders.SingleAsync(x => x.Id == permanent.Id);
            current.IsStorage = false; await updateDb.SaveChangesAsync();
        }
        // db still tracks the old permanent=true entity; cleanup must re-read it.
        var result = await fixture.Services(db).Archive.CleanupSourceDuplicatesAsync(s.Id, temporary.Id);
        Assert.Equal(0, result.Deleted); Assert.Equal(1, result.Skipped);
        Assert.False(permanent.IsStorage);
    }

    [Fact]
    public async Task SymlinkedStorageFolderAndWrongOwnershipAreRejected()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var other = await fixture.SeedAsync(db);
        var archive = fixture.Services(db).Archive;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => archive.CleanupSourceDuplicatesAsync(s.Id, other.Sources.First().Id));
        var realPath = Path.Combine(fixture.Root, s.StoragePath);
        var actual = realPath + "-actual";
        Directory.Move(realPath, actual); Directory.CreateSymbolicLink(realPath, actual);
        await Assert.ThrowsAsync<ArgumentException>(() => archive.CleanupSourceDuplicatesAsync(s.Id, s.Sources.First().Id));
    }

    [Fact]
    public async Task HttpContractEnforcesManagerConfirmationOwnershipAndPermanentTarget()
    {
        using var db = new AppDbContext(fixture.Options);
        var s = await fixture.SeedAsync(db); var temporary = s.Sources.First();
        await fixture.ProduceAsync(db, s, temporary); await fixture.ProduceAsync(db, s, s.Sources.Last());
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddControllers().AddApplicationPart(typeof(SeriesController).Assembly);
        builder.Services.AddScoped(_ => new AppDbContext(fixture.Options));
        builder.Services.AddScoped(sp => fixture.Services(sp.GetRequiredService<AppDbContext>()).Archive);
        builder.Services.AddTransient(sp => new SeriesController(NullLogger<SeriesController>.Instance,
            null!, null!, null!, sp.GetRequiredService<SeriesArchiveService>(), null!, null!, sp.GetRequiredService<AppDbContext>(), null!, null!));
        builder.Services.AddControllers().AddControllersAsServices();
        await using var app = builder.Build();
        app.Use(async (context, next) =>
        {
            context.Items["AuthEnabled"] = true;
            if (Enum.TryParse<UserLevel>(context.Request.Headers["Test-Level"], out var level))
                context.Items["User"] = new UserEntity { Level = level };
            await next(context);
        });
        app.MapControllers(); await app.StartAsync();
        using var client = app.GetTestClient();
        var url = $"/api/serie/{s.Id}/sources/{temporary.Id}/cleanup-duplicates";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(url, new { confirmed = true })).StatusCode);
        client.DefaultRequestHeaders.Add("Test-Level", "User");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(url, new { confirmed = true })).StatusCode);
        client.DefaultRequestHeaders.Remove("Test-Level"); client.DefaultRequestHeaders.Add("Test-Level", "Manager");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync(url, new { confirmed = false })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync($"/api/serie/{s.Id}/sources/{Guid.NewGuid()}/cleanup-duplicates", new { confirmed = true })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/serie/{s.Id}/sources/{s.Sources.Last().Id}/cleanup-duplicates", new { confirmed = true })).StatusCode);
        var response = await client.PostAsJsonAsync(url, new { confirmed = true });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SourceDuplicateCleanupResultDto>();
        Assert.Equal(1, result!.Deleted); Assert.Equal(0, result.Skipped);
        await app.StopAsync();
    }
}
