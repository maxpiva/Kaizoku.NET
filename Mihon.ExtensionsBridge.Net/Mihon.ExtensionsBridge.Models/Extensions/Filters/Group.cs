namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // Group
    // -----------------------------------------
    public abstract class Group<V> : Filter<List<V>>
    {
        protected Group(string name, List<V> state)
            : base(name, state)
        {
        }
    }
}
