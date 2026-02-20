using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Abstractions
{
    public interface IInternalExtensionInterop : IExtensionInterop, IDisposable
    {
        Task ShutdownAsync(CancellationToken token);

    }
}



    
