using KaizokuBackend.Models.Database;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Models;

namespace KaizokuBackend.Extensions
{
    public static class PackageExtensions
    {

        public static (string packageName, string sourceId) GetPackageAndSourceId(this string mihonproviderid)
        {
            if (string.IsNullOrEmpty(mihonproviderid))
                throw new ArgumentException("Mihon provider id cannot be null or empty", nameof(mihonproviderid));
            string[] split = mihonproviderid.Split('|');
            if (split.Length < 2)
                throw new ArgumentException("Mihon provider id must be in the format 'packageName|sourceId'", nameof(mihonproviderid));
            string packageName = split[0];
            string sourceId = split[1];
            return (packageName, sourceId);
        }
        public static string GetMihonProviderId(this TachiyomiExtension ext, TachiyomiSource src)
        {
            return ext.Package+"|" + src.Id;
        }
        public static string GetMihonProviderId(this RepositoryEntry ext, TachiyomiSource src)
        {
            return ext.Extension.Package + "|" + src.Id;
        }
        public static TachiyomiSource? FindSource(this TachiyomiExtension extension, string mihonproviderid)
        {
            (string packageName, string sourceId) = GetPackageAndSourceId(mihonproviderid);
            if (extension.Package != packageName)
                return null;
            return extension.Sources.FirstOrDefault(s => s.Id == sourceId);
        }
        public static (RepositoryEntry? Entry, TachiyomiExtension? Extension, TachiyomiSource? Source) FindSource(this RepositoryGroup group, string mihonproviderid)
        {
            (string packageName, string sourceId) = GetPackageAndSourceId(mihonproviderid);
            RepositoryEntry entry = group.GetActiveEntry();
            TachiyomiExtension extension = entry.Extension;
            if (extension.Package != packageName)
                return (null, null, null);
            TachiyomiSource? source = extension.Sources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return (null, null, null);
            return (entry, extension, source);
        }
        public static (TachiyomiExtension? Extension,TachiyomiSource? Source) FindSource(this IEnumerable<TachiyomiExtension> extensions, string mihonproviderid)
        {
            (string packageName, string sourceId) = GetPackageAndSourceId(mihonproviderid);
            TachiyomiExtension? extension = extensions.FirstOrDefault(e => e.Package == packageName);
            if (extension == null)
                return (null, null);
            TachiyomiSource? source = extension.Sources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return (null, null);
            return (extension, source);
        }

        public static (TachiyomiRepository? Repository, TachiyomiExtension? Extension, TachiyomiSource? Source) FindSource(this IEnumerable<TachiyomiRepository> repos, string mihonproviderid)
        {
            (string packageName, string sourceId) = GetPackageAndSourceId(mihonproviderid);
            TachiyomiExtension? extension = repos.SelectMany(a=>a.Extensions).FirstOrDefault(b => b.Package == packageName);
            if (extension == null)
                return (null, null, null);
            TachiyomiSource? source = extension.Sources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return (null, null, null);
            TachiyomiRepository repo = repos.First(r => r.Extensions.Contains(extension));
            return (repo, extension, source);
        }
        public static TachiyomiSource? BestMatch(this TachiyomiExtension ext, string name, string? language = null)
        {
            List<TachiyomiSource> sources = ext.Sources.Where(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (sources.Count == 0)
            {
                if (ext.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    sources = ext.Sources;
                }
            }
            if (sources.Count == 0)
                return null;
            if (string.IsNullOrWhiteSpace(language))
            {
                TachiyomiSource? src = ext.Sources.FirstOrDefault(a=>a.Language=="all");
                if (src == null)
                    src = ext.Sources.First();
                return src;
            }
            List<TachiyomiSource> filtered = sources.Where(s => s.Language.Equals(language, StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count==0)
                filtered = sources.Where(s => s.Language.Equals("all", StringComparison.OrdinalIgnoreCase)).ToList();
            if (filtered.Count == 0)
                return null;
            return filtered.First();
        }
        public static ProviderStorageEntity BestMatch(this List<ProviderStorageEntity> providers, List<RepositoryGroup> local_repo, string name, string? language = null)
        {
            foreach (RepositoryGroup group in local_repo)
            {
                RepositoryEntry entry = group.GetActiveEntry();
                TachiyomiExtension ext = entry.Extension;
                TachiyomiSource? src = ext.BestMatch(name, language);
                if (src != null)
                {
                    string mihonProviderId = ext.GetMihonProviderId(src);
                    ProviderStorageEntity? provider = providers.FirstOrDefault(p => p.MihonProviderId == mihonProviderId);
                    if (provider != null)
                        return provider;
                }
            }
            return null;
        }
        public static (TachiyomiRepository? Repository, TachiyomiExtension? Extension, TachiyomiSource? Source) FindFromNameAndLanguage(this IEnumerable<TachiyomiRepository> repos, string name, string? language = null)
        {
            foreach(TachiyomiRepository repo in repos)
            {
                foreach(TachiyomiExtension extension in repo.Extensions)
                {
                    TachiyomiSource? src = extension.BestMatch(name, language);
                    if (src!=null)
                        return (repo, extension, src);
                }
            }
            return (null, null, null);
        }

        public static (RepositoryGroup? Group, TachiyomiExtension? Extension, TachiyomiSource? Source) FindSource(this IEnumerable<RepositoryGroup> groups, string mihonproviderid)
        {
            (string packageName, string sourceId) = GetPackageAndSourceId(mihonproviderid);
            RepositoryGroup? group = groups.FirstOrDefault(e => e.GetActiveEntry().Extension.Package == packageName);
            if (group == null)
                return (null, null, null);
            TachiyomiExtension? extension = group.GetActiveEntry().Extension;
            TachiyomiSource? source = extension.Sources.FirstOrDefault(s => s.Id == sourceId);
            if (source == null)
                return (null, null, null);
            return (group, extension, source);
        }
    }
}
