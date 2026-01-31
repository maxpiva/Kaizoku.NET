using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Linq;

namespace IKVMLambdaPatch
{
    internal static class GeneralConstructorPatcher
    {
        public static bool FixGeneralConstructorsTree(TypeDefinition type)
        {
            var touched = FixGeneralConstructors(type);
            foreach (var nested in type.NestedTypes)
            {
                touched |= FixGeneralConstructorsTree(nested);
            }
            return touched;
        }

        private static bool FixGeneralConstructors(TypeDefinition type)
        {
            var touched = false;

            foreach (var ctor in type.Methods)
            {
                if (!ctor.IsConstructor || !ctor.HasBody)
                    continue;

                if (!ctor.ToString().Contains("$1::.ctor") && !ctor.ToString().Contains("$2::.ctor"))
                    continue;
                //if (!CheckRepeatedParameters(ctor))
                //    continue;

                var instructions = ctor.Body.Instructions;
                for (var i = 0; i < instructions.Count; i++)
                {
                    var instruction = instructions[i];
                    if (instruction.OpCode != OpCodes.Stfld)
                        continue;

                    if (instruction.Operand is not FieldReference fieldRef)
                        continue;

                    var field = fieldRef.Resolve();
                    if (field == null || field.IsStatic)
                        continue;

                    var desiredParamIndex = FindBestMatchingParameterIndex(field.FieldType, ctor.Parameters);
                    if (desiredParamIndex < 0)
                        continue;

                    if (!TryResolveValueSource(ctor, i - 1, out var currentParamIndex))
                        continue;

                    // If the currently used parameter already matches the field type, don't change it
                    if (currentParamIndex >= 0 && currentParamIndex < ctor.Parameters.Count)
                    {
                        var currentParamType = ctor.Parameters[currentParamIndex].ParameterType;
                        if (AreTypesEquivalent(field.FieldType, currentParamType))
                            continue;
                    }

                    // Only patch when a better matching parameter exists by type and index differs
                    if (currentParamIndex != desiredParamIndex)
                    {
                        if (TryReplaceLoadWithArg(ctor, i - 1, desiredParamIndex))
                        {
                            touched = true;
                        }
                    }
                }
            }

            return touched;
        }
        public static bool CheckRepeatedParameters(MethodDefinition def)
        {
            HashSet<string> seen = new();
            foreach(var param in def.Parameters)
            {
                string name = param.Name;
                if (!string.IsNullOrEmpty(name))
                {
                    char last = name.Last();
                    if (char.IsDigit(last))
                    {
                        name = name.Substring(0, name.Length - 1);
                    }
                }
                if (!seen.Add(name))
                {
                    return true;
                }
            }
            return false;
        }
        private static int FindBestMatchingParameterIndex(TypeReference fieldType, Mono.Collections.Generic.Collection<ParameterDefinition> parameters)
        {
            for (var i = 0; i < parameters.Count; i++)
            {
                if (AreTypesEquivalent(fieldType, parameters[i].ParameterType))
                    return i;
            }
            return parameters.Count > 0 ? 0 : -1;
        }

        private static bool TryReplaceLoadWithArg(MethodDefinition ctor, int startIndex, int desiredParamIndex)
        {
            var instructions = ctor.Body.Instructions;
            for (var i = startIndex; i >= 0; i--)
            {
                var ins = instructions[i];
                switch (ins.OpCode.Code)
                {
                    case Code.Castclass:
                    case Code.Box:
                    case Code.Nop:
                        continue;
                    case Code.Ldarg_0:
                    case Code.Ldarg_1:
                    case Code.Ldarg_2:
                    case Code.Ldarg_3:
                    case Code.Ldarg_S:
                    case Code.Ldarg:
                    case Code.Ldloc_0:
                    case Code.Ldloc_1:
                    case Code.Ldloc_2:
                    case Code.Ldloc_3:
                    case Code.Ldloc_S:
                    case Code.Ldloc:
                    {
                        var il = ctor.Body.GetILProcessor();
                        var replacement = CreateLoadArgInstruction(desiredParamIndex + 1, ctor);
                        il.Replace(ins, replacement);
                        return true;
                    }
                    default:
                        return false;
                }
            }
            return false;
        }
        
        private static Instruction CreateLoadArgInstruction(int position, MethodDefinition ctor)
        {
            return position switch
            {
                0 => Instruction.Create(OpCodes.Ldarg_0),
                1 => Instruction.Create(OpCodes.Ldarg_1),
                2 => Instruction.Create(OpCodes.Ldarg_2),
                3 => Instruction.Create(OpCodes.Ldarg_3),
                _ => Instruction.Create(OpCodes.Ldarg, ctor.Parameters[position - 1])
            };
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
    }
}
