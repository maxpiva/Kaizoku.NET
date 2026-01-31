namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // CheckBox
    // -----------------------------------------
    public abstract class CheckBox : Filter<bool>
    {
        protected CheckBox(string name, bool state = false)
            : base(name, state)
        {
        }
    }
}
