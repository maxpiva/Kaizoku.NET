using KaizokuBackend.Models.Dto;

namespace KaizokuBackend.Services.Naming;

/// <summary>
/// Interface for template parsing and validation
/// </summary>
public interface ITemplateParser
{
    /// <summary>
    /// Parses a file name template with the given variables
    /// </summary>
    string ParseFileName(string template, TemplateVariables vars, SettingsDto settings);

    /// <summary>
    /// Parses a folder path template with the given variables
    /// </summary>
    string ParseFolderPath(string template, TemplateVariables vars, SettingsDto settings);

    /// <summary>
    /// Validates a template string
    /// </summary>
    TemplateValidationResult ValidateTemplate(string template, TemplateType type);

    /// <summary>
    /// Gets a preview of the template with sample data
    /// </summary>
    string GetPreview(string template, TemplateType type);
}
