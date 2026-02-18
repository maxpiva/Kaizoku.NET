namespace KaizokuBackend.Utils
{
    public static class PathValidationHelper
    {
        public static bool IsValidPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            // Check for path traversal patterns
            if (path.Contains(".."))
                return false;

            // Check for invalid characters
            var invalidChars = Path.GetInvalidPathChars();
            if (path.Any(c => invalidChars.Contains(c)))
                return false;

            return true;
        }

        public static bool IsValidGuid(string? guid)
        {
            return !string.IsNullOrEmpty(guid) && Guid.TryParse(guid, out _);
        }

        /// <summary>
        /// Validates package/APK names used in provider routes.
        /// Package names should only contain alphanumeric, dots, underscores, and hyphens.
        /// </summary>
        public static bool IsValidPackageName(string? packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                return false;

            // Check for path traversal patterns
            if (packageName.Contains("..") || packageName.Contains('/') || packageName.Contains('\\'))
                return false;

            return true;
        }
    }
}
