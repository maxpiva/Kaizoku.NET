using com.sun.jna;
using Microsoft.Extensions.Logging;
using Mihon.ExtensionsBridge.Core.Abstractions;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;
using Mihon.ExtensionsBridge.Models.Extensions;

namespace Mihon.ExtensionsBridge.Core.Runtime.Gatekeeper
{
    // Proxy that gatekeeps calls and allows safe swap/unload of underlying ExtensionInterop
    public sealed class GatekeptExtensionInterop : IExtensionInterop
    {
        private readonly ILogger _logger;
        private readonly IWorkingFolderStructure _structure;

        private IInternalExtensionInterop _current;
        private List<ISourceInterop> _wrappedSources;

        private readonly SemaphoreSlim _gate = new(1, 1); // open gate when not blocked; callers wait when closed
        private volatile bool _isClosed = false;
        private int _inFlight = 0;
        private TaskCompletionSource<bool>? _drainTcs;

        public string Id => _current.Id;
        public string Name => _current.Name;

        public string Version { get => _current.Version; private set { /* ignored */ } }
        public List<ISourceInterop> Sources => _wrappedSources;
        private readonly Func<IWorkingFolderStructure, RepositoryEntry, ILogger, IInternalExtensionInterop> _factory;
        public GatekeptExtensionInterop(IWorkingFolderStructure structure, RepositoryEntry entry, Func<IWorkingFolderStructure, RepositoryEntry, ILogger, IInternalExtensionInterop> factory, ILogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _structure = structure ?? throw new ArgumentNullException(nameof(structure));
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _current = factory(structure, entry, logger);
            _wrappedSources = WrapSources(_current.Sources);
        }

        private List<ISourceInterop> WrapSources(List<ISourceInterop> sources)
        {
            var list = new List<ISourceInterop>(sources.Count);
            foreach (var s in sources)
                list.Add(new GatekeptSourceInterop(this, s, _logger));
            return list;
        }

        internal async Task EnterAsync(CancellationToken token)
        {
            // Fast-path: if open, proceed, else wait for reopen
            while (_isClosed)
            {
                await _gate.WaitAsync(token).ConfigureAwait(false);
                _gate.Release();
                if (_isClosed) // still closed, loop
                {
                    await Task.Delay(10, token).ConfigureAwait(false);
                }
            }
            Interlocked.Increment(ref _inFlight);
        }

        internal void Exit()
        {
            int now = Interlocked.Decrement(ref _inFlight);
            if (_isClosed && now == 0)
            {
                _drainTcs?.TrySetResult(true);
            }
        }

        internal async Task SwapAsync(RepositoryEntry newEntry, CancellationToken token = default)
        {
            // Close gate for new calls
            _isClosed = true;
            _drainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            // Wait for in-flight to drain
            if (Volatile.Read(ref _inFlight) > 0)
                await _drainTcs.Task.WaitAsync(token).ConfigureAwait(false);
            await _current.ShutdownAsync(token).ConfigureAwait(false);

            // Create new interop and wrap
            var next = _factory(_structure, newEntry, _logger);
            var nextWrapped = WrapSources(next.Sources);

            // Swap
            var old = _current;
            _current = next;
            _wrappedSources = nextWrapped;

            // Dispose old to unload
            try { old.Dispose(); } catch (Exception ex) { _logger.LogWarning(ex, "Error disposing old extension interop during swap"); }

            // Reopen gate
            _isClosed = false;
            _drainTcs = null;
        }

        internal void Dispose()
        {
            _isClosed = true;
            // best-effort: wait briefly for drain if any
            if (Volatile.Read(ref _inFlight) == 0)
            {
                _current.Dispose();
            }
            else
            {
                try
                {
                    _drainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    _drainTcs.Task.Wait(TimeSpan.FromSeconds(5));
                }
                catch { }
                _current.Dispose();
            }
            _gate.Dispose();
        }

        public async Task<List<UniquePreference>> LoadPreferencesAsync(CancellationToken token)
        {
            await EnterAsync(token).ConfigureAwait(false);
            try { return await _current.LoadPreferencesAsync(token).ConfigureAwait(false); }
            finally { Exit(); }
        }

        public async Task SavePreferencesAsync(List<UniquePreference> press, CancellationToken token)
        {
            await EnterAsync(token).ConfigureAwait(false);
            try { await _current.SavePreferencesAsync(press, token).ConfigureAwait(false); }
            finally { Exit(); }
        }

        internal async Task ShutdownAsync(CancellationToken token)
        {
            // Block new calls and drain inflight, then forward shutdown
            _isClosed = true;
            _drainTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (Volatile.Read(ref _inFlight) > 0)
                await _drainTcs.Task.WaitAsync(token).ConfigureAwait(false);
            await _current.ShutdownAsync(token).ConfigureAwait(false);
            _isClosed = false;
            _drainTcs = null;
        }
    }
}
