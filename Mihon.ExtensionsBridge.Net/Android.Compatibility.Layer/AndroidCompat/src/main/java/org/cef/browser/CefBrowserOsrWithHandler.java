// Port of upstream java-cef's CefBrowserOsrWithHandler, adapted to the
// me.friwi jcef-api fork used by jcefmaven (whose CefBrowser_N constructor
// takes CefBrowserSettings and which does not ship the CefRendering API).
//
// Lives in org.cef.browser because CefBrowser_N is package-private.
//
// Unlike CefBrowserOsr, this browser has no GLCanvas, no JOGL and no
// DropTarget: all rendering is delivered to the supplied CefRenderHandler,
// so it works in a headless AWT environment (java.awt.headless=true).
package org.cef.browser;

import org.cef.CefBrowserSettings;
import org.cef.CefClient;
import org.cef.callback.CefDragData;
import org.cef.handler.CefRenderHandler;
import org.cef.handler.CefScreenInfo;

import java.awt.Component;
import java.awt.Point;
import java.awt.Rectangle;
import java.awt.image.BufferedImage;
import java.nio.ByteBuffer;
import java.util.concurrent.CompletableFuture;
import java.util.function.Consumer;

// Implements CefRenderHandler directly (delegating to renderHandler_) instead
// of relying on getRenderHandler() alone: IKVM's JNI FindMethodID only
// resolves methods DECLARED on the target class, so native CEF callbacks like
// getScreenInfo throw NoSuchMethodError if the method is merely inherited.
// CefBrowserOsr declares the full surface for the same reason.
public class CefBrowserOsrWithHandler extends CefBrowser_N implements CefRenderHandler {
    private final CefRenderHandler renderHandler_;
    private final Component component_;

    /**
     * @param renderHandler receives onPaint buffers and geometry callbacks. Must not be null.
     * @param component optional component used only as an event-coordinate reference; may be a
     *                  non-displayable Canvas (never realized as a window).
     */
    public CefBrowserOsrWithHandler(
            CefClient client, String url, CefRequestContext context,
            CefRenderHandler renderHandler, Component component) {
        this(client, url, context, renderHandler, component, null, null, new CefBrowserSettings());
    }

    protected CefBrowserOsrWithHandler(
            CefClient client, String url, CefRequestContext context,
            CefRenderHandler renderHandler, Component component,
            CefBrowser_N parent, Point inspectAt, CefBrowserSettings settings) {
        super(client, url, context, parent, inspectAt, settings);
        assert renderHandler != null : "renderHandler must not be null";
        renderHandler_ = renderHandler;
        component_ = component;
    }

    @Override
    public CefRenderHandler getRenderHandler() {
        return this;
    }

    @Override
    public Rectangle getViewRect(CefBrowser browser) {
        return renderHandler_.getViewRect(browser);
    }

    @Override
    public boolean getScreenInfo(CefBrowser browser, CefScreenInfo screenInfo) {
        return renderHandler_.getScreenInfo(browser, screenInfo);
    }

    @Override
    public Point getScreenPoint(CefBrowser browser, Point viewPoint) {
        return renderHandler_.getScreenPoint(browser, viewPoint);
    }

    @Override
    public void onPopupShow(CefBrowser browser, boolean show) {
        renderHandler_.onPopupShow(browser, show);
    }

    @Override
    public void onPopupSize(CefBrowser browser, Rectangle size) {
        renderHandler_.onPopupSize(browser, size);
    }

    @Override
    public void onPaint(CefBrowser browser, boolean popup, Rectangle[] dirtyRects, ByteBuffer buffer,
            int width, int height) {
        renderHandler_.onPaint(browser, popup, dirtyRects, buffer, width, height);
    }

    @Override
    public void addOnPaintListener(Consumer<CefPaintEvent> listener) {
        renderHandler_.addOnPaintListener(listener);
    }

    @Override
    public void setOnPaintListener(Consumer<CefPaintEvent> listener) {
        renderHandler_.setOnPaintListener(listener);
    }

    @Override
    public void removeOnPaintListener(Consumer<CefPaintEvent> listener) {
        renderHandler_.removeOnPaintListener(listener);
    }

    @Override
    public boolean onCursorChange(CefBrowser browser, int cursorType) {
        return renderHandler_.onCursorChange(browser, cursorType);
    }

    @Override
    public boolean startDragging(CefBrowser browser, CefDragData dragData, int mask, int x, int y) {
        return renderHandler_.startDragging(browser, dragData, mask, x, y);
    }

    @Override
    public void updateDragCursor(CefBrowser browser, int operation) {
        renderHandler_.updateDragCursor(browser, operation);
    }

    @Override
    public void createImmediately() {
        createBrowserIfRequired();
    }

    @Override
    public Component getUIComponent() {
        return component_;
    }

    @Override
    public CompletableFuture<BufferedImage> createScreenshot(boolean nativeResolution) {
        CompletableFuture<BufferedImage> future = new CompletableFuture<>();
        future.completeExceptionally(
                new UnsupportedOperationException("Screenshots are not supported in handler-based OSR mode"));
        return future;
    }

    @Override
    protected CefBrowser_N createDevToolsBrowser(
            CefClient client, String url, CefRequestContext context,
            CefBrowser_N parent, Point inspectAt) {
        return new CefBrowserOsrWithHandler(
                client, url, context, renderHandler_, component_, parent, inspectAt, new CefBrowserSettings());
    }

    private void createBrowserIfRequired() {
        if (getNativeRef("CefBrowser") == 0) {
            if (getParentBrowser() != null) {
                createDevTools(getParentBrowser(), getClient(), 0, true, false, null, getInspectAt());
            } else {
                createBrowser(getClient(), 0, getUrl(), true, false, null, getRequestContext());
            }
        }
    }
}
