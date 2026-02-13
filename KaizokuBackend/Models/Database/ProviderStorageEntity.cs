using System.ComponentModel.DataAnnotations;
using KaizokuBackend.Models;
using KaizokuBackend.Models.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Models.Database;

[Index(nameof(Name))]
public class ProviderStorageEntity : ProviderSummaryBase
{
    [Key]
    public string MihonProviderId { get; set; }
    private string _name = string.Empty;

    public string Name
    {
        get => _name;
        set => _name = value ?? string.Empty;
    }

    public override string Provider
    {
        get => Name;
        set => Name = value;
    }

    public override string Scanlator { get; set; } = string.Empty;
    public override string Language { get; set; } = string.Empty;
    public string? SourceRepositoryName { get; set; }
    public string? SourceRepositoryId { get; set; }
    public string? SourcePackageName { get; set; }
    public string? SourceSourceId { get; set; }
    public override string? ThumbnailUrl { get; set; } = string.Empty;
    public bool IsNSFW { get; set; }
    public bool SupportLatest { get; set; }
    public override bool IsStorage { get; set; } = true;
    public bool IsEnabled { get; set; } = true;
    public bool IsBroken { get; set; } = false;
    public bool IsDead { get; set; } = false;
}
