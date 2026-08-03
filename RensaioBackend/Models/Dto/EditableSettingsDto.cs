using System.Text.Json.Serialization;

namespace RensaioBackend.Models.Dto;

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

    /// <summary>
    /// Upper bound on how many not-installed extensions a single discovery
    /// ("search more sources") request will shadow-load and search.
    /// </summary>
    [JsonPropertyName("maxDiscoverySearchExtensions")]
    public int MaxDiscoverySearchExtensions { get; set; } = 0;

    /// <summary>
    /// When true (default), discovery searches classload and search not-installed extensions in
    /// short-lived worker processes so their memory is returned to the OS afterwards and a crashing
    /// extension cannot take down the backend. When false (or when a worker cannot be spawned),
    /// the legacy in-process shadow-load path is used.
    /// </summary>
    [JsonPropertyName("discoverySearchWorkersEnabled")]
    public bool DiscoverySearchWorkersEnabled { get; set; } = true;

    /// <summary>
    /// How many extensions a single discovery worker process handles before it exits and is
    /// replaced (bounds per-worker memory growth, since loaded JARs cannot be unloaded).
    /// </summary>
    [JsonPropertyName("discoveryWorkerBatchSize")]
    public int DiscoveryWorkerBatchSize { get; set; } = 10;

    /// <summary>
    /// Maximum number of discovery worker processes running concurrently.
    /// </summary>
    [JsonPropertyName("maxDiscoveryWorkers")]
    public int MaxDiscoveryWorkers { get; set; } = 2;

    /// <summary>
    /// When true (default), discovery workers stay resident between sweeps with their classloaded
    /// extensions warm, so repeat searches skip the classload entirely. When false, every worker
    /// exits after its batch (pre-warm-pool behavior).
    /// </summary>
    [JsonPropertyName("discoveryWarmPoolEnabled")]
    public bool DiscoveryWarmPoolEnabled { get; set; } = true;

    /// <summary>
    /// How long an idle warm discovery worker stays resident before being recycled.
    /// </summary>
    [JsonPropertyName("discoveryWorkerIdleTimeout")]
    public TimeSpan DiscoveryWorkerIdleTimeout { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>Enables the local contribution collector. Disabled unless explicitly opted in.</summary>
    [JsonPropertyName("contributionCollectorEnabled")]
    public bool ContributionCollectorEnabled { get; set; } = false;

    /// <summary>Extension package names the collector is allowed to load.</summary>
    [JsonPropertyName("contributionPackageAllowlist")]
    public string[] ContributionPackageAllowlist { get; set; } = [];

    /// <summary>Source identities in package|numericSourceId form the collector is allowed to query.</summary>
    [JsonPropertyName("contributionSourceAllowlist")]
    public string[] ContributionSourceAllowlist { get; set; } = [];

    /// <summary>
    /// Master toggle: when true (default), every search automatically also sweeps eligible
    /// not-installed sources and streams those results into the same list. When false, search is
    /// installed-sources only and no discovery affordance is shown.
    /// </summary>
    [JsonPropertyName("discoveryIncludeInSearch")]
    public bool DiscoveryIncludeInSearch { get; set; } = true;

    /// <summary>
    /// When true (default), a low-priority background job pre-downloads and pre-converts the
    /// artifacts of all eligible not-installed extensions so the first discovery search never pays
    /// the cold download+dex2jar cost.
    /// </summary>
    [JsonPropertyName("discoveryPrecacheEnabled")]
    public bool DiscoveryPrecacheEnabled { get; set; } = true;
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

    // --- Health Monitoring Thresholds ---

    [JsonPropertyName("releaseCadenceMultiplierYellow")]
    public double ReleaseCadenceMultiplierYellow { get; set; } = 2.0;

    [JsonPropertyName("releaseCadenceMultiplierRed")]
    public double ReleaseCadenceMultiplierRed { get; set; } = 5.0;

    [JsonPropertyName("releaseCadenceDefaultDays")]
    public int ReleaseCadenceDefaultDays { get; set; } = 7;

    [JsonPropertyName("providerErrorYellowHours")]
    public int ProviderErrorYellowHours { get; set; } = 48;

    [JsonPropertyName("providerErrorRedHours")]
    public int ProviderErrorRedHours { get; set; } = 168;

    // --- Authentication Settings ---

    [JsonPropertyName("authenticationEnabled")]
    public bool AuthenticationEnabled { get; set; } = false;

    [JsonPropertyName("externalDomain")]
    public string ExternalDomain { get; set; } = string.Empty;
}
public enum NsfwVisibility
{
    AlwaysHide = 0,
    HideByDefault = 1,
    Show = 2,
}