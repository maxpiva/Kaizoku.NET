package eu.kanade.tachiyomi.network.interceptor

import android.annotation.SuppressLint
import android.app.Application
import android.os.Handler
import android.os.Looper
import android.util.Base64
import android.util.Log
import android.webkit.JavascriptInterface
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebViewClient
import extension.bridge.Settings
import extension.bridge.cef.RendererGate
import eu.kanade.tachiyomi.network.sourceMetadata
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import okhttp3.Headers.Companion.toHeaders
import okhttp3.Interceptor
import okhttp3.MediaType.Companion.toMediaType
import okhttp3.Protocol
import okhttp3.Request
import okhttp3.Response
import okhttp3.ResponseBody.Companion.toResponseBody
import okio.Buffer
import uy.kohesive.injekt.Injekt
import uy.kohesive.injekt.api.get
import xyz.nulldev.androidcompat.webkit.WebViewPool
import java.io.IOException
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.CountDownLatch
import java.util.concurrent.TimeUnit
import java.util.concurrent.atomic.AtomicReference

/**
 * An OkHttp interceptor that executes HTTP requests through a WebView using JavaScript `fetch` API.
 *
 * This interceptor is useful for bypassing certain protections (like Cloudflare Turnstile) or when you need
 * to execute requests in a browser context. It works by:
 * 1. Optionally loading a URL in the WebView to establish a specific domain context
 * 2. Executing a JavaScript `fetch` with the original request details via `evaluateJavascript`
 * 3. Encoding the response in base64 and sending it back via a JavaScript interface
 * 4. Building an OkHttp Response from the WebView response
 *
 * **Best Practices:**
 * - In most cases, you don't need to specify `loadUrl` - the interceptor will work without it
 * - Only use `loadUrl` in special cases when you need a specific URL/domain context for the fetch
 * - Always use the `filter` parameter to only intercept requests from the same domain to avoid CORS issues
 * - The same-domain context allows JavaScript execution and avoids Cross-Origin Resource Sharing problems
 *
 * **Resource bounds (since the jcef_helper leak fix):**
 * - The authoritative renderer budget is [RendererGate], enforced inside
 *   [xyz.nulldev.androidcompat.webkit.KcefWebViewProvider] at browser creation — covering
 *   interceptor, pool, AND extension-owned WebViews (e.g. Keiyoushi-style `runWebView`).
 * - This interceptor consults [RendererGate.canSpawn] before posting work to the looper; when the
 *   budget is exhausted it degrades to the direct OkHttp chain instead of spawning another helper.
 * - WebViews are pooled per host ([WebViewPool]); a pooled WebView's JS interface is registered
 *   ONCE (a per-entry dispatcher) and the per-request handler is swapped via an atomic reference,
 *   so consecutive requests on a reused WebView never race add/remove ordering.
 * - Destruction/eviction always happens in a `finally` block on success/timeout/error, so no
 *   renderer process outlives its request. The old `postDelayed(destroy, 1000)` race is gone.
 *
 * @param filter Optional function that determines which requests should be intercepted.
 *   Returns `true` to intercept the request via WebView, `false` to proceed normally.
 *   If `null`, all requests are intercepted.
 *   **Recommended**: Filter by domain to only intercept requests from the same domain:
 *   ```kotlin
 *   filter = { request -> request.url.toString().startsWith(baseUrl) }
 *   ```
 *
 * @param timeout Timeout in seconds for waiting for the WebView response. Default is 60 seconds.
 *
 * @param loadUrl Optional URL to load in the WebView to establish a specific domain context before executing the fetch.
 *   **Only use in special cases** when you need a specific URL/domain context. In most cases, this can be `null`.
 *   If provided, use a lightweight file from the same `baseUrl` domain:
 *   - `/robots.txt` (recommended - very lightweight)
 *   - `/favicon.ico` (small image file)
 *   - A small CSS file
 *   This ensures fast loading, same domain context (avoiding CORS), and proper JavaScript execution.
 *
 * @sample
 * ```kotlin
 * // Basic usage without loadUrl
 * override val client = network.client.newBuilder()
 *     .addInterceptor(
 *         WebViewFetchInterceptor(
 *             filter = { request -> request.url.toString().startsWith(baseUrl) }
 *         )
 *     )
 *     .build()
 *
 * // Special case: with loadUrl for specific domain context
 * override val client = network.client.newBuilder()
 *     .addInterceptor(
 *         WebViewFetchInterceptor(
 *             filter = { request -> request.url.toString().startsWith(baseUrl) },
 *             loadUrl = "$baseUrl/robots.txt"
 *         )
 *     )
 *     .build()
 * ```
 */
class WebViewFetchInterceptor(
    private val filter: ((Request) -> Boolean)? = null,
    private val timeout: Long = 60,
    private val loadUrl: String? = null,
) : Interceptor {

    private val handler = Handler(Looper.getMainLooper())
    private val context: Application by lazy { Injekt.get() }
    private val interceptorName: String = this::class.simpleName ?: "WebViewFetchInterceptor"
    private val json = Json

    /**
     * Per-entry dispatcher registered ONCE with the WebView. `current` points at the live
     * request's [JsInterface]; swapping it is atomic and never requires removing/readding the
     * JS interface, so pooled reuse has no add/remove ordering race.
     *
     * The JS page calls `window.android.onResponse(...)` / `window.android.onError(...)`, so this
     * object must expose exactly those methods (the KCEF provider maps all declared member
     * functions). Each call is forwarded to the current request's handler. `current` is deliberately
     * not a public property so no extra accessors leak into the JS surface.
     */
    private class Dispatcher(
        initial: JsInterface,
    ) {
        private val current: AtomicReference<JsInterface> = AtomicReference(initial)

        fun bind(next: JsInterface) {
            current.set(next)
        }

        @JavascriptInterface
        fun onResponse(
            statusCode: Int,
            statusMessage: String,
            headers: String,
            bodyBase64: String,
        ) {
            current.get().onResponse(statusCode, statusMessage, headers, bodyBase64)
        }

        @JavascriptInterface
        fun onError(error: String) {
            current.get().onError(error)
        }
    }

    private val dispatchers = ConcurrentHashMap<Long, Dispatcher>()

    companion object {
        private const val DELAY_MILLIS: Long = 1000
        private const val JS_INTERFACE_NAME = "android"
    }

    internal class FetchResponse(
        var statusCode: Int = 0,
        var statusMessage: String = "",
        var headers: String = "",
        var bodyBase64: String = "",
        var error: String = "",
    )

    internal class JsInterface(
        private val latch: CountDownLatch,
        var response: FetchResponse = FetchResponse(),
    ) {
        @JavascriptInterface
        fun onResponse(
            statusCode: Int,
            statusMessage: String,
            headers: String,
            bodyBase64: String,
        ) {
            response.statusCode = statusCode
            response.statusMessage = statusMessage
            response.headers = headers
            response.bodyBase64 = bodyBase64
            Log.d(
                "WebViewFetchInterceptor",
                "WebView fetch response: status=$statusCode, message=$statusMessage, bodySize=${bodyBase64.length} bytes (base64), headersLength=${headers.length}",
            )
            latch.countDown()
        }

        @JavascriptInterface
        fun onError(error: String) {
            response.error = error
            Log.e("WebViewFetchInterceptor", "WebView fetch error: $error")
            latch.countDown()
        }
    }

    /**
     * Intercepts the HTTP request and either processes it through WebView or proceeds normally.
     *
     * If the filter function returns `false` or the request doesn't match the filter criteria,
     * the request proceeds normally through the OkHttp chain. Otherwise, it's executed via WebView.
     *
     * @param chain The OkHttp interceptor chain
     * @return The HTTP response, either from WebView or from the normal chain
     * @throws IOException If the WebView request times out or encounters an error
     */
    @Synchronized
    override fun intercept(chain: Interceptor.Chain): Response {
        val request = chain.request()

        val metadata = request.sourceMetadata()
        val enabledForPackage = metadata?.let { meta ->
            val overrides = Settings.interceptorOverrides
            overrides[meta.packageName]?.get(interceptorName) ?: false
        } ?: false

        if (!enabledForPackage) {
            return chain.proceed(request)
        }

        // Use filter function if provided
        val shouldIntercept = filter?.invoke(request) ?: true

        if (!shouldIntercept) {
            return chain.proceed(request)
        }

        Log.d("WebViewFetchInterceptor", "Intercepting request: ${request.url}")

        return proceedWithWebView(chain, request)
    }

    /**
     * Executes the HTTP request through a WebView using JavaScript fetch API.
     *
     * This method:
     * 1. Prepares the request data (URL, method, headers, body)
     * 2. Consults the global renderer budget and acquires a pooled WebView (keyed by host)
     * 3. Establishes context:
     *    - If `loadUrl` is provided, loads that URL
     *    - Otherwise, uses the request's domain as base URL with empty HTML content
     * 4. Executes a JavaScript `fetch` with the original request details
     * 5. Waits for the response (with configurable timeout)
     * 6. Decodes the base64-encoded response and builds an OkHttp Response
     * 7. Returns the WebView to the pool (success) or evicts it (timeout/error) in a `finally`
     *    block — no renderer process is ever leaked.
     *
     * @param chain The OkHttp interceptor chain (used for graceful degradation)
     * @param request The original OkHttp request to execute
     * @return An OkHttp Response built from the WebView fetch response
     * @throws IOException If the request times out or the WebView returns an error
     */
    @SuppressLint("SetJavaScriptEnabled", "AddJavascriptInterface")
    private fun proceedWithWebView(chain: Interceptor.Chain, request: Request): Response {
        val poolEnabled = WebViewPool.isEnabled()
        val latch = CountDownLatch(1)
        var webView: WebView? = null
        var poolEntry: WebViewPool.Entry? = null
        val jsInterface = JsInterface(latch)

        // Prepare request data
        val requestUrl = request.url.toString()
        val requestMethod = request.method
        val requestHeaders = request.headers.toMultimap().mapValues {
            it.value.lastOrNull() ?: ""
        }.toMutableMap()

        // Get User-Agent from headers
        val userAgent = request.header("User-Agent") ?: ""

        // Get contentType from body if available, otherwise from headers
        val contentType = request.body?.contentType()?.toString() ?: ""

        // If body has contentType, use it in headers instead of the one from headers
        if (contentType.isNotEmpty()) {
            requestHeaders["Content-Type"] = contentType
        }

        // Convert body to string (always use string format)
        val bodyString = request.body?.let { body ->
            val buffer = Buffer()
            body.writeTo(buffer)
            buffer.readUtf8()
        } ?: ""

        Log.d(
            "WebViewFetchInterceptor",
            "Starting WebView fetch: method=$requestMethod, url=$requestUrl, contentType=$contentType, hasBody=${bodyString.isNotEmpty()}, bodySize=${bodyString.length} chars",
        )

        // JavaScript script that performs the fetch
        val jsScript = """
            (function() {
                const requestUrl = ${requestUrl.asJsonLiteral()};
                const requestMethod = ${requestMethod.asJsonLiteral()};
                const requestHeaders = ${requestHeaders.asJsonLiteral()};
                const bodyString = ${bodyString.asJsonLiteral()};
                const userAgent = ${userAgent.asJsonLiteral()};

                // Prepare body (always use string format)
                let body = null;
                if (bodyString && bodyString.length > 0) {
                    body = bodyString;
                }

                // Prepare headers
                const headers = new Headers();
                for (const [key, value] of Object.entries(requestHeaders)) {
                    headers.append(key, value);
                }

                // Ensure User-Agent is set
                if (userAgent && userAgent.length > 0) {
                    headers.set('User-Agent', userAgent);
                }

                // Perform fetch
                fetch(requestUrl, {
                    method: requestMethod,
                    headers: headers,
                    body: body,
                    credentials: 'include',
                    mode: 'cors',
                    cache: 'no-store',
                })
                .then(async (response) => {
                    // Read body as ArrayBuffer
                    const arrayBuffer = await response.arrayBuffer();

                    // Convert ArrayBuffer to base64
                    const bytes = new Uint8Array(arrayBuffer);
                    let binary = '';
                    for (let i = 0; i < bytes.length; i++) {
                        binary += String.fromCharCode(bytes[i]);
                    }
                    const bodyBase64 = btoa(binary);

                    // Convert headers to JSON string
                    const headersObj = {};
                    response.headers.forEach((value, key) => {
                        headersObj[key] = value;
                    });
                    const headersJson = JSON.stringify(headersObj);

                    // Call Android interface
                    window.android.onResponse(
                        response.status,
                        response.statusText,
                        headersJson,
                        bodyBase64
                    );
                })
                .catch((error) => {
                    window.android.onError(error.toString());
                });
                return true;
            })();
        """.trimIndent()

        // Fast advisory check: if the renderer budget is already exhausted (authoritative count
        // lives in RendererGate, updated by KcefWebViewProvider when browsers are created/destroyed),
        // degrade to the direct network chain instead of posting work to the main looper that will
        // be refused by the provider anyway. This covers interceptor, pool, AND extension-owned
        // WebViews (e.g. Keiyoushi-style runWebView) with a single source of truth.
        if (!RendererGate.canSpawn()) {
            Log.w(
                "WebViewFetchInterceptor",
                "Renderer budget exhausted (${RendererGate.stats()}); falling back to direct network",
            )
            return chain.proceed(request)
        }

        try {
            if (poolEnabled) {
                poolEntry = WebViewPool.acquire(request.url.host)
                if (poolEntry == null) {
                    Log.w(
                        "WebViewFetchInterceptor",
                        "WebView pool saturated (${WebViewPool.stats()}); falling back to direct network",
                    )
                    return chain.proceed(request)
                }
                // Bind (or reuse) the per-entry dispatcher BEFORE the looper block runs, so a
                // reused WebView's JS interface already points at this request's JsInterface.
                val dispatcher = dispatchers.computeIfAbsent(poolEntry.id) { Dispatcher(jsInterface) }
                dispatcher.bind(jsInterface)
            }

            val entry = poolEntry
            handler.post {
                try {
                    val wv =
                        if (entry != null && entry.webView != null) {
                            // Reuse an existing pooled WebView (same host -> same CefClient/cookie context).
                            entry.webView!!
                        } else {
                            val created = WebView(context)
                            if (entry != null) {
                                WebViewPool.attach(entry, created)
                            }
                            created
                        }
                    webView = wv

                    with(wv.settings) {
                        javaScriptEnabled = true
                        domStorageEnabled = true
                        databaseEnabled = true
                        useWideViewPort = false
                        loadWithOverviewMode = false
                        userAgentString = request.header("User-Agent")
                        mixedContentMode = WebSettings.MIXED_CONTENT_ALWAYS_ALLOW
                    }

                    // Register the dispatcher ONCE per WebView (only on first creation). On reuse
                    // the dispatcher is already bound and simply points at the current handler
                    // (bound above, before the looper hop).
                    if (entry != null && entry.webView == null) {
                        val dispatcher = dispatchers.computeIfAbsent(entry.id) { Dispatcher(jsInterface) }
                        wv.addJavascriptInterface(dispatcher, JS_INTERFACE_NAME)
                    }

                    wv.webViewClient = object : WebViewClient() {
                        override fun onPageFinished(view: WebView, url: String) {
                            // Execute script after page loads
                            view.evaluateJavascript(jsScript) { result ->
                                // Handle JavaScript errors here
                                if (result == null) {
                                    Log.e("WebViewFetchInterceptor", "JavaScript evaluation returned null")
                                    jsInterface.onError("Error executing JavaScript script")
                                }
                            }
                        }
                    }

                    // Establish context by using the same domain as the request
                    val baseUrl = loadUrl ?: "${request.url.scheme}://${request.url.host}/"
                    wv.loadDataWithBaseURL(baseUrl, " ", "text/html", null, null)
                } catch (t: Throwable) {
                    // Fail fast instead of hanging for the full timeout: the provider refuses to
                    // spawn a browser when the renderer budget was exhausted between our advisory
                    // canSpawn() check and the looper hop (rare race). Surface it as a WebView error
                    // so the interceptor throws immediately and the request can degrade.
                    Log.w("WebViewFetchInterceptor", "Failed to start WebView fetch: ${t.message}", t)
                    jsInterface.onError("Failed to start WebView: ${t.message ?: t.javaClass.simpleName}")
                }
            }

            // Wait for response
            val success = latch.await(timeout, TimeUnit.SECONDS)

            if (!success) {
                Log.e(
                    "WebViewFetchInterceptor",
                    "Timeout waiting for WebView response after ${timeout}s",
                )
                throw IOException("Timeout executing request in WebView")
            }

            val fetchResponse = jsInterface.response

            if (fetchResponse.error.isNotEmpty()) {
                Log.e("WebViewFetchInterceptor", "WebView returned error: ${fetchResponse.error}")
                throw IOException("WebView error: ${fetchResponse.error}")
            }

            // Decode body from base64
            val bodyBytes = if (fetchResponse.bodyBase64.isNotEmpty()) {
                Base64.decode(fetchResponse.bodyBase64, Base64.NO_WRAP)
            } else {
                ByteArray(0)
            }

            // Convert JSON headers to OkHttp Headers
            val responseHeaders = try {
                val headersMap =
                    if (fetchResponse.headers.isNotBlank()) {
                        json.decodeFromString<Map<String, String>>(fetchResponse.headers)
                    } else {
                        emptyMap()
                    }
                headersMap.toHeaders()
            } catch (e: Exception) {
                Log.w("WebViewFetchInterceptor", "Failed to parse response headers: ${e.message}")
                okhttp3.Headers.headersOf()
            }

            // Determine content type
            val contentTypeHeader = responseHeaders["Content-Type"]
            val mediaType = contentTypeHeader?.toMediaType() ?: "application/octet-stream".toMediaType()

            Log.d(
                "WebViewFetchInterceptor",
                "Building response: statusCode=${fetchResponse.statusCode}, statusMessage=${fetchResponse.statusMessage}, bodySize=${bodyBytes.size} bytes, contentType=$contentTypeHeader",
            )

            // Build Response
            return Response.Builder()
                .request(request)
                .protocol(Protocol.HTTP_1_1)
                .code(fetchResponse.statusCode)
                .message(fetchResponse.statusMessage)
                .headers(responseHeaders)
                .body(bodyBytes.toResponseBody(mediaType))
                .build()
        } catch (t: Throwable) {
            // On timeout/error: deterministically evict (destroy) the pooled WebView so its
            // renderer process is torn down instead of leaking.
            poolEntry?.let { entry ->
                WebViewPool.evict(entry, "interceptor-failure")
            }
            throw t
        } finally {
            // Success: keep the pooled WebView idle for reuse (the pool's idle sweep will evict it
            // once idleTimeoutMs elapses). Failure: already evicted above.
            poolEntry?.let { entry ->
                // Drop the dispatcher only when the entry is being destroyed; otherwise keep it
                // bound so reuse has no add/remove race.
                if (entry.closed) {
                    dispatchers.remove(entry.id)
                }
                WebViewPool.release(entry)
            }
            // Prune dispatcher entries whose pool entry was evicted by the watchdog sweep
            // (idle timeout or LRU) between requests — those WebViews are gone, so their
            // dispatchers must not accumulate.
            if (dispatchers.size > WebViewPool.size() + 16) {
                val live = dispatchers.keys.filter { !WebViewPool.contains(it) }
                live.forEach(dispatchers::remove)
            }

            // Pooling disabled: the per-request WebView must be destroyed deterministically now
            // that JS work is done (latch counted down) or timed out. Post to the main looper to
            // match the existing teardown pattern (never call WebView.destroy off-looper).
            if (!poolEnabled) {
                val wv2 = webView
                if (wv2 != null) {
                    handler.postDelayed(
                        { runCatching { wv2.destroy() } },
                        DELAY_MILLIS,
                    )
                }
            }
        }
    }
}

private fun String.asJsonLiteral(): String = Json.encodeToString(this)

private fun Map<String, String>.asJsonLiteral(): String = Json.encodeToString(this)
