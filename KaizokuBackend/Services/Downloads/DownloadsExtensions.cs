using KaizokuBackend.Models;
using KaizokuBackend.Models.Database;
using Mihon.ExtensionsBridge.Models.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;

namespace KaizokuBackend.Services.Downloads;

public static class DownloadsExtensions
{
    public static List<ChapterDownload> ToDownloads(this KaizokuBackend.Models.Database.SeriesEntity s, SeriesProviderEntity sp, List<ParsedChapter> sr, string storagePath)
    {
        var downloads = new List<ChapterDownload>();
        foreach (var chapter in sr)
        {
            downloads.Add(new ChapterDownload
            {
                Id = Guid.NewGuid(),
                SeriesProviderId = sp.Id,
                SeriesId = sp.SeriesId,
                MihonId = sp.MihonId,
                MihonProviderId = sp.MihonProviderId,
                BridgeItemInfo = sp.BridgeItemInfo,
                Scanlator = chapter.Scanlator,
                ChapterName = chapter.ParsedName,
                Index = chapter.Index,                
                ProviderName = sp.Provider,
                ComicUploadDateUTC = chapter.DateUpload.DateTime,
                Title = s.Title,
                SeriesTitle = sp.Title,
                Url = chapter.RealUrl,
                Language = sp.Language,
                ThumbnailUrl = string.IsNullOrEmpty(sp.ThumbnailUrl) ? s.ThumbnailUrl : sp.ThumbnailUrl,
                Chapter = chapter,
                StoragePath = storagePath,
                Artist = sp.Artist ?? s.Artist,
                Author = sp.Author ?? s.Author,
                ChapterCount = sp.ChapterCount,
                Type = s.Type,
                Tags = s.Genre,
            });
        }
        return downloads;
    }

    public static List<ChapterDownload> GenerateDownloadsFromChapterData(this KaizokuBackend.Models.Database.SeriesEntity series, SeriesProviderEntity serie, List<ParsedChapter>? chapterData)
    {
        List<ParsedChapter> wanted = [];
        List<ParsedChapter> skip_the_filter = [];
        var allSeries = series.Sources.ToList();

        if (chapterData != null && chapterData.Count > 0)
        {
            wanted = chapterData;
            chapterData.ForEach(a =>
            {
                if (string.IsNullOrEmpty(a.Scanlator))
                    a.Scanlator = serie.Provider;
            });

            if (serie.Scanlator == serie.Provider || string.IsNullOrEmpty(serie.Scanlator))
            {
                wanted = wanted.Where(a => string.IsNullOrEmpty(a.Scanlator) || a.Scanlator == serie.Provider).ToList();
            }
            else
            {
                wanted = wanted.Where(a => a.Scanlator == serie.Scanlator).ToList();
            }

            foreach (ParsedChapter c in wanted)
            {
                if (c.DateUpload > DateTimeOffset.UtcNow.AddYears(1000))
                {
                    try
                    {
                        DateTime dt = c.DateUpload.DateTime;
                        Models.Chapter? ns = serie.Chapters.FirstOrDefault(a => a.Number == c.ParsedNumber);
                        if (ns != null && !string.IsNullOrEmpty(ns.Filename) && ns.ProviderUploadDate.HasValue)
                        {
                            if (ns.DownloadDate == null || ns.DownloadDate.Value != ns.ProviderUploadDate.Value)
                            {
                                int seconds = dt.Subtract(ns.ProviderUploadDate.Value).Seconds;
                                if (seconds >= 60)
                                {
                                    skip_the_filter.Add(c);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                        throw;
                    }
                }
            }
            if (!serie.IsStorage)
            {
                List<decimal?> exists = allSeries.SelectMany(s => s.Chapters)
                    .Where(c => c.Filename != null)
                    .Select(c => c.Number).ToList();
                wanted = wanted.Where(c => !exists.Contains(c.ParsedNumber)).ToList();
            }
            else
            {
                List<decimal?> exists = serie.Chapters
                    .Where(c => c.Filename != null)
                    .Select(c => c.Number).ToList();
                wanted = wanted.Where(c => !exists.Contains(c.ParsedNumber)).ToList();
            }

            if (serie.ContinueAfterChapter != null)
            {
                wanted = wanted.Where(c => c.ParsedNumber > serie.ContinueAfterChapter).ToList();
            }
        }

        foreach (ParsedChapter c in skip_the_filter.ToList())
        {
            if (wanted.Contains(c))
                skip_the_filter.Remove(c);
        }

        List<ChapterDownload> chaps = series.ToDownloads(serie, wanted, series.StoragePath);
        if (skip_the_filter.Count > 0)
        {
            List<ChapterDownload> updates = series.ToDownloads(serie, skip_the_filter, series.StoragePath);
            updates.ForEach(a =>
            {
                a.IsUpdate = true;
                chaps.Add(a);
            });
        }
        return chaps;
    }
}
