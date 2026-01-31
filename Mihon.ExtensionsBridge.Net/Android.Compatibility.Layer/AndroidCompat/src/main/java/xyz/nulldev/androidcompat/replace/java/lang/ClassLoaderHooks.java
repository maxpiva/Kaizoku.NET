package xyz.nulldev.androidcompat.replace.java.lang;

import extension.bridge.logging.AndroidCompatLogger;

public final class ClassLoaderHooks {
    private static final AndroidCompatLogger logger = AndroidCompatLogger.forClass(ClassLoaderHooks.class);

    private ClassLoaderHooks() {}

    public static ClassLoader getClassLoader(Class<?> clazz) {
        if (clazz == null) {
            logger.warn("ClassLoaderHooks.getClassLoader invoked with null class reference");
            return null;
        }
        String originalLocation = resolveJarLocation(clazz);
        String jarLocation = convertDllLocationToJar(originalLocation);
        String logLocation = jarLocation != null ? jarLocation : originalLocation;
        logger.info(
            "ClassLoaderHooks.getClassLoader invoked for class "
                + clazz.getName()
                + " from jar "
            + logLocation);
        java.lang.ClassLoader loader = clazz.getClassLoader();
        if (loader == null) {
            logger.info("ClassLoaderHooks.getClassLoader returning bootstrap loader for class " + clazz.getName());
            return null;
        }
        java.lang.ClassLoader wrapped = ClassLoader.wrap(loader, jarLocation);
        if (wrapped != loader) {
            logger.info("ClassLoaderHooks.getClassLoader wrapped loader " + describeLoader(loader) + " for class " + clazz.getName());
        }
        return (ClassLoader) wrapped;
    }

    private static String describeLoader(java.lang.ClassLoader loader) {
        return loader == null ? "null" : loader.getClass().getName();
    }

    private static String resolveJarLocation(Class<?> source) {
        java.security.ProtectionDomain domain = source.getProtectionDomain();
        if (domain != null && domain.getCodeSource() != null && domain.getCodeSource().getLocation() != null) {
            return domain.getCodeSource().getLocation().toString();
        }
        return "unknown";
    }

    private static String convertDllLocationToJar(String location) {
        if (location == null) {
            return null;
        }
        String trimmed = location.trim();
        if (trimmed.isEmpty() || "unknown".equalsIgnoreCase(trimmed)) {
            return null;
        }
        String lower = trimmed.toLowerCase();
        int idx = lower.lastIndexOf(".dll");
        if (idx >= 0) {
            return trimmed.substring(0, idx) + ".jar" + trimmed.substring(idx + 4);
        }
        return trimmed;
    }
}
