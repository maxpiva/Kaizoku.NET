using KaizokuBackend.Models.Enums;
using System.Text.Json.Serialization;

namespace KaizokuBackend.Models.Dto;

public class ArchiveIntegrityResultDto
{
    [JsonPropertyName("result")]
    public ArchiveResult Result { get; set; }
    [JsonPropertyName("filename")]

    public string Filename { get; set; } = "";
}