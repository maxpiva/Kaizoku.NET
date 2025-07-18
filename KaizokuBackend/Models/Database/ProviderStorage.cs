using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

namespace KaizokuBackend.Models.Database;

[Index(nameof(Name), nameof(Lang))]
public class ProviderStorage
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string ApkName { get; set; }
    public required string PkgName { get; set; }
    public required string Name { get; set; } = string.Empty;
    public required string Lang { get; set; } = string.Empty;
    public long VersionCode { get; set; } = 0;
    public bool IsStorage { get; set; } = true;
    public bool IsDisabled { get; set; } = false;
    public List<Mappings> Mappings { get; set; } = [];

}

public class Mappings
{
    public SuwayomiSource? Source { get; set; }
    public List<SuwayomiPreference> Preferences { get; set; } = [];
}