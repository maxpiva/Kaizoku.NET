using kotlin.coroutines;
using System.Threading;

namespace Mihon.ExtensionsBridge.Core.Utilities
{
    public static class KotlinSuspendBridge
    {
        /// <summary>
        /// Calls a Kotlin suspend function (IKVM ABI: (Continuation)->object) and returns a Task you can await.
        /// CancellationToken cancels the .NET Task, but does NOT necessarily stop the Kotlin coroutine.
        /// </summary>
        public static Task<T> CallSuspend<T>(Func<Continuation, object> invoke, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Cancel the Task if token fires
            CancellationTokenRegistration ctr = default;
            if (cancellationToken.CanBeCanceled)
            {
                ctr = cancellationToken.Register(static state =>
                {
                    var tuple = ((TaskCompletionSource<T> tcs, CancellationToken token))state!;
                    tuple.tcs.TrySetCanceled(tuple.token);
                }, (tcs, cancellationToken));
            }

            var cont = new DotNetContinuation<T>(tcs, ctr);

            object returned;
            try
            {
                returned = invoke(cont);
            }
            catch (System.Exception ex)
            {
                cont.DisposeCancellationRegistration();
                tcs.TrySetException(ex);
                return tcs.Task;
            }

            // If it didn't suspend, it returned immediately.
            if (!IsCoroutineSuspended(returned))
            {
                CompleteFromKotlinResult(tcs, returned);
                cont.DisposeCancellationRegistration();
            }

            return tcs.Task;
        }

        private static void CompleteFromKotlinResult<T>(TaskCompletionSource<T> tcs, object resultOrValue)
        {
            // If already completed (e.g., cancellation), ignore.
            if (tcs.Task.IsCompleted)
                return;

            try
            {
                // Public Kotlin helper: throws if it's a failure wrapper
                kotlin.ResultKt.throwOnFailure(resultOrValue);

                // Success: Kotlin Result value-class representation uses the value itself
                tcs.TrySetResult((T)resultOrValue);
            }
            catch (System.Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        private static bool IsCoroutineSuspended(object value)
        {
            if (value == null) return false;

            var typeName = value.GetType().FullName ?? "";
            return typeName.Contains("kotlin.coroutines.intrinsics.CoroutineSingletons", StringComparison.Ordinal)
                   && (value.ToString()?.Contains("COROUTINE_SUSPENDED") == true);
        }

        private sealed class DotNetContinuation<T> : object, Continuation
        {
            private readonly TaskCompletionSource<T> _tcs;
            private CancellationTokenRegistration _ctr;

            public DotNetContinuation(TaskCompletionSource<T> tcs, CancellationTokenRegistration ctr)
            {
                _tcs = tcs;
                _ctr = ctr;
            }

            public CoroutineContext getContext()
                => kotlin.coroutines.EmptyCoroutineContext.INSTANCE;

            public void resumeWith(object result)
            {
                try
                {
                    CompleteFromKotlinResult(_tcs, result);
                }
                finally
                {
                    DisposeCancellationRegistration();
                }
            }

            public void DisposeCancellationRegistration()
            {
                // idempotent dispose
                _ctr.Dispose();
                _ctr = default;
            }
        }
    }
    public static class ObservableExtensions
    {
        // Define concrete Action1 implementations for onNext and onError
        class OnNextAction : rx.functions.Action1
        {
            private readonly Action<object> _action;
            public OnNextAction(Action<object> action) { _action = action; }
            public void call(object value) => _action(value);
        }
        class OnErrorAction : rx.functions.Action1
        {
            private readonly Action<object> _action;
            public OnErrorAction(Action<object> action) { _action = action; }
            public void call(object value) => _action(value);
        }
        class OnCompletedAction : rx.functions.Action0
        {
            private readonly Action _action;
            public OnCompletedAction(Action action) { _action = action; }
            public void call() => _action();
        }


        /// <summary>
        /// Consumes an RxJava Observable that emits zero or one item, returning the item or a default value.
        /// </summary>
        /// <typeparam name="T">The type of the item emitted by the observable.</typeparam>
        /// <param name="observable">The observable to consume.</param>
        /// <param name="token">A cancellation token to cancel the operation.</param>
        /// <returns>A task that resolves to the emitted item or the default value.</returns>
        public static Task<T?> ConsumeObservableOneOrDefaultAsync<T>(this rx.Observable observable, T? defaultVal = default, CancellationToken token = default)
        {
            var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool hasItem = false;
            T? firstItem = defaultVal;
            rx.Subscription? subscription = null;
            CancellationTokenRegistration ctr = default;
            int completed = 0;

            void Cleanup()
            {
                if (Interlocked.Exchange(ref completed, 1) != 0)
                    return;
                try
                {
                    subscription?.unsubscribe();
                }
                catch
                {
                    // Ignore disposal errors
                }
                ctr.Dispose();
            }

            subscription = observable.subscribe(
                new OnNextAction((object value) =>
                {
                    if (!hasItem)
                    {
                        hasItem = true;
                        firstItem = value is T t ? t : defaultVal;
                    }
                }),
                new OnErrorAction((object error) =>
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        Exception ex = error as Exception ?? new Exception(error?.ToString() ?? "Unknown observable error");
                        tcs.TrySetException(ex);
                    }
                    Cleanup();
                }),
                new OnCompletedAction(() =>
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        tcs.TrySetResult(hasItem ? (firstItem ?? defaultVal) : defaultVal);
                    }
                    Cleanup();
                })
            );

            if (token.CanBeCanceled)
            {
                ctr = token.Register(() =>
                {
                    tcs.TrySetCanceled(token);
                    Cleanup();
                });
            }

            return tcs.Task;
        }
    }

    
}
