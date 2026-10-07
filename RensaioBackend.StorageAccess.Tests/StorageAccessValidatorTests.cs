using RensaioBackend.Utils;
using Xunit;

namespace RensaioBackend.StorageAccess.Tests;

public class StorageAccessValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "rensaio-storage-test-" + Guid.NewGuid().ToString("N"));

    public StorageAccessValidatorTests() => Directory.CreateDirectory(_root);

    [Fact]
    public void WritableStorageLeavesExistingFilesAndNoProbe()
    {
        string existing = Path.Combine(_root, "existing.cbz");
        File.WriteAllText(existing, "existing content");
        UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(_root);

        StorageAccessValidator.EnsureWritable(_root);
        StorageAccessValidator.EnsureWritable(_root);

        Assert.Equal("existing content", File.ReadAllText(existing));
        Assert.Equal(new[] { existing }, Directory.GetFileSystemEntries(_root));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(mode!.Value, File.GetUnixFileMode(_root));
    }

    [Fact]
    public void MissingMountIsNotCreatedAndReportsPermissionGuidance()
    {
        string missing = Path.Combine(_root, "missing-mount");

        IOException error = Assert.Throws<IOException>(() => StorageAccessValidator.EnsureWritable(missing));

        Assert.IsType<DirectoryNotFoundException>(error.InnerException);
        Assert.Contains(missing, error.Message);
        Assert.Contains("PUID/PGID", error.Message);
        Assert.Contains("UMASK", error.Message);
        Assert.Contains("read-write", error.Message);
        Assert.False(Directory.Exists(missing));
        Assert.Empty(Directory.GetFileSystemEntries(_root));
    }

    [Fact]
    public void PermissionDeniedReportsGuidanceWithoutChangingTheDirectory()
    {
        if (OperatingSystem.IsWindows())
            return; // Unix mode fixture; Windows access is exercised by the other tests.

        string denied = Path.Combine(_root, "denied");
        Directory.CreateDirectory(denied);
        UnixFileMode originalMode = File.GetUnixFileMode(denied);
        UnixFileMode readOnlyMode = UnixFileMode.UserRead | UnixFileMode.UserExecute;
        File.SetUnixFileMode(denied, readOnlyMode);
        try
        {
            IOException error = Assert.Throws<IOException>(() => StorageAccessValidator.EnsureWritable(denied));
            Assert.IsType<UnauthorizedAccessException>(error.InnerException);
            Assert.Contains(denied, error.Message);
            Assert.Contains("PUID/PGID", error.Message);
            Assert.Equal(readOnlyMode, File.GetUnixFileMode(denied));
            Assert.Empty(Directory.GetFileSystemEntries(denied));
        }
        finally
        {
            // Restore only this test-created directory so the fixture can be removed.
            File.SetUnixFileMode(denied, originalMode);
        }
    }

    [Fact]
    public void FileInsteadOfStorageDirectoryIsPreserved()
    {
        string file = Path.Combine(_root, "not-a-directory");
        File.WriteAllText(file, "keep me");

        Assert.Throws<IOException>(() => StorageAccessValidator.EnsureWritable(file));

        Assert.Equal("keep me", File.ReadAllText(file));
        Assert.Equal(new[] { file }, Directory.GetFileSystemEntries(_root));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void UnconfiguredStorageReportsActionableError(string storageFolder)
    {
        IOException error = Assert.Throws<IOException>(() => StorageAccessValidator.EnsureWritable(storageFolder));
        Assert.Contains("Storage write check failed", error.Message);
        Assert.IsType<DirectoryNotFoundException>(error.InnerException);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
