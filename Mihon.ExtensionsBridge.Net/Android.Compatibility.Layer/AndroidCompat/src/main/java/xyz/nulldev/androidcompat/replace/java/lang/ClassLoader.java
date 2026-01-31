package xyz.nulldev.androidcompat.replace.java.lang;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.IOException;
import java.io.InputStream;
import java.net.MalformedURLException;
import java.net.URL;
import java.security.CodeSource;
import java.util.ArrayList;
import java.util.Collections;
import java.util.Enumeration;
import java.util.List;
import java.util.Objects;
import java.util.jar.JarEntry;
import java.util.jar.JarFile;

import extension.bridge.logging.AndroidCompatLogger;

public class ClassLoader extends java.lang.ClassLoader {
    private static final AndroidCompatLogger logger = AndroidCompatLogger.forClass(ClassLoader.class);
    private volatile String jarLocation;

    public ClassLoader() {
        super();
    }

    public ClassLoader(java.lang.ClassLoader parent) {
        super(parent);
    }

    private ClassLoader(java.lang.ClassLoader parent, String jarLocation) {
        super(parent);
        configureJar(jarLocation);
    }

    public static java.lang.ClassLoader getSystemClassLoader() {
        return wrapStaticLoader("getSystemClassLoader", java.lang.ClassLoader.getSystemClassLoader());
    }

    public static java.lang.ClassLoader getPlatformClassLoader() {
        return wrapStaticLoader("getPlatformClassLoader", java.lang.ClassLoader.getPlatformClassLoader());
    }

    public static URL getSystemResource(String name) {
        logStaticLookup("getSystemResource", name);
        return java.lang.ClassLoader.getSystemResource(name);
    }

    public static InputStream getSystemResourceAsStream(String name) {
        logStaticLookup("getSystemResourceAsStream", name);
        return java.lang.ClassLoader.getSystemResourceAsStream(name);
    }

    public static Enumeration<URL> getSystemResources(String name) throws IOException {
        logStaticLookup("getSystemResources", name);
        return java.lang.ClassLoader.getSystemResources(name);
    }

    protected static boolean registerAsParallelCapable() {
        return java.lang.ClassLoader.registerAsParallelCapable();
    }

    @Override
    public URL getResource(String name) {
        logLookup("getResource", name);
        String normalized = normalizeResourceName(name);
        URL jarUrl = findJarResourceUrl(normalized);
        if (jarUrl != null) {
            return jarUrl;
        }
        return super.getResource(name);
    }

    @Override
    public Enumeration<URL> getResources(String name) throws IOException {
        logLookup("getResources", name);
        String normalized = normalizeResourceName(name);
        URL jarUrl = findJarResourceUrl(normalized);
        Enumeration<URL> parentResources = super.getResources(name);
        if (jarUrl == null) {
            return parentResources;
        }
        List<URL> combined = new ArrayList<>();
        combined.add(jarUrl);
        while (parentResources.hasMoreElements()) {
            combined.add(parentResources.nextElement());
        }
        return Collections.enumeration(combined);
    }

    @Override
    public InputStream getResourceAsStream(String name) {
        logLookup("getResourceAsStream", name);
        String normalized = normalizeResourceName(name);
        InputStream jarStream = openJarResourceStream(normalized);
        if (jarStream != null) {
            return jarStream;
        }
        return super.getResourceAsStream(name);
    }

    private void logLookup(String method, String name) {
        logger.info("ClassLoader " + method + " invoked for resource " + name + " from jar " + describeJarLocation());
    }

    private static void logStaticLookup(String method, String name) {
        logger.info("ClassLoader static " + method + " invoked for resource " + name + " from jar " + resolveJarLocation(ClassLoader.class));
    }

    static java.lang.ClassLoader wrap(java.lang.ClassLoader loader, String jarLocation) {
        if (loader == null) {
            return null;
        }
        if (loader instanceof ClassLoader) {
            ClassLoader compatLoader = (ClassLoader) loader;
            compatLoader.configureJar(jarLocation);
            return compatLoader;
        }
        return new ClassLoader(loader, jarLocation);
    }

    private static java.lang.ClassLoader wrapStaticLoader(String method, java.lang.ClassLoader loader) {
        logStaticLookup(method, null);
        return wrap(loader, null);
    }

    private String normalizeResourceName(String name) {
        if (name == null || name.isEmpty()) {
            return name;
        }
        return name.startsWith("/") ? name.substring(1) : name;
    }

    private URL findJarResourceUrl(String normalizedName) {
        if (!jarContainsEntry(normalizedName)) {
            return null;
        }
        return buildJarResourceUrl(normalizedName);
    }

    private InputStream openJarResourceStream(String normalizedName) {
        byte[] bytes = readJarEntryBytes(normalizedName);
        if (bytes == null) {
            return null;
        }
        return new ByteArrayInputStream(bytes);
    }

    private boolean jarContainsEntry(String normalizedName) {
        if (normalizedName == null) {
            return false;
        }
        JarFile jar = openJarFile();
        if (jar == null) {
            return false;
        }
        try {
            JarEntry entry = findEntry(jar, normalizedName);
            return entry != null;
        } finally {
            closeJarQuietly(jar);
        }
    }

    private byte[] readJarEntryBytes(String normalizedName) {
        if (normalizedName == null) {
            return null;
        }
        JarFile jar = openJarFile();
        if (jar == null) {
            return null;
        }
        try {
            JarEntry entry = findEntry(jar, normalizedName);
            if (entry == null) {
                return null;
            }
            InputStream inputStream = jar.getInputStream(entry);
            try {
                int initialSize = 1024;
                long entrySize = entry.getSize();
                if (entrySize > 0 && entrySize <= Integer.MAX_VALUE) {
                    initialSize = (int) entrySize;
                }
                ByteArrayOutputStream buffer = new ByteArrayOutputStream(initialSize);
                byte[] chunk = new byte[4096];
                int read;
                while ((read = inputStream.read(chunk)) != -1) {
                    buffer.write(chunk, 0, read);
                }
                return buffer.toByteArray();
            } finally {
                try {
                    inputStream.close();
                } catch (IOException e) {
                    logger.warn("Failed to close resource stream " + normalizedName + " from jar " + describeJarLocation(), e);
                }
            }
        } catch (IOException e) {
            logger.warn("Failed to read resource " + normalizedName + " from jar " + describeJarLocation(), e);
            return null;
        } finally {
            closeJarQuietly(jar);
        }
    }

    private JarEntry findEntry(JarFile jar, String normalizedName) {
        JarEntry entry = jar.getJarEntry(normalizedName);
        if (entry == null && normalizedName.startsWith("/")) {
            entry = jar.getJarEntry(normalizedName.substring(1));
        }
        return entry;
    }

    private URL buildJarResourceUrl(String entryName) {
        File file = deriveJarDiskFile(this.jarLocation);
        if (file == null || entryName == null) {
            return null;
        }
        try {
            URL fileUrl = file.toURI().toURL();
            return new URL("jar:" + fileUrl.toExternalForm() + "!/" + entryName);
        } catch (MalformedURLException e) {
            logger.warn("Failed to build jar URL for " + entryName + " from jar " + describeJarLocation(), e);
            return null;
        }
    }

    private synchronized void configureJar(String targetLocation) {
        String sanitized = sanitizeJarLocation(targetLocation);
        if (Objects.equals(this.jarLocation, sanitized)) {
            return;
        }
        this.jarLocation = sanitized;
    }

    private JarFile openJarFile() {
        File file = deriveJarDiskFile(this.jarLocation);
        if (file == null) {
            return null;
        }
        if (!file.exists()) {
            logger.warn("Jar file " + file + " does not exist for " + describeJarLocation());
            return null;
        }
        try {
            return new JarFile(file);
        } catch (IOException e) {
            logger.warn("Failed to open jar file " + file + " for " + describeJarLocation(), e);
            return null;
        }
    }

    private void closeJarQuietly(JarFile jar) {
        if (jar == null) {
            return;
        }
        try {
            jar.close();
        } catch (IOException e) {
            logger.warn("Failed to close jar " + describeJarLocation(), e);
        }
    }

    private File deriveJarDiskFile(String location) {
        if (location == null) {
            return null;
        }
        if (location.startsWith("file:")) {
            try {
                return new File(new URL(location).toURI());
            } catch (Exception e) {
                logger.warn("Failed to convert jar location " + location + " to file path", e);
                return null;
            }
        }
        return new File(location);
    }

    private String sanitizeJarLocation(String location) {
        if (location == null) {
            return null;
        }
        String trimmed = location.trim();
        if (trimmed.isEmpty() || "unknown".equalsIgnoreCase(trimmed)) {
            return null;
        }
        return trimmed;
    }

    private String describeJarLocation() {
        return jarLocation != null ? jarLocation : resolveJarLocation();
    }

    private String resolveJarLocation() {
        return resolveJarLocation(getClass());
    }

    private static String resolveJarLocation(Class<?> source) {
        CodeSource codeSource = source.getProtectionDomain().getCodeSource();
        if (codeSource != null && codeSource.getLocation() != null) {
            return codeSource.getLocation().toString();
        }
        return "unknown";
    }
}
