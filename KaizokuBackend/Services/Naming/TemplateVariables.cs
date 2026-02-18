namespace KaizokuBackend.Services.Naming;

/// <summary>
/// Variables available for template expansion in file and folder naming
/// </summary>
public record TemplateVariables(
    string Series,
    decimal? Chapter,
    int? Volume,
    string Provider,
    string? Scanlator,
    string Language,
    string? Title,       // Chapter title
    DateTime? UploadDate,
    string? Type,        // Manga, Manhwa, etc.
    decimal? MaxChapter
);
