using System.Text.Json.Serialization;
using KaizokuBackend.Extensions;

namespace KaizokuBackend.Models;

public class Settings : EditableSettings
{
    private string _storageFolder = string.Empty;

    [JsonPropertyName("storageFolder")]
    public string StorageFolder
    {
        get => _storageFolder.SanitizeDirectory();
        set => _storageFolder = value;
    }

}