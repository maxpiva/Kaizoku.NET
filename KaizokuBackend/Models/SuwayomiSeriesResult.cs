namespace KaizokuBackend.Models;

public class SuwayomiSeriesResult
{
    public List<SuwayomiSeries> MangaList { get; set; } = [];
    public bool HasNextPage { get; set; } = false;
}