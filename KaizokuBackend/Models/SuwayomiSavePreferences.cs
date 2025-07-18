namespace KaizokuBackend.Models;

public class SuwayomiSavePreferences
{
    public string name { get; set; } = "";
    public string lang { get; set; } = "";
    public Dictionary<string, object> Props { get; set; } = [];
}