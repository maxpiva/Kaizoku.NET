package extension.bridge.cef

import extension.bridge.Settings
import java.util.concurrent.atomic.AtomicInteger
import java.util.concurrent.atomic.AtomicLong

/**
 * Global renderer budget for CEF renderer processes.
 *
 * EVERY CEF browser (and therefore every `jcef_helper --type=renderer` subprocess) is created
 * inside [xyz.nulldev.androidcompat.webkit.KcefWebViewProvider], which is the factory for ALL
 * `android.webkit.WebView` instances in the process — regardless of whether the WebView was
 * created by `WebViewFetchInterceptor`, the `WebViewPool`, or an extension's own helper
 * (e.g. Keiyoushi-style `runWebView`/`WebViewSession` utilities).
 *
 * The provider therefore owns the single source of truth for the live-renderer count:
 *  - [reserve] is called before a browser is created (on the main looper).
 *  - [release] is called when that browser is closed/destroyed.
 *
 * [canSpawn] is a fast, non-reserving advisory check used by the interceptor to degrade
 * gracefully (fall back to the direct OkHttp chain) before it posts work to the main looper.
 * The authoritative enforcement is [reserve] inside the provider, so the cap can never be
 * exceeded even when the advisory check loses a race.
 *
 * Thread-safety: atomic counters only — safe to call from any thread, never touches JNI.
 */
object RendererGate {
    private const val DEFAULT_MAX_RENDERERS = 4

    private val live = AtomicInteger(0)
    private val peak = AtomicInteger(0)
    private val refused = AtomicLong(0)

    /** Effective renderer cap (>= 1). Falls back to the default when unset/invalid. */
    fun maxRenderers(): Int {
        val configured = Settings.cefMaxRenderers
        return if (configured > 0) configured else DEFAULT_MAX_RENDERERS
    }

    fun liveCount(): Int = live.get()

    /**
     * Fast, non-reserving check: would [reserve] currently succeed?
     * Used by the interceptor to skip the looper hop and fall back to the direct network chain
     * when the budget is already exhausted.
     */
    fun canSpawn(): Boolean = live.get() < maxRenderers()

    /**
     * Atomically reserves a renderer slot if one is available. Called by the provider immediately
     * before creating a CEF browser. Returns false when the cap is reached — the caller must NOT
     * create a browser then (the extension will see a clean failure/timeout and its own retry or
     * fallback logic can kick in).
     */
    fun reserve(): Boolean {
        val cap = maxRenderers()
        while (true) {
            val current = live.get()
            if (current >= cap) {
                refused.incrementAndGet()
                return false
            }
            if (live.compareAndSet(current, current + 1)) {
                peak.accumulateAndGet(current + 1, ::maxOf)
                return true
            }
        }
    }

    /** Releases a previously reserved slot. Safe to call from a `finally` block or close path. */
    fun release() {
        live.updateAndGet { current -> if (current > 0) current - 1 else 0 }
    }

    fun stats(): String =
        "live=${live.get()} peak=${peak.get()} max=${maxRenderers()} refused=${refused.get()}"
}
