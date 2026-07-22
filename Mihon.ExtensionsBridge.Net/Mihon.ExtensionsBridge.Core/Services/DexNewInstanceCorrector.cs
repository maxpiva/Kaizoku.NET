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
            int retyped = 0, skippedMethods = 0, splitLocals = 0;

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

                    // Generic register-coalescing repair: split incorrectly merged local-variable webs
                    // back onto distinct slots. Isolated try so a split failure never aborts conversion
                    // of the method.
                    try
                    {
                        splitLocals += SplitLocalWebs(cn, mn);
                    }
                    catch (System.Exception ex)
                    {
                        _logger.LogTrace(ex, "DexNewInstanceCorrector: local-web split skipped {Class}.{Method}{Desc}", cn.name, mn.name, mn.desc);
                    }
                }
            }

            int synth = SynthesizeConstructors(byName, ctorNeeded);

            if (retyped > 0 || synth > 0 || splitLocals > 0)
                _logger.LogDebug(
                    "DexNewInstanceCorrector: retypedNew={Retyped} synthCtors={Synth} splitLocals={SplitLocals} skippedMethods={Skipped}",
                    retyped, synth, splitLocals, skippedMethods);
        }

        /// <summary>
        /// Generic repair for dex2jar's <em>register coalescing</em> (local-variable colouring) defect: dex2jar's
        /// register allocator frequently reuses a single JVM local slot for two source-level variables whose live
        /// ranges it believes to be disjoint. When that colouring is wrong under ART/IKVM semantics, two distinct
        /// values (e.g. the raw chapter-DTO list and the mapped <c>SChapter</c> list) end up sharing one slot and
        /// bleed into each other, yielding a list that contains both the source DTOs and the mapped results.
        /// <para>
        /// This pass reverses the (incorrect) coalescing generically — without matching any particular method
        /// shape — by recomputing each slot's independent <em>def-use webs</em> and renumbering interfering webs
        /// onto fresh slots. It runs a <see cref="SourceInterpreter"/> dataflow analysis, unions every use with all
        /// of its reaching definitions (stores / <c>IINC</c> / the synthetic parameter node), then partitions the
        /// stores of each original slot into connected webs. The first web (and any parameter web) keeps the
        /// original slot; each additional web is moved to a brand-new slot, and every <c>xLOAD</c>/<c>xSTORE</c>/
        /// <c>IINC</c> is rewritten to the slot of the web it belongs to. Category-2 (<c>long</c>/<c>double</c>)
        /// width is preserved. <c>COMPUTE_MAXS</c> in <see cref="WriteClass"/> recomputes the final frame sizes.
        /// </para>
        /// </summary>
        /// <returns>The number of webs relocated to a fresh slot (0 if the method was left untouched).</returns>
        private int SplitLocalWebs(ClassNode cn, MethodNode mn)
        {
            var insns = mn.instructions.toArray();
            if (insns.Length == 0)
                return 0;

            // Subroutines make slot lifetimes ambiguous; never rewrite methods that use JSR/RET.
            foreach (var p in insns)
            {
                int op = p.getOpcode();
                if (op == Opcodes.JSR || op == Opcodes.RET)
                    return 0;
            }

            var index = new Dictionary<AbstractInsnNode, int>(ReferenceEqualityComparer.Instance);
            for (int i = 0; i < insns.Length; i++)
                index[insns[i]] = i;

            Frame[] frames;
            var paramInterp = new ParamSeedingSourceInterpreter();
            try
            {
                var analyzer = new Analyzer(paramInterp);
                frames = analyzer.analyze(cn.name, mn);
            }
            catch (AnalyzerException)
            {
                return 0;
            }

            // Parameter slot layout / widths (this + declared args).
            var paramWidth = new Dictionary<int, int>();
            {
                int slot = 0;
                if ((mn.access & Opcodes.ACC_STATIC) == 0)
                {
                    paramWidth[0] = 1;
                    slot = 1;
                }
                foreach (var at in org.objectweb.asm.Type.getArgumentTypes(mn.desc))
                {
                    paramWidth[slot] = at.getSize();
                    slot += at.getSize();
                }
            }

            var uf = new UnionFind();

            // Reaching definition ids for a local read of slot v at instruction i.
            // A store/iinc node is identified by its instruction index (>= 0); reading the initial
            // parameter value is identified by the synthetic parameter id ParamId(slot) (< 0). Parameter
            // values are seeded as real producing markers by ParamSeedingSourceInterpreter, so a use
            // reached by BOTH a parameter and a later store carries both ids at the merge and is unioned
            // into a single web (the parameter web stays pinned) instead of losing the parameter linkage.
            List<int> ReachingIds(int i, int v)
            {
                var ids = new List<int>();
                var fr = frames[i];
                if (fr == null)
                    return ids;
                var sv = (SourceValue)fr.getLocal(v);
                bool any = false;
                foreach (var d in IterateInsns(sv.insns))
                {
                    if (index.TryGetValue(d, out var di))
                    {
                        ids.Add(di);
                        any = true;
                    }
                    else if (paramInterp.TryGetParamSlot(d, out var pslot))
                    {
                        ids.Add(ParamId(pslot));
                        any = true;
                    }
                }
                if (!any)
                    ids.Add(ParamId(v));
                return ids;
            }

            // Pass 1: register store nodes and unify each use with its reaching definitions.
            for (int i = 0; i < insns.Length; i++)
            {
                var p = insns[i];
                int op = p.getOpcode();
                if (IsLoadOpcode(op))
                {
                    int v = ((VarInsnNode)p).var;
                    var ids = ReachingIds(i, v);
                    for (int k = 1; k < ids.Count; k++)
                        uf.Union(ids[0], ids[k]);
                }
                else if (op == Opcodes.IINC)
                {
                    int v = ((IincInsnNode)p).var;
                    var ids = ReachingIds(i, v);
                    ids.Add(i); // IINC read-modify-writes v: same web as its reaching defs.
                    for (int k = 1; k < ids.Count; k++)
                        uf.Union(ids[0], ids[k]);
                }
                else if (IsStoreOpcode(op))
                {
                    uf.Add(i);
                }
            }

            // Partition all nodes into webs (connected components).
            var webs = new Dictionary<int, List<int>>();
            foreach (var id in uf.AllNodes())
            {
                int root = uf.Find(id);
                if (!webs.TryGetValue(root, out var members))
                    webs[root] = members = new List<int>();
                members.Add(id);
            }

            int SlotOfNode(int id)
            {
                if (id < 0)
                    return -id - 1;
                var p = insns[id];
                return p.getOpcode() == Opcodes.IINC ? ((IincInsnNode)p).var : ((VarInsnNode)p).var;
            }

            int WidthOfNode(int id)
            {
                if (id < 0)
                    return paramWidth.TryGetValue(-id - 1, out var pw) ? pw : 1;
                int op = insns[id].getOpcode();
                return (op == Opcodes.LSTORE || op == Opcodes.DSTORE) ? 2 : 1;
            }

            // Group webs by their original slot, recording width and whether the web owns a parameter.
            var bySlot = new Dictionary<int, List<(int root, int width, bool isParam)>>();
            foreach (var kv in webs)
            {
                int slot = -1;
                int width = 1;
                bool isParam = false;
                foreach (var id in kv.Value)
                {
                    int s = SlotOfNode(id);
                    if (slot == -1)
                        slot = s;
                    else if (slot != s)
                        return 0; // inconsistent slot in one web: model violated, stay safe.
                    if (WidthOfNode(id) == 2)
                        width = 2;
                    if (id < 0)
                        isParam = true;
                }
                if (!bySlot.TryGetValue(slot, out var list))
                    bySlot[slot] = list = new List<(int, int, bool)>();
                list.Add((kv.Key, width, isParam));
            }

            // Assign new slots: the parameter web (or the first web) keeps the original slot; every
            // additional web on the same slot is relocated to a fresh slot above maxLocals.
            var newSlotForRoot = new Dictionary<int, int>();
            int nextFree = mn.maxLocals;
            int relocated = 0;
            foreach (var kv in bySlot)
            {
                int origSlot = kv.Key;
                var group = kv.Value;
                if (group.Count <= 1)
                {
                    if (group.Count == 1)
                        newSlotForRoot[group[0].root] = origSlot;
                    continue;
                }

                int keeperIdx = group.FindIndex(g => g.isParam);
                if (keeperIdx < 0)
                    keeperIdx = 0;

                for (int gi = 0; gi < group.Count; gi++)
                {
                    if (gi == keeperIdx)
                    {
                        newSlotForRoot[group[gi].root] = origSlot;
                    }
                    else
                    {
                        newSlotForRoot[group[gi].root] = nextFree;
                        nextFree += group[gi].width;
                        relocated++;
                    }
                }
            }

            if (relocated == 0)
                return 0;

            // Snapshot original slot numbers + maxLocals so we can roll back if the rewrite fails to verify.
            var originalVars = new Dictionary<AbstractInsnNode, int>(ReferenceEqualityComparer.Instance);
            foreach (var p in insns)
            {
                int op = p.getOpcode();
                if (IsLoadOpcode(op) || IsStoreOpcode(op))
                    originalVars[p] = ((VarInsnNode)p).var;
                else if (op == Opcodes.IINC)
                    originalVars[p] = ((IincInsnNode)p).var;
            }
            int originalMaxLocals = mn.maxLocals;

            // Pass 2: rewrite every local access to the slot of the web it belongs to.
            for (int i = 0; i < insns.Length; i++)
            {
                var p = insns[i];
                int op = p.getOpcode();
                if (IsLoadOpcode(op))
                {
                    int v = ((VarInsnNode)p).var;
                    var ids = ReachingIds(i, v);
                    if (ids.Count == 0)
                        continue;
                    if (newSlotForRoot.TryGetValue(uf.Find(ids[0]), out var ns))
                        ((VarInsnNode)p).var = ns;
                }
                else if (op == Opcodes.IINC)
                {
                    var iinc = (IincInsnNode)p;
                    var ids = ReachingIds(i, iinc.var);
                    ids.Add(i);
                    if (newSlotForRoot.TryGetValue(uf.Find(ids[0]), out var ns))
                        iinc.var = ns;
                }
                else if (IsStoreOpcode(op))
                {
                    if (newSlotForRoot.TryGetValue(uf.Find(i), out var ns))
                        ((VarInsnNode)p).var = ns;
                }
            }

            mn.maxLocals = System.Math.Max(mn.maxLocals, nextFree);

            // Self-validation: a generic transform must never emit unverifiable bytecode. dex2jar's
            // register colouring can hide parameter/store merges that SourceInterpreter cannot see (the
            // parameter's initial value has no producing instruction, so a use reached by both the param
            // and a later store is not unioned with the param web). If the rewrite desynced a load/store
            // pair the verifier rejects it, so re-run the analyser over the rewritten method and roll the
            // whole method back to its original slots on any failure.
            if (!VerifiesLocals(cn, mn))
            {
                foreach (var kv in originalVars)
                {
                    int op = kv.Key.getOpcode();
                    if (op == Opcodes.IINC)
                        ((IincInsnNode)kv.Key).var = kv.Value;
                    else
                        ((VarInsnNode)kv.Key).var = kv.Value;
                }
                mn.maxLocals = originalMaxLocals;
                _logger.LogTrace(
                    "DexNewInstanceCorrector: local-web split reverted (verify failed) in {Class}.{Method}{Desc}",
                    cn.name, mn.name, mn.desc);
                return 0;
            }

            _logger.LogTrace(
                "DexNewInstanceCorrector: split {Relocated} local web(s) in {Class}.{Method}{Desc}",
                relocated, cn.name, mn.name, mn.desc);
            return relocated;
        }

        /// <summary>
        /// Re-runs a <see cref="BasicVerifier"/> dataflow analysis over <paramref name="mn"/> and returns
        /// <c>false</c> if it throws — i.e. the (rewritten) method would fail bytecode verification.
        /// </summary>
        private static bool VerifiesLocals(ClassNode cn, MethodNode mn)
        {
            try
            {
                new Analyzer(new BasicVerifier()).analyze(cn.name, mn);
                return true;
            }
            catch (AnalyzerException)
            {
                return false;
            }
            catch (System.Exception)
            {
                return false;
            }
        }

        private static int ParamId(int slot) => -(slot + 1);

        private static bool IsLoadOpcode(int op)
            => op >= Opcodes.ILOAD && op <= Opcodes.ALOAD;

        private static bool IsStoreOpcode(int op)
            => op >= Opcodes.ISTORE && op <= Opcodes.ASTORE;

        /// <summary>Minimal integer union-find used by <see cref="SplitLocalWebs"/> to group local def-use webs.</summary>
        private sealed class UnionFind
        {
            private readonly Dictionary<int, int> _parent = new();

            public void Add(int x)
            {
                if (!_parent.ContainsKey(x))
                    _parent[x] = x;
            }

            public int Find(int x)
            {
                Add(x);
                int root = x;
                while (_parent[root] != root)
                    root = _parent[root];
                // Path compression.
                while (_parent[x] != root)
                {
                    int next = _parent[x];
                    _parent[x] = root;
                    x = next;
                }
                return root;
            }

            public void Union(int a, int b)
            {
                int ra = Find(a), rb = Find(b);
                if (ra != rb)
                    _parent[rb] = ra;
            }

            public IEnumerable<int> AllNodes() => new List<int>(_parent.Keys);
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
            {
                // Diagnostic: this guard leaves an Object-typed NEW unrepaired. If the method
                // that builds chapters (references 'r' / returns SChapter) lands here, that is a
                // strong signal the R8 mistranslation for this method is not oracle-recoverable.
                _logger.LogTrace(
                    "DexNewInstanceCorrector: oracle mismatch, skipping {Class}.{Method}{Desc} (jvmNews={JvmNews}, dexNews={DexNews})",
                    cn.name, mn.name, mn.desc, jvmNews.Count, dex == null ? -1 : dex.Count);
                return (0, 1); // guard: counts disagree -> leave method untouched
            }

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
            catch (AnalyzerException ex)
            {
                _logger.LogTrace(ex,
                    "DexNewInstanceCorrector: dataflow analysis failed, skipping {Class}.{Method}{Desc}",
                    cn.name, mn.name, mn.desc);
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

        /// <summary>
        /// <see cref="SourceInterpreter"/> that seeds every incoming parameter slot with a unique marker
        /// producing instruction. The stock interpreter represents parameter values with an empty
        /// producing-instruction set, so a local read reached by both a parameter and a later store loses
        /// the parameter linkage at the merge — which caused <see cref="SplitLocalWebs"/> to over-split
        /// parameter-carrying slots (notably Kotlin suspend state machines) and emit unverifiable bytecode.
        /// By giving each parameter slot a real marker, the merge carries both the marker and the store, so
        /// the web is correctly unified and the parameter slot stays pinned.
        /// </summary>
        private sealed class ParamSeedingSourceInterpreter : SourceInterpreter
        {
            private readonly Dictionary<AbstractInsnNode, int> _paramMarkers = new(ReferenceEqualityComparer.Instance);

            public ParamSeedingSourceInterpreter() : base(Opcodes.ASM9) { }

            public override SourceValue newParameterValue(bool isInstanceMethod, int local, org.objectweb.asm.Type type)
            {
                int size = type.getSize();
                var marker = new InsnNode(Opcodes.NOP);
                _paramMarkers[marker] = local;
                return new SourceValue(size, marker);
            }

            public bool TryGetParamSlot(AbstractInsnNode insn, out int slot)
                => _paramMarkers.TryGetValue(insn, out slot);
        }
    }
}
