namespace Mihon.ExtensionsBridge.IKVMCompiler.Abstractions;

public interface IIkvmCompiler
{
    Task CompileAsync(string jar, string versionNumber = "1.0.0.0", CancellationToken cancellationToken = default);

    string Version { get; }
}

