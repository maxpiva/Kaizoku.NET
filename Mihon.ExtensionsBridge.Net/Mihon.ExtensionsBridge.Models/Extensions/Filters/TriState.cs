namespace Mihon.ExtensionsBridge.Models.Extensions.Filters
{
    // -----------------------------------------
    // TriState
    // -----------------------------------------
    public abstract class TriState : Filter<int>
    {
        protected TriState(string name, int state = STATE_IGNORE)
            : base(name, state)
        {
        }

        public bool IsIgnored() => State == STATE_IGNORE;
        public bool IsIncluded() => State == STATE_INCLUDE;
        public bool IsExcluded() => State == STATE_EXCLUDE;

        public const int STATE_IGNORE = 0;
        public const int STATE_INCLUDE = 1;
        public const int STATE_EXCLUDE = 2;
    }
}
