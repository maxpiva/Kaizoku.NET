namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // Separator
    // -----------------------------------------
    public sealed class Separator : Filter<object>
    {
        public Separator(string name = "")
            : base(name, 0)
        {
        }
    }
}
