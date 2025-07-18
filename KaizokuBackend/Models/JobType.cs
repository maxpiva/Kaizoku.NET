namespace KaizokuBackend.Models;

public enum JobType
{
    ScanLocalFiles,
    InstallAdditionalExtensions,
    SearchProviders,
    ImportSeries,
    GetChapters,
    GetLatest,
    Download,
    UpdateExtensions,
    UpdateAllSeries,
    DailyUpdate
}