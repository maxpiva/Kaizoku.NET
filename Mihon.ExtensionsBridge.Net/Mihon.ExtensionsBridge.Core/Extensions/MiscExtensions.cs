

using sun.security.x509;
using Mihon.ExtensionsBridge.Models;
using Mihon.ExtensionsBridge.Models.Abstractions;

namespace Mihon.ExtensionsBridge.Core.Extensions;

public static class MiscExtensions
{
    public static string RepoFromUrl(string repo)
    {
        if (repo.EndsWith("index.json", StringComparison.InvariantCultureIgnoreCase) )
            repo = repo[..^(10)];
        if (repo.EndsWith("index.min.json", StringComparison.InvariantCultureIgnoreCase))
            repo = repo[..^(14)];
        if (repo.EndsWith("/"))
            repo  = repo[..^(1)];
        return repo;
    }

    public static string CombineUrl(this string repoUrl, params string[] segments)
    {
        if (string.IsNullOrEmpty(repoUrl))
            return string.Empty;
        var trimmed = repoUrl.TrimEnd('/');
        foreach (var seg in segments)
        {
            trimmed += "/" + seg.Trim('/');
        }
        return trimmed;
    }
    public static java.lang.ClassLoader ClassLoader => java.lang.Class.forName("eu.kanade.tachiyomi.source.SourceFactory").getClassLoader();
    public static T InvokeInJavaContext<T>(this Func<T> function)
    {
        var original = java.lang.Thread.currentThread().getContextClassLoader();
        try
        {
            java.lang.Thread.currentThread().setContextClassLoader(ClassLoader);
            return function();
        }
        finally
        {
            java.lang.Thread.currentThread().setContextClassLoader(original);
        }
    }
    public static void InvokeInJavaContext(this Action function)
    {
        var original = java.lang.Thread.currentThread().getContextClassLoader();
        try
        {
            java.lang.Thread.currentThread().setContextClassLoader(ClassLoader);
            function();
        }
        finally
        {
            java.lang.Thread.currentThread().setContextClassLoader(original);
        }
    }
    public static async Task<T> InvokeInJavaContextAsync<T>(this Func<Task<T>> function)
    {
        // Run the function on the current thread so we control the Java Thread context for its synchronous part.
        // If the function uses Task.Run to switch threads, it must set the class loader inside that Task as well.
        var original = java.lang.Thread.currentThread().getContextClassLoader();
        try
        {
            java.lang.Thread.currentThread().setContextClassLoader(ClassLoader);
            return await function().ConfigureAwait(false);
        }
        finally
        {
            java.lang.Thread.currentThread().setContextClassLoader(original);
        }
    }


    public static string GetApkUrl(this TachiyomiExtension ext, TachiyomiRepository repository)
    {
        return $"{RepoFromUrl(repository.Url)}/apk/{ext.Apk}";
    }
    public static string GetIconUrl(this TachiyomiExtension ext, TachiyomiRepository repository)
    {
        string iconName = Path.ChangeExtension(ext.Apk, "png");
        return $"{RepoFromUrl(repository.Url)}/icon/{iconName}";
    }

    public static async Task<T> RunBlockingIoAsync<T>(this SemaphoreSlim semaphore, Func<T> func, CancellationToken ct)
    {
        await semaphore.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return await Task.Run(func, ct).ConfigureAwait(false);
        }
        finally
        {
            semaphore.Release();
        }
    }
}
