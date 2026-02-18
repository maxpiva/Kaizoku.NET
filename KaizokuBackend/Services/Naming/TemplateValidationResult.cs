namespace KaizokuBackend.Services.Naming;

/// <summary>
/// Result of template validation
/// </summary>
public record TemplateValidationResult(
    bool IsValid,
    List<string> Errors,
    List<string> Warnings,
    List<string> UsedVariables
);

/// <summary>
/// Type of template being validated
/// </summary>
public enum TemplateType
{
    FileName,
    FolderPath
}
