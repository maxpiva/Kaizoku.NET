using Mono.Cecil;
using Mono.Cecil.Cil;
using static System.Runtime.InteropServices.JavaScript.JSType;


namespace IKVMLambdaPatch
{
    internal static class Program
    {
        private static byte[]? ParsePublicKeyToken(string? hex)
        {
            if (string.IsNullOrWhiteSpace(hex))
                return null;

            hex = hex.Trim();
            if (hex.StartsWith("0x", System.StringComparison.OrdinalIgnoreCase))
                hex = hex[2..];

            if (hex.Length % 2 != 0)
                throw new System.ArgumentException("Public key token hex must have an even length.", nameof(hex));

            var bytes = new byte[hex.Length / 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = System.Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }

            return bytes;
        }
        private static bool RemoveAssemblyReference(AssemblyDefinition assembly, string nameOrFullName)
        {
            var modified = false;

            foreach (var module in assembly.Modules)
            {
                var refs = module.AssemblyReferences;
                var toRemove = refs
                    .Where(r =>
                        string.Equals(r.Name, nameOrFullName, System.StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(r.FullName, nameOrFullName, System.StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var r in toRemove)
                {
                    refs.Remove(r);
                    modified = true;
                }
            }

            return modified;
        }

        private static bool AddAssemblyReference(
            AssemblyDefinition assembly,
            string name,
            System.Version? version = null,
            string? culture = null,
            string? publicKeyTokenHex = null)
        {
            var modified = false;
            var ver = version ?? new System.Version(0, 0, 0, 0);
            var cultureString = culture ?? string.Empty;
            var pkt = ParsePublicKeyToken(publicKeyTokenHex);

            foreach (var module in assembly.Modules)
            {
                // Avoid duplicates by name
                if (module.AssemblyReferences.Any(r =>
                        string.Equals(r.Name, name, System.StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var reference = new AssemblyNameReference(name, ver)
                {
                    Culture = cultureString,
                    PublicKeyToken = pkt
                };

                module.AssemblyReferences.Add(reference);
                modified = true;
            }

            return modified;
        }

        private static int Main(string[] args)
        {
            if (args.Length <2)
            {
                Console.WriteLine("Usage: CILPatcher <source> <destination>");
                return 1;
            }
            string source = Path.GetFullPath(args[0]);
            string destination = Path.GetFullPath(args[1]);
            if (!File.Exists(source))
            {
                Console.WriteLine($"Source file '{source}' does not exist.");
                return 1;
            }
            string directorydest = Path.GetDirectoryName(destination)!;
            if (!Directory.Exists(directorydest))
            {
                Directory.CreateDirectory(directorydest);
            }
         
            Console.WriteLine($"Patching {source}");

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(Path.GetDirectoryName(source)!);
             var assembly = AssemblyDefinition.ReadAssembly(source, new ReaderParameters { AssemblyResolver = resolver });
            var modified = false;
            foreach (var module in assembly.Modules)
            {
                //RemoveAssemblyReference(assembly, "System.Private.CoreLib");
                //AddAssemblyReference(assembly, "System.Runtime", new System.Version(10, 0, 0, 0), "neutral", "B03F5F7F11D50A3A");
                //AddAssemblyReference(assembly, "System.Threading.Thread", new System.Version(10, 0, 0, 0), "neutral", "B03F5F7F11D50A3A");
                //AddAssemblyReference(assembly, "System.Threading", new System.Version(10, 0, 0, 0), "neutral", "B03F5F7F11D50A3A");
                //AddAssemblyReference(assembly, "System.Console", new System.Version(10, 0, 0, 0), "neutral", "B03F5F7F11D50A3A");
                //modified = true;
                foreach (var type in module.Types)
                {
                    modified |= GeneralConstructorPatcher.FixGeneralConstructorsTree(type);
                    modified |= FixJSOUP.FixAnonLambdaTree(type);
                }
            }

    
            if (!modified)
            {
                Console.WriteLine("No lambda constructors required patching.");
                return 0;
            }
            assembly.Write(destination);
            Console.WriteLine($"Saved patched assembly to {destination}");
            return 0;
        }
        /*
        private static bool FixLambdaTree(TypeDefinition type)
        {
            var touched = FixLambda(type);

            foreach (var nested in type.NestedTypes)
            {
                touched |= FixLambdaTree(nested);
            }

            return touched;
        }

        private static bool FixLambda(TypeDefinition type)
        {
            if (!IsLambda(type))
                return false;

            var ctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.HasBody);
            if (ctor == null || ctor.Parameters.Count == 0)
                return false;

            var instanceFields = type.Fields.Where(f => !f.IsStatic).ToList();
            if (instanceFields.Count == 0)
                return false;

            var assignments = BuildAssignments(instanceFields, ctor.Parameters.ToList());
            if (assignments == null || assignments.Count == 0)
                return false;

            var existing = ExtractExistingAssignments(ctor);
            if (AlreadyMatches(assignments, existing))
                return false;

            var baseCtor = FindLambdaBaseConstructor(type);
            if (baseCtor == null)
                return false;

            var arity = ExtractLambdaArity(ctor) ?? 0;

            RewriteConstructor(ctor, assignments, baseCtor, arity);
            return true;
        }

        private static bool IsLambda(TypeDefinition type) =>
            type.BaseType?.FullName == "kotlin.jvm.internal.Lambda" &&
            type.Methods.Any(m => m.Name == "invoke");

        private static List<LambdaAssignment>? BuildAssignments(
            IReadOnlyCollection<FieldDefinition> fields,
            IReadOnlyCollection<ParameterDefinition> parameters)
        {
            if (parameters.Count != fields.Count)
                return MatchByOrder(fields, parameters);

            var result = new List<LambdaAssignment>(parameters.Count);
            var remaining = new HashSet<FieldDefinition>(fields);

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters.ElementAt(i);
                var match = remaining.FirstOrDefault(f => AreTypesEquivalent(f.FieldType, parameter.ParameterType));
                if (match == null)
                {
                    match = remaining.FirstOrDefault();
                }

                if (match == null)
                {
                    return null;
                }

                result.Add(CreateAssignment(match, parameter, i));
                remaining.Remove(match);
            }

            return result;
        }

        private static List<LambdaAssignment>? MatchByOrder(
            IReadOnlyCollection<FieldDefinition> fields,
            IReadOnlyCollection<ParameterDefinition> parameters)
        {
            if (parameters.Count > fields.Count)
                return null;

            var result = new List<LambdaAssignment>(fields.Count);
            var pairs = fields.Zip(parameters, (field, parameter) => (field, parameter)).ToList();

            if (pairs.Count == 0)
                return null;

            for (var i = 0; i < pairs.Count; i++)
            {
                result.Add(CreateAssignment(pairs[i].field, pairs[i].parameter, i));
            }

            if (parameters.Count < fields.Count)
            {
                // Reuse the last parameter for any remaining fields.
                var lastParameter = parameters.Last();
                for (var i = parameters.Count; i < fields.Count; i++)
                {
                    result.Add(CreateAssignment(fields.ElementAt(i), lastParameter, parameters.Count - 1));
                }
            }

            return result;
        }

        private static LambdaAssignment CreateAssignment(FieldDefinition field, ParameterDefinition parameter, int parameterIndex)
        {
            var needsCast = !AreTypesEquivalent(field.FieldType, parameter.ParameterType);
            var castTarget = needsCast ? field.FieldType : null;
            return new LambdaAssignment(field, parameterIndex, castTarget);
        }

        private static bool AreTypesEquivalent(TypeReference fieldType, TypeReference parameterType)
        {
            if (fieldType.FullName == parameterType.FullName)
                return true;

            if (fieldType is GenericInstanceType genericField && parameterType is GenericInstanceType genericParameter)
            {
                if (genericField.ElementType.FullName != genericParameter.ElementType.FullName)
                    return false;

                return genericField.GenericArguments
                    .Zip(genericParameter.GenericArguments, (f, p) => f.FullName == p.FullName)
                    .All(equal => equal);
            }

            return false;
        }

        private static List<ExistingAssignment> ExtractExistingAssignments(MethodDefinition ctor)
        {
            var result = new List<ExistingAssignment>();
            var instructions = ctor.Body.Instructions;

            for (var i = 0; i < instructions.Count; i++)
            {
                var instruction = instructions[i];
                if (instruction.OpCode != OpCodes.Stfld)
                    continue;

                if (instruction.Operand is not FieldReference fieldRef)
                    continue;

                var field = fieldRef.Resolve();
                if (field == null)
                    continue;

                if (!TryResolveValueSource(ctor, i - 1, out var parameterIndex))
                    continue;

                result.Add(new ExistingAssignment(field, parameterIndex));
            }

            return result;
        }

        private static bool TryResolveValueSource(MethodDefinition ctor, int instructionIndex, out int parameterIndex)
        {
            parameterIndex = -1;
            var instructions = ctor.Body.Instructions;

            for (var i = instructionIndex; i >= 0; i--)
            {
                var instruction = instructions[i];
                switch (instruction.OpCode.Code)
                {
                    case Code.Castclass:
                    case Code.Box:
                    case Code.Nop:
                        continue;

                    case Code.Ldarg_0:
                        return false;

                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                    case Code.Ldarg_S:
                    case Code.Ldarg:
                        parameterIndex = GetArgumentIndex(instruction, ctor);
                        return parameterIndex >= 0;

                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                        var localIndex = GetLocalIndex(instruction);
                        if (TryResolveLocalSource(ctor, i, localIndex, out parameterIndex))
                            return true;
                        return false;

                    default:
                        return false;
                }
            }

            return false;
        }

        private static bool TryResolveLocalSource(MethodDefinition ctor, int instructionIndex, int localIndex, out int parameterIndex)
        {
            parameterIndex = -1;
            var instructions = ctor.Body.Instructions;

            for (var i = instructionIndex; i >= 0; i--)
            {
                var instruction = instructions[i];
                if (!IsStoreToLocal(instruction, localIndex))
                    continue;

                return TryResolveValueSource(ctor, i - 1, out parameterIndex);
            }

            return false;
        }

        private static bool IsStoreToLocal(Instruction instruction, int localIndex)
        {
            return instruction.OpCode.Code switch
            {
                Code.Stloc_0 => localIndex == 0,
                Code.Stloc_1 => localIndex == 1,
                Code.Stloc_2 => localIndex == 2,
                Code.Stloc_3 => localIndex == 3,
                Code.Stloc_S or Code.Stloc => GetLocalIndex(instruction) == localIndex,
                _ => false
            };
        }

        private static int GetArgumentIndex(Instruction instruction, MethodDefinition method)
        {
            var hasThis = method.HasThis;

            return instruction.OpCode.Code switch
            {
                Code.Ldarg_0 => hasThis ? -1 : 0,
                Code.Ldarg_1 => hasThis ? 0 : 1,
                Code.Ldarg_2 => hasThis ? 1 : 2,
                Code.Ldarg_3 => hasThis ? 2 : 3,
                Code.Ldarg_S or Code.Ldarg => instruction.Operand switch
                {
                    ParameterDefinition parameter => parameter.Index,
                    sbyte sb => sb,
                    byte b => b,
                    int i => i,
                    _ => -1
                },
                _ => -1
            };
        }

        private static int GetLocalIndex(Instruction instruction)
        {
            return instruction.OpCode.Code switch
            {
                Code.Ldloc_0 or Code.Stloc_0 => 0,
                Code.Ldloc_1 or Code.Stloc_1 => 1,
                Code.Ldloc_2 or Code.Stloc_2 => 2,
                Code.Ldloc_3 or Code.Stloc_3 => 3,
                Code.Ldloc_S or Code.Stloc_S or Code.Ldloc or Code.Stloc => instruction.Operand switch
                {
                    VariableDefinition variable => variable.Index,
                    sbyte sb => sb,
                    byte b => b,
                    int i => i,
                    _ => 0
                },
                _ => 0
            };
        }

        private static bool AlreadyMatches(
            IReadOnlyCollection<LambdaAssignment> desired,
            IReadOnlyCollection<ExistingAssignment> existing)
        {
            if (desired.Count == 0)
                return true;

            var existingMap = existing
                .GroupBy(e => e.Field)
                .ToDictionary(group => group.Key, group => group.First());

            foreach (var assignment in desired)
            {
                if (!existingMap.TryGetValue(assignment.Field, out var current))
                    return false;

                if (current.ParameterIndex != assignment.ParameterIndex)
                    return false;
            }

            return true;
        }

        private static MethodReference? FindLambdaBaseConstructor(TypeDefinition type)
        {
            var methods = type.BaseType.Resolve()
                ?.Methods;
            var constructor = methods?.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1);
            return methods?.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32");
        }
        private static int? ExtractLambdaArity(MethodDefinition ctor)
        {
            var instructions = ctor.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode == OpCodes.Call &&
                    instructions[i].Operand is MethodReference method &&
                    method.Resolve()?.DeclaringType.FullName == "kotlin.jvm.internal.Lambda")
                {
                    var value = instructions.ElementAtOrDefault(i - 1);
                    if (value == null) return null;

                    switch (value.OpCode.Code)
                    {
                        case Code.Ldc_I4_M1: return -1;
                        case Code.Ldc_I4: return (int)value.Operand;
                        case Code.Ldc_I4_0: return 0;
                        case Code.Ldc_I4_1: return 1;
                        case Code.Ldc_I4_2: return 2;
                        case Code.Ldc_I4_3: return 3;
                        case Code.Ldc_I4_4: return 4;
                        case Code.Ldc_I4_5: return 5;
                        case Code.Ldc_I4_6: return 6;
                        case Code.Ldc_I4_7: return 7;
                        case Code.Ldc_I4_8: return 8;
                        default: return 0;
                    }
                }
            }

            return null;
        }

        private static void RewriteConstructor(
            MethodDefinition ctor,
            IReadOnlyCollection<LambdaAssignment> assignments,
            MethodReference baseConstructor,
            int arity)
        {
            var body = ctor.Body;
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();
            body.InitLocals = false;

            var il = body.GetILProcessor();

            foreach (var assignment in assignments.OrderBy(a => a.ParameterIndex))
            {
                il.Emit(OpCodes.Ldarg_0);
                EmitLoadArg(il, assignment.ParameterIndex + 1, ctor);

                if (assignment.CastTarget != null)
                {
                    il.Emit(OpCodes.Castclass, ctor.Module.ImportReference(assignment.CastTarget));
                }

                il.Emit(OpCodes.Stfld, assignment.Field);
            }

            il.Emit(OpCodes.Ldarg_0);
            EmitLoadConstant(il, arity);
            il.Emit(OpCodes.Call, ctor.Module.ImportReference(baseConstructor));

            var ret = Instruction.Create(OpCodes.Ret);
            il.Emit(OpCodes.Leave, ret);
            il.Append(ret);
        }

        private static void EmitLoadArg(ILProcessor il, int position, MethodDefinition ctor)
        {
            switch (position)
            {
                case 0:
                    il.Emit(OpCodes.Ldarg_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldarg_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldarg_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldarg_3);
                    break;
                default:
                    il.Emit(OpCodes.Ldarg, ctor.Parameters[position - 1]);
                    break;
            }
        }


        private static void EmitLoadConstant(ILProcessor il, int value)
        {
            switch (value)
            {
                case -1:
                    il.Emit(OpCodes.Ldc_I4_M1);
                    break;
                case 0:
                    il.Emit(OpCodes.Ldc_I4_0);
                    break;
                case 1:
                    il.Emit(OpCodes.Ldc_I4_1);
                    break;
                case 2:
                    il.Emit(OpCodes.Ldc_I4_2);
                    break;
                case 3:
                    il.Emit(OpCodes.Ldc_I4_3);
                    break;
                case 4:
                    il.Emit(OpCodes.Ldc_I4_4);
                    break;
                case 5:
                    il.Emit(OpCodes.Ldc_I4_5);
                    break;
                case 6:
                    il.Emit(OpCodes.Ldc_I4_6);
                    break;
                case 7:
                    il.Emit(OpCodes.Ldc_I4_7);
                    break;
                case 8:
                    il.Emit(OpCodes.Ldc_I4_8);
                    break;
                default:
                    il.Emit(OpCodes.Ldc_I4, value);
                    break;
            }
        }

        private sealed record LambdaAssignment(FieldDefinition Field, int ParameterIndex, TypeReference? CastTarget);
        private sealed record ExistingAssignment(FieldDefinition Field, int ParameterIndex);

 



































        private static bool FixLambda(TypeDefinition type)
        {
            if (!IsLambda(type))
                return false;

            var ctor = type.Methods.FirstOrDefault(m => m.IsConstructor && m.HasBody);
            if (ctor == null || ctor.Parameters.Count == 0)
                return false;

            var instanceFields = type.Fields.Where(f => !f.IsStatic).ToList();
            if (instanceFields.Count == 0)
                return false;

            var fieldMap = MatchFieldsToParameters(instanceFields, ctor.Parameters.ToList());
            if (fieldMap == null)
                return false;
            if (instanceFields.Count<2)
                return false;
            var baseCtor = FindLambdaBaseConstructor(type);
            if (baseCtor == null)
                return false;

            var arity = ExtractLambdaArity(ctor) ?? 0;

            var proposed = BuildConstructorInstructions(ctor, fieldMap, baseCtor, arity);
            var existing = ctor.Body.Instructions.ToList();

            if (InstructionsMatch(existing, proposed))
                return false;

            ApplyInstructions(ctor, proposed);
            return true;
        }

        private static bool IsLambda(TypeDefinition type) =>
            type.BaseType?.FullName == "kotlin.jvm.internal.Lambda" &&
            type.Methods.Any(m => m.Name == "invoke");

        private static Dictionary<int, FieldDefinition>? MatchFieldsToParameters(
            IReadOnlyCollection<FieldDefinition> fields,
            IReadOnlyCollection<ParameterDefinition> parameters)
        {
            var map = new Dictionary<int, FieldDefinition>(parameters.Count);
            var remaining = new HashSet<FieldDefinition>(fields);

            for (var i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters.ElementAt(i);
                var match = remaining.FirstOrDefault(f => TypesMatch(f.FieldType, parameter.ParameterType));
                if (match == null)
                {
                    match = remaining.FirstOrDefault();
                }

                if (match == null)
                {
                    return null;
                }

                map[i] = match;
                remaining.Remove(match);
            }

            return map;
        }

        private static bool TypesMatch(TypeReference field, TypeReference parameter)
        {
            if (field.FullName == parameter.FullName)
                return true;

            if (field is GenericInstanceType fieldGeneric && parameter is GenericInstanceType paramGeneric)
            {
                if (fieldGeneric.ElementType.FullName != paramGeneric.ElementType.FullName)
                    return false;

                return fieldGeneric.GenericArguments.Zip(paramGeneric.GenericArguments,
                    (f, p) => f.FullName == p.FullName).All(result => result);
            }

            return false;
        }

        private static MethodReference? FindLambdaBaseConstructor(TypeDefinition type)
        {
            var methods = type.BaseType.Resolve()
                ?.Methods;
            var constructor = methods?.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1);
            return methods?.FirstOrDefault(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.Int32");
        }

        private static int? ExtractLambdaArity(MethodDefinition ctor)
        {
            var instructions = ctor.Body.Instructions;
            for (var i = 0; i < instructions.Count; i++)
            {
                if (instructions[i].OpCode == OpCodes.Call &&
                    instructions[i].Operand is MethodReference method &&
                    method.Resolve()?.DeclaringType.FullName == "kotlin.jvm.internal.Lambda")
                {
                    var value = instructions.ElementAtOrDefault(i - 1);
                    if (value == null) return null;

                    switch (value.OpCode.Code)
                    {
                        case Code.Ldc_I4_M1: return -1;
                        case Code.Ldc_I4: return (int)value.Operand;
                        case Code.Ldc_I4_0: return 0;
                        case Code.Ldc_I4_1: return 1;
                        case Code.Ldc_I4_2: return 2;
                        case Code.Ldc_I4_3: return 3;
                        case Code.Ldc_I4_4: return 4;
                        case Code.Ldc_I4_5: return 5;
                        case Code.Ldc_I4_6: return 6;
                        case Code.Ldc_I4_7: return 7;
                        case Code.Ldc_I4_8: return 8;
                        default: return 0;
                    }
                }
            }

            return null;
        }

        private static void RewriteConstructor(
            MethodDefinition ctor,
            Dictionary<int, FieldDefinition> fieldMap,
            MethodReference baseConstructor,
            int arity)
        {
            var body = ctor.Body;
            foreach(var ins in body.Instructions)
            {
                Console.WriteLine(ins);
            }
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();
            body.InitLocals = false;

            var il = body.GetILProcessor();

            foreach (var kvp in fieldMap.OrderBy(kvp => kvp.Key))
            {
                il.Emit(OpCodes.Ldarg_0);
                EmitLoadArg(il, kvp.Key + 1);
                il.Emit(OpCodes.Stfld, kvp.Value);
            }

            il.Emit(OpCodes.Ldarg_0);
            EmitLoadConstant(il, arity);
            il.Emit(OpCodes.Call, ctor.Module.ImportReference(baseConstructor));
            il.Emit(OpCodes.Ret);
            foreach (var ins in body.Instructions)
            {
                Console.WriteLine(ins);
            }
        }

        private static void EmitLoadArg(ILProcessor il, int index)
        {
            switch (index)
            {
                case 0: il.Emit(OpCodes.Ldarg_0); break;
                case 1: il.Emit(OpCodes.Ldarg_1); break;
                case 2: il.Emit(OpCodes.Ldarg_2); break;
                case 3: il.Emit(OpCodes.Ldarg_3); break;
                default: il.Emit(OpCodes.Ldarg_S, (byte)index); break;
            }
        }

        private static void EmitLoadConstant(ILProcessor il, int value)
        {
            switch (value)
            {
                case -1: il.Emit(OpCodes.Ldc_I4_M1); break;
                case 0: il.Emit(OpCodes.Ldc_I4_0); break;
                case 1: il.Emit(OpCodes.Ldc_I4_1); break;
                case 2: il.Emit(OpCodes.Ldc_I4_2); break;
                case 3: il.Emit(OpCodes.Ldc_I4_3); break;
                case 4: il.Emit(OpCodes.Ldc_I4_4); break;
                case 5: il.Emit(OpCodes.Ldc_I4_5); break;
                case 6: il.Emit(OpCodes.Ldc_I4_6); break;
                case 7: il.Emit(OpCodes.Ldc_I4_7); break;
                case 8: il.Emit(OpCodes.Ldc_I4_8); break;
                default: il.Emit(OpCodes.Ldc_I4, value); break;
            }
        }



    private static IList<Instruction> BuildConstructorInstructions(
        MethodDefinition ctor,
        Dictionary<int, FieldDefinition> fieldMap,
        MethodReference baseConstructor,
        int arity)
        {
            var module = ctor.Module;
            var instructions = new List<Instruction>();

            foreach (var kvp in fieldMap.OrderBy(k => k.Key))
            {
                instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
                instructions.Add(CreateLoadArgInstruction(kvp.Key + 1, ctor));
                instructions.Add(Instruction.Create(OpCodes.Stfld, kvp.Value));
            }

            instructions.Add(Instruction.Create(OpCodes.Ldarg_0));
            instructions.Add(CreateLoadConstantInstruction(arity));
            instructions.Add(Instruction.Create(OpCodes.Call, module.ImportReference(baseConstructor)));

            var ret = Instruction.Create(OpCodes.Ret);
            instructions.Add(Instruction.Create(OpCodes.Leave, ret));
            instructions.Add(ret);

            return instructions;
        }

        private static void ApplyInstructions(MethodDefinition ctor, IList<Instruction> instructions)
        {
            var body = ctor.Body;
            foreach (var instruction in body.Instructions)
            {
                Console.WriteLine(instruction);
            }
            body.Instructions.Clear();
            body.ExceptionHandlers.Clear();
            body.InitLocals = false;

            foreach (var instruction in instructions)
            {
                body.Instructions.Add(instruction);
                
            }
            foreach (var instruction in body.Instructions)
            {
                Console.WriteLine(instruction);
            }
        }

        private static Instruction CreateLoadArgInstruction(int argumentPosition, MethodDefinition ctor)
        {
            return argumentPosition switch
            {
                0 => Instruction.Create(OpCodes.Ldarg_0),
                1 => Instruction.Create(OpCodes.Ldarg_1),
                2 => Instruction.Create(OpCodes.Ldarg_2),
                3 => Instruction.Create(OpCodes.Ldarg_3),
                _ => Instruction.Create(OpCodes.Ldarg, ctor.Parameters[argumentPosition - 1])
            };
        }

        private static Instruction CreateLoadConstantInstruction(int value) =>
            value switch
            {
                -1 => Instruction.Create(OpCodes.Ldc_I4_M1),
                0 => Instruction.Create(OpCodes.Ldc_I4_0),
                1 => Instruction.Create(OpCodes.Ldc_I4_1),
                2 => Instruction.Create(OpCodes.Ldc_I4_2),
                3 => Instruction.Create(OpCodes.Ldc_I4_3),
                4 => Instruction.Create(OpCodes.Ldc_I4_4),
                5 => Instruction.Create(OpCodes.Ldc_I4_5),
                6 => Instruction.Create(OpCodes.Ldc_I4_6),
                7 => Instruction.Create(OpCodes.Ldc_I4_7),
                8 => Instruction.Create(OpCodes.Ldc_I4_8),
                _ => Instruction.Create(OpCodes.Ldc_I4, value)
            };

    private sealed record InstructionSignature(string OpCode, string? Operand);

    private static bool InstructionsMatch(IList<Instruction> existing, IList<Instruction> proposed)
    {
        if (existing.Count != proposed.Count)
            return false;

        var existingSignatures = BuildSignatures(existing);
        var proposedSignatures = BuildSignatures(proposed);

        for (var i = 0; i < existingSignatures.Count; i++)
        {
            if (existingSignatures[i].OpCode != proposedSignatures[i].OpCode)
                return false;

            if (!Equals(existingSignatures[i].Operand, proposedSignatures[i].Operand))
                return false;
        }

        return true;
    }

    private static List<InstructionSignature> BuildSignatures(IList<Instruction> instructions)
    {
        var hasThis = instructions.FirstOrDefault()?.Operand switch
        {
            ParameterDefinition parameter => parameter.Method?.HasThis ?? false,
            _ => true
        };

        var signatures = new List<InstructionSignature>(instructions.Count);
        for (var i = 0; i < instructions.Count; i++)
        {
            var ins = instructions[i];
            signatures.Add(new InstructionSignature(
                NormalizeOpCode(ins.OpCode),
                BuildOperandKey(ins, instructions, hasThis)));
        }

        return signatures;
    }

    private static string NormalizeOpCode(OpCode opcode) =>
        opcode.Code switch
        {
            Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S or Code.Ldarg => "Ldarg",
            Code.Leave or Code.Leave_S => "Leave",
            Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4_M1 => "Ldc_I4",
            _ => opcode.Code.ToString()
        };

    private static string? BuildOperandKey(Instruction instruction, IList<Instruction> sequence, bool hasThis)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldarg_0 or Code.Ldarg_1 or Code.Ldarg_2 or Code.Ldarg_3 or Code.Ldarg_S or Code.Ldarg =>
                $"arg:{GetArgumentIndex(instruction, hasThis)}",
            Code.Stfld => (instruction.Operand as FieldReference)?.FullName,
            Code.Call => (instruction.Operand as MethodReference)?.FullName,
            Code.Leave or Code.Leave_S =>
                instruction.Operand is Instruction target ? $"inst:{sequence.IndexOf(target)}" : null,
            Code.Ldc_I4 or Code.Ldc_I4_S or Code.Ldc_I4_0 or Code.Ldc_I4_1 or Code.Ldc_I4_2 or Code.Ldc_I4_3 or Code.Ldc_I4_4 or Code.Ldc_I4_5 or Code.Ldc_I4_6 or Code.Ldc_I4_7 or Code.Ldc_I4_8 or Code.Ldc_I4_M1 =>
                $"int:{GetInt32Value(instruction)}",
            _ => instruction.Operand switch
            {
                null => null,
                Instruction target => $"inst:{sequence.IndexOf(target)}",
                FieldReference field => field.FullName,
                MethodReference method => method.FullName,
                TypeReference type => type.FullName,
                string text => $"str:{text}",
                int number => $"int:{number}",
                _ => instruction.Operand.ToString()
            }
        };
    }

    private static int GetArgumentIndex(Instruction instruction, bool hasThis)
    {
        return instruction.OpCode.Code switch
        {
            Code.Ldarg_0 => 0,
            Code.Ldarg_1 => 1,
            Code.Ldarg_2 => 2,
            Code.Ldarg_3 => 3,
            Code.Ldarg_S or Code.Ldarg => instruction.Operand switch
            {
                ParameterDefinition parameter => parameter.Index + (hasThis ? 1 : 0),
                sbyte sb => sb,
                byte b => b,
                int i => i,
                _ => 0
            },
            _ => 0
        };
    }

    private static int GetInt32Value(Instruction instruction) =>
        instruction.OpCode.Code switch
        {
            Code.Ldc_I4_M1 => -1,
            Code.Ldc_I4_0 => 0,
            Code.Ldc_I4_1 => 1,
            Code.Ldc_I4_2 => 2,
            Code.Ldc_I4_3 => 3,
            Code.Ldc_I4_4 => 4,
            Code.Ldc_I4_5 => 5,
            Code.Ldc_I4_6 => 6,
            Code.Ldc_I4_7 => 7,
            Code.Ldc_I4_8 => 8,
            Code.Ldc_I4_S => instruction.Operand switch
            {
                sbyte sb => sb,
                byte b => (sbyte)b,
                _ => Convert.ToSByte(instruction.Operand)
            },
            Code.Ldc_I4 => instruction.Operand is int value ? value : Convert.ToInt32(instruction.Operand),
            _ => 0
        };


        // Plan (pseudocode):
        // - Implement RemoveAssemblyReference(AssemblyDefinition, string):
        //   - Iterate modules in the assembly.
        //   - For each module, find AssemblyNameReference entries whose Name or FullName matches (case-insensitive).
        //   - Remove all matches; return true if any removal occurred.
        // - Implement AddAssemblyReference(AssemblyDefinition, string, Version?, string?, string?):
        //   - Iterate modules in the assembly.
        //   - If a reference with the same Name exists, skip adding for that module.
        //   - Create a new AssemblyNameReference with provided name, version (default 0.0.0.0), culture (default empty), and optional public key token (hex).
        //   - Add to module.AssemblyReferences; return true if added to at least one module.
        // - Implement helper ParsePublicKeyToken(string?) to convert hex string to byte[] (returns null if null/empty).

     
        */
       
    }
}

