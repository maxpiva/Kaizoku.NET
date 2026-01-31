namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // Text
    // -----------------------------------------
    public abstract class Text : Filter<string>
    {
        protected Text(string name, string state = "")
            : base(name, state)
        {
        }
    }
}
