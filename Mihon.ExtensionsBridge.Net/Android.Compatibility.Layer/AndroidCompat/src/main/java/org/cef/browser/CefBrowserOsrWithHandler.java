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
import org.cef.handler.CefRenderHandler;

import java.awt.Component;
import java.awt.Point;
import java.awt.image.BufferedImage;
import java.util.concurrent.CompletableFuture;

public class CefBrowserOsrWithHandler extends CefBrowser_N {
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
        return renderHandler_;
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
