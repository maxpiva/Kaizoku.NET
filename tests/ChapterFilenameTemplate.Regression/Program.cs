using System.Text;
using RensaioBackend.Services.Helpers;

int checks = 0;
void Check(bool ok, string message) { checks++; if (!ok) throw new Exception(message); }
string Name(string pattern = "Capítulo {chapter:0000}", decimal? chapter = 12.5m,
    string source = "Manga/World", string language = "en", string sourceId = "provider|1", string title = "Series", string url = "chapter/12.5") =>
    ChapterFilenameTemplate.Render(pattern, title, chapter, "Half chapter", source, language, sourceId, "Team", url);
Check(ChapterFilenameTemplate.Validate("") == null, "default");
Check(ChapterFilenameTemplate.Validate("  ") == null, "whitespace default");
Check(ChapterFilenameTemplate.Validate("{series} {chapter} {title} {source} {language}") == null, "tokens");
foreach (var invalid in new[] { "../{chapter}", "C:\\{chapter}", "{chapter}.cbz", "{chapter}.exe", "{series}", "{unknown} {chapter}", "{chapter:00.0}", "{chapter:000000000}", "{series:000} {chapter}", "{chapter}\n", "{{chapter}}", new string('a', 161) + "{chapter}" })
    Check(ChapterFilenameTemplate.Validate(invalid) != null, "reject " + invalid);
Check(Name().StartsWith("Capítulo 0012.5"), "padding retains fraction");
Check(Name() == Name(), "deterministic");
Check(Name(chapter: 12) != Name(chapter: 12.5m), "fraction distinct");
Check(Name(language: "en") != Name(language: "pt"), "language distinct");
Check(Name(source: "a/b") != Name(source: "a\\b"), "sanitized sources distinct");
Check(Name(sourceId: "provider|1") != Name(sourceId: "provider|2"), "same-name source distinct");
Check(Name(url: "chapter/a") != Name(url: "chapter/b"), "same-number chapter distinct");
var hostile = Name("{series} {chapter}", title: "../../evil.exe\\folder\u0000");
Check(!hostile.Contains('/') && !hostile.Contains('\\') && !hostile.Contains('\u0000'), "path containment");
Check(Path.GetFileName(hostile) == hostile && hostile.EndsWith(".cbz"), "leaf and forced extension");
var longName = Name("{series} {chapter}", title: string.Concat(Enumerable.Repeat("📚シリーズ", 100)));
Check(Encoding.UTF8.GetByteCount(longName) <= 240 && longName.EndsWith(".cbz"), "UTF8 length and extension");
Check(!Name("[{source}] {chapter}").StartsWith('['), "no misleading canonical parser match");
Check(Name(chapter: null).Contains("unknown"), "unknown chapter");
Console.WriteLine($"PASS: {checks} filename-template regression checks");
