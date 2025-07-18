namespace KaizokuBackend.Services.Import.KavitaParser
{
    public class Metadata
    {
        public string type { get; set; } = "";
        public string name { get; set; } = "";
        public string description_formatted { get; set; } = "";
        public string description_text { get; set; } = "";
        public string status { get; set; } = "";
        public int? year { get; set; }
        public string ComicImage { get; set; } = "";
        public string publisher { get; set; } = "";
        public int comicId { get; set; }
        public string booktype { get; set; } = "";
        public int total_issues { get; set; }
        public string publication_run { get; set; } = "";
    }

    public class SeriesInfo
    {
        public Metadata metadata { get; set; } = new Metadata();
    }
}
