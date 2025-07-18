using System.Text.Json.Serialization;

namespace KaizokuBackend.Models;

public class ArchiveIntegrityResult
{
    [JsonPropertyName("result")]
    public ArchiveResult Result { get; set; }
    [JsonPropertyName("filename")]

    public string Filename { get; set; } = "";
}