namespace Mihon.ExtensionsBridge.Models.Abstractions
{
    public interface ITemporaryDirectory : IDisposable
    {
        string Path { get; }
    }
}
