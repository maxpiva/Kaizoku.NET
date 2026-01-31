package app.cash.quickjs;

import com.oracle.truffle.js.scriptengine.GraalJSScriptEngine;
import org.graalvm.polyglot.Context;
import org.graalvm.polyglot.HostAccess;
import org.graalvm.polyglot.PolyglotAccess;
import org.graalvm.polyglot.Value;

import javax.script.ScriptException;
import java.io.Closeable;
import java.nio.charset.StandardCharsets;

public final class QuickJs implements Closeable {
    private GraalJSScriptEngine engine;

    public static QuickJs create() {
        return new QuickJs();
    }

    public QuickJs() {
        Context.Builder builder =
            Context
                .newBuilder("js")
                .allowHostAccess(HostAccess.ALL)
                .allowPolyglotAccess(PolyglotAccess.NONE)
                .allowHostClassLoading(false);
        this.engine = GraalJSScriptEngine.create(null, builder);
    }

    public Object evaluate(String script, String ignoredFileName) {
        return this.evaluate(script);
    }

    public Object evaluate(String script) {
        GraalJSScriptEngine activeEngine = ensureEngine();
        try {
            Object result = activeEngine.eval(script);
            return translateType(result);
        } catch (ScriptException exception) {
            throw new QuickJsException(exception.getMessage(), exception);
        }
    }

    private GraalJSScriptEngine ensureEngine() {
        GraalJSScriptEngine active = this.engine;
        if (active == null) {
            throw new QuickJsException("QuickJs engine is closed");
        }
        return active;
    }

    private Object translateType(Object value) {
        if (value == null) {
            return null;
        }
        if (value instanceof Value) {
            return translateValue((Value) value);
        }
        return value;
    }

    private Object translateValue(Value obj) {
        if (obj.isBoolean()) {
            return obj.asBoolean();
        } else if (obj.hasArrayElements()) {
            if (obj.getArraySize() == 0) {
                return new int[0];
            }

            Value element = obj.getArrayElement(0);
            if (element.isBoolean()) {
                boolean[] values = new boolean[(int) obj.getArraySize()];
                for (int index = 0; index < values.length; index++) {
                    values[index] = obj.getArrayElement(index).asBoolean();
                }
                return values;
            } else if (element.isNumber()) {
                if (element.fitsInInt()) {
                    int[] values = new int[(int) obj.getArraySize()];
                    for (int index = 0; index < values.length; index++) {
                        values[index] = obj.getArrayElement(index).asInt();
                    }
                    return values;
                } else if (element.fitsInLong()) {
                    long[] values = new long[(int) obj.getArraySize()];
                    for (int index = 0; index < values.length; index++) {
                        values[index] = obj.getArrayElement(index).asLong();
                    }
                    return values;
                } else {
                    double[] values = new double[(int) obj.getArraySize()];
                    for (int index = 0; index < values.length; index++) {
                        values[index] = obj.getArrayElement(index).asDouble();
                    }
                    return values;
                }
            } else if (element.isHostObject()) {
                Object[] values = new Object[(int) obj.getArraySize()];
                for (int index = 0; index < values.length; index++) {
                    values[index] = obj.getArrayElement(index).asHostObject();
                }
                return values;
            } else if (element.isString()) {
                String[] values = new String[(int) obj.getArraySize()];
                for (int index = 0; index < values.length; index++) {
                    values[index] = obj.getArrayElement(index).asString();
                }
                return values;
            }
        } else if (obj.isNumber()) {
            if (obj.fitsInInt()) {
                return obj.asInt();
            } else if (obj.fitsInLong()) {
                return obj.asLong();
            } else {
                return obj.asDouble();
            }
        } else if (obj.isHostObject()) {
            return obj.asHostObject();
        } else if (obj.isString()) {
            return obj.asString();
        }
        return obj;
    }

    public byte[] compile(String sourceCode, String ignoredFileName) {
        return sourceCode.getBytes(StandardCharsets.UTF_8);
    }

    public Object execute(byte[] bytecode) {
        return this.evaluate(new String(bytecode, StandardCharsets.UTF_8));
    }

    public <T> void set(String name, Class<T> ignoredType, T object) {
        ensureEngine().put(name, object);
    }

    @Override
    public void close() {
        if (this.engine != null) {
            try {
                this.engine.close();
            } catch (Exception ignored) {
                // ignore close exceptions to match previous behavior
            }
            this.engine = null;
        }
    }
}
