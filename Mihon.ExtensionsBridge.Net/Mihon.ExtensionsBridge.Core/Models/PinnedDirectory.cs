using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Models
{
    /// <summary>
    /// An <see cref="ITemporaryDirectory"/> facade over a caller-owned persistent folder.
    /// Unlike <see cref="TemporaryDirectory"/>, disposal is a no-op: the folder holds cache
    /// artifacts (e.g. discovery shadow-load APK/JAR files) that are meant to outlive the work unit.
    /// </summary>
    public sealed class PinnedDirectory : ITemporaryDirectory
    {
        public string Path { get; }

        public PinnedDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));
            Directory.CreateDirectory(path);
            Path = path;
        }

        public void Dispose()
        {
            // Intentionally empty: the folder is a persistent cache owned by the caller.
        }
    }
}
