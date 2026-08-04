namespace RensaioBackend.Models.Enums;

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
    DailyUpdate,
    StatusCheck,
    ScrobblerSync,
    VerifyAllSeries,
    PrepareDiscovery,
    CollectContributions,
    UploadContributions
}