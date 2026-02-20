using System;
using System.Collections.Generic;
using System.Linq;
using KaizokuBackend.Models;

namespace KaizokuBackend.Extensions;

/// <summary>
/// Helper extensions for consolidating import metadata between scans.
/// </summary>
public static class ImportSeriesResultExtensions
{
    public static bool MergeProvidersFrom(this ImportSeriesResult target, ImportSeriesResult source)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(source);

        bool changed = false;
        List<ImportProviderSnapshot> newProviders = new();

        foreach (ImportProviderSnapshot provider in source.Providers)
        {
            ImportProviderSnapshot? existing = target.Providers.FirstOrDefault(a =>
                a.Provider.Equals(provider.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                a.Language.Equals(provider.Language, StringComparison.InvariantCultureIgnoreCase) &&
                a.Scanlator.Equals(provider.Scanlator, StringComparison.InvariantCultureIgnoreCase))
                ?? target.Providers.FirstOrDefault(a =>
                    a.Provider.Equals(provider.Provider, StringComparison.InvariantCultureIgnoreCase) &&
                    a.Language.Equals(provider.Language, StringComparison.InvariantCultureIgnoreCase))
                ?? target.Providers.FirstOrDefault(a =>
                    a.Provider.Equals(provider.Provider, StringComparison.InvariantCultureIgnoreCase));

            if (existing == null)
            {
                newProviders.Add(provider);
                changed = true;
                continue;
            }

            existing.ChapterList = provider.ChapterList;
            existing.ChapterCount = provider.ChapterCount;
            existing.Archives = provider.Archives;
            existing.IsDisabled = provider.IsDisabled;
            existing.Language = provider.Language;
            existing.Provider = provider.Provider;
            existing.Scanlator = provider.Scanlator;
            if (!string.IsNullOrEmpty(provider.Title))
            {
                existing.Title = provider.Title;
            }
            if (!string.IsNullOrEmpty(provider.Scanlator))
            {
                existing.Scanlator = provider.Scanlator;
            }
            changed = true;
        }

        if (newProviders.Count > 0)
        {
            target.Providers.AddRange(newProviders);
        }

        return changed;
    }
}

