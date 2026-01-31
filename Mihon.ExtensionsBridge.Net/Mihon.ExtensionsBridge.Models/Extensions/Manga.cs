using System.Runtime.CompilerServices;
[assembly: InternalsVisibleTo("Mihon.ExtensionsBridge.Core")]
namespace Mihon.ExtensionsBridge.Models.Extensions
{
    public class Manga
    {
        public string Url { get; set; }
        public string Title { get; set; }
        public string? Artist { get; set; }
        public string? Author { get; set; }
        public string? Description { get; set; }
        public string? Genre { get; set; }
        public Status  Status { get; set; }
        public string? ThumbnailUrl { get; set; }
        public UpdateStrategy UpdateStrategy { get; set; }
        public bool Initialized { get; set; }

    }
}
