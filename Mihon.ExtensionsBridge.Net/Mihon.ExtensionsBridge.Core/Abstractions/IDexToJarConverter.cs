using Mihon.ExtensionsBridge.Models;
using System.Threading;
using System.Threading.Tasks;

namespace Mihon.ExtensionsBridge.Core.Abstractions;

/// <summary>
/// Converts Android DEX bytecode contained within an APK into a consolidated Java archive.
/// </summary>
public interface IDex2JarConverter
{
    Task<bool> ConvertAsync(ExtensionWorkUnit workUnit, CancellationToken token = default);

    string Version { get; }
}
