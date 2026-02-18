using KaizokuBackend.Models;
using KaizokuBackend.Services.Naming;
using Xunit;

namespace KaizokuBackend.Tests.Services.Naming;

/// <summary>
/// Tests for TemplateParser functionality including template parsing, validation, and preview generation
/// </summary>
public class TemplateParserTests
{
    private readonly ITemplateParser _parser;
    private readonly Settings _settings;

    public TemplateParserTests()
    {
        _parser = new TemplateParser();
        _settings = new Settings
        {
            CategorizedFolders = true
        };
    }

    private static TemplateVariables CreateSampleVariables() => new(
        Series: "One Piece",
        Chapter: 1089m,
        Volume: 105,
        Provider: "MangaDex",
        Scanlator: "TCBScans",
        Language: "en",
        Title: "The Beginning",
        UploadDate: new DateTime(2024, 6, 15),
        Type: "Manga",
        MaxChapter: 1200m
    );

    #region ParseFileName Tests

    [Fact]
    public void ParseFileName_BasicTemplate_ReturnsCorrectResult()
    {
        // Arrange
        var template = "{Series} - {Chapter}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece - 1089", result);
    }

    [Fact]
    public void ParseFileName_WithPaddingFormat_AppliesPaddingCorrectly()
    {
        // Arrange
        var template = "{Series} - Chapter {Chapter:000}";
        var vars = CreateSampleVariables() with { Chapter = 5m };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece - Chapter 005", result);
    }

    [Fact]
    public void ParseFileName_WithFourDigitPadding_AppliesPaddingCorrectly()
    {
        // Arrange
        var template = "{Chapter:0000}";
        var vars = CreateSampleVariables() with { Chapter = 42m };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("0042", result);
    }

    [Fact]
    public void ParseFileName_AutoPadsBasedOnMaxChapter_WorksCorrectly()
    {
        // Arrange
        var template = "{Series} {Chapter}";
        var vars = CreateSampleVariables() with { Chapter = 5m, MaxChapter = 9999m };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Should pad to 4 digits based on MaxChapter 9999
        Assert.Equal("One Piece 0005", result);
    }

    [Fact]
    public void ParseFileName_DecimalChapter_FormatsCorrectly()
    {
        // Arrange
        var template = "{Series} {Chapter}";
        var vars = CreateSampleVariables() with { Chapter = 123.5m };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece 0123.5", result);
    }

    [Fact]
    public void ParseFileName_SanitizesInvalidCharacters_ReplacesCorrectly()
    {
        // Arrange
        var template = "{Series} {Chapter}";
        var vars = CreateSampleVariables() with { Series = "Test:Series/With*Invalid?Chars" };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Invalid filename characters should be removed/replaced
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("/", result);
        Assert.DoesNotContain("*", result);
        Assert.DoesNotContain("?", result);
    }

    [Fact]
    public void ParseFileName_RemovesParentheses_FromSeriesName()
    {
        // Arrange
        var template = "{Series}";
        var vars = CreateSampleVariables() with { Series = "Series (Name)" };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Parentheses should be removed by SanitizeForTemplate
        Assert.Equal("Series Name", result);
    }

    [Fact]
    public void ParseFileName_WithAllVariables_FormatsCorrectly()
    {
        // Arrange
        var template = "[{Provider}][{Language}] {Series} - Ch.{Chapter} Vol.{Volume:00} {Title} {Year}-{Month}-{Day}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Contains("MangaDex", result);
        Assert.Contains("en", result);
        Assert.Contains("One Piece", result);
        Assert.Contains("1089", result);
        Assert.Contains("105", result);
        Assert.Contains("2024", result);
        Assert.Contains("06", result);
        Assert.Contains("15", result);
    }

    [Fact]
    public void ParseFileName_WithProvider_FormatsWithScanlator()
    {
        // Arrange
        var template = "[{Provider}]";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Should include scanlator if different from provider
        Assert.Contains("MangaDex", result);
        Assert.Contains("TCBScans", result);
    }

    [Fact]
    public void ParseFileName_WithScanlatorVariable_ReturnsScanlator()
    {
        // Arrange
        var template = "{Scanlator}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("TCBScans", result);
    }

    [Fact]
    public void ParseFileName_WithNullScanlator_ReturnsEmptyString()
    {
        // Arrange
        var template = "{Series} [{Scanlator}]";
        var vars = CreateSampleVariables() with { Scanlator = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece []", result);
    }

    [Fact]
    public void ParseFileName_WithTitle_FormatsWithBrackets()
    {
        // Arrange
        var template = "{Series} {Title}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Title should be wrapped in parentheses
        Assert.Contains("(The Beginning)", result);
    }

    [Fact]
    public void ParseFileName_WithChapterTitle_SkipsTitle()
    {
        // Arrange
        var template = "{Series} {Title}";
        var vars = CreateSampleVariables() with { Title = "Chapter 5" };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Should skip title if it contains "chapter"
        Assert.Equal("One Piece", result.Trim());
    }

    [Fact]
    public void ParseFileName_WithNullVolume_ReturnsEmptyForVolume()
    {
        // Arrange
        var template = "{Series} Vol.{Volume}";
        var vars = CreateSampleVariables() with { Volume = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece Vol.", result);
    }

    [Fact]
    public void ParseFileName_WithNullChapter_ReturnsEmptyForChapter()
    {
        // Arrange
        var template = "{Series} {Chapter}";
        var vars = CreateSampleVariables() with { Chapter = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece", result.Trim());
    }

    [Fact]
    public void ParseFileName_TrimsMultipleSpaces_ToSingleSpace()
    {
        // Arrange
        var template = "{Series}    {Chapter}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert - Multiple spaces should be replaced with single space
        Assert.DoesNotContain("  ", result);
    }

    [Fact]
    public void ParseFileName_VolumeWithDefaultPadding_PadsToTwoDigits()
    {
        // Arrange
        var template = "Vol.{Volume}";
        var vars = CreateSampleVariables() with { Volume = 5 };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("Vol.05", result);
    }

    [Fact]
    public void ParseFileName_VolumeWithCustomPadding_AppliesPadding()
    {
        // Arrange
        var template = "Vol.{Volume:000}";
        var vars = CreateSampleVariables() with { Volume = 5 };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("Vol.005", result);
    }

    #endregion

    #region ParseFolderPath Tests

    [Fact]
    public void ParseFolderPath_SimpleTemplate_ReturnsCorrectPath()
    {
        // Arrange
        var template = "{Type}/{Series}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert
        Assert.Equal(Path.Combine("Manga", "One Piece"), result);
    }

    [Fact]
    public void ParseFolderPath_WithMultipleSegments_BuildsCorrectPath()
    {
        // Arrange
        var template = "{Type}/{Provider}/{Series}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert
        var segments = result.Split(Path.DirectorySeparatorChar);
        Assert.Equal(3, segments.Length);
        Assert.Contains("Manga", segments);
        Assert.Contains("MangaDex", segments);
        Assert.Contains("One Piece", segments);
    }

    [Fact]
    public void ParseFolderPath_SanitizesEachSegment_Correctly()
    {
        // Arrange
        var template = "{Series}";
        var vars = CreateSampleVariables() with { Series = "Test:Series/Invalid" };

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert - Should remove invalid path characters
        Assert.DoesNotContain(":", result);
        Assert.DoesNotContain("/", result);
    }

    [Fact]
    public void ParseFolderPath_WithBackslashes_NormalizesToSystemSeparator()
    {
        // Arrange
        var template = "{Type}\\{Series}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert
        var segments = result.Split(Path.DirectorySeparatorChar);
        Assert.Equal(2, segments.Length);
    }

    [Fact]
    public void ParseFolderPath_WithYear_FormatsCorrectly()
    {
        // Arrange
        var template = "{Year}/{Series}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert
        Assert.Contains("2024", result);
    }

    [Fact]
    public void ParseFolderPath_RemovesEmptySegments_Correctly()
    {
        // Arrange
        var template = "{Series}///{Type}";
        var vars = CreateSampleVariables();

        // Act
        var result = _parser.ParseFolderPath(template, vars, _settings);

        // Assert - Empty segments should be removed
        var segments = result.Split(Path.DirectorySeparatorChar);
        Assert.All(segments, s => Assert.NotEmpty(s));
    }

    #endregion

    #region ValidateTemplate Tests

    [Fact]
    public void ValidateTemplate_WithUnknownVariable_ReturnsError()
    {
        // Arrange
        var template = "{Series} {InvalidVar}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.False(result.IsValid);
        Assert.NotEmpty(result.Errors);
        Assert.Contains(result.Errors, e => e.Contains("InvalidVar"));
    }

    [Fact]
    public void ValidateTemplate_WithValidVariables_ReturnsValid()
    {
        // Arrange
        var template = "{Series} - {Chapter}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateTemplate_MissingSeries_ReturnsWarning()
    {
        // Arrange
        var template = "{Chapter}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.True(result.IsValid); // Still valid, just warned
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Series"));
    }

    [Fact]
    public void ValidateTemplate_MissingChapter_ReturnsWarning()
    {
        // Arrange
        var template = "{Series}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Chapter"));
    }

    [Fact]
    public void ValidateTemplate_FolderPathMissingSeries_ReturnsWarning()
    {
        // Arrange
        var template = "{Type}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FolderPath);

        // Assert
        Assert.True(result.IsValid);
        Assert.NotEmpty(result.Warnings);
        Assert.Contains(result.Warnings, w => w.Contains("Series"));
    }

    [Fact]
    public void ValidateTemplate_EmptyTemplate_ReturnsError()
    {
        // Arrange
        var template = "";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    [Fact]
    public void ValidateTemplate_WhitespaceTemplate_ReturnsError()
    {
        // Arrange
        var template = "   ";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("empty"));
    }

    [Fact]
    public void ValidateTemplate_FileNameWithChapterVariable_NotAllowedInFolderPath()
    {
        // Arrange
        var template = "{Series}/{Chapter}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FolderPath);

        // Assert
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.Contains("Chapter"));
    }

    [Fact]
    public void ValidateTemplate_TracksUsedVariables_Correctly()
    {
        // Arrange
        var template = "{Series} {Chapter} {Volume}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.Equal(3, result.UsedVariables.Count);
        Assert.Contains("Series", result.UsedVariables, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Chapter", result.UsedVariables, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Volume", result.UsedVariables, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTemplate_WithFormatSpecifier_ValidatesVariableName()
    {
        // Arrange
        var template = "{Chapter:000}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.True(result.IsValid);
        Assert.Contains("Chapter", result.UsedVariables, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidateTemplate_FolderPathWithValidVariables_Succeeds()
    {
        // Arrange
        var template = "{Type}/{Series}/{Language}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FolderPath);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion

    #region GetPreview Tests

    [Fact]
    public void GetPreview_FileNameTemplate_ReturnsSampleOutput()
    {
        // Arrange
        var template = "{Series} - {Chapter}";

        // Act
        var result = _parser.GetPreview(template, TemplateType.FileName);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("One Piece", result);
    }

    [Fact]
    public void GetPreview_FolderPathTemplate_ReturnsSampleOutput()
    {
        // Arrange
        var template = "{Type}/{Series}";

        // Act
        var result = _parser.GetPreview(template, TemplateType.FolderPath);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("Manga", result);
        Assert.Contains("One Piece", result);
    }

    [Fact]
    public void GetPreview_ComplexTemplate_GeneratesPreview()
    {
        // Arrange
        var template = "[{Provider}] {Series} - Ch.{Chapter:000} {Title}";

        // Act
        var result = _parser.GetPreview(template, TemplateType.FileName);

        // Assert
        Assert.NotEmpty(result);
        Assert.Contains("MangaDex", result);
        Assert.Contains("One Piece", result);
    }

    [Fact]
    public void GetPreview_WithDecimalChapter_ShowsDecimalInPreview()
    {
        // Arrange - The sample data uses 1089.5
        var template = "{Chapter}";

        // Act
        var result = _parser.GetPreview(template, TemplateType.FileName);

        // Assert - Should show decimal chapter from sample data
        Assert.Contains(".", result);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void ParseFileName_WithNullUploadDate_HandlesGracefully()
    {
        // Arrange
        var template = "{Series} {Year}-{Month}-{Day}";
        var vars = CreateSampleVariables() with { UploadDate = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("One Piece", result.Trim()); // Date parts should be empty
    }

    [Fact]
    public void ParseFileName_WithNullType_UsesDefault()
    {
        // Arrange
        var template = "{Type}";
        var vars = CreateSampleVariables() with { Type = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("Manga", result); // Should default to "Manga"
    }

    [Fact]
    public void ParseFileName_LanguageVariable_ConvertedToLowercase()
    {
        // Arrange
        var template = "{Language}";
        var vars = CreateSampleVariables() with { Language = "EN" };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Equal("en", result);
    }

    [Fact]
    public void ParseFileName_TitleWithParentheses_ReplacedWithBrackets()
    {
        // Arrange
        var template = "{Title}";
        var vars = CreateSampleVariables() with { Title = "Title (with parens)" };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Contains("[with parens]", result);
        Assert.DoesNotContain("(with parens)", result);
    }

    [Fact]
    public void ParseFileName_ProviderWithHyphen_ReplacedWithUnderscore()
    {
        // Arrange
        var template = "{Provider}";
        var vars = CreateSampleVariables() with { Provider = "Manga-Site", Scanlator = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Contains("Manga_Site", result);
    }

    [Fact]
    public void ParseFileName_ProviderWithBrackets_ReplacedWithParens()
    {
        // Arrange
        var template = "{Provider}";
        var vars = CreateSampleVariables() with { Provider = "[Provider]", Scanlator = null };

        // Act
        var result = _parser.ParseFileName(template, vars, _settings);

        // Assert
        Assert.Contains("(Provider)", result);
        Assert.DoesNotContain("[Provider]", result);
    }

    [Fact]
    public void ValidateTemplate_CaseInsensitive_RecognizesVariables()
    {
        // Arrange
        var template = "{SERIES} {chapter}";

        // Act
        var result = _parser.ValidateTemplate(template, TemplateType.FileName);

        // Assert
        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    #endregion
}
