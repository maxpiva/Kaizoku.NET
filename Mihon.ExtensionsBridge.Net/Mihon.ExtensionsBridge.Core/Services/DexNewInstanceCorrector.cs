using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using org.objectweb.asm;
using org.objectweb.asm.tree;
using org.objectweb.asm.tree.analysis;

namespace Mihon.ExtensionsBridge.Core.Services
{
    /// <summary>
    /// Authoritative oracle of the <c>new-instance</c> type operands present in an APK's DEX, keyed by
    /// <c>(className, methodName, methodDescriptor)</c> and ordered by their appearance in method code.
    /// </summary>
    /// <remarks>
    /// dex2jar mistranslates R8's stripped-constructor pattern
    /// (<c>new-instance vX, LT;</c> followed by <c>invoke-direct {vX}, Lsuper;-&gt;&lt;init&gt;()V</c> when
    /// <c>T</c>'s trivial <c>&lt;init&gt;</c> was removed) into <c>new java/lang/Object</c> +
    /// <c>invokespecial Object.&lt;init&gt;</c>. The resulting <c>Object</c> instance stored into a typed field
    /// is rejected by the verifier or fails at runtime. Recovering the real type <c>T</c> requires reading the
    /// DEX directly; this oracle does so via dex2jar's own reader/visitor API (no external tooling).
    /// </remarks>
    internal sealed class DexNewInstanceOracle
    {
        // key = className '\0' methodName '\0' methodDescriptor  ->  ordered internal type names.
        private readonly Dictionary<string, List<string>> _map = new(System.StringComparer.Ordinal);

        /// <summary>
        /// Builds the oracle directly from the APK bytes using dex2jar's <see cref="com.googlecode.d2j.reader.MultiDexFileReader"/>
        /// and a <see cref="com.googlecode.d2j.visitors.DexFileVisitor"/> chain that records <c>NEW_INSTANCE</c> operands.
        /// </summary>
        /// <param name="apkBytes">The raw APK bytes (the same buffer already read for conversion).</param>
        /// <returns>A populated oracle.</returns>
        public static DexNewInstanceOracle Build(byte[] apkBytes)
        {
            var oracle = new DexNewInstanceOracle();
            var reader = com.googlecode.d2j.reader.MultiDexFileReader.open(apkBytes);
            reader.accept(new FileVisitor(oracle));
            return oracle;
        }

        /// <summary>
        /// Returns the ordered list of <c>new-instance</c> internal type names for the given method, or <c>null</c>.
        /// </summary>
        public List<string>? Get(string className, string methodName, string methodDesc)
            => _map.TryGetValue(Key(className, methodName, methodDesc), out var list) ? list : null;

        private void Record(string className, string methodName, string methodDesc, List<string> types)
        {
            var key = Key(className, methodName, methodDesc);
            // First occurrence wins; a well-formed extension APK defines each method once.
            if (!_map.ContainsKey(key))
                _map[key] = types;
        }

        private static string Key(string className, string methodName, string methodDesc)
            => className + "\0" + methodName + "\0" + methodDesc;

        /// <summary>Strips a <c>L...;</c> object descriptor to its internal name; leaves anything else as-is.</summary>
        private static string InternalName(string desc)
            => (desc != null && desc.Length > 2 && desc[0] == 'L' && desc[desc.Length - 1] == ';')
                ? desc.Substring(1, desc.Length - 2)
                : desc;

        private static string DescriptorOf(com.googlecode.d2j.Method method)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append('(');
            var parameters = method.getParameterTypes();
            if (parameters != null)
                foreach (var p in parameters)
                    sb.Append(p);
            sb.Append(')');
            sb.Append(method.getReturnType());
            return sb.ToString();
        }

        private sealed class FileVisitor : com.googlecode.d2j.visitors.DexFileVisitor
        {
            private readonly DexNewInstanceOracle _oracle;
            public FileVisitor(DexNewInstanceOracle oracle) => _oracle = oracle;

            public override com.googlecode.d2j.visitors.DexClassVisitor visit(
                int access, string className, string superClass, string[] interfaces)
                => new ClassVisitor(_oracle, InternalName(className));
        }

        private sealed class ClassVisitor : com.googlecode.d2j.visitors.DexClassVisitor
        {
            private readonly DexNewInstanceOracle _oracle;
            private readonly string _className;
            public ClassVisitor(DexNewInstanceOracle oracle, string className)
            {
                _oracle = oracle;
                _className = className;
            }

            public override com.googlecode.d2j.visitors.DexMethodVisitor visitMethod(
                int access, com.googlecode.d2j.Method method)
                => new MethodVisitor(_oracle, _className, method.getName(), DescriptorOf(method));
        }

        private sealed class MethodVisitor : com.googlecode.d2j.visitors.DexMethodVisitor
        {
            private readonly DexNewInstanceOracle _oracle;
            private readonly string _className;
            private readonly string _methodName;
            private readonly string _methodDesc;
            public MethodVisitor(DexNewInstanceOracle oracle, string className, string methodName, string methodDesc)
            {
                _oracle = oracle;
                _className = className;
                _methodName = methodName;
                _methodDesc = methodDesc;
            }

            public override com.googlecode.d2j.visitors.DexCodeVisitor visitCode()
                => new CodeVisitor(_oracle, _className, _methodName, _methodDesc);
        }

        private sealed class CodeVisitor : com.googlecode.d2j.visitors.DexCodeVisitor
        {
            private readonly DexNewInstanceOracle _oracle;
            private readonly string _className;
            private readonly string _methodName;
            private readonly string _methodDesc;
            private readonly List<string> _types = new();
            public CodeVisitor(DexNewInstanceOracle oracle, string className, string methodName, string methodDesc)
            {
                _oracle = oracle;
                _className = className;
                _methodName = methodName;
                _methodDesc = methodDesc;
            }

            public override void visitTypeStmt(com.googlecode.d2j.reader.Op op, int a, int b, string type)
            {
                // visitTypeStmt is also used for CHECK_CAST / INSTANCE_OF / NEW_ARRAY; only NEW_INSTANCE allocates.
                if (ReferenceEquals(op, com.googlecode.d2j.reader.Op.NEW_INSTANCE))
                    _types.Add(InternalName(type));
            }

            public override void visitEnd()
                => _oracle.Record(_className, _methodName, _methodDesc, _types);
        }
    }

    /// <summary>
    /// Applies the oracle-driven correction of dex2jar's <c>NEW java/lang/Object</c> mistranslation across a set of
    /// converted classes, synthesises missing default constructors on the recovered target types, and lowers the
    /// class-file version while stripping <c>StackMapTable</c> frames so downstream inference verifiers accept the
    /// now type-correct code. Ported from the validated <c>FixInit2</c> reference.
    /// </summary>
    internal sealed class DexNewInstanceCorrector
    {
        private readonly DexNewInstanceOracle _oracle;
        private readonly ILogger _logger;

        /// <summary>Java class-file major version 49 (Java 5) — forces HotSpot's type-inference verifier.</summary>
        private const int TargetMajorVersion = 49;

        public DexNewInstanceCorrector(DexNewInstanceOracle oracle, ILogger logger)
        {
            _oracle = oracle;
            _logger = logger;
        }

        /// <summary>
        /// Retypes mistranslated <c>NEW</c> instructions across all supplied classes and synthesises any missing
        /// default constructors on the recovered target types.
        /// </summary>
        /// <param name="nodes">All converted classes in the JAR, as ASM tree nodes.</param>
        public void CorrectAll(IReadOnlyList<ClassNode> nodes)
        {
            var byName = new Dictionary<string, ClassNode>(System.StringComparer.Ordinal);
            foreach (var cn in nodes)
                byName[cn.name] = cn;

            var ctorNeeded = new HashSet<string>(System.StringComparer.Ordinal);
            int retyped = 0, skippedMethods = 0;

            foreach (var cn in nodes)
            {
                for (int i = 0; i < cn.methods.size(); i++)
                {
                    var mn = (MethodNode)cn.methods.get(i);
                    if (mn.instructions.size() == 0)
                        continue;
                    try
                    {
                        var result = FixMethod(cn, mn, ctorNeeded);
                        retyped += result.retyped;
                        skippedMethods += result.skipped;
                    }
                    catch (System.Exception ex)
                    {
                        // Safe fallback: never let one method abort the conversion.
                        skippedMethods++;
                        _logger.LogTrace(ex, "DexNewInstanceCorrector: skipped method {Class}.{Method}{Desc}", cn.name, mn.name, mn.desc);
                    }
                }
            }

            int synth = SynthesizeConstructors(byName, ctorNeeded);

            if (retyped > 0 || synth > 0)
                _logger.LogDebug(
                    "DexNewInstanceCorrector: retypedNew={Retyped} synthCtors={Synth} skippedMethods={Skipped}",
                    retyped, synth, skippedMethods);
        }

        /// <summary>
        /// Serialises a corrected class: for non-<c>invokedynamic</c> classes it strips <c>StackMapTable</c> frames and
        /// lowers the version to 49; classes containing <c>invokedynamic</c> keep their version and frames untouched.
        /// </summary>
        public byte[] WriteClass(ClassNode cn)
        {
            if (!HasInvokeDynamic(cn))
            {
                StripFrames(cn);
                cn.version = TargetMajorVersion;
            }

            // COMPUTE_MAXS: dex2jar's declared max_stack is frequently too small for HotSpot's v49
            // type-inference verifier ("Stack size too large"), so recompute it. COMPUTE_MAXS is a purely
            // local pass (no classpath needed) and never rewrites StackMapTable frames, so the invokedynamic
            // classes that retain their frames above are unaffected. Matches the validated FixInit2 reference.
            var cw = new ClassWriter(ClassWriter.COMPUTE_MAXS);
            cn.accept(cw);
            return cw.toByteArray();
        }

        private (int retyped, int skipped) FixMethod(ClassNode cn, MethodNode mn, HashSet<string> ctorNeeded)
        {
            // Ordered JVM NEW instructions.
            var jvmNews = new List<TypeInsnNode>();
            for (AbstractInsnNode p = mn.instructions.getFirst(); p != null; p = p.getNext())
                if (p.getOpcode() == Opcodes.NEW)
                    jvmNews.Add((TypeInsnNode)p);
            if (jvmNews.Count == 0)
                return (0, 0);

            if (!jvmNews.Any(t => t.desc == "java/lang/Object"))
                return (0, 0);

            var dex = _oracle.Get(cn.name, mn.name, mn.desc);
            if (dex == null || dex.Count != jvmNews.Count)
                return (0, 1); // guard: counts disagree -> leave method untouched

            // Residual matching: remove concrete (non-Object) JVM types from a copy of the DEX list (order-preserving
            // multiset), then assign the leftover DEX types, in order, to the Object-typed NEWs, in order.
            var residual = new List<string>(dex);
            foreach (var t in jvmNews)
                if (t.desc != "java/lang/Object")
                    residual.Remove(t.desc);

            var newToType = new Dictionary<TypeInsnNode, string>(ReferenceEqualityComparer.Instance);
            int ri = 0;
            foreach (var t in jvmNews)
            {
                if (t.desc != "java/lang/Object")
                    continue;
                if (ri >= residual.Count)
                    break;
                string target = residual[ri++];
                if (target == "java/lang/Object")
                    continue; // genuine new Object
                newToType[t] = target;
            }
            if (newToType.Count == 0)
                return (0, 0);

            // Pair each retyped NEW with its <init> invokespecial through origin tracking.
            Frame[] frames;
            try
            {
                var analyzer = new Analyzer(new OriginInterpreter());
                frames = analyzer.analyze(cn.name, mn);
            }
            catch (AnalyzerException)
            {
                return (0, 1);
            }

            var insns = mn.instructions.toArray();
            for (int i = 0; i < insns.Length; i++)
            {
                if (insns[i].getOpcode() != Opcodes.INVOKESPECIAL)
                    continue;
                var min = (MethodInsnNode)insns[i];
                if (min.name != "<init>")
                    continue;
                var fr = frames[i];
                if (fr == null)
                    continue;

                int argSize = org.objectweb.asm.Type.getArgumentTypes(min.desc).Length;
                int recvIndex = fr.getStackSize() - 1 - argSize;
                if (recvIndex < 0)
                    continue;
                var recv = (SourceValue)fr.getStack(recvIndex);
                foreach (var src in IterateInsns(recv.insns))
                {
                    if (src is TypeInsnNode tin && newToType.TryGetValue(tin, out var target))
                    {
                        // Only the mistranslated ()V pattern is safe to rewrite; leave any non-()V ctor alone.
                        if (min.desc == "()V")
                        {
                            min.owner = target;
                            ctorNeeded.Add(target);
                        }
                    }
                }
            }

            int n = 0;
            foreach (var kv in newToType)
            {
                kv.Key.desc = kv.Value;
                n++;
            }
            return (n, 0);
        }

        private int SynthesizeConstructors(Dictionary<string, ClassNode> byName, HashSet<string> ctorNeeded)
        {
            int synth = 0;
            foreach (var target in ctorNeeded)
            {
                if (!byName.TryGetValue(target, out var cn))
                    continue; // target lives in a library, not this JAR

                bool has = false;
                for (int i = 0; i < cn.methods.size(); i++)
                {
                    var m = (MethodNode)cn.methods.get(i);
                    if (m.name == "<init>" && m.desc == "()V")
                    {
                        has = true;
                        break;
                    }
                }
                if (has)
                    continue;

                string sup = cn.superName ?? "java/lang/Object";
                var ctor = new MethodNode(Opcodes.ACC_PUBLIC | Opcodes.ACC_SYNTHETIC, "<init>", "()V", (string?)null, (string[]?)null);
                var il = ctor.instructions;
                il.add(new VarInsnNode(Opcodes.ALOAD, 0));
                il.add(new MethodInsnNode(Opcodes.INVOKESPECIAL, sup, "<init>", "()V", false));
                il.add(new InsnNode(Opcodes.RETURN));
                ctor.maxStack = 1;
                ctor.maxLocals = 1;
                cn.methods.add(ctor);
                synth++;
            }
            return synth;
        }

        private static void StripFrames(ClassNode cn)
        {
            for (int i = 0; i < cn.methods.size(); i++)
            {
                var mn = (MethodNode)cn.methods.get(i);
                for (AbstractInsnNode p = mn.instructions.getFirst(); p != null;)
                {
                    AbstractInsnNode next = p.getNext();
                    if (p is FrameNode)
                        mn.instructions.remove(p);
                    p = next;
                }
            }
        }

        private static bool HasInvokeDynamic(ClassNode cn)
        {
            for (int i = 0; i < cn.methods.size(); i++)
            {
                var mn = (MethodNode)cn.methods.get(i);
                for (AbstractInsnNode p = mn.instructions.getFirst(); p != null; p = p.getNext())
                    if (p.getOpcode() == Opcodes.INVOKEDYNAMIC)
                        return true;
            }
            return false;
        }

        private static IEnumerable<AbstractInsnNode> IterateInsns(java.util.Set set)
        {
            if (set == null)
                yield break;
            var it = set.iterator();
            while (it.hasNext())
                yield return (AbstractInsnNode)it.next();
        }

        /// <summary>
        /// <see cref="SourceInterpreter"/> that preserves value origin through copy operations (dup/load/store),
        /// so a stack value can be traced back to the <c>NEW</c> that produced it even across local slots.
        /// </summary>
        private sealed class OriginInterpreter : SourceInterpreter
        {
            public OriginInterpreter() : base(Opcodes.ASM9) { }

            public override SourceValue copyOperation(AbstractInsnNode insn, SourceValue value) => value;
        }
    }
}
