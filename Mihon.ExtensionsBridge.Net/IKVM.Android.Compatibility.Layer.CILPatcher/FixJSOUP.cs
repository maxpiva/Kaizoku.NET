using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

namespace IKVMLambdaPatch
{
    internal class FixJSOUP
    {
        public static bool FixAnonLambdaTree(TypeDefinition type)
        {
            var touched = FixAnonLambda(type);
            foreach (var nested in type.NestedTypes)
            {
                touched |= FixAnonLambda(nested);
            }
            return touched;
        }

        public static bool FixAnonLambda(TypeDefinition t)
        {
            if (t.FullName.Contains("__<>Anon1"))
            { 
                if (t.FullName.Contains("StringUtil"))
                {
                    var m = t.Methods.FirstOrDefault(x => x.Name == "accept" && x.Parameters.Count == 2);
                    if (m == null)
                        return false;
                    var il = m.Body.Instructions;

                    // Find the ldobj java.lang.CharSequence instruction
                    var ldobj = il.First(i =>
                        i.OpCode == OpCodes.Ldobj &&
                        ((TypeReference)i.Operand).FullName == "java.lang.CharSequence");

                    // In your listing, the sequence is:
                    // ldloca.s, ldloc.0, stfld, ldloca.s, ldobj, callvirt add(object)
                    var ldloca1 = ldobj.Previous?.Previous;      // first ldloca.s
                    var ldloc0 = ldobj.Previous;               // ldloc.0 OR (depends on exact layout)
                    var stfld = ldobj.Previous?.Previous?.Next?.Next; // safer to remove by range below

                    // More robust: remove 4 instructions immediately before ldobj plus ldobj itself,
                    // then insert ldloc.0 in place of ldobj.
                    var start = ldobj;
                    for (int k = 0; k < 4; k++) start = start.Previous; // should land on first ldloca.s

                    // Remove ldloca.s, ldloc.0, stfld, ldloca.s, ldobj
                    var cur = start;
                    var il2 = m.Body.GetILProcessor();
                  

                    for (int k = 0; k < 5; k++)
                    {
                        var next = cur.Next;
                        il2.Remove(cur);
                        cur = next;
                    }

                    // Insert ldloc.0 where the ldobj used to be (cur now points at callvirt add)
                    il2.InsertBefore(cur, Instruction.Create(OpCodes.Ldloc_0));
                    return true;
                }
            }
            return false;
        }
    }
}
