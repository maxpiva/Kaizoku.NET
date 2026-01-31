namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface IFilter
    {
        string Name { get; }
        object? UntypedState { get; }
    }
}
