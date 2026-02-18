using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class EditableSettingsDto
{
    [JsonPropertyName("preferredLanguages")]
    public string[] PreferredLanguages { get; set; } = [];
    [JsonPropertyName("mihonRepositories")]
    public string[] MihonRepositories { get; set; } = [];
    [JsonPropertyName("numberOfSimultaneousDownloads")]
    public int NumberOfSimultaneousDownloads { get; set; } = 10;

    [JsonPropertyName("numberOfSimultaneousSearches")]
    public int NumberOfSimultaneousSearches { get; set; } = 10;
    [JsonPropertyName("chapterDownloadFailRetryTime")]
    public TimeSpan ChapterDownloadFailRetryTime { get; set; } = TimeSpan.FromMinutes(30);
    [JsonPropertyName("chapterDownloadFailRetries")]
    public int ChapterDownloadFailRetries { get; set; } = 144;

    [JsonPropertyName("perTitleUpdateSchedule")]
    public TimeSpan PerTitleUpdateSchedule { get; set; }
    [JsonPropertyName("perSourceUpdateSchedule")]
    public TimeSpan PerSourceUpdateSchedule { get; set; }
    [JsonPropertyName("extensionsCheckForUpdateSchedule")]
    public TimeSpan ExtensionsCheckForUpdateSchedule { get; set; }

    [JsonPropertyName("categorizedFolders")]
    public bool CategorizedFolders { get; set; } = true;
    [JsonPropertyName("categories")]
    public string[] Categories { get; set; } = [];
    [JsonPropertyName("flareSolverrEnabled")]
    public bool FlareSolverrEnabled { get; set; }
    [JsonPropertyName("flareSolverrUrl")]
    public string FlareSolverrUrl { get; set; } = "http://localhost:8191";
    [JsonPropertyName("flareSolverrTimeout")]
    public TimeSpan FlareSolverrTimeout { get; set; } = TimeSpan.FromSeconds(60);
    [JsonPropertyName("flareSolverrSessionTtl")]
    public TimeSpan FlareSolverrSessionTtl { get; set; } = TimeSpan.FromMinutes(15);
    [JsonPropertyName("flareSolverrAsResponseFallback")]
    public bool FlareSolverrAsResponseFallback { get; set; } = false;

    [JsonPropertyName("isWizardSetupComplete")]
    public bool IsWizardSetupComplete { get; set; } = false;

    [JsonPropertyName("wizardSetupStepCompleted")]
    public int WizardSetupStepCompleted { get; set; } = 0;

    [JsonPropertyName("numberOfSimultaneousDownloadsPerProvider")]
    public int NumberOfSimultaneousDownloadsPerProvider { get; set; } = 3;

    [JsonPropertyName("socksProxyEnabled")]
    public bool SocksProxyEnabled { get; set; } = false;
    [JsonPropertyName("socksProxyVersion")]
    public int SocksProxyVersion { get; set; } = 5;
    [JsonPropertyName("socksProxyHost")]
    public string SocksProxyHost { get; set; } = "";
    [JsonPropertyName("socksProxyPort")]
    public int SocksProxyPort { get; set; } = 0;
    [JsonPropertyName("socksProxyUsername")]
    public string SocksProxyUsername { get; set; } = "";
    [JsonPropertyName("socksProxyPassword")]
    public string SocksProxyPassword { get; set; } = "";
    [JsonPropertyName("nsfwVisibility")]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public NsfwVisibility NsfwVisibility { get; set; } = NsfwVisibility.HideByDefault;

    [JsonPropertyName("fileNameTemplate")]
    public string FileNameTemplate { get; set; } = "[{Provider}][{Language}] {Series} {Chapter}";

    [JsonPropertyName("folderTemplate")]
    public string FolderTemplate { get; set; } = "{Type}/{Series}";

    [JsonPropertyName("outputFormat")]
    public int OutputFormat { get; set; } = 0; // 0 = CBZ, 1 = PDF

    [JsonPropertyName("includeChapterTitle")]
    public bool IncludeChapterTitle { get; set; } = false;
}
public enum NsfwVisibility
{
    AlwaysHide = 0,
    HideByDefault = 1,
    Show = 2,
}