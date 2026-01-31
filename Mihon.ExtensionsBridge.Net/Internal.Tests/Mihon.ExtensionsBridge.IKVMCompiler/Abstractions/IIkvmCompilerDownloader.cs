namespace Mihon.ExtensionsBridge.IKVMCompiler.Abstractions
{
    public interface IIkvmCompilerDownloader
    {
        Task CompilerDownloadAsync(CancellationToken cancellationToken = default);
    }
}