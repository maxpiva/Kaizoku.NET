namespace KaizokuBackend.Services.Import.Models
{
    public class NewDetectedChapter
    {
        public string Provider { get; set; } = string.Empty;
        public string ProviderThumb { get; set; } = string.Empty;
        public string Scanlator { get; set; } = string.Empty;
        public string Title { get; set; } = "";
        public string Language { get; set; } = "";
        public decimal? Chapter { get; set; } = 0;
        public bool IsKaizokuMatch { get; set; }
        public string Filename { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime CreationDate { get; set; }
    }

}
