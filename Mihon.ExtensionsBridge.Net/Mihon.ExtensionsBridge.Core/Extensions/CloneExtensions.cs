using System;
using System.Collections.Generic;
using System.Text;
using Mihon.ExtensionsBridge.Models;

namespace Mihon.ExtensionsBridge.Core.Extensions
{
    public static class CloneExtensions
    {
        public static TachiyomiSource Clone(this TachiyomiSource source)
        {
            if (source is null)
                throw new ArgumentNullException(nameof(source));
            return new TachiyomiSource
            {
                Id = source.Id,
                Name = source.Name,
                Language = source.Language,
                BaseUrl = source.BaseUrl,
                VersionId = source.VersionId
            };
        }

        public static TachiyomiExtension Clone(this TachiyomiExtension extension)
        {
            if (extension is null)
                throw new ArgumentNullException(nameof(extension));
            return new TachiyomiExtension
            {
                Name = extension.Name,
                Language = extension.Language,
                Package = extension.Package,
                Apk = extension.Apk,
                VersionCode = extension.VersionCode,
                Version = extension.Version,
                Nsfw = extension.Nsfw,
                Sources = extension.Sources.ConvertAll(source => source.Clone())
            };
        }
        public static TachiyomiRepository Clone(this TachiyomiRepository repository)
        {
            if (repository is null)
                throw new ArgumentNullException(nameof(repository));
            return new TachiyomiRepository(repository.Url)
            {
                Id = repository.Id,
                LastUpdatedUTC = repository.LastUpdatedUTC,
                Fingerprint = repository.Fingerprint,
                Name = repository.Name,
                WebSite = repository.WebSite,
                Extensions = repository.Extensions.ConvertAll(extension => extension.Clone())
            };
        }
        public static RepositoryEntry Clone(this RepositoryEntry entry)
        {
            if (entry is null)
                throw new ArgumentNullException(nameof(entry));
            return new RepositoryEntry
            {
                RepositoryId = entry.RepositoryId,
                IsLocal = entry.IsLocal,
                Name = entry.Name,
                Extension = entry.Extension.Clone(),
                DownloadUrl = entry.DownloadUrl,
                DownloadUTC = entry.DownloadUTC,
                Apk = entry.Apk.Clone(),
                Jar = entry.Jar.Clone(),
                //Dll = entry.Dll.Clone(),
                Icon = entry.Icon.Clone(),
                ClassName = entry.ClassName,
            };
        }
        public static RepositoryGroup Clone(this RepositoryGroup group)
        {
            if (group is null)
                throw new ArgumentNullException(nameof(group));
            return new RepositoryGroup
            {
                Name = group.Name,
                Entries = group.Entries.ConvertAll(entry => entry.Clone()),
                ActiveEntry = group.ActiveEntry,
                AutoUpdate = group.AutoUpdate
            };
        }
        public static FileHash Clone(this FileHash fileHash)
        {
            if (fileHash is null)
                throw new ArgumentNullException(nameof(fileHash));
            return new FileHash
            {
                FileName = fileHash.FileName,
                SHA256 = fileHash.SHA256
            };
        }
        public static FileHashVersion Clone(this FileHashVersion fileHashVersion)
        {
            if (fileHashVersion is null)
                throw new ArgumentNullException(nameof(fileHashVersion));
            return new FileHashVersion
            {
                FileName = fileHashVersion.FileName,
                SHA256 = fileHashVersion.SHA256,
                Version = fileHashVersion.Version
            };
        }
    }
}
