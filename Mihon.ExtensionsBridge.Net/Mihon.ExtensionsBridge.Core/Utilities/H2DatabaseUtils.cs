using extension.bridge;
using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Core.Utilities
{
    public static class H2DatabaseUtils
    {


        static List<string> columnNames = new List<string>{ "id", "url", "title", "thumbnail_url","artist","author","description","genre","status","update_strategy","source" };
        public static Dictionary<int, (long, ParsedManga)>? ObtainMangaTableFromSuwayomiIfPossible(string database)
        {
            if (!File.Exists(database+".mv.db"))
            {
                return null;
            }
            Dictionary<string, int> mappings = new Dictionary<string, int>();
            string[][] values = H2TableReader.INSTANCE.readTable(database, "MANGA");
            int columns = values[0].Length;
            int rows = values.Length;
            for(int i = 0; i < columns; i++)
            {
                for(int x=0;x<columnNames.Count;x++)
                {
                    string lcol = columnNames[x].ToLower();
                    string rcol = values[0][i].ToLower();
                    if (lcol.Equals(rcol))
                        mappings[lcol] = i;
                }
            }
            Dictionary<int, (long, ParsedManga)> mangaTable = new Dictionary<int, (long, ParsedManga)>();
            for(int y = 1;y<rows;y++)
            {
                int id = int.Parse(values[y][mappings["id"]]);
                string url = values[y][mappings["url"]];
                string title = values[y][mappings["title"]];
                string thumbnailUrl = values[y][mappings["thumbnail_url"]];
                string artist = values[y][mappings["artist"]];
                string author = values[y][mappings["author"]];
                string description = values[y][mappings["description"]];
                string genre = values[y][mappings["genre"]];
                string status = values[y][mappings["status"]];
                string updateStrategy = values[y][mappings["update_strategy"]];
                long source = 0;
                long.TryParse(values[y][mappings["source"]], out source);
                mangaTable[id] = (source, new ParsedManga
                {
                    Url = url,
                    Title = title,
                    ThumbnailUrl = thumbnailUrl,
                    Artist = artist,
                    Author = author,
                    Description = description,
                    Genre = genre,
                    Status = (Status)int.Parse(status),
                    UpdateStrategy = updateStrategy == "ALWAYS_UPDATE" ? UpdateStrategy.ALWAYS_UPDATE : UpdateStrategy.ONLY_FETCH_ONCE
                });
            }
            return mangaTable;
        }
    }

}
