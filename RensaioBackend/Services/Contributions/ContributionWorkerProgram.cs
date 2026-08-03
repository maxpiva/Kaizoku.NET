using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Runtime;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;
using RensaioBackend.Services.Search;
using RensaioBackend.Services.Search.Discovery;
using System.Text.Json;

namespace RensaioBackend.Services.Contributions;

public static class ContributionWorkerProgram
{
    public const string ModeArg = "--contribution-worker";

    public static bool IsWorkerInvocation(string[] args) => args.Contains(ModeArg);

    public static async Task<int> RunAsync()
    {
        var protocolWriter = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
        Console.SetOut(Console.Error);
        using ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder
            .SetMinimumLevel(LogLevel.Information)
            .AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace));
        ILogger logger = loggerFactory.CreateLogger("ContributionWorker");

        ContributionWorkerRequest? request;
        try
        {
            using var stdin = new StreamReader(Console.OpenStandardInput());
            string? line = await stdin.ReadLineAsync().ConfigureAwait(false);
            request = line == null ? null : JsonSerializer.Deserialize<ContributionWorkerRequest>(line, ContributionWorkerJson.Options);
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Contribution worker could not parse its assignment.");
            return 2;
        }

        if (request?.Extension?.Entry?.Extension == null || string.IsNullOrWhiteSpace(request.ScratchFolder))
        {
            logger.LogCritical("Contribution worker received an invalid assignment.");
            return 2;
        }

        await using var stdout = protocolWriter;
        ContributionWorkerResponse response;
        string androidFolder = Path.Combine(request.ScratchFolder, "android");
        string tempFolder = Path.Combine(request.ScratchFolder, "temp");
        try
        {
            DiscoveryWorkerRuntime.Initialize(androidFolder, tempFolder, loggerFactory.CreateLogger("Android"));
            DiscoveryWorkerRuntime.ApplyPreferences(request.Preferences ?? new Mihon.ExtensionsBridge.Models.Preferences(), logger);
            response = await CollectAsync(request, tempFolder, logger).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Contribution worker assignment failed.");
            response = new ContributionWorkerResponse { Success = false, Error = ex.Message };
        }
        finally
        {
            DiscoveryWorkerRuntime.Shutdown(logger);
        }

        await stdout.WriteLineAsync(ContributionWorkerJson.LinePrefix +
            JsonSerializer.Serialize(response, ContributionWorkerJson.Options)).ConfigureAwait(false);
        return response.Success ? 0 : 1;
    }

    private static async Task<ContributionWorkerResponse> CollectAsync(
        ContributionWorkerRequest request, string tempFolder, ILogger logger)
    {
        var extension = request.Extension;
        string package = extension.Entry.Extension.Package;
        IExtensionInterop interop = DiscoveryWorkerRuntime.LoadExtension(
            extension.Entry, extension.Folder, request.ScratchFolder, tempFolder, logger);
        TimeSpan timeout = request.SourceTimeoutSeconds > 0
            ? TimeSpan.FromSeconds(request.SourceTimeoutSeconds)
            : SourceTimeout.DefaultTimeout;
        var requestedSources = request.SourceIds.ToHashSet();
        List<ISourceInterop> sources = interop.Sources.Where(s => requestedSources.Contains(s.Id)).ToList();
        if (sources.Count != requestedSources.Count)
        {
            long[] missing = requestedSources.Except(sources.Select(s => s.Id)).ToArray();
            throw new InvalidOperationException($"Sources {string.Join(',', missing)} were not found in {package}.");
        }

        var records = new List<ContributionRecordV1>();
        foreach (ISourceInterop source in sources)
        {
            MangaList popular = await SourceTimeout.RunAsync(
                c => source.GetPopularAsync(1, c), timeout, CancellationToken.None).ConfigureAwait(false);
            MangaList? latest = null;
            if (source.SupportsLatest)
            {
                latest = await SourceTimeout.RunAsync(
                    c => source.GetLatestAsync(1, c), timeout, CancellationToken.None).ConfigureAwait(false);
            }

            var popularByUrl = (popular?.Mangas ?? []).Where(m => m != null && !string.IsNullOrWhiteSpace(m.Url))
                .GroupBy(m => m.Url, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var latestByUrl = (latest?.Mangas ?? []).Where(m => m != null && !string.IsNullOrWhiteSpace(m.Url))
                .GroupBy(m => m.Url, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (string url in popularByUrl.Keys.Concat(latestByUrl.Keys).Distinct(StringComparer.Ordinal))
            {
                bool inPopular = popularByUrl.TryGetValue(url, out ParsedManga? popularManga);
                bool inLatest = latestByUrl.TryGetValue(url, out ParsedManga? latestManga);
                ParsedManga manga = popularManga ?? latestManga!;
                records.Add(ContributionRecordV1.FromManga(package, source.Id, source.Name, source.Language,
                    manga, inPopular, inLatest));
            }
        }

        return new ContributionWorkerResponse
        {
            Success = true,
            Batch = new ContributionBatchV1 { Records = records }
        };
    }
}
