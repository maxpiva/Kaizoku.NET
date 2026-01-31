package android.support.multidex;

import android.content.Context;
import extension.bridge.logging.AndroidCompatLogger;

/**
 * MultiDex that does nothing.
 */
public class MultiDex {
    private static final AndroidCompatLogger logger = AndroidCompatLogger.forClass(MultiDex.class);

    public static void install(Context context) {
        logger.debug(() -> "Ignoring MultiDex installation attempt for app: " + context.getPackageName());
    }
}
