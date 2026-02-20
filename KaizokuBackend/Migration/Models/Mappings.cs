namespace KaizokuBackend.Migration.Models
{
    public class Mappings
    {
        public SuwayomiSource? Source { get; set; }
        public List<SuwayomiPreference> Preferences { get; set; } = [];
    }
}
