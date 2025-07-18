namespace KaizokuBackend.Models;

public class SuwayomiGraphQLSeriesResult
{
    public List<SuwayomiSeries> Mangas { get; set; } = [];
    public bool HasNextPage { get; set; } = false;
}