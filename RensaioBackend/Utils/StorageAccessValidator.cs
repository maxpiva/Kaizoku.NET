namespace RensaioBackend.Utils;

/// <summary>Checks storage access as the running application user, without changing mount permissions.</summary>
public static class StorageAccessValidator
{
    public static void EnsureWritable(string storageFolder)
    {
        try
        {
            // Never create the root: a missing mount must not silently become a local library.
            if (string.IsNullOrWhiteSpace(storageFolder) || !Directory.Exists(storageFolder))
                throw new DirectoryNotFoundException("The configured storage directory does not exist or is inaccessible.");

            string probeDirectory = Path.Combine(storageFolder, ".rensaio-write-check-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(probeDirectory);
            try
            {
                // Test directory traversal and file creation, not just mode bits (ACLs and
                // read-only mounts can deny writes even when the mode looks writable).
                using var probe = new FileStream(Path.Combine(probeDirectory, "probe"),
                    FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose);
                probe.WriteByte(0);
                probe.Flush();
            }
            finally
            {
                Directory.Delete(probeDirectory);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Storage write check failed for '{storageFolder}': {ex.Message} " +
                "Downloads may fail. Check that the storage mount exists, is read-write, and grants " +
                "the application user write and directory traversal (execute) access. In Docker, verify " +
                "PUID/PGID against the host directory ownership and ACLs. UMASK only affects newly created " +
                "files; it cannot grant access to an existing mount. No permissions were changed.", ex);
        }
    }
}
