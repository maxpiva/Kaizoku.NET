using System.Collections;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    public sealed class FilterList : IReadOnlyList<IFilter>
    {
        private readonly List<IFilter> _list;
        public FilterList(params IFilter[] fs) : this(fs != null && fs.Length > 0 ? fs.ToList() : [])
        {
        }
        public FilterList(IReadOnlyList<IFilter> list) => _list = list?.ToList() ?? new List<IFilter>();



        public int Count => _list.Count;

        public IFilter this[int index] => _list[index];

        public IEnumerator<IFilter> GetEnumerator() => _list.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
