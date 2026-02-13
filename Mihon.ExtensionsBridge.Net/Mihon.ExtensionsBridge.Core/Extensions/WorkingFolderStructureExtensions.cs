using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Text;
using Mihon.ExtensionsBridge.Core.Models;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;


namespace Mihon.ExtensionsBridge.Core.Extensions
{

    public static class WorkingFolderStructureExtensions
    {
        
        public static TemporaryDirectory CreateTemporaryDirectory(this IWorkingFolderStructure folder)
        {
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));
            return new TemporaryDirectory(folder);
        }

        public static string GetExtensionFolder(this IWorkingFolderStructure folder, RepositoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (entry.Extension==null)
                throw new ArgumentException("RepositoryEntry must have a valid Extension", nameof(entry));
            return GetExtensionFolder(folder, entry.Extension);
        }

        public static string GetExtensionFolder(this IWorkingFolderStructure folder, TachiyomiExtension extension)
        {
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));
            if (extension == null)
                throw new ArgumentNullException(nameof(extension));
            var folderName = Path.Combine(folder.ExtensionsFolder, extension.GetName());
            if (!Directory.Exists(folderName))
                Directory.CreateDirectory(folderName);
            return folderName;
        }

        public static string GetActiveExtensionVersionFolder(this IWorkingFolderStructure folder, RepositoryGroup group)
        {
            if (folder == null)
                throw new ArgumentNullException(nameof(folder));
            if (group==null)
                throw new ArgumentNullException(nameof(group));
            RepositoryEntry entry = group.GetActiveEntry();
            return GetExtensionVersionFolder(folder, entry);
        }
        public static string GetExtensionVersionFolder(this IWorkingFolderStructure folder, RepositoryEntry entry)
        {
            if (entry == null)
                throw new ArgumentNullException(nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.RepositoryId))
                throw new ArgumentException("RepositoryEntry must have a valid RepositoryId", nameof(entry));
            if (string.IsNullOrWhiteSpace(entry.Extension.Version))
                throw new ArgumentException("RepositoryEntry must have a valid Extension.Version", nameof(entry));
            var folderName = Path.Combine(folder.ExtensionsFolder, entry.Extension.GetName());
            var versionFolder = Path.Combine(folderName, entry.Extension.Version+"_"+entry.RepositoryId);
            if (!Directory.Exists(versionFolder))
                Directory.CreateDirectory(versionFolder);
            return versionFolder;
        }

        public static async Task SavePreferencesAsync(this IWorkingFolderStructure workingStructure, Mihon.ExtensionsBridge.Models.Preferences prefs, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (prefs == null)
                throw new ArgumentNullException(nameof(prefs));
            if (string.IsNullOrEmpty(workingStructure.WorkingFolder))
                throw new ArgumentException("Working folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));
            string fileName = "preferences.json";
            var outputFile = Path.Combine(workingStructure.WorkingFolder, fileName);
            string json = System.Text.Json.JsonSerializer.Serialize(prefs, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputFile, json, token).ConfigureAwait(false);
        }
        public static async Task<Mihon.ExtensionsBridge.Models.Preferences?> LoadPreferencesAsync(this IWorkingFolderStructure workingStructure, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (string.IsNullOrEmpty(workingStructure.WorkingFolder))
                throw new ArgumentException("Working folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));
            string fileName = "preferences.json";
            var outputFile = Path.Combine(workingStructure.WorkingFolder, fileName);
            if (File.Exists(outputFile))
            {
                string json = await File.ReadAllTextAsync(outputFile, token).ConfigureAwait(false);
                return System.Text.Json.JsonSerializer.Deserialize<Mihon.ExtensionsBridge.Models.Preferences>(json);
            }
            return null;
        }
        public static async Task<List<RepositoryGroup>?> LoadLocalRepositoryGroupsAsync(this IWorkingFolderStructure workingStructure, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (string.IsNullOrEmpty(workingStructure.ExtensionsFolder))
                throw new ArgumentException("Extensions folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));
            var localRepoFile = Path.Combine(workingStructure.ExtensionsFolder, "local_repository.json");
            if (File.Exists(localRepoFile))
            {
                string json = await File.ReadAllTextAsync(localRepoFile, token).ConfigureAwait(false);
                return System.Text.Json.JsonSerializer.Deserialize<List<RepositoryGroup>>(json);
            }
            return new List<RepositoryGroup>();
        }
   
        public static async Task SaveLocalRepositoryGroupsAsync(this IWorkingFolderStructure workingStructure, List<RepositoryGroup> groups, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (string.IsNullOrEmpty(workingStructure.ExtensionsFolder))
                throw new ArgumentException("Extensions folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));
            if (groups == null)
                throw new ArgumentNullException(nameof(groups));
            if (!Directory.Exists(workingStructure.ExtensionsFolder))
                Directory.CreateDirectory(workingStructure.ExtensionsFolder);

            var localRepoFile = Path.Combine(workingStructure.ExtensionsFolder, "local_repository.json");
            var json = System.Text.Json.JsonSerializer.Serialize(groups, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(localRepoFile, json, token).ConfigureAwait(false);
        }


        public static async Task SaveOnlineRepositoryAsync(this IWorkingFolderStructure workingStructure, TachiyomiRepository repo, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (repo == null)
                throw new ArgumentNullException(nameof(repo));
            if (string.IsNullOrEmpty(workingStructure.ExtensionsFolder))
                throw new ArgumentException("Extensions folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));

            if (string.IsNullOrWhiteSpace(repo.Url))
                throw new ArgumentException("Repository URL cannot be null or whitespace.", nameof(repo));
            string fileName = "onlinerepo_" + repo.Id + ".json";
            var outputFile = Path.Combine(workingStructure.ExtensionsFolder, fileName);
            string json = System.Text.Json.JsonSerializer.Serialize(repo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(outputFile, json, token).ConfigureAwait(false);
        }


        public static async Task<TachiyomiRepository?> LoadOnlineRepositoryAsync(this IWorkingFolderStructure workingStructure, string url, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));
            if (string.IsNullOrWhiteSpace(url))
                throw new ArgumentException("URL cannot be null or whitespace.", nameof(url));
            if (string.IsNullOrEmpty(workingStructure.ExtensionsFolder))
                throw new ArgumentException("Extensions folder path cannot be null or empty.", nameof(workingStructure.ExtensionsFolder));

            string fileName = "onlinerepo_" + HashingExtensions.SHA256FromUrl(url) + ".json"; ;
            var filePath = Path.Combine(workingStructure.ExtensionsFolder, fileName);

            if (!File.Exists(filePath))
                return null;

            string json = await File.ReadAllTextAsync(filePath, token).ConfigureAwait(false);
            return System.Text.Json.JsonSerializer.Deserialize<TachiyomiRepository>(json);
        }

        public static async Task<List<TachiyomiRepository>> LoadOnlineRepositoriesAsync(this IWorkingFolderStructure workingStructure, CancellationToken token = default)
        {
            if (workingStructure == null)
                throw new ArgumentNullException(nameof(workingStructure));

            var repositories = new List<TachiyomiRepository>();
            var extensionFiles = Directory.GetFiles(workingStructure.ExtensionsFolder, "onlinerepo_*.json");

            foreach (var file in extensionFiles)
            {
                token.ThrowIfCancellationRequested();
                string json = await File.ReadAllTextAsync(file, token).ConfigureAwait(false);
                var repo = System.Text.Json.JsonSerializer.Deserialize<TachiyomiRepository>(json);
                if (repo != null)
                    repositories.Add(repo);
            }

            return repositories;
        }
    }
}
