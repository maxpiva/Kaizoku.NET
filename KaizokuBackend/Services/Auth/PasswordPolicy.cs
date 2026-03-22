using System.Text.RegularExpressions;

namespace KaizokuBackend.Services.Auth
{
    /// <summary>
    /// Centralized password policy enforcement.
    /// Rules: min 8 characters, at least one letter, at least one number.
    /// </summary>
    public static partial class PasswordPolicy
    {
        public const int MinLength = 8;

        /// <summary>
        /// Validates a password against the policy. Returns null if valid, or an error message if not.
        /// </summary>
        public static string? Validate(string? password)
        {
            if (string.IsNullOrWhiteSpace(password))
                return "Password is required.";

            if (password.Length < MinLength)
                return $"Password must be at least {MinLength} characters.";

            if (!HasLetterRegex().IsMatch(password))
                return "Password must contain at least one letter.";

            if (!HasDigitRegex().IsMatch(password))
                return "Password must contain at least one number.";

            return null;
        }

        /// <summary>
        /// Returns true if the password meets the current policy requirements.
        /// Used at login time to detect legacy weak passwords that need updating.
        /// </summary>
        public static bool MeetsPolicy(string? password)
        {
            return Validate(password) == null;
        }

        [GeneratedRegex(@"[a-zA-Z]")]
        private static partial Regex HasLetterRegex();

        [GeneratedRegex(@"\d")]
        private static partial Regex HasDigitRegex();
    }
}
