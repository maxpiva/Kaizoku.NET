using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Mihon.ExtensionsBridge.Core.Preferences;

/// <summary>
/// File-backed implementation of <see cref="IPreferenceStore"/> inspired by Android's SharedPreferences.
/// </summary>
public sealed class FilePreferenceStore : IPreferenceStore
{
    private readonly string _storagePath;
    private readonly ILogger<FilePreferenceStore> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, JsonElement> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public FilePreferenceStore(string storagePath, ILogger<FilePreferenceStore> logger)
    {
        _storagePath = storagePath ?? throw new ArgumentNullException(nameof(storagePath));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask SetStringAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        await SetValueAsync(key, JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value)).RootElement.Clone(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<string?> GetStringAsync(string key, CancellationToken cancellationToken = default)
    {
        var element = await GetValueAsync(key, cancellationToken).ConfigureAwait(false);
        return element.HasValue && element.Value.ValueKind == JsonValueKind.String ? element.Value.GetString() : null;
    }

    public async ValueTask SetLongAsync(string key, long value, CancellationToken cancellationToken = default)
    {
        await SetValueAsync(key, JsonDocument.Parse(JsonSerializer.SerializeToUtf8Bytes(value)).RootElement.Clone(), cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<long?> GetLongAsync(string key, CancellationToken cancellationToken = default)
    {
        var element = await GetValueAsync(key, cancellationToken).ConfigureAwait(false);
        return element.HasValue && element.Value.ValueKind == JsonValueKind.Number ? element.Value.GetInt64() : null;
    }

    public async ValueTask RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            _cache.TryRemove(key, out _);
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask<JsonElement?> GetValueAsync(string key, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            return _cache.TryGetValue(key, out var element) ? element : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask SetValueAsync(string key, JsonElement element, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            _cache[key] = element;
            await PersistAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async ValueTask EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        if (!File.Exists(_storagePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_storagePath)!);
            await File.WriteAllTextAsync(_storagePath, "{}", cancellationToken).ConfigureAwait(false);
            _initialized = true;
            return;
        }

        try
        {
            await using var stream = File.OpenRead(_storagePath);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            foreach (var property in document.RootElement.EnumerateObject())
            {
                _cache[property.Name] = property.Value.Clone();
            }
            _initialized = true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load preference store, starting fresh.");
            _cache.Clear();
            await File.WriteAllTextAsync(_storagePath, "{}", cancellationToken).ConfigureAwait(false);
            _initialized = true;
        }
    }

    private async ValueTask PersistAsync(CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(_cache, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(_storagePath, json, cancellationToken).ConfigureAwait(false);
    }
}
