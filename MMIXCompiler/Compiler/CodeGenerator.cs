﻿using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace MMIXCompiler.Compiler;

internal class CodeGenerator
{
    public static readonly MAddr TextSegment = new(0x0000000000000000);
    public static readonly MAddr DataSegment = new(0x2000000000000000);
    public static readonly MAddr PoolSegment = new(0x4000000000000000);
    public static readonly MAddr StackSegment = new(0x6000000000000000);
    private const int AddrSize = 8;
    public const bool debug = true;

    public MAddr StaticHeapOffset;
    public readonly Dictionary<string, StaticAllocation> StaticFields = new();
    public readonly Dictionary<string, StaticAllocation> StaticStrings = new();
    public static readonly Dictionary<string, Size> TypeSizes = new();
    public readonly Dictionary<string, Size> FieldOffsets = new();

    public static readonly TypeReference primVoid;
    public static readonly TypeReference primBool;
    public static readonly TypeReference primI8;
    public static readonly TypeReference primI16;
    public static readonly TypeReference primI32;
    public static readonly TypeReference primI64;
    public static readonly TypeReference primU8;
    public static readonly TypeReference primU16;
    public static readonly TypeReference primU32;
    public static readonly TypeReference primU64;
    public static readonly TypeReference primPtr;
    public static readonly TypeReference primArr;

    static CodeGenerator()
    {
        var mscoreAssembly = AssemblyDefinition.ReadAssembly(typeof(object).Assembly.Location);
        var mxstdAssembly = AssemblyDefinition.ReadAssembly(typeof(MMIXMarker).Assembly.Location);
        var mxstdModule = mxstdAssembly.MainModule;
        var mscoreModule = mscoreAssembly.MainModule;

#pragma warning disable IDE0049
        primVoid = mscoreModule.GetType(typeof(void).FullName);
        primBool = mscoreModule.GetType(typeof(bool).FullName);
        primI8 =  mscoreModule.GetType(typeof(SByte).FullName);
        primI16 = mscoreModule.GetType(typeof(Int16).FullName);
        primI32 = mscoreModule.GetType(typeof(Int32).FullName);
        primI64 = mscoreModule.GetType(typeof(Int64).FullName);
        primU8 =  mscoreModule.GetType(typeof(Byte).FullName);
        primU16 = mscoreModule.GetType(typeof(UInt16).FullName);
        primU32 = mscoreModule.GetType(typeof(UInt32).FullName);
        primU64 = mscoreModule.GetType(typeof(UInt64).FullName);
        primPtr = mscoreModule.GetType(typeof(UIntPtr).FullName);
        primArr = mscoreModule.GetType(typeof(Array).FullName);
#pragma warning restore IDE0049
    }

    public string Compile(Stream data)
    {
        StaticHeapOffset = PoolSegment;
        StaticFields.Clear();
        StaticStrings.Clear();
        TypeSizes.Clear();

        var strb = new StringBuilder();
        strb.GenOp("", "LOC", $"#{100}");

        var asm = AssemblyDefinition.ReadAssembly(data);
        var module = asm.MainModule;

        foreach (var type in module.Types)
        {
            if (!type.IsClass)
                continue;

            foreach (var method in type.Methods)
            {
                if (!method.HasBody
                    || method.IsConstructor
                    || !method.IsIL
                    || !method.IsStatic)
                    continue;

                if (method.Body.CodeSize == 0)
                    GenerateStdLib(strb, method.Name);
                else
                    strb.Append(GenerateMethod(method));
            }
        }

        GenerateStaticContent(strb);

        //strb.GenOp("", "LOC", "Data_Segment");
        //strb.GenOp("", "GREG", "@");
        //Size dataAddress = DataSegment;
        //foreach (var staticItem in StaticHeapAllocation.OrderBy(x => x.address.Bytes))
        //{
        //	if (dataAddress != staticItem.address)
        //		strb.GenOp("", "LOC", (staticItem.address + DataSegment).ToString());
        //	strb.GenOp(staticItem.label, "OCTA", staticItem.value.ToString());
        //	dataAddress = staticItem.address + staticItem.size;
        //}

        return strb.ToString();
    }

    //public void ReserveStaticFields(TypeDefinition type)
    //{
    //	foreach (var sfld in type.Fields)
    //	{
    //		if (sfld.IsStatic)
    //		{
    //			var size = GetSize(sfld.FieldType);
    //			ReserveStaticHeap(size);
    //		}
    //	}
    //}

    public MAddr GetStaticFieldAddress(FieldDefinition field) // -> label
    {
        if (!StaticFields.TryGetValue(field.FullName, out var alloc))
        {
            var size = GetSize(field.FieldType);
            alloc = new StaticAllocation(StaticHeapOffset, size, "", 0);
            StaticFields.Add(field.FullName, alloc);
            StaticHeapOffset += size;
        }
        return alloc.Address;
    }

    public StaticAllocation GetStaticStringAddress(string str)
    {
        if (!StaticStrings.TryGetValue(str, out var alloc))
        {
            var size = Size.FromOctets(2) + Size.FromBytes((ulong)str.Length + 1UL);
            alloc = new StaticAllocation(StaticHeapOffset, size, "", 0);
            StaticStrings.Add(str, alloc);
            StaticHeapOffset += size;
        }
        return alloc;
    }

    public void ReserveStaticHeap(string fld, Size size, string label, ulong? value = null) // -> label
    {
        StaticFields.Add(fld, new StaticAllocation(StaticHeapOffset, size, label, value ?? 0));
        StaticHeapOffset += size;
    }

    public string GenerateMethod(MethodDefinition method)
    {
        var strb = new StringBuilder();

        strb.GenNop(method.Name, "() [] = ?");

        var stack = CalcStack(method);

        GenerateMethodStart(strb, stack);

        var instructions = method.Body.Instructions;
        string[] asmList = new string[instructions.Count];
        var flowQueue = new Queue<(int index, VirtualStackElem[] stack)>();

        var hasInJump = new HashSet<Instruction>();
        for (int i = 0; i < method.Body.Instructions.Count; i++)
        {
            var instruct = method.Body.Instructions[i];

            if (instruct.Operand is Instruction target)
            {
                hasInJump.Add(target);
            }
        }

        flowQueue.Enqueue((0, Array.Empty<VirtualStackElem>()));

        var blockStrb = new StringBuilder();
        while (flowQueue.Count > 0)
        {
            var (i, lstack) = flowQueue.Dequeue();

            stack.Load(lstack);
            while (i < instructions.Count)
            {
                if (asmList[i] != null)
                    break;
                blockStrb.Clear();
                var instruct = instructions[i];
                GenerateInstruction(blockStrb, stack, instruct, method, hasInJump);
                asmList[i] = blockStrb.ToString();

                if (instruct.OpCode.Code.IsUnCondJump())
                {
                    i = instructions.IndexOf((Instruction)instruct.Operand);
                }
                else if (instruct.OpCode.Code.IsCondJump())
                {
                    flowQueue.Enqueue((instructions.IndexOf((Instruction)instruct.Operand), stack.Save()));
                    i++;
                }
                else
                {
                    i++;
                }
            }
        }

        foreach (var asm in asmList.Where(x => x != null))
            strb.Append(asm);

        if (stack.DynStack.Count > 0)
            strb.GenComment("Stack not empty !!");
        strb.AppendLine();
        return strb.ToString();
    }

    public void GenerateStaticContent(StringBuilder strb)
    {
        strb.GenOp("", "LOC", "Data_Segment");

        foreach (var (str, addr) in StaticStrings)
        {
            strb.GenOp("", "LOC", $"{addr.Address.Ptr:X2}");
            // String struct
            strb.GenOp("", "OCTA", "0,0");
            // String Data
            foreach (var line in Encoding.ASCII.GetBytes(str).Concat(new byte[] { 0 }).Chunk(80 / 4))
            {
                strb.GenOp("", "BYTE", string.Join(',', line.Select(c => $"#{(int)c:X2}")));
            }
        }
    }

    public void GenerateInstruction(StringBuilder strb, VirtualStack stack, Instruction instruct, MethodDefinition method, HashSet<Instruction> hasInJump)
    {
        string lbl = "";
        if (hasInJump.Contains(instruct))
            lbl = Label(method, instruct);

        if (debug)
        {
            strb.GenComment(instruct.ToString()
                //+ " " + string.Join(",", stack.DynStack.Reverse().Select(x => x.type))
                );
        }

        var code = instruct.OpCode.Code;
        var ci = (int)code;
        switch (instruct.OpCode.Code)
        {
            case Code.Nop: strb.GenNop(lbl); break;
            case Code.Break: NotImplemented(instruct, strb); break;
            case Code.Ldarg_0:
            case Code.Ldarg_1:
            case Code.Ldarg_2:
            case Code.Ldarg_3:
            case Code.Ldarg_S:
            case Code.Ldarg:
                int ldargIndex = code > Code.Ldarg_3 ? ((ParameterReference)instruct.Operand).Index : ci - 2;
                stack.Push(method.Parameters[ldargIndex].ParameterType);
                PushStack(strb, lbl, stack, stack.Parameter, ldargIndex);
                break;
            case Code.Ldloc_0:
            case Code.Ldloc_1:
            case Code.Ldloc_2:
            case Code.Ldloc_3:
            case Code.Ldloc_S:
            case Code.Ldloc:
                int ldlocIndex = code > Code.Ldloc_3 ? ((VariableReference)instruct.Operand).Index : ci - 6;
                stack.Push(method.Body.Variables[ldlocIndex].VariableType);
                PushStack(strb, lbl, stack, stack.Locals, ldlocIndex);
                break;
            case Code.Stloc_0:
            case Code.Stloc_1:
            case Code.Stloc_2:
            case Code.Stloc_3:
            case Code.Stloc_S:
            case Code.Stloc:
                int stlocIndex = code > Code.Stloc_3 ? ((VariableReference)instruct.Operand).Index : ci - 10;
                PopStack(strb, lbl, stack, stack.Locals, stlocIndex);
                stack.Pop();
                break;
            case Code.Ldarga_S:
            case Code.Ldarga: NotImplemented(instruct, strb); break;
            case Code.Starg_S:
            case Code.Starg:
                int stargIndex = ((ParameterReference)instruct.Operand).Index;
                PopStack(strb, lbl, stack, stack.Parameter, stargIndex);
                stack.Pop();
                break;
            case Code.Ldloca_S:
            case Code.Ldloca:
                int ldlocaIndex = ((VariableReference)instruct.Operand).Index;
                stack.Push(primPtr);
                strb.GenOp(lbl, "GET", stack.Reg(), "rO");
                strb.GenOp(lbl, "ADD", stack.Reg(), stack.Reg(), (stack.Locals.Elements[ldlocaIndex].Offset.Octets * AddrSize).ToString());
                break;
            case Code.Ldnull:
                stack.Push(primPtr);
                strb.Lbl(lbl).GenLoadConst("", stack.Reg(), 0);
                break;
            case Code.Ldc_I4_M1:
            case Code.Ldc_I4_0:
            case Code.Ldc_I4_1:
            case Code.Ldc_I4_2:
            case Code.Ldc_I4_3:
            case Code.Ldc_I4_4:
            case Code.Ldc_I4_5:
            case Code.Ldc_I4_6:
            case Code.Ldc_I4_7:
            case Code.Ldc_I4_8:
                stack.Push(primI32);
                strb.GenOp(lbl, "SET", stack.Reg(), (ci - 22).ToString());
                break;
            case Code.Ldc_I4_S:
            case Code.Ldc_I4:
                stack.Push(primI32);
                strb.GenLoadConst(lbl, stack.Reg(), unchecked((ulong)Convert.ToInt64(instruct.Operand)));
                break;
            case Code.Ldc_I8:
                stack.Push(primI64);
                strb.GenLoadConst(lbl, stack.Reg(), unchecked((ulong)Convert.ToInt64(instruct.Operand)));
                break;
            case Code.Ldc_R4: NotImplemented(instruct, strb); break;
            case Code.Ldc_R8: NotImplemented(instruct, strb); break;
            case Code.Dup:
                var dupType = stack.DynStack.Peek().Type;
                var dupTypeSize = GetSize(dupType);
                StackMove(strb, stack.RegNum(), stack.RegNum() + dupTypeSize.Octets, dupTypeSize.Octets);
                stack.Push(dupType);
                break;
            case Code.Pop:
                strb.GenNop(lbl);
                stack.Pop();
                break;
            case Code.Jmp: NotImplemented(instruct, strb); break;
            case Code.Call:
                {
                    var callMethod = (MethodReference)instruct.Operand;
                    int callParamSize = callMethod.Parameters.Sum(x => GetSize(x.ParameterType).Octets);

                    int stackEnd = stack.DynEndOffset.Octets;
                    for (int i = 0; i < callMethod.Parameters.Count; i++)
                        stack.Pop();
                    int stackToCall = stack.DynEndOffset.Octets;
                    var mainReg = stackToCall.Reg(); // Main result register

                    StackMove(strb, stackToCall, stackToCall + 1, stackEnd - stackToCall);



                    strb.GenOp(lbl, "PUSHJ", mainReg, callMethod.Name);

                    stack.Push(callMethod.ReturnType);
                }
                break;
            case Code.Calli: NotImplemented(instruct, strb); break;
            case Code.Ret:
                if (stack.DoesCall)
                {
                    strb.GenOp("", "PUT", "rJ", stack.Backup.Elements[0].Offset.Octets.Reg());
                }
                if (stack.Return.Count == 0)
                {
                    if (method.Name == "Main")
                    {
                        strb.Lbl(lbl).GenLoadConst("", 255.Reg(), 0);
                        strb.GenOp("", "TRAP", "0", "Halt", "0");
                    }
                    else
                    {
                        strb.GenOp(lbl, "POP");
                    }
                }
                else
                {
                    // PUSH/POP is weird http://mmix.cs.hm.edu/doc/instructions/pushjandpopexample.html

                    strb.GenNop(lbl);

                    var stackReturn = stack.DynEndOffset.Octets - stack.Return.BlockSize;
                    StackMove(strb, stackReturn, stack.Return.BlockOffset + stack.Return.BlockSize - 1, 1); // Move the 'primary' result reg
                    StackMove(strb, stackReturn + 1, stack.Return.BlockOffset, stack.Return.BlockSize - 1);
                    strb.GenOp("", "POP", stack.Return.Elements[0].Size.Octets.ToString(), "0");
                }
                for (int i = 0; i < stack.Return.Count; i++)
                    stack.Pop();
                // TODO CHECK STACK == 0
                break;
            case Code.Br_S:
            case Code.Br:
                strb.GenOp(lbl, "JMP", Label(method, (Instruction)instruct.Operand));
                break;
            case Code.Beq_S:
            case Code.Beq:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BZ", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Bge_S:
            case Code.Bge:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BNN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Bgt_S:
            case Code.Bgt:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Ble_S:
            case Code.Ble:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BNP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Blt_S:
            case Code.Blt:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Bne_Un_S:
            case Code.Bne_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BNZ", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Bge_Un_S:
            case Code.Bge_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BNN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Bgt_Un_S:
            case Code.Bgt_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Ble_Un_S:
            case Code.Ble_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BNP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Blt_Un_S:
            case Code.Blt_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "BN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
                stack.Pop(); stack.Pop();
                break;
            case Code.Brfalse_S:
            case Code.Brfalse:
                strb.GenOp(lbl, "BZ", stack.Reg(), Label(method, (Instruction)instruct.Operand));
                stack.Pop();
                break;
            case Code.Brtrue_S:
            case Code.Brtrue:
                strb.GenOp(lbl, "BNZ", stack.Reg(), Label(method, (Instruction)instruct.Operand));
                stack.Pop();
                break;
            case Code.Switch: NotImplemented(instruct, strb); break;
            case Code.Ldind_I1:
            case Code.Ldind_U1:
            case Code.Ldind_I2:
            case Code.Ldind_U2:
            case Code.Ldind_I4:
            case Code.Ldind_U4:
            case Code.Ldind_I8:
                {
                    var reg = stack.PopReg();
                    stack.Push(GetFamiliarType(code));
                    strb.GenOp(lbl, "LD" + GetFamiliar(code), stack.Reg(), reg);
                    break;
                }
            case Code.Ldind_I: goto case Code.Ldind_I8;
            case Code.Ldind_R4: NotImplemented(instruct, strb); break;
            case Code.Ldind_R8: NotImplemented(instruct, strb); break;
            case Code.Ldind_Ref: goto case Code.Ldind_I;
            case Code.Stind_I1:
            case Code.Stind_I2:
            case Code.Stind_I4:
            case Code.Stind_I8:
                {
                    var reg1 = stack.PopReg();
                    var reg2 = stack.PopReg();
                    strb.GenOp(lbl, "ST" + GetFamiliar(code), reg1, reg2);
                    break;
                }
            case Code.Stind_I: goto case Code.Stind_I8;
            case Code.Stind_R4: NotImplemented(instruct, strb); break;
            case Code.Stind_R8: NotImplemented(instruct, strb); break;
            case Code.Stind_Ref: goto case Code.Stind_I;
            case Code.Add:
                strb.GenOp(lbl, "ADD", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Sub:
                strb.GenOp(lbl, "SUB", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Mul:
                strb.GenOp(lbl, "MUL", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Div:
                strb.GenOp(lbl, "DIV", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Div_Un:
                strb.GenOp(lbl, "DIVU", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Rem:
                strb.GenOp(lbl, "DIV", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "GET", stack.Reg(1), "rR");
                stack.Pop();
                break;
            case Code.Rem_Un:
                strb.GenOp(lbl, "DIVU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "GET", stack.Reg(1), "rR");
                stack.Pop();
                break;
            case Code.And:
                strb.GenOp(lbl, "AND", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Or:
                strb.GenOp(lbl, "OR", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Xor:
                strb.GenOp(lbl, "XOR", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Shl:
                strb.GenOp(lbl, "SL", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Shr:
                strb.GenOp(lbl, "SR", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Shr_Un:
                strb.GenOp(lbl, "SRU", stack.Reg(1), stack.Reg(1), stack.Reg());
                stack.Pop();
                break;
            case Code.Neg:
                strb.GenOp(lbl, "NEG", stack.Reg(), "0", stack.Reg());
                stack.Pop();
                break;
            case Code.Not:
                strb.GenOp(lbl, "XOR", stack.Reg(), stack.Reg(), 0xFFFF_FFFF_FFFF_FFFFL.ToString());
                stack.Pop();
                break;
            case Code.Conv_I1:
                strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFL.ToString());
                stack.Pop();
                stack.Push(primI8);
                break;
            case Code.Conv_I2:
                strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFFL.ToString());
                stack.Pop();
                stack.Push(primI16);
                break;
            case Code.Conv_I4:
                //strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFF_FFFFL.ToString());
                stack.Pop();
                stack.Push(primI32);
                break;
            case Code.Conv_I8:
                strb.GenNop(lbl);
                stack.Pop();
                stack.Push(primI64);
                break;
            case Code.Conv_U1:
                strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFL.ToString());
                stack.Pop();
                stack.Push(primU8);
                break;
            case Code.Conv_U2:
                strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFFL.ToString());
                stack.Pop();
                stack.Push(primU16);
                break;
            case Code.Conv_U4:
                //strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFF_FFFFL.ToString());
                stack.Pop();
                stack.Push(primU32);
                break;
            case Code.Conv_U8:
                strb.GenNop(lbl);
                stack.Pop();
                stack.Push(primU64);
                break;
            case Code.Conv_I: goto case Code.Conv_I8;
            case Code.Conv_U: goto case Code.Conv_U8;
            case Code.Conv_R4: NotImplemented(instruct, strb); break;
            case Code.Conv_R8: NotImplemented(instruct, strb); break;
            case Code.Callvirt: NotImplemented(instruct, strb); break;
            case Code.Cpobj: NotImplemented(instruct, strb); break;
            case Code.Ldobj: NotImplemented(instruct, strb); break;
            case Code.Ldstr:
                {
                    var strAlloc = GetStaticStringAddress((string)instruct.Operand);

                    stack.Push(primArr);
                    var regPtr = stack.Reg(0, 0);
                    var regLength = stack.Reg(0, 1);

                    strb.GenLoadConst(lbl, regPtr, strAlloc.Address.Ptr);
                }
                break;
            case Code.Newobj: NotImplemented(instruct, strb); break;
            case Code.Castclass: NotImplemented(instruct, strb); break;
            case Code.Isinst: NotImplemented(instruct, strb); break;
            case Code.Conv_R_Un: NotImplemented(instruct, strb); break;
            case Code.Unbox: NotImplemented(instruct, strb); break;
            case Code.Throw: NotImplemented(instruct, strb); break;
            case Code.Ldfld:
            case Code.Ldsfld:
                {
                    var fld = (FieldDefinition)instruct.Operand;
                    int fieldOffset;
                    Reg sourceAddressReg;

                    if (code == Code.Ldsfld)
                    {
                        stack.Push(primU64);
                        sourceAddressReg = stack.Reg();
                        stack.Pop();
                        var staticAddr = GetStaticFieldAddress(fld);
                        strb.GenLoadConst(lbl, sourceAddressReg, staticAddr.Ptr);
                        fieldOffset = 0;
                    }
                    else
                    {
                        sourceAddressReg = stack.PopReg();
                        fieldOffset = GetFldOffset(fld).Octets;
                    }

                    stack.Push(fld.FieldType);

                    var declType = fld.DeclaringType;
                    if (declType.IsPointer || declType.IsClass)
                    {
                        var octs = GetSize(fld.FieldType).Octets;
                        for (int i = octs - 1; i >= 0; i--)
                        {
                            strb.GenOp(lbl, "LDOU", stack.Reg(0, i), sourceAddressReg, ((fieldOffset + i) * AddrSize).ToString());
                        }
                    }
                    else
                    {
                        NotImplemented(instruct, strb);
                    }
                }
                break;
            case Code.Ldflda: NotImplemented(instruct, strb); break;
            case Code.Stfld:
            case Code.Stsfld:
                /* ref: Store field
                 * (-1) object
                 * ( 0) value [x:size]
                 * 
                 * stat: Store field
                 * ( 0) value [x:size]
                 * ( 1) tmp: static address
                 */
                {
                    var fld = (FieldDefinition)instruct.Operand;
                    int regNum;
                    int fieldOffset;
                    Reg targetAddressReg;

                    if (code == Code.Stsfld)
                    {
                        var staticAddr = GetStaticFieldAddress(fld);
                        stack.Push(primU64);
                        targetAddressReg = stack.Reg(0);
                        regNum = 1;
                        strb.GenLoadConst(lbl, targetAddressReg, staticAddr.Ptr);
                        fieldOffset = 0;
                    }
                    else
                    {
                        targetAddressReg = stack.Reg(1);
                        regNum = 0;
                        fieldOffset = GetFldOffset(fld).Octets;
                    }

                    var declType = fld.DeclaringType;
                    if (declType.IsPointer || declType.IsClass)
                    {
                        for (int i = 0; i < GetSize(fld.FieldType).Octets; i++)
                        {
                            strb.GenOp(lbl, "STOU", stack.Reg(regNum, i), targetAddressReg, ((fieldOffset + i) * AddrSize).ToString());
                        }
                    }
                    else
                    {
                        NotImplemented(instruct, strb);
                    }
                    stack.Pop(); stack.Pop();
                }
                break;
            case Code.Ldsflda:
                {
                    var fld = (FieldDefinition)instruct.Operand;
                    var staticAddr = GetStaticFieldAddress(fld);
                    stack.Push(primU64);
                    strb.GenLoadConst(lbl, stack.Reg(), staticAddr.Ptr);
                }
                break;
            case Code.Stobj: NotImplemented(instruct, strb); break;
            case Code.Conv_Ovf_I1_Un: /* TODO TRIP */ goto case Code.Conv_I1;
            case Code.Conv_Ovf_I2_Un: /* TODO TRIP */ goto case Code.Conv_I2;
            case Code.Conv_Ovf_I4_Un: /* TODO TRIP */ goto case Code.Conv_I4;
            case Code.Conv_Ovf_I8_Un: /* TODO TRIP */ goto case Code.Conv_I8;
            case Code.Conv_Ovf_U1_Un: /* TODO TRIP */ goto case Code.Conv_U1;
            case Code.Conv_Ovf_U2_Un: /* TODO TRIP */ goto case Code.Conv_U2;
            case Code.Conv_Ovf_U4_Un: /* TODO TRIP */ goto case Code.Conv_U4;
            case Code.Conv_Ovf_U8_Un: /* TODO TRIP */ goto case Code.Conv_U8;
            case Code.Conv_Ovf_I_Un:  /* TODO TRIP */ goto case Code.Conv_I;
            case Code.Conv_Ovf_U_Un:  /* TODO TRIP */ goto case Code.Conv_U;
            case Code.Box: NotImplemented(instruct, strb); break;
            /* Array read access
            * (0) length
            * ->
            * (0) array [ 0:ptr 1:len ]
            */
            case Code.Newarr:
                {
                    var regBackupAllocCount = stack.Reg();
                    stack.Push(primU64);
                    var regAllocCount = stack.Reg();
                    stack.Push(primU64);
                    var regAllocBytes = stack.Reg();
                    stack.Pop(); stack.Pop(); stack.Pop();

                    strb.GenOp(lbl, "MUL", regAllocBytes, regBackupAllocCount, GetSize((TypeReference)instruct.Operand).Bytes.ToString());
                    strb.GenOp("", "PUSHJ", regAllocCount, "malloc");
                    strb.GenOp("", "SET", regAllocBytes, regBackupAllocCount);
                    strb.GenOp("", "SET", regBackupAllocCount, regAllocCount);
                    strb.GenOp("", "SET", regAllocCount, regAllocBytes);
                    stack.Push(primArr);
                }
                break;
            case Code.Ldlen:
                {
                    var reg = stack.PopReg();
                    stack.Push(primU64);
                    strb.GenOp(lbl, "SUB", reg, reg, "8");
                    strb.GenOp("", "LDO", reg, reg);
                    break;
                }
            case Code.Ldelema:
                {
                    var reg1 = stack.PopReg();
                    //reg2 = stack.PopReg(); // array
                    //stack.PushManual(1); // TODO

                    //TODO
                    //int ldelemaSize = GetSize((TypeReference)instruct.Operand);
                    break;
                }
            /* Array read access
             * (-2) array [ 0:ptr 1:len ]
             * ( 0) index
             */
            case Code.Ldelem_I1:
            case Code.Ldelem_U1:
            case Code.Ldelem_I2:
            case Code.Ldelem_U2:
            case Code.Ldelem_I4:
            case Code.Ldelem_U4:
            case Code.Ldelem_I8:
                {
                    var regIndex = stack.PopReg(); // index
                    var (regArray, typArray) = stack.PopRegTyp(); // array
                    stack.Push(typArray.GetElementType());
                    strb.GenOp(lbl, "LD" + GetFamiliar(code), stack.Reg(), regArray, regIndex);
                    break;
                }
            case Code.Ldelem_I: goto case Code.Ldelem_I8;
            case Code.Ldelem_R4: NotImplemented(instruct, strb); break;
            case Code.Ldelem_R8: NotImplemented(instruct, strb); break;
            case Code.Ldelem_Ref: NotImplemented(instruct, strb); break;
            /* Array write access
             * (-3) array [ 0:ptr 1:len ]
             * (-1) index
             * ( 0) value
             */
            case Code.Stelem_I: goto case Code.Stelem_I8;
            case Code.Stelem_I1:
            case Code.Stelem_I2:
            case Code.Stelem_I4:
            case Code.Stelem_I8:
                {
                    var reg1 = stack.PopReg(); // value
                    var reg2 = stack.PopReg(); // index
                    var reg3 = stack.PopReg(); // array [ 0:ptr 1:len ]
                    strb.GenOp(lbl, "ST" + GetFamiliar(code), reg1, reg3, reg2);
                    break;
                }
            case Code.Stelem_R4: NotImplemented(instruct, strb); break;
            case Code.Stelem_R8: NotImplemented(instruct, strb); break;
            case Code.Stelem_Ref: NotImplemented(instruct, strb); break;
            case Code.Ldelem_Any: NotImplemented(instruct, strb); break;
            case Code.Stelem_Any: NotImplemented(instruct, strb); break;
            case Code.Unbox_Any: NotImplemented(instruct, strb); break;
            case Code.Conv_Ovf_I1: /* TODO TRIP */ goto case Code.Conv_I1;
            case Code.Conv_Ovf_U1: /* TODO TRIP */ goto case Code.Conv_I2;
            case Code.Conv_Ovf_I2: /* TODO TRIP */ goto case Code.Conv_I4;
            case Code.Conv_Ovf_U2: /* TODO TRIP */ goto case Code.Conv_I8;
            case Code.Conv_Ovf_I4: /* TODO TRIP */ goto case Code.Conv_U1;
            case Code.Conv_Ovf_U4: /* TODO TRIP */ goto case Code.Conv_U2;
            case Code.Conv_Ovf_I8: /* TODO TRIP */ goto case Code.Conv_U4;
            case Code.Conv_Ovf_U8: /* TODO TRIP */ goto case Code.Conv_U8;
            case Code.Conv_Ovf_I: /* TODO TRIP */ goto case Code.Conv_I;
            case Code.Conv_Ovf_U: /* TODO TRIP */ goto case Code.Conv_U;
            case Code.Refanyval: NotImplemented(instruct, strb); break;
            case Code.Ckfinite: NotImplemented(instruct, strb); break;
            case Code.Mkrefany: NotImplemented(instruct, strb); break;
            case Code.Ldtoken: NotImplemented(instruct, strb); break;
            case Code.Add_Ovf: NotImplemented(instruct, strb); break;
            case Code.Add_Ovf_Un: NotImplemented(instruct, strb); break;
            case Code.Mul_Ovf: NotImplemented(instruct, strb); break;
            case Code.Mul_Ovf_Un: NotImplemented(instruct, strb); break;
            case Code.Sub_Ovf: NotImplemented(instruct, strb); break;
            case Code.Sub_Ovf_Un: NotImplemented(instruct, strb); break;
            case Code.Endfinally: NotImplemented(instruct, strb); break;
            case Code.Leave: NotImplemented(instruct, strb); break;
            case Code.Leave_S: NotImplemented(instruct, strb); break;
            case Code.Arglist: NotImplemented(instruct, strb); break;
            case Code.Ceq:
                // TODO convert to popreg
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "ZSZ", stack.Reg(1), stack.Reg(1), "1");
                stack.Pop(); stack.Pop();
                stack.Push(primBool);
                break;
            case Code.Cgt:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "ZSP", stack.Reg(1), stack.Reg(1), "1");
                stack.Pop(); stack.Pop();
                stack.Push(primBool);
                break;
            case Code.Cgt_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "ZSP", stack.Reg(1), stack.Reg(1), "1");
                stack.Pop(); stack.Pop();
                stack.Push(primBool);
                break;
            case Code.Clt:
                strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "ZSN", stack.Reg(1), stack.Reg(1), "1");
                stack.Pop(); stack.Pop();
                stack.Push(primBool);
                break;
            case Code.Clt_Un:
                strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
                strb.GenOp("", "ZSN", stack.Reg(1), stack.Reg(1), "1");
                stack.Pop(); stack.Pop();
                stack.Push(primBool);
                break;
            case Code.Ldftn: NotImplemented(instruct, strb); break;
            case Code.Ldvirtftn: NotImplemented(instruct, strb); break;
            case Code.Localloc: NotImplemented(instruct, strb); break;
            case Code.Endfilter: NotImplemented(instruct, strb); break;
            case Code.Unaligned: NotImplemented(instruct, strb); break;
            case Code.Volatile: NotImplemented(instruct, strb); break;
            case Code.Tail: NotImplemented(instruct, strb); break;
            case Code.Initobj:
                {
                    var (reg, typ) = stack.PopRegTyp();
                    var initSize = GetSize(typ).Octets;
                    for (int i = 0; i < initSize; i++)
                        strb.GenOp(lbl, "STCO", "0", reg, (i * AddrSize).ToString());
                    break;
                }
            case Code.Constrained: NotImplemented(instruct, strb); break;
            case Code.Cpblk: NotImplemented(instruct, strb); break;
            case Code.Initblk: NotImplemented(instruct, strb); break;
            case Code.No: NotImplemented(instruct, strb); break;
            case Code.Rethrow: NotImplemented(instruct, strb); break;
            case Code.Sizeof:
                var _sizeof = GetSize((TypeReference)instruct.Operand);
                stack.Push(primU64);

                strb.GenOp(lbl, "SET", stack.Reg(), _sizeof.Bytes8.ToString());
                break;
            case Code.Refanytype: NotImplemented(instruct, strb); break;
            case Code.Readonly: NotImplemented(instruct, strb); break;
            default: NotImplemented(instruct, strb); break;
        }
    }

    public static void PushStack(StringBuilder strb, string lbl, VirtualStack stack, VirtualStackBlock block, int index)
    {
        int size = block.Elements[index].Size.Octets;
        for (int i = 0; i < size; i++)
        {
            strb.GenOp(i == 0 ? lbl : "", "SET",
                stack.Reg(0, i),
                block.Reg(index, i));
        }
    }

    public static void PopStack(StringBuilder strb, string lbl, VirtualStack stack, VirtualStackBlock block, int index)
    {
        int size = block.Elements[index].Size.Octets;
        for (int i = 0; i < size; i++)
        {
            strb.GenOp(i == 0 ? lbl : "", "SET",
                block.Reg(index, i),
                stack.Reg(0, i));
        }
    }

    public static void StackMove(StringBuilder strb, int from, int to, int length)
    {
        if (length <= 0 || from == to)
            return;

        if (to > from)
        {
            for (int i = 0; i < length; i++)
            {
                strb.GenOp("", "SET", (to + length - (i + 1)).Reg(), (from + length - (i + 1)).Reg());
            }
        }
        else
        {
            for (int i = 0; i < length; i++)
            {
                strb.GenOp("", "SET", (to + i).Reg(), (from + i).Reg());
            }
        }
    }

    private static VirtualStack CalcStack(MethodDefinition method)
    {
        var stack = new VirtualStack
        {
            DoesCall = method.Body.Instructions.Any(op => op.OpCode.Code is Code.Call or Code.Calli or Code.Callvirt),
        };

        int cumIndex = 0;
        int cumOffset = 0;
        AccumulateStack(ref cumIndex, ref cumOffset, stack.Return, GetSize(method.ReturnType) == Size.Zero ? Array.Empty<TypeReference>() : new[] { method.ReturnType }, "RET");
        AccumulateStack(ref cumIndex, ref cumOffset, stack.Parameter, method.Parameters.Select(x => x.ParameterType), "ARG");
        AccumulateStack(ref cumIndex, ref cumOffset, stack.Backup, stack.DoesCall ? new[] { primPtr } : Array.Empty<TypeReference>(), "BAK");
        AccumulateStack(ref cumIndex, ref cumOffset, stack.Locals, method.Body.Variables.Select(x => x.VariableType), "LOC");

        stack.FixedEndIndex = cumIndex;
        stack.FixedEndOffset = cumOffset;
        stack.FixedStack = stack.BlockList.SelectMany(block => block.Elements).ToArray();

        return stack;
    }

    private static void AccumulateStack(ref int cumIndex, ref int cumOffset, VirtualStackBlock block, IEnumerable<TypeReference> elements, string desc)
    {
        block.IndexStart = cumIndex;
        block.BlockOffset = cumOffset;
        int localSize = 0;
        var buildList = new List<VirtualStackElem>();

        foreach (var elem in elements)
        {
            var size = GetSize(elem);
            buildList.Add(new(Offset: Size.FromOctets(cumOffset), Size: size, elem, desc));

            cumOffset += size.Octets;
            localSize += size.Octets;
            cumIndex++;
        }

        block.Elements = buildList.ToArray();
        block.BlockSize = localSize;
    }

    public static Size GetSize(TypeReference type)
    {
        if (type.FullName == "System.Void")
            return Size.Zero;
        if (type.FullName == "System.Array" || type.IsArray)
            return Size.FromBytes(AddrSize * 2);
        if (type.FullName == "System.String")
            return Size.FromBytes(AddrSize * 2);
        if (type.IsPointer || type.Equals(primPtr))
            return Size.FromBytes(AddrSize);
        if (type.IsPinned)
        {
            var ptype = (PinnedType)type;
            return GetSize(ptype.ElementType);
        }
        if (type.IsByReference)
            return Size.FromBytes(AddrSize);
        if (type.FullName == "System.Boolean")
            return Size.FromBytes(1);
        if (type.FullName == "System.Byte")
            return Size.FromBytes(1);
        if (type.FullName == "System.SByte")
            return Size.FromBytes(1);
        if (type.FullName == "System.Int16")
            return Size.FromBytes(2);
        if (type.FullName == "System.UInt16")
            return Size.FromBytes(2);
        if (type.FullName == "System.Int32")
            return Size.FromBytes(4);
        if (type.FullName == "System.UInt32")
            return Size.FromBytes(4);
        if (type.FullName == "System.Int64")
            return Size.FromBytes(8);
        if (type.FullName == "System.UInt64")
            return Size.FromBytes(8);
        if (!type.IsPrimitive && type.IsDefinition)
        {
            if (!type.IsValueType)
                return Size.FromBytes(AddrSize);

            if (!TypeSizes.TryGetValue(type.FullName, out var size))
            {
                var dtype = (TypeDefinition)type;
                size = GetClassSize(dtype);
                TypeSizes[type.FullName] = size;
            }
            return size;
        }
        throw new Exception("Unknown size");
        //return Size.FromOctets(1);
    }

    public static Size GetClassSize(TypeDefinition type)
    {
        var size = Size.Zero;
        foreach (var fld in type.Fields)
        {
            if (fld.IsStatic) continue;
            size += GetSize(fld.FieldType);
        }
        return size;
    }

    public Size GetFldOffset(FieldDefinition fld)
    {
        if (!FieldOffsets.TryGetValue(fld.FullName, out var size))
        {
            var offset = Size.Zero;
            foreach (var tfld in fld.DeclaringType.Fields)
            {
                if (tfld.IsStatic) continue;
                FieldOffsets.Add(tfld.FullName, offset);
                offset += GetSize(tfld.FieldType);
            }
            size = FieldOffsets[fld.FullName];
        }
        return size;
    }

    private static void NotImplemented(Instruction instruction, StringBuilder strb)
    {
        strb.Append("% Unknown: ").AppendLine(instruction.OpCode.Name);
        throw new NotImplementedException();
    }

    public static string Label(MethodDefinition method, Instruction instruction)
        => $"{method.Name}_{instruction.Offset}";

    private static string GetFamiliar(Code code)
    {
        string name = code.ToString().ToUpper();
        if (name.Contains("_U1")) return "BU";
        if (name.Contains("_U2")) return "WU";
        if (name.Contains("_U4")) return "TU";
        if (name.Contains("_U8")) return "OU";
        if (name.Contains("_I1")) return "B";
        if (name.Contains("_I2")) return "W";
        if (name.Contains("_I4")) return "T";
        if (name.Contains("_I8")) return "O";

        //if (name.Contains("_U")) return "OU";
        //if (name.Contains("_I")) return "O";
        throw new InvalidOperationException();
    }

    private static TypeReference GetFamiliarType(Code code)
    {
        string name = code.ToString().ToUpper();
        if (name.Contains("_U1")) return primU8;
        if (name.Contains("_U2")) return primU16;
        if (name.Contains("_U4")) return primU32;
        if (name.Contains("_U8")) return primU64;
        if (name.Contains("_I1")) return primI8;
        if (name.Contains("_I2")) return primI16;
        if (name.Contains("_I4")) return primI32;
        if (name.Contains("_I8")) return primI64;

        //if (name.Contains("_U")) return "OU";
        //if (name.Contains("_I")) return "O";
        throw new InvalidOperationException();
    }

    private static void GenerateStdLib(StringBuilder strb, string name)
    {
        switch (name)
        {
            case "cread":
                strb.GenNop("cread"); // 0,1: array
                strb.GenOp("", "GET", 2.Reg(), "rJ");
                strb.GenOp("", "SET", 4.Reg(), GetSize(primArr).Bytes8.ToString());
                strb.GenOp("", "PUSHJ", 3.Reg(), "malloc");
                strb.GenOp("", "STOU", 0.Reg(), 3.Reg(), "0");
                strb.GenOp("", "STOU", 1.Reg(), 3.Reg(), "8");
                strb.GenOp("", "SET", 255.Reg(), 3.Reg());
                strb.GenOp("", "TRAP", "0", "Fgets", "StdIn");
                strb.GenOp("", "SET", 4.Reg(), 3.Reg());
                strb.GenOp("", "PUSHJ", 3.Reg(), "free");
                strb.GenOp("", "PUT", "rJ", 2.Reg());
                strb.GenOp("", "POP");
                break;

            case "cwrite":
                strb.GenNop("cwrite"); // 0: arrptr
                strb.GenOp("", "SET", 255.Reg(), 0.Reg());
                strb.GenOp("", "TRAP", "0", "Fputs", "StdOut");
                strb.GenOp("", "POP");
                break;

            case "delarr":
                strb.GenNop("delarr"); // 0,1: array
                strb.GenOp("", "GET", 1.Reg(), "rJ");
                strb.GenOp("", "SET", 3.Reg(), 0.Reg());
                strb.GenOp("", "PUSHJ", 2.Reg(), "free");
                strb.GenOp("", "PUT", "rJ", 1.Reg());
                strb.GenOp("", "POP");
                break;

            case "read_file": // (filename, mode)
                /*
                {
                    BufferPtr -> 
                        {

                        }
                    TextRead
                }
                 */
                strb.GenNop("delarr"); // 0,1: array 2:mode
                strb.GenOp("", "GET", 1.Reg(), "rJ");



                strb.GenOp("", "PUT", "rJ", 1.Reg());
                strb.GenOp("", "POP");
                break;

            default:
                throw new InvalidOperationException();
        }
        strb.AppendLine();
    }

    private static void GenerateMethodStart(StringBuilder strb, VirtualStack stack)
    {
        if (stack.DoesCall)
        {
            strb.GenOp("", "GET", stack.Backup.Elements[0].Offset.Octets.Reg(), "rJ");
        }

        // moves incomming parameter (offset 0) into the reserved parameter block
        StackMove(strb, 0, stack.Parameter.BlockOffset, stack.Parameter.BlockSize);
    }
}

