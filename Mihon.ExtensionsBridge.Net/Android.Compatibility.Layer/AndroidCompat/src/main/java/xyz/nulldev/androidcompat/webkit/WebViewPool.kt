package xyz.nulldev.androidcompat.webkit

import android.os.Handler
import android.os.Looper
import android.util.Log
import android.webkit.WebView
import extension.bridge.Settings
import extension.bridge.cef.RendererGate
import java.util.concurrent.atomic.AtomicLong
import java.util.concurrent.locks.ReentrantLock
import kotlin.concurrent.withLock

/**
 * Global, cross-extension pool of [WebView] instances keyed by request host.
 *
 * Every WebView-backed HTTP request (from any extension) goes through this pool instead of
 * creating a brand-new [WebView] (and therefore a brand-new CEF renderer process) per request.
 *
 * ## Threading contract (critical)
 *
 * This codebase has a documented reverse-JNI hazard: CEF *native* threads must never call into
 * Java/IKVM. Java threads calling *into* CEF are safe, but for consistency with the rest of the
 * bridge, **all WebView/CEF work (create, navigate, destroy) is confined to the Android main
 * looper**, exactly like the existing teardown pattern in
 * [KcefWebViewProvider] and [WebViewFetchInterceptor].
 *
 * The pump/watchdog thread (see `CefMessageLoopBridge.pumpWork`) is only ever allowed to call
 * [sweep], which is **pure bookkeeping** (lock-protected LRU/cap math) plus posting destruction to
 * the main looper. It never touches JNI itself.
 *
 * ## Concurrency
 *
 * Entries are keyed by a unique id (not by host), so multiple concurrent requests for the SAME
 * host each get their own [Entry] without overwriting each other. Reuse works by scanning for an
 * idle entry with the same host; if none exists and [RendererGate] has budget, a fresh entry is
 * reserved and the caller creates the WebView on the main looper.
 *
 * ## Renderer budget
 *
 * The authoritative cap on live renderer processes lives in [RendererGate], enforced at browser
 * creation inside [KcefWebViewProvider] (the factory for EVERY WebView in the process, including
 * extension-owned ones like Keiyoushi-style `runWebView`). This pool therefore does NOT keep its
 * own renderer count; it only asks [RendererGate] whether a new browser may be spawned before
 * reserving a fresh entry.
 */
object WebViewPool {
    private const val TAG = "WebViewPool"

    private val lock = ReentrantLock()
    private val entries = LinkedHashMap<Long, Entry>()
    private val idGen = AtomicLong(0)

    class Entry(
        val host: String,
        val id: Long,
    ) {
        @Volatile var webView: WebView? = null
        @Volatile var inUse: Boolean = true
        @Volatile var closed: Boolean = false
        @Volatile var lastUsedNanos: Long = System.nanoTime()
    }

    /** Whether pooling is enabled by configuration. */
    fun isEnabled(): Boolean = Settings.cefWebViewPoolEnabled

    /** Idle timeout (ms) before an idle pooled WebView is destroyed. */
    fun idleTimeoutMs(): Long {
        val configured = Settings.cefIdleTimeoutMs
        return if (configured > 0) configured else DEFAULT_IDLE_TIMEOUT_MS
    }

    private const val DEFAULT_IDLE_TIMEOUT_MS = 300_000L

    /**
     * Acquires a pooled [Entry] for [host].
     *
     * - Scans for an idle, non-closed entry with the same host and returns it immediately
     *   (reuse never needs a new renderer slot).
     * - Otherwise, if [RendererGate] still has budget, reserves a fresh entry (webView == null).
     *   The caller MUST create the [WebView] on the main looper and call [attach] inside that
     *   looper block; if creation fails, the caller MUST call [discard].
     * - Returns `null` when the renderer budget is exhausted and no idle entry can be reused;
     *   the caller should fall back to the direct network/FlareSolverr chain instead of spawning
     *   an extra renderer.
     */
    fun acquire(host: String): Entry? {
        lock.withLock {
            // 1) Reuse an existing idle entry for this host (no new renderer needed).
            val existing = entries.values
                .firstOrNull { it.host == host && !it.inUse && !it.closed && it.webView != null }
            if (existing != null) {
                existing.inUse = true
                existing.lastUsedNanos = System.nanoTime()
                return existing
            }

            // 2) A new browser would be needed; consult the authoritative renderer budget.
            if (!RendererGate.canSpawn()) {
                Log.w(TAG, "Renderer budget exhausted (${RendererGate.stats()}); refusing acquire for $host")
                return null
            }

            // 3) Reserve a fresh entry; the WebView is created on the main looper by the caller.
            val entry = Entry(host, idGen.incrementAndGet())
            entries[entry.id] = entry
            return entry
        }
    }

    /**
     * Attaches a freshly created [WebView] to [entry]. MUST be called on the main looper thread,
     * from the same looper block that constructed the WebView.
     */
    fun attach(entry: Entry, webView: WebView) {
        entry.webView = webView
        entry.lastUsedNanos = System.nanoTime()
    }

    /**
     * Marks [entry] as idle again, keeping it in the pool for reuse. Safe from any thread.
     */
    fun release(entry: Entry) {
        lock.withLock {
            entry.inUse = false
            entry.lastUsedNanos = System.nanoTime()
        }
    }

    /**
     * Removes [entry] from the pool and schedules deterministic destruction of its WebView on the
     * main looper (where `browser.close(true)` / `client.dispose()` are safe). If [entry] has no
     * attached WebView yet it is simply discarded.
     */
    fun evict(entry: Entry, reason: String) {
        lock.withLock {
            entries.remove(entry.id)
            entry.closed = true
            entry.inUse = false
        }
        val webView = entry.webView
        if (webView != null) {
            Log.i(TAG, "Evicting WebView id=${entry.id} host=${entry.host} reason=$reason")
            postDestroy(webView)
        }
    }

    /**
     * Discards a placeholder [entry] whose WebView was never created (e.g. an exception on the
     * looper before construction). Does not touch JNI.
     */
    fun discard(entry: Entry) {
        lock.withLock {
            entries.remove(entry.id)
            entry.closed = true
            entry.inUse = false
        }
    }

    /**
     * Idle sweep — the ONLY method the pump/watchdog thread may call.
     *
     * Runs pure bookkeeping under [lock] (never JNI): destroys entries that have been idle longer
     * than [idleTimeoutMs]. Actual destruction is posted to the main looper via [postDestroy].
     */
    fun sweep() {
        val idleNanos = idleTimeoutMs() * 1_000_000L
        val now = System.nanoTime()

        val toEvict = lock.withLock {
            val victims = mutableListOf<Entry>()

            val iterator = entries.entries.iterator()
            while (iterator.hasNext()) {
                val entry = iterator.next().value
                if (entry.closed) {
                    iterator.remove()
                    continue
                }
                if (entry.inUse) continue
                if (entry.webView == null) continue
                if (now - entry.lastUsedNanos > idleNanos) {
                    victims.add(entry)
                    iterator.remove()
                    entry.closed = true
                }
            }

            victims
        }

        toEvict.forEach { entry ->
            val webView = entry.webView
            if (webView != null) {
                Log.i(TAG, "Sweep evicting WebView id=${entry.id} host=${entry.host}")
                postDestroy(webView)
            }
        }
    }

    fun size(): Int = lock.withLock { entries.values.count { !it.closed } }

    /** True if [id] is still a live (non-closed) pooled entry. */
    fun contains(id: Long): Boolean = lock.withLock { entries[id]?.closed == false }

    fun stats(): String = lock.withLock {
        val alive = entries.values.filter { !it.closed }
        val inUse = alive.count { it.inUse }
        "poolSize=${alive.size} inUse=$inUse idleMs=${idleTimeoutMs()}"
    }

    private fun postDestroy(webView: WebView) {
        runCatching {
            Handler(Looper.getMainLooper()).post {
                runCatching { webView.destroy() }
                    .onFailure { t -> Log.w(TAG, "WebView destroy failed", t) }
            }
        }.onFailure { t ->
            Log.w(TAG, "Unable to post WebView destroy to main looper", t)
        }
    }
}
