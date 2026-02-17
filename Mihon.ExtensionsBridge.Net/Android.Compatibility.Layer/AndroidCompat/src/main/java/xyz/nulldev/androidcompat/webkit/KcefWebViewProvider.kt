package xyz.nulldev.androidcompat.webkit

import android.content.Intent
import android.content.res.Configuration
import android.graphics.Bitmap
import android.graphics.Canvas
import android.graphics.Paint
import android.graphics.Picture
import android.graphics.Rect
import android.graphics.drawable.Drawable
import android.net.Uri
import android.net.http.SslCertificate
import android.os.Bundle
import android.os.Handler
import android.os.Message
import android.print.PrintDocumentAdapter
import android.util.Log
import android.util.SparseArray
import android.view.DragEvent
import android.view.KeyEvent
import android.view.MotionEvent
import android.view.PointerIcon
import android.view.View
import android.view.ViewGroup.LayoutParams
import android.view.ViewStructure
import android.view.WindowInsets
import android.view.accessibility.AccessibilityEvent
import android.view.accessibility.AccessibilityNodeInfo
import android.view.accessibility.AccessibilityNodeProvider
import android.view.autofill.AutofillValue
import android.view.inputmethod.EditorInfo
import android.view.inputmethod.InputConnection
import android.view.textclassifier.TextClassifier
import android.webkit.DownloadListener
import android.webkit.ValueCallback
import android.webkit.WebBackForwardList
import android.webkit.WebChromeClient
import android.webkit.WebMessage
import android.webkit.WebMessagePort
import android.webkit.WebResourceRequest
import android.webkit.WebResourceResponse
import android.webkit.WebSettings
import android.webkit.WebView
import android.webkit.WebView.HitTestResult
import android.webkit.WebView.PictureListener
import android.webkit.WebView.VisualStateCallback
import android.webkit.WebViewClient
import android.webkit.WebViewProvider
import android.webkit.WebViewProvider.ScrollDelegate
import android.webkit.WebViewProvider.ViewDelegate
import android.webkit.WebViewRenderProcess
import android.webkit.WebViewRenderProcessClient
import xyz.nulldev.androidcompat.webkit.JoglNativeLoader
import kotlinx.serialization.Serializable
import kotlinx.serialization.decodeFromString
import kotlinx.serialization.encodeToString
import kotlinx.serialization.json.Json
import me.friwi.jcefmaven.CefAppBuilder
import me.friwi.jcefmaven.CefInitializationException
import me.friwi.jcefmaven.UnsupportedPlatformException
import org.cef.CefApp
import org.cef.CefClient
import org.cef.CefSettings
import org.cef.browser.CefBrowser
import org.cef.browser.CefFrame
import org.cef.browser.CefMessageRouter
import org.cef.browser.CefPaintEvent
import org.cef.callback.CefCallback
import org.cef.callback.CefQueryCallback
import org.cef.handler.CefDisplayHandlerAdapter
import org.cef.handler.CefFocusHandler
import org.cef.handler.CefLoadHandler
import org.cef.handler.CefLoadHandlerAdapter
import org.cef.handler.CefMessageRouterHandlerAdapter
import org.cef.handler.CefRequestHandler
import org.cef.handler.CefRenderHandlerAdapter
import org.cef.handler.CefRequestHandlerAdapter
import org.cef.handler.CefResourceHandler
import org.cef.handler.CefResourceHandlerAdapter
import org.cef.handler.CefResourceRequestHandler
import org.cef.handler.CefResourceRequestHandlerAdapter
import org.cef.misc.BoolRef
import org.cef.misc.IntRef
import org.cef.misc.StringRef
import org.cef.network.CefPostData
import org.cef.network.CefPostDataElement
import org.cef.network.CefRequest
import org.cef.network.CefResponse
import org.koin.mp.KoinPlatformTools
import extension.bridge.cef.CefAppBridge
import java.awt.Canvas as AwtCanvas
import java.awt.Rectangle
import java.awt.event.InputEvent as AwtInputEvent
import java.awt.event.KeyEvent as AwtKeyEvent
import java.awt.event.MouseEvent as AwtMouseEvent
import java.awt.event.MouseWheelEvent as AwtMouseWheelEvent
import java.awt.image.BufferedImage
import java.awt.image.DataBufferInt
import java.io.BufferedWriter
import java.io.ByteArrayOutputStream
import java.io.File
import java.io.IOException
import java.io.InputStream
import java.lang.reflect.Method
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.StandardCharsets
import java.util.Base64
import java.util.Locale
import java.util.UUID
import java.util.concurrent.ConcurrentHashMap
import java.util.concurrent.Executor
import java.util.function.Consumer
import kotlin.math.max
import kotlin.math.roundToInt
import kotlin.reflect.KClass
import kotlin.reflect.KFunction
import kotlin.reflect.full.declaredMemberFunctions
import kotlin.reflect.jvm.javaMethod

class KcefWebViewProvider(
    private val view: WebView,
) : WebViewProvider {
    private val settings = KcefWebSettings()
    private var viewClient = WebViewClient()
    private var chromeClient = WebChromeClient()
    private val mappings: MutableList<FunctionMapping> = mutableListOf()
    private val mappingIndex = ConcurrentHashMap<String, FunctionMapping>()
    private val urlHttpMapping: MutableMap<String, String> = mutableMapOf()
    private var initialRequestData: InitialRequestData? = null

    private var cefClient: CefClient? = null
    private var browser: CefBrowser? = null
    private var messageRouter: CefMessageRouter? = null
    private val renderHandler = HeadlessRenderHandler()
    private val viewDelegate = KcefViewDelegate()
    private val scrollDelegate = KcefScrollDelegate()
    private val awtEventComponent = object : AwtCanvas() {}
    @Volatile
    private var backgroundColor: Int = 0xFFFFFFFF.toInt()

    private val evalCallbacks = ConcurrentHashMap<String, ValueCallback<String>>()

    private val handler = Handler(view.webViewLooper)

    private object BrowserInterop {
        private val implClass = runCatching { Class.forName("org.cef.browser.CefBrowser_N") }.getOrNull()
        private val primitiveInt = Int::class.javaPrimitiveType ?: Integer.TYPE
        private val wasResizedMethod = fetch("wasResized", primitiveInt, primitiveInt)
        private val sendMouseEventMethod = fetch("sendMouseEvent", AwtMouseEvent::class.java)
        private val sendMouseWheelEventMethod = fetch("sendMouseWheelEvent", AwtMouseWheelEvent::class.java)
        private val sendKeyEventMethod = fetch("sendKeyEvent", AwtKeyEvent::class.java)

        private fun fetch(name: String, vararg params: Class<*>): Method? =
            implClass?.runCatching {
                getDeclaredMethod(name, *params).apply { isAccessible = true }
            }?.getOrNull()

        private fun invoke(method: Method?, browser: CefBrowser, vararg args: Any?) {
            val targetClass = implClass ?: return
            if (method == null || !targetClass.isInstance(browser)) {
                Log.w(TAG, "CefBrowser implementation ${browser.javaClass.name} does not expose ${method?.name ?: "unknown"}")
                return
            }
            try {
                method.invoke(browser, *args)
            } catch (t: Throwable) {
                Log.w(TAG, "Failed to invoke ${method.name}", t)
            }
        }

        fun wasResized(browser: CefBrowser, width: Int, height: Int) =
            invoke(wasResizedMethod, browser, width, height)

        fun sendMouseEvent(browser: CefBrowser, event: AwtMouseEvent) =
            invoke(sendMouseEventMethod, browser, event)

        fun sendMouseWheelEvent(browser: CefBrowser, event: AwtMouseWheelEvent) =
            invoke(sendMouseWheelEventMethod, browser, event)

        fun sendKeyEvent(browser: CefBrowser, event: AwtKeyEvent) =
            invoke(sendKeyEventMethod, browser, event)
    }

    companion object {
        const val TAG = "KcefWebViewProvider"
        const val QUERY_FN = "cefQuery"
        const val QUERY_CANCEL_FN = "cefQueryCancel"
        private const val EVAL_INTERFACE = "__suwayomiEval"
        private const val BLANK_URI = "about:blank"
        private const val WHEEL_TICKS_PER_SCROLL = 3f
        private val NULL_DEVICE_PATH =
            if (System.getProperty("os.name")?.lowercase(Locale.US)?.contains("win") == true) {
                "NUL"
            } else {
                "/dev/null"
            }
        private val HOVER_ACTIONS = setOf(
            MotionEvent.ACTION_HOVER_ENTER,
            MotionEvent.ACTION_HOVER_EXIT,
            MotionEvent.ACTION_HOVER_MOVE,
        )
        private val missingSettingsFields = ConcurrentHashMap.newKeySet<String>()

        private val initHandler: InitBrowserHandler by KoinPlatformTools.defaultContext().get().inject()

        private fun ensureCefApp(): CefApp =
            CefAppBridge.getOrCreate {
                val builder = CefAppBuilder()
                builder.setInstallDir(resolveInstallDir())
                builder.getCefSettings().apply {
                    windowless_rendering_enabled = true
                    persist_session_cookies = true
                    log_severity = CefSettings.LogSeverity.LOGSEVERITY_DISABLE
                    log_file = NULL_DEVICE_PATH
                    trySetBooleanOption("external_message_pump", true)
                    trySetBooleanOption("multi_threaded_message_loop", false)
                }
                builder.addJcefArgs(
                   "--no-sandbox",
                    "--disable-logging",
                    "--log-file=$NULL_DEVICE_PATH",
                    "--disable-gpu",
                    "--disable-gpu-compositing",
                    "--use-gl=swiftshader",
                    "--off-screen-rendering-enabled",
                    "--disable-dev-shm-usage",
                    "--change-stack-guard-on-fork=disable",
                )

                try {
                    builder.build()
                } catch (e: UnsupportedPlatformException) {
                    throw IllegalStateException("Unsupported platform for JCEF", e)
                } catch (e: CefInitializationException) {
                    throw IllegalStateException("Failed to initialize JCEF", e)
                } catch (e: InterruptedException) {
                    Thread.currentThread().interrupt()
                    throw IllegalStateException("Interrupted while initializing JCEF", e)
                } catch (e: IOException) {
                    throw IllegalStateException("Unable to install JCEF binaries", e)
                }
            }

        private fun resolveInstallDir(): File {
            val configured = System.getProperty("jcef.dir")
            Log.d(TAG, "JCEF Directory $configured")
            val base = configured?.let(::File) ?: File(System.getProperty("user.home"), ".suwayomi/jcef")
            if (!base.exists() && !base.mkdirs()) {
                throw IllegalStateException("Unable to create JCEF install directory at ${base.absolutePath}")
            }
            return base
        }

        fun ensureRuntimeReady() {
            val installDir = resolveInstallDir()
            ensureCefApp()
            JoglNativeLoader.ensureLoaded(installDir)
        }

        private fun CefSettings.trySetBooleanOption(fieldName: String, value: Boolean) {
            val field = runCatching { javaClass.getDeclaredField(fieldName) }.getOrNull()
            if (field == null) {
                if (missingSettingsFields.add(fieldName)) {
                    Log.i(TAG, "CefSettings does not expose '$fieldName'; skipping toggle")
                }
                return
            }
            runCatching {
                field.isAccessible = true
                field.setBoolean(this, value)
            }.onFailure {
                Log.w(TAG, "Unable to set CefSettings.$fieldName", it)
            }
        }
    }

    public interface InitBrowserHandler {
        public fun init(provider: KcefWebViewProvider)
    }

    private data class InitialRequestData(
        private val additionalHttpHeaders: Map<String, String>? = null,
        private val myPostData: ByteArray? = null,
    ) {
        fun apply(request: CefRequest?) {
            request?.apply {
                Log.v(TAG, "Initial request: applying headers and post data")
                if (!additionalHttpHeaders.isNullOrEmpty()) {
                    additionalHttpHeaders.forEach {
                        setHeaderByName(it.key, it.value, true)
                    }
                }

                if (myPostData != null) {
                    postData =
                        CefPostData.create().apply {
                            addElement(
                                CefPostDataElement.create().apply {
                                    setToBytes(myPostData.size, myPostData)
                                },
                            )
                        }
                }
            }
        }
    }

    private fun requireClient(): CefClient =
        cefClient ?: throw IllegalStateException("JCEF client is not initialized")

    private fun createBrowserForUrl(url: String): CefBrowser =
        requireClient()
            .createBrowser(url, true, false)
            .apply {
                createImmediately()
                ensureInitialSize()
            }

    private fun createBrowserWithHtml(html: String): CefBrowser =
        requireClient()
            .createBrowser(BLANK_URI, true, false)
            .apply {
                createImmediately()
                ensureInitialSize()
                val encoded = Base64.getEncoder().encodeToString(html.toByteArray(StandardCharsets.UTF_8))
                loadURL("data:text/html;base64,$encoded")
            }

    private fun shutdownBrowser() {
        browser?.let {
            it.close(true)
        }
        browser = null
    }

    private fun CefBrowser.ensureInitialSize() {
        val width = currentViewWidth()
        val height = currentViewHeight()
        BrowserInterop.wasResized(this, width, height)
    }

    private fun currentViewWidth(): Int =
        max(
            view.width.takeIf { it > 0 }
                ?: view.measuredWidth.takeIf { it > 0 }
                ?: view.minimumWidth.takeIf { it > 0 }
                ?: 1,
            1,
        )

    private fun currentViewHeight(): Int =
        max(
            view.height.takeIf { it > 0 }
                ?: view.measuredHeight.takeIf { it > 0 }
                ?: view.minimumHeight.takeIf { it > 0 }
                ?: 1,
            1,
        )

    private class CefWebResourceRequest(
        val request: CefRequest?,
        val frame: CefFrame?,
        val redirect: Boolean,
    ) : WebResourceRequest {
        override fun getUrl(): Uri = Uri.parse(request?.url)

        override fun isForMainFrame(): Boolean = frame?.isMain ?: false

        override fun isRedirect(): Boolean = redirect

        override fun hasGesture(): Boolean = false

        override fun getMethod(): String = request?.method ?: "GET"

        override fun getRequestHeaders(): Map<String, String> {
            val headers = mutableMapOf<String, String>()
            request?.getHeaderMap(headers)
            return headers
        }
    }

    private fun httpStatusText(code: Int): String =
        when (code) {
            400 -> "Bad Request"
            401 -> "Unauthorized"
            403 -> "Forbidden"
            404 -> "Not Found"
            408 -> "Request Timeout"
            409 -> "Conflict"
            410 -> "Gone"
            412 -> "Precondition Failed"
            413 -> "Payload Too Large"
            415 -> "Unsupported Media Type"
            418 -> "I'm a teapot"
            429 -> "Too Many Requests"
            in 400..499 -> "Client Error"
            500 -> "Internal Server Error"
            502 -> "Bad Gateway"
            503 -> "Service Unavailable"
            504 -> "Gateway Timeout"
            in 500..599 -> "Server Error"
            else -> "HTTP Error"
        }

    @Suppress("DEPRECATION")
    private fun notifyLegacyError(errorCode: Int, description: String, failingUrl: String) {
        viewClient.onReceivedError(view, errorCode, description, failingUrl)
    }

    private inner class DisplayHandler : CefDisplayHandlerAdapter() {
        override fun onConsoleMessage(
            browser: CefBrowser,
            level: CefSettings.LogSeverity,
            message: String,
            source: String,
            line: Int,
        ): Boolean {
            Log.v(TAG, "$source:$line[$level]: $message")
            return true
        }

        override fun onAddressChange(
            browser: CefBrowser,
            frame: CefFrame,
            url: String,
        ) {
            Log.d(TAG, "Navigate to $url")
        }

        override fun onStatusMessage(
            browser: CefBrowser,
            value: String,
        ) {
            Log.v(TAG, "Status update: $value")
        }
    }

    private inner class LoadHandler : CefLoadHandlerAdapter() {
        override fun onLoadEnd(
            browser: CefBrowser,
            frame: CefFrame,
            httpStatusCode: Int,
        ) {
            val url = frame.url ?: ""
            val syntheticRequest = buildSyntheticRequest(frame)
            Log.v(TAG, "Load end $url")
            handler.post {
                if (httpStatusCode == 404) {
                    notifyLegacyError(WebViewClient.ERROR_FILE_NOT_FOUND, "Not Found", url)
                }
                if (httpStatusCode == 429) {
                    notifyLegacyError(WebViewClient.ERROR_TOO_MANY_REQUESTS, "Too Many Requests", url)
                }
                if (httpStatusCode >= 400) {
                    val syntheticResponse = buildSyntheticResponse(httpStatusCode)
                    viewClient.onReceivedHttpError(view, syntheticRequest, syntheticResponse)
                }
                viewClient.onPageFinished(view, url)
                chromeClient.onProgressChanged(view, 100)
            }
        }

        override fun onLoadError(
            browser: CefBrowser,
            frame: CefFrame,
            errorCode: CefLoadHandler.ErrorCode,
            errorText: String,
            failedUrl: String,
        ) {
            Log.w(TAG, "Load error ($failedUrl) [$errorCode]: $errorText")
            handler.post {
                notifyLegacyError(
                    WebViewClient.ERROR_UNKNOWN,
                    errorText,
                    frame.url ?: failedUrl,
                )
            }
        }

        override fun onLoadStart(
            browser: CefBrowser,
            frame: CefFrame,
            transitionType: CefRequest.TransitionType,
        ) {
            Log.v(TAG, "Load start, pushing mappings")
            mappings.forEach {
                val js =
                    """
                        window.${it.interfaceName} = window.${it.interfaceName} || {}
                        window.${it.interfaceName}.${it.functionName} = async function() {
                            const args = await Promise.all(Array.from(arguments));
                            return new Promise((resolve, reject) => {
                                window.$QUERY_FN({
                                    request: JSON.stringify({
                                        functionName: ${Json.encodeToString(it.functionName)},
                                        interfaceName: ${Json.encodeToString(it.interfaceName)},
                                        args,
                                    }),
                                    persistent: false,
                                    onSuccess: resolve,
                                    onFailure: (_, err) => reject(err),
                                })
                            });
                        }
                    """
                browser.executeJavaScript(js, "SUWAYOMI ${it.toNice()}", 0)
            }

            handler.post { viewClient.onPageStarted(view, frame.url, null) }
        }

        private fun buildSyntheticRequest(frame: CefFrame): WebResourceRequest {
            val uri = Uri.parse(frame.url ?: BLANK_URI)
            val isMain = frame.isMain
            return object : WebResourceRequest {
                override fun getUrl(): Uri = uri
                override fun isForMainFrame(): Boolean = isMain
                override fun isRedirect(): Boolean = false
                override fun hasGesture(): Boolean = false
                override fun getMethod(): String = "GET"
                override fun getRequestHeaders(): Map<String, String> = emptyMap()
            }
        }

        private fun buildSyntheticResponse(httpStatusCode: Int): WebResourceResponse =
            WebResourceResponse(
                null,
                StandardCharsets.UTF_8.name(),
                httpStatusCode,
                httpStatusText(httpStatusCode),
                emptyMap(),
                null,
            )
    }

    private inner class MessageRouterHandler : CefMessageRouterHandlerAdapter() {
        override fun onQuery(
            browser: CefBrowser,
            frame: CefFrame,
            queryId: Long,
            request: String,
            persistent: Boolean,
            callback: CefQueryCallback,
        ): Boolean {
            val invoke =
                try {
                    Json.decodeFromString<FunctionCall>(request)
                } catch (e: Exception) {
                    Log.w(TAG, "Invalid request received $e")
                    return false
                }

            if (invoke.interfaceName == EVAL_INTERFACE) {
                handleEvalResult(
                    invoke.functionName,
                    invoke.args.getOrNull(0),
                    invoke.args.getOrNull(1),
                )
                callback.success("")
                return true
            }

            mappingIndex[mappingKey(invoke.interfaceName, invoke.functionName)]?.let {
                    handler.post {
                        try {
                            Log.v(
                                TAG,
                                "Received request to invoke ${it.toNice()} with ${invoke.args.size} args",
                            )
                            val retval = it.fn.call(it.obj, *invoke.args)
                            callback.success(retval.toString())
                        } catch (e: Exception) {
                            Log.w(TAG, "JS-invoke on ${it.toNice()} failed:", e)
                            callback.failure(0, e.message)
                        }
                    }
                    return true
                }
            return false
        }
    }

    private abstract class ArrayResponseResourceHandler : CefResourceHandlerAdapter() {
        protected var resolvedData: ByteArray? = null
        protected var readOffset = 0

        override fun getResponseHeaders(
            response: CefResponse,
            responseLength: IntRef,
            redirectUrl: StringRef,
        ) {
            responseLength.set(resolvedData?.size ?: 0)
            response.status = 200
            response.statusText = "OK"
            response.mimeType = "text/html"
        }

        override fun readResponse(
            dataOut: ByteArray,
            bytesToRead: Int,
            bytesRead: IntRef,
            callback: CefCallback,
        ): Boolean {
            val data = resolvedData ?: return false
            val bytesToTransfer = minOf(bytesToRead, data.size - readOffset)
            Log.v(
                TAG,
                "readResponse: $readOffset/${data.size}, reading $bytesToRead->$bytesToTransfer",
            )
            data.copyInto(dataOut, startIndex = readOffset, endIndex = readOffset + bytesToTransfer)
            bytesRead.set(bytesToTransfer)
            readOffset += bytesToTransfer
            return bytesToTransfer != 0
        }
    }

    private inner class WebResponseResourceHandler(
        val webResponse: WebResourceResponse,
    ) : ArrayResponseResourceHandler() {
        override fun processRequest(
            request: CefRequest,
            callback: CefCallback,
        ): Boolean {
            Log.v(TAG, "Handling request from client's response for ${request.url}")
            try {
                val dataStream = webResponse.data
                if (dataStream == null) {
                    Log.w(TAG, "Client response stream missing for ${request.url}")
                    resolvedData = ByteArray(0)
                } else {
                    dataStream.use { stream ->
                        resolvedData = stream.readAllBytesCompat()
                    }
                    Log.v(TAG, "Resolved client response for ${resolvedData?.size ?: 0} bytes")
                }
            } catch (e: IOException) {
                Log.w(TAG, "Failed to read client data", e)
            }
            callback.Continue()
            return true
        }

        override fun getResponseHeaders(
            response: CefResponse,
            responseLength: IntRef,
            redirectUrl: StringRef,
        ) {
            super.getResponseHeaders(response, responseLength, redirectUrl)
            webResponse.responseHeaders?.forEach { response.setHeaderByName(it.key, it.value, true) }
            response.status = webResponse.statusCode
            response.mimeType = webResponse.mimeType
        }
    }

    private inner class HtmlResponseResourceHandler(
        val html: String,
    ) : ArrayResponseResourceHandler() {
        override fun processRequest(
            request: CefRequest,
            callback: CefCallback,
        ): Boolean {
            Log.v(TAG, "Handling request from HTML cache for ${request.url}")
            resolvedData = html.toByteArray()
            callback.Continue()
            return true
        }
    }

    private inner class ResourceRequestHandler : CefResourceRequestHandlerAdapter() {
        override fun onBeforeResourceLoad(
            browser: CefBrowser?,
            frame: CefFrame?,
            request: CefRequest,
        ): Boolean {
            initialRequestData?.apply(request)
            initialRequestData = null
            request.setHeaderByName("user-agent", settings.userAgentString, true)

            val cancel =
                viewClient.shouldOverrideUrlLoading(
                    view,
                    CefWebResourceRequest(request, frame, false),
                )
            Log.v(TAG, "Resource ${request.url}, result is cancel? $cancel")

            handler.post { viewClient.onLoadResource(view, frame?.url) }

            return cancel || settings.blockNetworkLoads
        }

        override fun getResourceHandler(
            browser: CefBrowser,
            frame: CefFrame,
            request: CefRequest,
        ): CefResourceHandler? {
            val isInitialLoad = frame.url.isEmpty() && request.method == "GET"
            Log.v(TAG, "Request ${request.method} ${request.url} is initial? $isInitialLoad")
            val response =
                if (isInitialLoad) {
                    null
                } else {
                    viewClient.shouldInterceptRequest(
                        view,
                        CefWebResourceRequest(request, frame, false),
                    )
                }
            if (response == null) {
                urlHttpMapping[request.url.trimEnd('/')]?.let {
                    return HtmlResponseResourceHandler(it)
                }
            }
            response ?: return null
            return WebResponseResourceHandler(response)
        }
    }

    private inner class RequestHandler : CefRequestHandlerAdapter() {
        override fun getResourceRequestHandler(
            browser: CefBrowser,
            frame: CefFrame,
            request: CefRequest,
            isNavigation: Boolean,
            isDownload: Boolean,
            requestInitiator: String,
            disableDefaultHandling: BoolRef,
        ): CefResourceRequestHandler? = ResourceRequestHandler()

    }

    private inner class FocusHandler : CefFocusHandler {
        override fun onTakeFocus(browser: CefBrowser?, next: Boolean) {
            // No-op; we never hand focus back to the host view when running headless.
        }

        override fun onGotFocus(browser: CefBrowser?) {
            // Headless/off-screen context: acknowledge focus but never forward to the stub WebView.
        }

        override fun onSetFocus(browser: CefBrowser?, source: CefFocusHandler.FocusSource?): Boolean {
            browser?.setFocus(true)
            return true
        }
    }

    private inner class HeadlessRenderHandler : CefRenderHandlerAdapter() {
        private val frameLock = Any()
        private var frame: RenderFrame? = null

        override fun getViewRect(browser: CefBrowser): Rectangle = viewBounds()

        override fun addOnPaintListener(listener: Consumer<CefPaintEvent>) {
            // Rendering is handled via onPaint; listeners are not used in headless mode.
        }

        override fun setOnPaintListener(listener: Consumer<CefPaintEvent>) {
            // No-op. Consumers should rely on onPaint callbacks instead.
        }

        override fun removeOnPaintListener(listener: Consumer<CefPaintEvent>) {
            // No-op. Nothing to clean up.
        }

        override fun onPaint(
            browser: CefBrowser,
            popup: Boolean,
            dirtyRects: Array<Rectangle>,
            buffer: ByteBuffer,
            width: Int,
            height: Int,
        ) {
            if (width <= 0 || height <= 0) {
                return
            }
            val renderFrame = ensureFrame(width, height)
            buffer.order(ByteOrder.LITTLE_ENDIAN)
            buffer.position(0)
            val dst = (renderFrame.image.raster.dataBuffer as DataBufferInt).data
            buffer.asIntBuffer().get(dst, 0, width * height)
            synchronized(frameLock) {
                frame = renderFrame
            }
            view.postInvalidate()
        }

        fun drawInto(canvas: Canvas) {
            val snapshot = synchronized(frameLock) { frame } ?: return
            if ((backgroundColor ushr 24) != 0) {
                canvas.drawColor(backgroundColor)
            }
            canvas.drawBitmap(snapshot.bitmap, 0f, 0f, null)
        }

        fun contentWidth(): Int = synchronized(frameLock) { frame?.width ?: currentViewWidth() }

        fun contentHeight(): Int = synchronized(frameLock) { frame?.height ?: currentViewHeight() }

        fun clear() {
            synchronized(frameLock) {
                frame = null
            }
        }

        private fun ensureFrame(width: Int, height: Int): RenderFrame {
            val existing = synchronized(frameLock) { frame }
            if (existing != null && existing.width == width && existing.height == height) {
                return existing
            }
            Log.v(TAG, "Headless render surface resized to $width x $height")
            val image = BufferedImage(width, height, BufferedImage.TYPE_INT_ARGB_PRE)
            val bitmap = Bitmap(image)
            val created = RenderFrame(width, height, image, bitmap)
            synchronized(frameLock) {
                frame = created
            }
            return created
        }

        private fun viewBounds(): Rectangle =
            Rectangle(0, 0, currentViewWidth(), currentViewHeight())
    }

    private data class RenderFrame(
        val width: Int,
        val height: Int,
        val image: BufferedImage,
        val bitmap: Bitmap,
    )

    private fun handleTouchEvent(event: MotionEvent): Boolean {
        val cefBrowser = browser ?: return false
        if (event.actionMasked == MotionEvent.ACTION_SCROLL) {
            return handleScrollEvent(event)
        }
        if (event.pointerCount == 0) {
            return false
        }
        sendMouseEvent(cefBrowser, event, treatAsHover = false)
        return true
    }

    private fun handleHoverEvent(event: MotionEvent): Boolean {
        val cefBrowser = browser ?: return false
        sendMouseEvent(cefBrowser, event, treatAsHover = true)
        return true
    }

    private fun handleScrollEvent(event: MotionEvent): Boolean {
        val cefBrowser = browser ?: return false
        val verticalDelta = -event.getAxisValue(MotionEvent.AXIS_VSCROLL)
        val horizontalDelta = event.getAxisValue(MotionEvent.AXIS_HSCROLL)
        if (verticalDelta == 0f && horizontalDelta == 0f) {
            return false
        }
        val modifiers = translateMetaState(event.metaState)
        if (verticalDelta != 0f) {
            val wheelEvent =
                AwtMouseWheelEvent(
                    awtEventComponent,
                    AwtMouseEvent.MOUSE_WHEEL,
                    System.currentTimeMillis(),
                    modifiers,
                    event.x.roundToInt(),
                    event.y.roundToInt(),
                    0,
                    false,
                    AwtMouseWheelEvent.WHEEL_UNIT_SCROLL,
                    1,
                    (verticalDelta * WHEEL_TICKS_PER_SCROLL).roundToInt(),
                )
            BrowserInterop.sendMouseWheelEvent(cefBrowser, wheelEvent)
        }
        if (horizontalDelta != 0f) {
            val wheelEvent =
                AwtMouseWheelEvent(
                    awtEventComponent,
                    AwtMouseEvent.MOUSE_WHEEL,
                    System.currentTimeMillis(),
                    modifiers or AwtInputEvent.SHIFT_DOWN_MASK,
                    event.x.roundToInt(),
                    event.y.roundToInt(),
                    0,
                    false,
                    AwtMouseWheelEvent.WHEEL_UNIT_SCROLL,
                    1,
                    (-horizontalDelta * WHEEL_TICKS_PER_SCROLL).roundToInt(),
                )
            BrowserInterop.sendMouseWheelEvent(cefBrowser, wheelEvent)
        }
        return true
    }

    private fun sendMouseEvent(browser: CefBrowser, event: MotionEvent, treatAsHover: Boolean) {
        val action = event.actionMasked
        val id =
            when (action) {
                MotionEvent.ACTION_DOWN, MotionEvent.ACTION_POINTER_DOWN -> AwtMouseEvent.MOUSE_PRESSED
                MotionEvent.ACTION_UP, MotionEvent.ACTION_POINTER_UP -> AwtMouseEvent.MOUSE_RELEASED
                MotionEvent.ACTION_HOVER_MOVE -> AwtMouseEvent.MOUSE_MOVED
                MotionEvent.ACTION_HOVER_ENTER -> AwtMouseEvent.MOUSE_ENTERED
                MotionEvent.ACTION_HOVER_EXIT -> AwtMouseEvent.MOUSE_EXITED
                MotionEvent.ACTION_MOVE -> if (event.buttonState != 0) AwtMouseEvent.MOUSE_DRAGGED else AwtMouseEvent.MOUSE_MOVED
                MotionEvent.ACTION_CANCEL -> AwtMouseEvent.MOUSE_RELEASED
                else -> return
            }
        if (treatAsHover && action !in HOVER_ACTIONS) {
            return
        }
        val pointerIndex = event.actionIndex.coerceAtLeast(0).coerceAtMost(event.pointerCount - 1)
        val buttonInfo = resolveButtonInfo(event.buttonState, action == MotionEvent.ACTION_DOWN || action == MotionEvent.ACTION_UP)
        val modifiers = translateMetaState(event.metaState) or buttonInfo.mask
        val awtEvent =
            AwtMouseEvent(
                awtEventComponent,
                id,
                System.currentTimeMillis(),
                modifiers,
                event.getX(pointerIndex).roundToInt(),
                event.getY(pointerIndex).roundToInt(),
                1,
                false,
                buttonInfo.button,
            )
        BrowserInterop.sendMouseEvent(browser, awtEvent)
    }

    private data class ButtonInfo(val button: Int, val mask: Int)

    private fun resolveButtonInfo(buttonState: Int, defaultPrimary: Boolean): ButtonInfo {
        var resolvedState = buttonState
        if (resolvedState == 0 && defaultPrimary) {
            resolvedState = MotionEvent.BUTTON_PRIMARY
        }
        return when {
            resolvedState and MotionEvent.BUTTON_PRIMARY != 0 -> ButtonInfo(AwtMouseEvent.BUTTON1, AwtInputEvent.BUTTON1_DOWN_MASK)
            resolvedState and MotionEvent.BUTTON_SECONDARY != 0 -> ButtonInfo(AwtMouseEvent.BUTTON3, AwtInputEvent.BUTTON3_DOWN_MASK)
            resolvedState and MotionEvent.BUTTON_TERTIARY != 0 -> ButtonInfo(AwtMouseEvent.BUTTON2, AwtInputEvent.BUTTON2_DOWN_MASK)
            else -> ButtonInfo(AwtMouseEvent.NOBUTTON, 0)
        }
    }

    private fun translateMetaState(metaState: Int): Int {
        var modifiers = 0
        if ((metaState and KeyEvent.META_SHIFT_ON) != 0) {
            modifiers = modifiers or AwtInputEvent.SHIFT_DOWN_MASK
        }
        if ((metaState and KeyEvent.META_CTRL_ON) != 0) {
            modifiers = modifiers or AwtInputEvent.CTRL_DOWN_MASK
        }
        if ((metaState and KeyEvent.META_ALT_ON) != 0) {
            modifiers = modifiers or AwtInputEvent.ALT_DOWN_MASK
        }
        if ((metaState and KeyEvent.META_META_ON) != 0) {
            modifiers = modifiers or AwtInputEvent.META_DOWN_MASK
        }
        return modifiers
    }

    private fun handleKeyDown(event: KeyEvent): Boolean {
        val cefBrowser = browser ?: return false
        val keyDown = buildAwtKeyEvent(event, AwtKeyEvent.KEY_PRESSED) ?: return false
        BrowserInterop.sendKeyEvent(cefBrowser, keyDown)
        buildAwtKeyEvent(event, AwtKeyEvent.KEY_TYPED)?.let { BrowserInterop.sendKeyEvent(cefBrowser, it) }
        return true
    }

    private fun handleKeyUp(event: KeyEvent): Boolean {
        val cefBrowser = browser ?: return false
        val keyUp = buildAwtKeyEvent(event, AwtKeyEvent.KEY_RELEASED) ?: return false
        BrowserInterop.sendKeyEvent(cefBrowser, keyUp)
        return true
    }

    private fun buildAwtKeyEvent(event: KeyEvent, id: Int): AwtKeyEvent? {
        val keyCode = translateKeyCode(event.keyCode)
        val keyChar =
            when (id) {
                AwtKeyEvent.KEY_TYPED -> {
                    val unicode = event.unicodeChar
                    if (unicode == 0) {
                        AwtKeyEvent.CHAR_UNDEFINED
                    } else {
                        unicode.toChar()
                    }
                }
                else -> AwtKeyEvent.CHAR_UNDEFINED
            }
        if (keyCode == AwtKeyEvent.VK_UNDEFINED && keyChar == AwtKeyEvent.CHAR_UNDEFINED) {
            return null
        }
        val location = keyLocationFor(event.keyCode)
        return AwtKeyEvent(
            awtEventComponent,
            id,
            System.currentTimeMillis(),
            translateMetaState(event.metaState),
            keyCode,
            keyChar,
            location,
        )
    }

    private fun translateKeyCode(androidCode: Int): Int =
        when {
            androidCode in KeyEvent.KEYCODE_A..KeyEvent.KEYCODE_Z -> AwtKeyEvent.VK_A + (androidCode - KeyEvent.KEYCODE_A)
            androidCode in KeyEvent.KEYCODE_0..KeyEvent.KEYCODE_9 -> AwtKeyEvent.VK_0 + (androidCode - KeyEvent.KEYCODE_0)
            androidCode in KeyEvent.KEYCODE_NUMPAD_0..KeyEvent.KEYCODE_NUMPAD_9 -> AwtKeyEvent.VK_NUMPAD0 + (androidCode - KeyEvent.KEYCODE_NUMPAD_0)
            androidCode == KeyEvent.KEYCODE_ENTER -> AwtKeyEvent.VK_ENTER
            androidCode == KeyEvent.KEYCODE_DPAD_LEFT -> AwtKeyEvent.VK_LEFT
            androidCode == KeyEvent.KEYCODE_DPAD_RIGHT -> AwtKeyEvent.VK_RIGHT
            androidCode == KeyEvent.KEYCODE_DPAD_UP -> AwtKeyEvent.VK_UP
            androidCode == KeyEvent.KEYCODE_DPAD_DOWN -> AwtKeyEvent.VK_DOWN
            androidCode == KeyEvent.KEYCODE_DPAD_CENTER -> AwtKeyEvent.VK_SPACE
            androidCode == KeyEvent.KEYCODE_DEL -> AwtKeyEvent.VK_BACK_SPACE
            androidCode == KeyEvent.KEYCODE_FORWARD_DEL -> AwtKeyEvent.VK_DELETE
            androidCode == KeyEvent.KEYCODE_TAB -> AwtKeyEvent.VK_TAB
            androidCode == KeyEvent.KEYCODE_SPACE -> AwtKeyEvent.VK_SPACE
            androidCode == KeyEvent.KEYCODE_ESCAPE -> AwtKeyEvent.VK_ESCAPE
            androidCode == KeyEvent.KEYCODE_MOVE_HOME -> AwtKeyEvent.VK_HOME
            androidCode == KeyEvent.KEYCODE_MOVE_END -> AwtKeyEvent.VK_END
            androidCode == KeyEvent.KEYCODE_PAGE_UP -> AwtKeyEvent.VK_PAGE_UP
            androidCode == KeyEvent.KEYCODE_PAGE_DOWN -> AwtKeyEvent.VK_PAGE_DOWN
            androidCode == KeyEvent.KEYCODE_F1 -> AwtKeyEvent.VK_F1
            androidCode == KeyEvent.KEYCODE_F2 -> AwtKeyEvent.VK_F2
            androidCode == KeyEvent.KEYCODE_F3 -> AwtKeyEvent.VK_F3
            androidCode == KeyEvent.KEYCODE_F4 -> AwtKeyEvent.VK_F4
            androidCode == KeyEvent.KEYCODE_F5 -> AwtKeyEvent.VK_F5
            androidCode == KeyEvent.KEYCODE_F6 -> AwtKeyEvent.VK_F6
            androidCode == KeyEvent.KEYCODE_F7 -> AwtKeyEvent.VK_F7
            androidCode == KeyEvent.KEYCODE_F8 -> AwtKeyEvent.VK_F8
            androidCode == KeyEvent.KEYCODE_F9 -> AwtKeyEvent.VK_F9
            androidCode == KeyEvent.KEYCODE_F10 -> AwtKeyEvent.VK_F10
            androidCode == KeyEvent.KEYCODE_F11 -> AwtKeyEvent.VK_F11
            androidCode == KeyEvent.KEYCODE_F12 -> AwtKeyEvent.VK_F12
            androidCode == KeyEvent.KEYCODE_SHIFT_LEFT || androidCode == KeyEvent.KEYCODE_SHIFT_RIGHT -> AwtKeyEvent.VK_SHIFT
            androidCode == KeyEvent.KEYCODE_CTRL_LEFT || androidCode == KeyEvent.KEYCODE_CTRL_RIGHT -> AwtKeyEvent.VK_CONTROL
            androidCode == KeyEvent.KEYCODE_ALT_LEFT || androidCode == KeyEvent.KEYCODE_ALT_RIGHT -> AwtKeyEvent.VK_ALT
            else -> AwtKeyEvent.VK_UNDEFINED
        }

    private fun keyLocationFor(androidCode: Int): Int =
        when (androidCode) {
            KeyEvent.KEYCODE_SHIFT_LEFT,
            KeyEvent.KEYCODE_CTRL_LEFT,
            KeyEvent.KEYCODE_ALT_LEFT -> AwtKeyEvent.KEY_LOCATION_LEFT
            KeyEvent.KEYCODE_SHIFT_RIGHT,
            KeyEvent.KEYCODE_CTRL_RIGHT,
            KeyEvent.KEYCODE_ALT_RIGHT -> AwtKeyEvent.KEY_LOCATION_RIGHT
            in KeyEvent.KEYCODE_NUMPAD_0..KeyEvent.KEYCODE_NUMPAD_EQUALS -> AwtKeyEvent.KEY_LOCATION_NUMPAD
            else -> AwtKeyEvent.KEY_LOCATION_STANDARD
        }

    private inner class KcefViewDelegate : ViewDelegate {

        override fun shouldDelayChildPressedState(): Boolean = false

        override fun onProvideVirtualStructure(structure: ViewStructure) {}

        override fun onProvideAutofillVirtualStructure(
            structure: ViewStructure,
            flags: Int,
        ) {
        }

        override fun autofill(values: SparseArray<AutofillValue>) {}

        override fun isVisibleToUserForAutofill(virtualId: Int): Boolean = true

        override fun onProvideContentCaptureStructure(
            structure: ViewStructure,
            flags: Int,
        ) {}

        override fun getAccessibilityNodeProvider(): AccessibilityNodeProvider? = null

        override fun onInitializeAccessibilityNodeInfo(info: AccessibilityNodeInfo) {}

        override fun onInitializeAccessibilityEvent(event: AccessibilityEvent) {}

        override fun performAccessibilityAction(action: Int, arguments: Bundle): Boolean = false

        override fun setOverScrollMode(mode: Int) {}

        override fun setScrollBarStyle(style: Int) {}

        override fun onDrawVerticalScrollBar(
            canvas: Canvas,
            scrollBar: Drawable,
            l: Int,
            t: Int,
            r: Int,
            b: Int,
        ) {}

        override fun onOverScrolled(
            scrollX: Int,
            scrollY: Int,
            clampedX: Boolean,
            clampedY: Boolean,
        ) {}

        override fun onWindowVisibilityChanged(visibility: Int) {}

        override fun onDraw(canvas: Canvas) {
            renderHandler.drawInto(canvas)
        }

        override fun setLayoutParams(layoutParams: LayoutParams) {}

        override fun performLongClick(): Boolean = false

        override fun onConfigurationChanged(newConfig: Configuration) {}

        override fun onCreateInputConnection(outAttrs: EditorInfo): InputConnection? = null

        override fun onDragEvent(event: DragEvent): Boolean = false

        override fun onKeyMultiple(
            keyCode: Int,
            repeatCount: Int,
            event: KeyEvent,
        ): Boolean = false

        override fun onKeyDown(
            keyCode: Int,
            event: KeyEvent,
        ): Boolean = handleKeyDown(event)

        override fun onKeyUp(
            keyCode: Int,
            event: KeyEvent,
        ): Boolean = handleKeyUp(event)

        override fun onAttachedToWindow() {}

        override fun onDetachedFromWindow() {}

        override fun onMovedToDisplay(displayId: Int, config: Configuration) {}

        override fun onVisibilityChanged(changedView: View, visibility: Int) {}

        override fun onWindowFocusChanged(hasWindowFocus: Boolean) {}

        override fun onFocusChanged(
            focused: Boolean,
            direction: Int,
            previouslyFocusedRect: Rect?,
        ) {}

        override fun setFrame(left: Int, top: Int, right: Int, bottom: Int): Boolean = true

        override fun onSizeChanged(w: Int, h: Int, ow: Int, oh: Int) {
            browser?.let { BrowserInterop.wasResized(it, max(w, 1), max(h, 1)) }
        }

        override fun onScrollChanged(l: Int, t: Int, oldl: Int, oldt: Int) {}

        override fun dispatchKeyEvent(event: KeyEvent): Boolean =
            when (event.action) {
                KeyEvent.ACTION_DOWN -> handleKeyDown(event)
                KeyEvent.ACTION_UP -> handleKeyUp(event)
                else -> false
            }

        override fun onTouchEvent(ev: MotionEvent): Boolean = handleTouchEvent(ev)

        override fun onHoverEvent(event: MotionEvent): Boolean = handleHoverEvent(event)

        override fun onGenericMotionEvent(event: MotionEvent): Boolean =
            if (event.action == MotionEvent.ACTION_SCROLL) handleScrollEvent(event) else false

        override fun onTrackballEvent(ev: MotionEvent): Boolean = handleTouchEvent(ev)

        override fun requestFocus(direction: Int, previouslyFocusedRect: Rect?): Boolean = true

        override fun onMeasure(widthMeasureSpec: Int, heightMeasureSpec: Int) {}

        override fun requestChildRectangleOnScreen(
            child: View,
            rect: Rect,
            immediate: Boolean,
        ): Boolean = false

        override fun setBackgroundColor(color: Int) {
            backgroundColor = color
            view.postInvalidate()
        }

        override fun setLayerType(layerType: Int, paint: Paint?) {}

        override fun preDispatchDraw(canvas: Canvas) {}

        override fun onStartTemporaryDetach() {}

        override fun onFinishTemporaryDetach() {}

        override fun onActivityResult(requestCode: Int, resultCode: Int, data: Intent?) {}

        override fun getHandler(originalHandler: Handler): Handler = originalHandler

        override fun findFocus(originalFocusedView: View): View = view

        override fun onApplyWindowInsets(insets: WindowInsets?): WindowInsets? = insets

        override fun onResolvePointerIcon(event: MotionEvent, pointerIndex: Int): PointerIcon? = null
    }

    private inner class KcefScrollDelegate : ScrollDelegate {
        override fun computeHorizontalScrollRange(): Int = renderHandler.contentWidth()

        override fun computeHorizontalScrollOffset(): Int = 0

        override fun computeVerticalScrollRange(): Int = renderHandler.contentHeight()

        override fun computeVerticalScrollOffset(): Int = 0

        override fun computeVerticalScrollExtent(): Int = currentViewHeight()

        override fun computeScroll() {}
    }

    override fun init(
        javaScriptInterfaces: Map<String, Any>?,
        privateBrowsing: Boolean,
    ) {
        Log.v(TAG, "KcefWebViewProvider: initialize")
        destroy()
        val cefApp = ensureCefApp()

        val client = cefApp.createClient()
        client.addDisplayHandler(DisplayHandler())
        client.addLoadHandler(LoadHandler())
        client.addRequestHandler(RequestHandler())
        client.addFocusHandler(FocusHandler())

        val routerConfig = CefMessageRouter.CefMessageRouterConfig().apply {
            jsQueryFunction = QUERY_FN
            jsCancelFunction = QUERY_CANCEL_FN
        }
        val router = CefMessageRouter.create(routerConfig, MessageRouterHandler())
        client.addMessageRouter(router)

        cefClient = client
        messageRouter = router
        initHandler.init(this)
    }

    override fun setHorizontalScrollbarOverlay(overlay: Boolean): Unit = throw RuntimeException("Stub!")

    override fun setVerticalScrollbarOverlay(overlay: Boolean): Unit = throw RuntimeException("Stub!")

    override fun overlayHorizontalScrollbar(): Boolean = throw RuntimeException("Stub!")

    override fun overlayVerticalScrollbar(): Boolean = throw RuntimeException("Stub!")

    override fun getVisibleTitleHeight(): Int = throw RuntimeException("Stub!")

    override fun getCertificate(): SslCertificate = throw RuntimeException("Stub!")

    override fun setCertificate(certificate: SslCertificate): Unit = throw RuntimeException("Stub!")

    override fun savePassword(
        host: String,
        username: String,
        password: String,
    ): Unit = throw RuntimeException("Stub!")

    override fun setHttpAuthUsernamePassword(
        host: String,
        realm: String,
        username: String,
        password: String,
    ): Unit = throw RuntimeException("Stub!")

    override fun getHttpAuthUsernamePassword(
        host: String,
        realm: String,
    ): Array<String> = throw RuntimeException("Stub!")

    override fun destroy() {
        shutdownBrowser()
        messageRouter?.let { router ->
            cefClient?.removeMessageRouter(router)
            router.dispose()
        }
        messageRouter = null
        cefClient?.dispose()
        cefClient = null
        evalCallbacks.clear()
        renderHandler.clear()
    }

    override fun setNetworkAvailable(networkUp: Boolean): Unit = throw RuntimeException("Stub!")

    override fun saveState(outState: Bundle): WebBackForwardList = throw RuntimeException("Stub!")

    override fun savePicture(
        b: Bundle,
        dest: File,
    ): Boolean = throw RuntimeException("Stub!")

    override fun restorePicture(
        b: Bundle,
        src: File,
    ): Boolean = throw RuntimeException("Stub!")

    override fun restoreState(inState: Bundle): WebBackForwardList = throw RuntimeException("Stub!")

    override fun loadUrl(
        loadUrl: String,
        additionalHttpHeaders: Map<String, String>,
    ) {
        shutdownBrowser()
        chromeClient.onProgressChanged(view, 0)
        initialRequestData = InitialRequestData(additionalHttpHeaders = additionalHttpHeaders)
        browser = createBrowserForUrl(loadUrl)
        Log.d(TAG, "Page loaded at URL $loadUrl")
    }

    override fun loadUrl(url: String) {
        loadUrl(url, emptyMap())
    }

    override fun postUrl(
        url: String,
        postData: ByteArray,
    ) {
        shutdownBrowser()
        chromeClient.onProgressChanged(view, 0)
        initialRequestData = InitialRequestData(myPostData = postData)
        browser = createBrowserForUrl(url)
        Log.d(TAG, "Page posted at URL $url")
    }

    override fun loadData(
        data: String,
        mimeType: String,
        encoding: String,
    ) {
        loadDataWithBaseURL(null, data, mimeType, encoding, null)
    }

    override fun loadDataWithBaseURL(
        baseUrl: String?,
        data: String,
        mimeType: String,
        encoding: String,
        historyUrl: String?,
    ) {
        shutdownBrowser()
        chromeClient.onProgressChanged(view, 0)

        browser =
            (
                baseUrl?.let { url ->
                    urlHttpMapping[url.trimEnd('/')] = data
                    createBrowserForUrl(url)
                }
                    ?: createBrowserWithHtml(data)
            )
        Log.d(TAG, "Page loaded from data at base URL $baseUrl")
    }

    override fun evaluateJavaScript(
        script: String,
        resultCallback: ValueCallback<String>,
    ) {
        val activeBrowser = browser
        if (activeBrowser == null) {
            handler.post { resultCallback.onReceiveValue(null) }
            return
        }

        val token = UUID.randomUUID().toString()
        evalCallbacks[token] = resultCallback

        val js = buildEvalScript(token, script.removePrefix("javascript:"))
        activeBrowser.mainFrame?.executeJavaScript(js, "suwayomi://eval", 0)
    }

    private fun buildEvalScript(token: String, body: String): String {
        val tokenLiteral = Json.encodeToString(token)
        val interfaceLiteral = Json.encodeToString(EVAL_INTERFACE)
        val bodyLiteral = Json.encodeToString(body)

        return """
            (function() {
                const token = $tokenLiteral;
                const source = $bodyLiteral;
                const notify = (payload, error) => window.$QUERY_FN({
                    request: JSON.stringify({
                        interfaceName: $interfaceLiteral,
                        functionName: token,
                        args: [payload, error ?? ""]
                    }),
                    persistent: false,
                    onSuccess: () => {},
                    onFailure: () => {}
                });

                let fn;
                try {
                    fn = new Function(source);
                } catch (err) {
                    notify("null", err && err.toString ? err.toString() : String(err));
                    return;
                }

                const execute = () => {
                    try {
                        return fn.call(window);
                    } catch (err) {
                        throw err;
                    }
                };

                Promise.resolve()
                    .then(execute)
                    .then(result => {
                        let serialized;
                        try {
                            serialized = JSON.stringify(result ?? null);
                        } catch (err) {
                            notify("null", err && err.toString ? err.toString() : String(err));
                            return;
                        }
                        if (serialized === undefined) {
                            serialized = "null";
                        }
                        notify(serialized, "");
                    })
                    .catch(err => {
                        notify("null", err && err.toString ? err.toString() : String(err));
                    });
            })();
        """.trimIndent()
    }

    private fun handleEvalResult(token: String, resultJson: String?, error: String?) {
        val callback = evalCallbacks.remove(token) ?: return
        handler.post {
            if (!error.isNullOrEmpty()) {
                callback.onReceiveValue(null)
                return@post
            }
            val normalized = when (resultJson) {
                null -> null
                "null" -> null
                else -> resultJson
            }
            callback.onReceiveValue(normalized)
        }
    }

    override fun saveWebArchive(filename: String): Unit = throw RuntimeException("Stub!")

    override fun saveWebArchive(
        basename: String,
        autoname: Boolean,
        callback: ValueCallback<String>,
    ): Unit = throw RuntimeException("Stub!")

    override fun stopLoading() {
        browser!!.stopLoad()
    }

    override fun reload() {
        browser!!.reload()
    }

    override fun canGoBack(): Boolean = browser!!.canGoBack()

    override fun goBack() {
        browser!!.goBack()
    }

    override fun canGoForward(): Boolean = browser!!.canGoForward()

    override fun goForward() {
        browser!!.goForward()
    }

    override fun canGoBackOrForward(steps: Int): Boolean = throw RuntimeException("Stub!")

    override fun goBackOrForward(steps: Int): Unit = throw RuntimeException("Stub!")

    override fun isPrivateBrowsingEnabled(): Boolean = throw RuntimeException("Stub!")

    override fun pageUp(top: Boolean): Boolean = throw RuntimeException("Stub!")

    override fun pageDown(bottom: Boolean): Boolean = throw RuntimeException("Stub!")

    override fun insertVisualStateCallback(
        requestId: Long,
        callback: VisualStateCallback,
    ): Unit = throw RuntimeException("Stub!")

    override fun clearView(): Unit = throw RuntimeException("Stub!")

    override fun capturePicture(): Picture = throw RuntimeException("Stub!")

    override fun createPrintDocumentAdapter(documentName: String): PrintDocumentAdapter = throw RuntimeException("Stub!")

    override fun getScale(): Float = throw RuntimeException("Stub!")

    override fun setInitialScale(scaleInPercent: Int): Unit = throw RuntimeException("Stub!")

    override fun invokeZoomPicker(): Unit = throw RuntimeException("Stub!")

    override fun getHitTestResult(): HitTestResult = throw RuntimeException("Stub!")

    override fun requestFocusNodeHref(hrefMsg: Message): Unit = throw RuntimeException("Stub!")

    override fun requestImageRef(msg: Message): Unit = throw RuntimeException("Stub!")

    override fun getUrl(): String = browser!!.url

    override fun getOriginalUrl(): String = browser!!.url

    override fun getTitle(): String = throw RuntimeException("Stub!")

    override fun getFavicon(): Bitmap = throw RuntimeException("Stub!")

    override fun getTouchIconUrl(): String = throw RuntimeException("Stub!")

    override fun getProgress(): Int = throw RuntimeException("Stub!")

    override fun getContentHeight(): Int = renderHandler.contentHeight()

    override fun getContentWidth(): Int = renderHandler.contentWidth()

    override fun pauseTimers(): Unit = throw RuntimeException("Stub!")

    override fun resumeTimers(): Unit = throw RuntimeException("Stub!")

    override fun onPause(): Unit = throw RuntimeException("Stub!")

    override fun onResume(): Unit = throw RuntimeException("Stub!")

    override fun isPaused(): Boolean = throw RuntimeException("Stub!")

    override fun freeMemory(): Unit = throw RuntimeException("Stub!")

    override fun clearCache(includeDiskFiles: Boolean): Unit = throw RuntimeException("Stub!")

    override fun clearFormData(): Unit = throw RuntimeException("Stub!")

    override fun clearHistory(): Unit = throw RuntimeException("Stub!")

    override fun clearSslPreferences(): Unit = throw RuntimeException("Stub!")

    override fun copyBackForwardList(): WebBackForwardList = throw RuntimeException("Stub!")

    override fun setFindListener(listener: WebView.FindListener): Unit = throw RuntimeException("Stub!")

    override fun findNext(forward: Boolean): Unit = throw RuntimeException("Stub!")

    override fun findAll(find: String): Int = throw RuntimeException("Stub!")

    override fun findAllAsync(find: String): Unit = throw RuntimeException("Stub!")

    override fun showFindDialog(
        text: String,
        showIme: Boolean,
    ): Boolean = throw RuntimeException("Stub!")

    override fun clearMatches(): Unit = throw RuntimeException("Stub!")

    override fun documentHasImages(response: Message): Unit = throw RuntimeException("Stub!")

    override fun setWebViewClient(client: WebViewClient) {
        viewClient = client
    }

    override fun getWebViewClient(): WebViewClient = viewClient

    override fun getWebViewRenderProcess(): WebViewRenderProcess? = throw RuntimeException("Stub!")

    override fun setWebViewRenderProcessClient(
        executor: Executor?,
        client: WebViewRenderProcessClient?,
    ): Unit = throw RuntimeException("Stub!")

    override fun getWebViewRenderProcessClient(): WebViewRenderProcessClient? = throw RuntimeException("Stub!")

    override fun setDownloadListener(listener: DownloadListener): Unit = throw RuntimeException("Stub!")

    override fun setWebChromeClient(client: WebChromeClient) {
        chromeClient = client
    }

    override fun getWebChromeClient(): WebChromeClient = chromeClient

    @Suppress("DEPRECATION")
    override fun setPictureListener(listener: PictureListener): Unit = throw RuntimeException("Stub!")

    @Serializable
    private data class FunctionCall(
        val interfaceName: String,
        val functionName: String,
        val args: Array<String>,
    )

    private data class FunctionMapping(
        val interfaceName: String,
        val functionName: String,
        val obj: Any,
        val fn: KFunction<*>,
    ) {
        fun toNice(): String = "$interfaceName.$functionName"
    }

    private fun mappingKey(interfaceName: String, functionName: String): String = "$interfaceName#$functionName"

    override fun addJavascriptInterface(
        obj: Any,
        interfaceName: String,
    ) {
        val cls = obj::class
        mappings.addAll(
            cls.declaredMemberFunctions.map {
                it.javaMethod?.isAccessible = true
                val map = FunctionMapping(interfaceName, it.name, obj, it)
                Log.v(TAG, "Exposing: ${map.toNice()}")
                mappingIndex[mappingKey(interfaceName, it.name)] = map
                map
            },
        )
    }

    override fun removeJavascriptInterface(interfaceName: String) {
        val iterator = mappings.iterator()
        while (iterator.hasNext()) {
            val mapping = iterator.next()
            if (mapping.interfaceName == interfaceName) {
                iterator.remove()
                mappingIndex.remove(mappingKey(mapping.interfaceName, mapping.functionName))
            }
        }
    }

    override fun createWebMessageChannel(): Array<WebMessagePort> = throw RuntimeException("Stub!")

    override fun postMessageToMainFrame(
        message: WebMessage,
        targetOrigin: Uri,
    ): Unit = throw RuntimeException("Stub!")

    override fun getSettings(): WebSettings = settings

    override fun setMapTrackballToArrowKeys(setMap: Boolean): Unit = throw RuntimeException("Stub!")

    override fun flingScroll(
        vx: Int,
        vy: Int,
    ): Unit = throw RuntimeException("Stub!")

    override fun getZoomControls(): View = throw RuntimeException("Stub!")

    override fun canZoomIn(): Boolean = throw RuntimeException("Stub!")

    override fun canZoomOut(): Boolean = throw RuntimeException("Stub!")

    override fun zoomBy(zoomFactor: Float): Boolean = throw RuntimeException("Stub!")

    override fun zoomIn(): Boolean = throw RuntimeException("Stub!")

    override fun zoomOut(): Boolean = throw RuntimeException("Stub!")

    override fun dumpViewHierarchyWithProperties(
        out: BufferedWriter,
        level: Int,
    ): Unit = throw RuntimeException("Stub!")

    override fun findHierarchyView(
        className: String,
        hashCode: Int,
    ): View = throw RuntimeException("Stub!")

    override fun setRendererPriorityPolicy(
        rendererRequestedPriority: Int,
        waivedWhenNotVisible: Boolean,
    ): Unit = throw RuntimeException("Stub!")

    override fun getRendererRequestedPriority(): Int = throw RuntimeException("Stub!")

    override fun getRendererPriorityWaivedWhenNotVisible(): Boolean = throw RuntimeException("Stub!")

    @Suppress("unused")
    override fun setTextClassifier(textClassifier: TextClassifier?) {}

    override fun getTextClassifier(): TextClassifier = TextClassifier.NO_OP

    override fun getViewDelegate(): ViewDelegate = viewDelegate

    override fun getScrollDelegate(): ScrollDelegate = scrollDelegate

    override fun notifyFindDialogDismissed(): Unit = throw RuntimeException("Stub!")
}

private fun InputStream.readAllBytesCompat(): ByteArray {
    val buffer = ByteArrayOutputStream()
    val chunk = ByteArray(DEFAULT_BUFFER_SIZE)
    while (true) {
        val read = this.read(chunk)
        if (read == -1) {
            break
        }
        buffer.write(chunk, 0, read)
    }
    return buffer.toByteArray()
}
