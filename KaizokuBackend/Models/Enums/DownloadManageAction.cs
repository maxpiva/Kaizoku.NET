namespace KaizokuBackend.Models.Enums;

/// <summary>
/// Actions available for managing downloads in the queue
/// </summary>
public enum DownloadManageAction
{
    /// <summary>Remove a single download from the queue (waiting/failed/completed)</summary>
    Remove,
    /// <summary>Retry a single failed download</summary>
    Retry,
    /// <summary>Clear all downloads with a given status</summary>
    ClearAll,
    /// <summary>Retry all failed downloads</summary>
    RetryAll,
}
