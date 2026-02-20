using androidx.preference;
using eu.kanade.tachiyomi.source;
using eu.kanade.tachiyomi.source.model;
using Mihon.ExtensionsBridge;
using Mihon.ExtensionsBridge.Core.Extensions;
using Mihon.ExtensionsBridge.Core.Utilities;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Extensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using static android.telecom.Call;
using Page = Mihon.ExtensionsBridge.Models.Extensions.Page;
using UpdateStrategy = Mihon.ExtensionsBridge.Models.Extensions.UpdateStrategy;

namespace Mihon.ExtensionsBridge.Core.Extensions
{
    public static class ConversionsExtensions
    {
        private const BindingFlags KotlinFieldFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private static T ReadField<T>(object instance, string fieldName, T defaultValue)
        {
            if (instance == null) return defaultValue;
            var t = instance.GetType();
            var f = t.GetField(fieldName, KotlinFieldFlags);
            if (f == null) return defaultValue;
            var val = f.GetValue(instance);
            if (val == null) return defaultValue;
            if (val is T tv) return tv;
            try
            {
                return (T)Convert.ChangeType(val, typeof(T));
            }
            catch
            {
                return defaultValue;
            }
        }
        public static string ParsedName(this TachiyomiExtension extension)
        {
            if (extension.Name.StartsWith("Tachiyomi:"))
                return extension.Name.Substring(10).Trim();
            return extension.Name;
        }
        

        /*
        public static eu.kanade.tachiyomi.source.model.FilterList ToFilterList(this ExtensionBridge.Repository.Extensions.Filters.FilterList filterList)
        {
            if (filterList == null)
                throw new ArgumentNullException(nameof(filterList));
            var fl = new eu.kanade.tachiyomi.source.model.FilterList();
            foreach (var filter in filterList)
            {
                if (filter is ExtensionBridge.Repository.Extensions.Filters.Separator)
                {
                    fl.add(new eu.kanade.tachiyomi.source.model.Filter.Separator());
                }
                else if (filter is ExtensionBridge.Repository.Extensions.Filters.CheckBox cbf)
                {
                    fl.add(new eu.kanade.tachiyomi.source.model.Filter.CheckBox(cbf.Name, cbf.State));
                }
                else if (filter is ExtensionBridge.Repository.Extensions.Filter.SelectFilter sf)
                {
                    fl.Add(new Filter.Select(sf.Name, sf.Options.ToArray(), sf.SelectedIndex));
                }
                else if (filter is ExtensionBridge.Repository.Extensions.Filter.TextFilter tf)
                {
                    fl.Add(new Filter.Text(tf.Name, tf.Text));
                }
            }
            return fl;
        }
        */
        public static MangaList ToMangaList(this eu.kanade.tachiyomi.source.model.MangasPage mangaPage, eu.kanade.tachiyomi.source.online.HttpSource source)
        {
            return new MangaList
            {
                Mangas = mangaPage.getMangas().toArray().Cast<eu.kanade.tachiyomi.source.model.SManga>()
                .Select(m => {
                    ParsedManga p = m.ToManga<ParsedManga>();
                    p.RealUrl = source.getMangaUrl(m);
                    return p;
                }).ToList(),
                HasNextPage = mangaPage.getHasNextPage()
            };
        }
        public static string GetString(java.lang.CharSequence seq)
        {
            if (seq == null)
                return "";
            return seq.toString();
        }
        public static KeyPreference ToKeyPreference(this androidx.preference.Preference preference, int index = 0)
        {
            if (preference == null)
                throw new ArgumentNullException(nameof(preference));
            KeyPreference pref = new KeyPreference
            {
                Type = preference.GetType().Name,
                Key = preference.getKey(),
                Title = GetString(preference.getTitle()),
                Summary = GetString(preference.getSummary()),
                DefaultValue = preference.getDefaultValue().ToString() ?? "",
                DefaultValueType = preference.getDefaultValueType(),
                CurrentValue = preference.getCurrentValue().ToString() ?? "",
                Visible = preference.getVisible(),
                Index = index
            };
            if (preference is androidx.preference.ListPreference listPreference)
            {
                pref.Entries = listPreference.getEntries().Select(e => e.ToString() ?? "").ToList();
                pref.EntryValues = listPreference.getEntryValues().Select(ev => ev.ToString() ?? "").ToList();
            }
            if (preference is androidx.preference.MultiSelectListPreference multiSelectListPreference)
            {
                pref.Entries = multiSelectListPreference.getEntries().Select(e => e.ToString() ?? "").ToList();
                pref.EntryValues = multiSelectListPreference.getEntryValues().Select(ev => ev.ToString() ?? "").ToList();
            }
            if (preference is androidx.preference.DialogPreference editTextPreference)
            {
                
                pref.DialogTitle = GetString(editTextPreference.getDialogTitle());
                pref.DialogMessage = GetString(editTextPreference.getDialogMessage());
            }
            if (preference is androidx.preference.EditTextPreference editTextPref)
            {
                pref.Text = editTextPref.getText() ?? "";
            }
            return pref;
        }
        public static List<ParsedChapter> ToParsedChapters(this IEnumerable<eu.kanade.tachiyomi.source.model.SChapter> chapters, string mangaTitle, eu.kanade.tachiyomi.source.model.SManga manga, eu.kanade.tachiyomi.source.online.HttpSource source)
        {
            ArgumentNullException.ThrowIfNull(chapters);
            ArgumentNullException.ThrowIfNull(source);

            var parsedChapters = chapters
                .Select(chapter =>
                {
                    source.prepareNewChapter(chapter,manga);
                    var parsed = chapter.ToChapter<ParsedChapter>();
                    parsed.ParsedNumber = ChapterUtils.ParseChapterNumber(mangaTitle, parsed.Name, parsed.ChapterNumber);
                    parsed.ParsedName = ChapterUtils.Sanitize(parsed.Name, mangaTitle);
                    parsed.RealUrl = source.getChapterUrl(chapter);
                    return parsed;
                })
                .OrderBy(ch => ch.ParsedNumber)
                .ToList();

            for (var i = 0; i < parsedChapters.Count; i++)
            {
                parsedChapters[i].Index = i;
            }

            return parsedChapters;
        }


        public static T ToChapter<T>(this eu.kanade.tachiyomi.source.model.SChapter chapter) where T: Chapter,new()
        {
            if (chapter == null) 
                throw new ArgumentNullException(nameof(chapter));
            // Avoid getters that may rely on late-init Kotlin properties
            string name = ReadField<string>(chapter, "name", string.Empty);
            string url = ReadField<string>(chapter, "url", string.Empty);
            string scanlator = ReadField<string>(chapter, "scanlator", string.Empty);
            long uploaded = ReadField<long>(chapter, "date_upload", 0);
            // chapter_number is a float in Tachiyomi
            float chNum = ReadField<float>(chapter, "chapter_number", 0f);
            DateTimeOffset upload;
            try
            {
                upload = DateTimeOffset.FromUnixTimeMilliseconds(uploaded);
            }
            catch
            {
                upload = DateTimeOffset.UtcNow;
                //Ignore, bad parse will just use current time
            }
            return new T
            {
                Name = name,
                Url = url,
                Scanlator = scanlator,
                DateUpload = upload,
                ChapterNumber = chNum,
            };
        }
        public static Page ToPage(this eu.kanade.tachiyomi.source.model.Page page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            // Avoid getters that may rely on late-init Kotlin properties
            int index = ReadField<int>(page, "index", 0);
            string url = ReadField<string>(page, "url", string.Empty);
            string imageUrl = ReadField<string>(page, "imageUrl", string.Empty);
            return new Page
            {
                Index = index,
                Url = url,
                ImageUrl = imageUrl,
            };
        }
        public static eu.kanade.tachiyomi.source.model.Page ToSPage(this Page page)
        {
            if (page == null)
                throw new ArgumentNullException(nameof(page));
            return new eu.kanade.tachiyomi.source.model.Page(page.Index, page.Url ?? page.ImageUrl ?? "", page.ImageUrl, android.net.Uri.parse(page.Url));
        }
        public static eu.kanade.tachiyomi.source.model.SChapterImpl ToSChapter(this Chapter chapter)
        {
            if (chapter == null)
                throw new ArgumentNullException(nameof(chapter));
            var schapter = new eu.kanade.tachiyomi.source.model.SChapterImpl();
            schapter.setName(chapter.Name ?? string.Empty);
            schapter.setUrl(chapter.Url ?? string.Empty);
            schapter.setScanlator(chapter.Scanlator);
            schapter.setDate_upload(chapter.DateUpload.ToUnixTimeSeconds());
            schapter.setChapter_number(chapter.ChapterNumber);
            return schapter;
        }
        public static eu.kanade.tachiyomi.source.model.SManga ToSManga(this Manga manga)
        {

            if (manga == null)
                throw new ArgumentNullException(nameof(manga));
            var smanga = new eu.kanade.tachiyomi.source.model.SMangaImpl();
            smanga.setUrl(manga.Url ?? string.Empty);
            smanga.setTitle(manga.Title ?? string.Empty);
            smanga.setArtist(manga.Artist);
            smanga.setAuthor(manga.Author);
            smanga.setDescription(manga.Description);
            smanga.setGenre(manga.Genre);
            smanga.setStatus((int)manga.Status);
            smanga.setThumbnail_url(manga.ThumbnailUrl);
            if (manga.UpdateStrategy== UpdateStrategy.ALWAYS_UPDATE)
                smanga.setUpdate_strategy(eu.kanade.tachiyomi.source.model.UpdateStrategy.ALWAYS_UPDATE);
            else
                smanga.setUpdate_strategy(eu.kanade.tachiyomi.source.model.UpdateStrategy.ONLY_FETCH_ONCE);
            smanga.setInitialized(manga.Initialized);
            return smanga;
        }
        private static void ReplaceFieldIfNeeded(eu.kanade.tachiyomi.source.model.SManga details, string? original, string field, Action<string> action)
        {
            string val = ReadField<string>(details, field, string.Empty);
            if (string.IsNullOrEmpty(val))
            {
                action(original ?? "");
            }
        }

        public static void PrefillMissing(eu.kanade.tachiyomi.source.model.SManga details, Manga original)
        {
            if (details == null)
                return;
            if (original == null)
                return;
            ReplaceFieldIfNeeded(details, original.Title, "title", details.setTitle);
            ReplaceFieldIfNeeded(details, original.Url, "url", details.setUrl);
            ReplaceFieldIfNeeded(details, original.Artist, "artist", details.setArtist);
            ReplaceFieldIfNeeded(details, original.Author, "author", details.setAuthor);
            ReplaceFieldIfNeeded(details, original.Description, "description", details.setDescription);
            ReplaceFieldIfNeeded(details, original.Genre, "genre", details.setGenre);
            ReplaceFieldIfNeeded(details, original.ThumbnailUrl, "thumbnail_url", details.setThumbnail_url);
        }


        public static T ToManga<T>(this eu.kanade.tachiyomi.source.model.SManga smanga, Manga backup = null) where T: Manga, new()
        {
            if (smanga == null) throw new ArgumentNullException(nameof(smanga));
            PrefillMissing(smanga, backup);
            // Read Kotlin backing fields directly to avoid late-init getter exceptions
            string title = ReadField<string>(smanga, "title", string.Empty);
            string url = ReadField<string>(smanga, "url", string.Empty);
            string artist = ReadField<string>(smanga, "artist", string.Empty);
            string author = ReadField<string>(smanga, "author", string.Empty);
            string description = ReadField<string>(smanga, "description", string.Empty);
            string genre = ReadField<string>(smanga, "genre", string.Empty);
            int status = ReadField<int>(smanga, "status", 0);
            string thumb = ReadField<string>(smanga, "thumbnail_url", string.Empty);
            var strategy = ReadField<eu.kanade.tachiyomi.source.model.UpdateStrategy>(
                smanga, "update_strategy", eu.kanade.tachiyomi.source.model.UpdateStrategy.ONLY_FETCH_ONCE);
            bool initialized = ReadField<bool>(smanga, "initialized", false);
            return new T
            {
                Title = title,
                Url = url,
                Artist = artist,
                Author = author,
                Description = description,
                Genre = genre,
                Status = (Status)status,
                ThumbnailUrl = thumb,
                UpdateStrategy = strategy == eu.kanade.tachiyomi.source.model.UpdateStrategy.ALWAYS_UPDATE
                    ? UpdateStrategy.ALWAYS_UPDATE
                    : UpdateStrategy.ONLY_FETCH_ONCE,
                Initialized = initialized
            };
        }
    }
}
