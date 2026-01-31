namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // Select
    // -----------------------------------------
    public abstract class Select<V> : Filter<int>
    {
        public V[] Values { get; }

        protected Select(string name, V[] values, int state = 0)
            : base(name, state)
        {
            Values = values;
        }
    }
}
