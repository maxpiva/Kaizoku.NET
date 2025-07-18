namespace KaizokuBackend.Models;

public class SuwayomiPreference
{
    public string type { get; set; } = "";
    public SuwayomiProp props { get; set; } = new SuwayomiProp();
    public string Source { get; set; } = "";

}