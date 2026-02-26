package app.cash.quickjs;

import org.mozilla.javascript.*;

import java.io.Closeable;

public final class QuickJs implements Closeable {
    private Context context;
    private Scriptable scope;

    public static QuickJs create() {
        return new QuickJs();
    }

    public QuickJs() {
        this.context = Context.enter();
        this.context.setOptimizationLevel(-1); // Interpreted mode for better compatibility
        this.context.setLanguageVersion(Context.VERSION_ES6);
        // Enable more permissive error handling
        this.context.getWrapFactory().setJavaPrimitiveWrap(false);
        this.scope = context.initStandardObjects();
        
        // Add polyfills for modern JavaScript features
        try {
            // Add console object if needed
            context.evaluateString(scope, 
                "if (typeof console === 'undefined') { var console = { log: function() {} }; }", 
                "<init>", 1, null);
            
            // Polyfill for String.prototype.matchAll (ES2020)
            context.evaluateString(scope,
                "if (!String.prototype.matchAll) {" +
                "  String.prototype.matchAll = function(regex) {" +
                "    if (!regex.global) {" +
                "      throw new TypeError('matchAll requires global flag');" +
                "    }" +
                "    var matches = [];" +
                "    var str = this;" +
                "    var match;" +
                "    while ((match = regex.exec(str)) !== null) {" +
                "      matches.push(match);" +
                "    }" +
                "    return matches[Symbol && Symbol.iterator ? Symbol.iterator : '@@iterator']" +
                "      ? matches : { [Symbol.iterator]: function() {" +
                "          var i = 0;" +
                "          return {" +
                "            next: function() {" +
                "              return i < matches.length " +
                "                ? { value: matches[i++], done: false }" +
                "                : { done: true };" +
                "            }" +
                "          };" +
                "        }" +
                "      };" +
                "  };" +
                "}", 
                "<polyfill>", 1, null);
        } catch (Exception ignored) {}
    }

    public Object evaluate(String script, String ignoredFileName) {
        return this.evaluate(script);
    }

    public Object evaluate(String script) {
        try {
            // Wrap script evaluation with better error context
            Object result = context.evaluateString(scope, script, "<eval>", 1, null);
            return translateType(result);
        } catch (EvaluatorException e) {
            // Provide more context about syntax errors
            throw new QuickJsException("JavaScript syntax error: " + e.getMessage() + " (line " + e.lineNumber() + ")", e);
        } catch (Exception exception) {
            throw new QuickJsException(exception.getMessage(), exception);
        }
    }

    private Object translateType(Object obj) {
        if (obj == null || obj instanceof Undefined) {
            return null;
        }
        
        // Unwrap Rhino wrappers
        if (obj instanceof Wrapper) {
            obj = ((Wrapper) obj).unwrap();
        }
        
        // Handle native arrays
        if (obj instanceof NativeArray) {
            NativeArray array = (NativeArray) obj;
            int length = (int) array.getLength();
            
            if (length == 0) {
                return new int[0];
            }
            
            // Check first element to determine array type
            Object first = array.get(0, array);
            
            if (first instanceof Boolean) {
                boolean[] result = new boolean[length];
                for (int i = 0; i < length; i++) {
                    Object val = array.get(i, array);
                    result[i] = val instanceof Boolean ? (Boolean) val : false;
                }
                return result;
            } else if (first instanceof Number) {
                // Try int array first
                try {
                    int[] result = new int[length];
                    for (int i = 0; i < length; i++) {
                        Object val = array.get(i, array);
                        if (val instanceof Number) {
                            double d = ((Number) val).doubleValue();
                            if (d == Math.floor(d) && d >= Integer.MIN_VALUE && d <= Integer.MAX_VALUE) {
                                result[i] = ((Number) val).intValue();
                            } else {
                                throw new NumberFormatException();
                            }
                        }
                    }
                    return result;
                } catch (Exception e) {
                    // Fall back to double array
                    double[] result = new double[length];
                    for (int i = 0; i < length; i++) {
                        Object val = array.get(i, array);
                        result[i] = val instanceof Number ? ((Number) val).doubleValue() : 0.0;
                    }
                    return result;
                }
            } else if (first instanceof String) {
                String[] result = new String[length];
                for (int i = 0; i < length; i++) {
                    Object val = array.get(i, array);
                    result[i] = val != null ? val.toString() : null;
                }
                return result;
            } else {
                // Generic object array
                Object[] result = new Object[length];
                for (int i = 0; i < length; i++) {
                    result[i] = translateType(array.get(i, array));
                }
                return result;
            }
        }
        
        // Handle primitive types
        if (obj instanceof Boolean) {
            return obj;
        } else if (obj instanceof Number) {
            Number num = (Number) obj;
            double d = num.doubleValue();
            // Return int if it fits
            if (d == Math.floor(d) && d >= Integer.MIN_VALUE && d <= Integer.MAX_VALUE) {
                return num.intValue();
            }
            // Return long if it fits
            if (d == Math.floor(d) && d >= Long.MIN_VALUE && d <= Long.MAX_VALUE) {
                return num.longValue();
            }
            return d;
        } else if (obj instanceof String) {
            return obj;
        }
        
        return obj;
    }

    public byte[] compile(String sourceCode, String ignoredFileName) {
        return sourceCode.getBytes();
    }

    public Object execute(byte[] bytecode) {
        return this.evaluate(new String(bytecode));
    }

    public <T> void set(String name, Class<T> ignoredType, T object) {
        Object wrapped = Context.javaToJS(object, scope);
        scope.put(name, scope, wrapped);
    }

    @Override
    public void close() {
        if (this.context != null) {
            Context.exit();
            this.context = null;
            this.scope = null;
        }
    }
}