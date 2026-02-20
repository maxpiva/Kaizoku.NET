using System;
using System.Collections.Generic;
using System.Text;
using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Core.Extensions
{
    public static class RepositoriesExtensions
    {
        public static string GetRelativeVersionFolder(this RepositoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.RepositoryId))
                throw new ArgumentException("RepositoryEntry must have a valid RepositoryId", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Extension.Version))
                throw new ArgumentException("RepositoryEntry must have a valid Extension.Version", nameof(entry));
            var versionFolder = Path.Combine(entry.Extension.GetName(), entry.Extension.Version + "_" + entry.RepositoryId);
            return versionFolder;
        }

        public static RepositoryEntry GetActiveEntry(this RepositoryGroup group)
        {
            if (group == null)
                throw new ArgumentNullException(nameof(group));
            if (group.Entries == null || group.Entries.Count == 0)
                throw new ArgumentException("RepositoryGroup must have valid Entries", nameof(group));
            if (group.ActiveEntry < 0 || group.ActiveEntry >= group.Entries.Count)
                throw new ArgumentOutOfRangeException(nameof(group), "ActiveEntry index is out of range");
            return group.Entries[group.ActiveEntry];
        }
        public static string GetName(this TachiyomiExtension extension)
        {
            var expectedSuffix = $"-v{extension.Version}.apk";
            if (extension.Apk.EndsWith(expectedSuffix, StringComparison.Ordinal))
            {
                return extension.Apk.Substring(0, extension.Apk.Length - expectedSuffix.Length);
            }

            // Fallback: strip only extension if unexpected format
            var nameWithoutExt = Path.GetFileNameWithoutExtension(extension.Apk);
            var idx = nameWithoutExt.LastIndexOf("-v", StringComparison.Ordinal);
            return idx > 0 ? nameWithoutExt.Substring(0, idx) : nameWithoutExt;
        }

    }
}
