namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // Sort
    // -----------------------------------------
    public abstract class Sort : Filter<Sort.Selection?>
    {
        public string[] Values { get; }

        protected Sort(string name, string[] values, Selection? state = null)
            : base(name, state)
        {
            Values = values;
        }

        public record Selection(int Index, bool Ascending);
    }
}
