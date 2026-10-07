using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RensaioBackend.Data;
using RensaioBackend.Models;
using RensaioBackend.Models.Database;
using RensaioBackend.Models.Dto;
using RensaioBackend.Services.Downloads;
using RensaioBackend.Services.Jobs;
using RensaioBackend.Services.Jobs.Settings;
using RensaioBackend.Services.ReadState;
using RensaioBackend.Services.Series;
using RensaioBackend.Services.Settings;
using Xunit;

namespace RensaioBackend.Lifecycle.Tests;

[CollectionDefinition("Lifecycle", DisableParallelization = true)]
public class LifecycleCollection { }

internal sealed class LifecycleFixture : IAsyncDisposable
{
    public readonly string Scratch = Path.Combine(Path.GetTempPath(), "rensaio-lifecycle-" + Guid.NewGuid());
    public readonly AppDbContext Db;
    public readonly SeriesEntity Series = new() { Id = Guid.NewGuid(), Title = "Fixture", StoragePath = "Fixture" };
    public readonly SeriesProviderEntity Provider = new() { Id = Guid.NewGuid(), Title = "Fixture", Provider = "Fixture", MihonId = "fixture", MihonProviderId = "fixture", IsStorage = true };
    public readonly JobManagementService Jobs;
    public readonly DownloadCommandService Downloads;
    public readonly SeriesCommandService Commands;
    public readonly SettingsDto Settings;
    private readonly object? oldSettings;

    public LifecycleFixture()
    {
        Directory.CreateDirectory(Scratch);
        Settings = new SettingsDto { StorageFolder = Scratch };
        var field = typeof(SettingsService).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!;
        oldSettings = field.GetValue(null);
        field.SetValue(null, Settings);
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite("Data Source=:memory:").Options);
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["runtimeDirectory"] = Scratch }).Build();
        var settings = new SettingsService(config, null!, Db, null!);
        Jobs = new JobManagementService(Db, new JobsSettings(), null!, NullLogger<JobManagementService>.Instance);
        var state = new SeriesStateService(Db, settings, new RensaioJsonService(settings, NullLogger<RensaioJsonService>.Instance), NullLogger<SeriesStateService>.Instance);
        var providers = new SeriesProviderService(Db, settings,
            new JobBusinessService(Jobs, settings, NullLogger<JobBusinessService>.Instance), Jobs, NullLogger<SeriesProviderService>.Instance, state);
        Downloads = new DownloadCommandService(null!, Db, settings, Jobs, null!, null!, config, NullLogger<DownloadCommandService>.Instance, state, null!);
        Commands = new SeriesCommandService(Db, settings, null!, providers, NullLogger<SeriesCommandService>.Instance, Downloads, null!, null!, Jobs, null!, state, null!, null!, null!);
    }

    public async Task InitializeAsync()
    {
        await Db.Database.OpenConnectionAsync();
        await Db.Database.EnsureCreatedAsync();
        Provider.SeriesId = Series.Id;
        Series.Sources.Add(Provider);
        Db.Series.Add(Series);
        await Db.SaveChangesAsync();
        Directory.CreateDirectory(Path.Combine(Scratch, Series.StoragePath));
    }

    public ChapterDownload Chapter(int index = 1) => new() { SeriesId = Series.Id, SeriesProviderId = Provider.Id,
        Index = index, MihonId = "fixture", Provider = "Fixture", StoragePath = Series.StoragePath, Title = "Fixture",
        Chapter = new Mihon.ExtensionsBridge.Models.Extensions.ParsedChapter { Memo = System.Text.Json.JsonSerializer.SerializeToElement(new { }) } };

    public SeriesExtendedDto Update(bool paused) => new() { Id = Series.Id, Title = Series.Title, StoragePath = Series.StoragePath,
        PausedDownloads = paused, Providers = [new ProviderExtendedDto { Id = Provider.Id, IsStorage = true }] };

    public async ValueTask DisposeAsync()
    {
        await Db.DisposeAsync();
        typeof(SettingsService).GetField("_settings", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, oldSettings);
        Directory.Delete(Scratch, true);
    }
}
