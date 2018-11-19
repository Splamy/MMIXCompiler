using Mono.Cecil;
using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MMIXCompiler
{
	internal class Compiler
	{
		public static readonly Size TextSegment = Size.FromBytes(0x0000000000000000);
		public static readonly Size DataSegment = Size.FromBytes(0x2000000000000000);
		public static readonly Size PoolSegment = Size.FromBytes(0x4000000000000000);
		public static readonly Size StackSegment = Size.FromBytes(0x6000000000000000);
		private const int AddrSize = 8;
		public const bool debug = true;

		public Size StaticHeapUsed;
		public List<(Size address, Size size, string label, ulong value)> StaticHeapAllocation
			= new List<(Size address, Size size, string label, ulong value)>();
		public static Dictionary<string, string> StaticFields = new Dictionary<string, string>();
		public static Dictionary<string, Size> TypeSizes = new Dictionary<string, Size>();
		public static Dictionary<string, Size> FieldOffsets = new Dictionary<string, Size>();

		public static TypeReference primBool;
		public static TypeReference primI8;
		public static TypeReference primI16;
		public static TypeReference primI32;
		public static TypeReference primI64;
		public static TypeReference primU8;
		public static TypeReference primU16;
		public static TypeReference primU32;
		public static TypeReference primU64;
		public static TypeReference primPtr;
		public static TypeReference primArr;

		public string Compile(string code)
		{
			StaticHeapUsed = Size.FromBytes(100);
			StaticHeapAllocation.Clear();
			TypeSizes.Clear();

			using (var stream = File.OpenRead("TestCode.dll"))
			{
				return LoadBinData(stream);
			}
		}

		public string LoadBinData(Stream data)
		{
			var strb = new StringBuilder();

			var asm = AssemblyDefinition.ReadAssembly(data);
			var module = asm.MainModule;

			var stdType = module.Types.FirstOrDefault(x => x.Name == "MMIXTYP");

			primBool = stdType.Fields.FirstOrDefault(x => x.Name == "pBool").FieldType;
			primI8 = stdType.Fields.FirstOrDefault(x => x.Name == "pI8").FieldType;
			primI16 = stdType.Fields.FirstOrDefault(x => x.Name == "pI16").FieldType;
			primI32 = stdType.Fields.FirstOrDefault(x => x.Name == "pI32").FieldType;
			primI64 = stdType.Fields.FirstOrDefault(x => x.Name == "pI64").FieldType;
			primU8 = stdType.Fields.FirstOrDefault(x => x.Name == "pU8").FieldType;
			primU16 = stdType.Fields.FirstOrDefault(x => x.Name == "pU16").FieldType;
			primU32 = stdType.Fields.FirstOrDefault(x => x.Name == "pU32").FieldType;
			primU64 = stdType.Fields.FirstOrDefault(x => x.Name == "pU64").FieldType;
			primPtr = stdType.Fields.FirstOrDefault(x => x.Name == "pPtr").FieldType;
			primArr = stdType.Fields.FirstOrDefault(x => x.Name == "pArr").FieldType;

			foreach (var type in module.Types)
			{
				if (!type.IsClass)
					continue;

				if (type.Name == "MMIXTYP")
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

			var content = strb.ToString();
			strb.Clear();

			strb.GenOp("", "LOC", $"#{StaticHeapUsed.Bytes8}");
			strb.Append(content);

			strb.GenOp("", "LOC", "Data_Segment");
			strb.GenOp("", "GREG", "@");
			Size dataAddress = DataSegment;
			foreach (var staticItem in StaticHeapAllocation.OrderBy(x => x.address.Bytes))
			{
				if (dataAddress != staticItem.address)
					strb.GenOp("", "LOC", (staticItem.address + DataSegment).ToString());
				strb.GenOp(staticItem.label, "OCTA", staticItem.value.ToString());
				dataAddress = staticItem.address + staticItem.size;
			}

			return strb.ToString();
		}

		public void ReserveStaticFields(TypeDefinition type)
		{
			foreach (var sfld in type.Fields)
			{
				if (sfld.IsStatic)
				{
					var size = GetSize(sfld.FieldType);
					ReserveStaticHeap(size);
				}
			}
		}

		public string ReserveStaticHeap(Size size, ulong? value = null) // -> label
		{
			var label = "mem_" + StaticHeapUsed.Bytes.ToString("X2");
			StaticHeapAllocation.Add(label, (StaticHeapUsed, value ?? 0));
			StaticHeapUsed += size;
			return label;
		}

		public string GenerateMethod(MethodDefinition method)
		{
			var strb = new StringBuilder();

			strb.GenNopCom(method.Name, "() [] = ?");

			var stack = CalcStack(method);

			GenerateMethodStart(strb, stack);

			bool doesCall = false;

			var instructions = method.Body.Instructions;
			string[] asmList = new string[instructions.Count];
			var flowQueue = new Queue<(int index, Stack.StackElem[] stack)>();

			var hasInJump = new HashSet<Instruction>();
			for (int i = 0; i < method.Body.Instructions.Count; i++)
			{
				var instruct = method.Body.Instructions[i];

				if (instruct.Operand is Instruction target)
				{
					hasInJump.Add(target);
				}

				var code = instruct.OpCode.Code;
				if (code == Code.Call)
					doesCall = true;
			}

			flowQueue.Enqueue((0, new Stack.StackElem[0]));

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
						i = instructions.IndexOf(instruct.Operand as Instruction);
					}
					else if (instruct.OpCode.Code.IsCondJump())
					{
						flowQueue.Enqueue((instructions.IndexOf(instruct.Operand as Instruction), stack.Save()));
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
				strb.GenCom("Stack not empty !!");
			strb.AppendLine();
			return strb.ToString();
		}

		public void GenerateInstruction(StringBuilder strb, Stack stack, Instruction instruct, MethodDefinition method, HashSet<Instruction> hasInJump)
		{
			string reg1, reg2, reg3;
			TypeReference typ1, typ2, typ3;
			string lbl = "";
			if (hasInJump.Contains(instruct))
				lbl = Label(method, instruct);

			if (debug)
			{
				strb.GenCom(instruct.ToString()
					//+ " " + string.Join(",", stack.DynStack.Reverse().Select(x => x.type))
					);
			}

			var code = instruct.OpCode.Code;
			var ci = (int)code;
			switch (instruct.OpCode.Code)
			{
			case Code.Nop: strb.GenNop(lbl); break;
			case Code.Break: Nop(instruct, strb); break;
			case Code.Ldarg_0:
			case Code.Ldarg_1:
			case Code.Ldarg_2:
			case Code.Ldarg_3:
			case Code.Ldarg_S:
			case Code.Ldarg:
				int ldargIndex = code > Code.Ldarg_3 ? ((VariableReference)instruct.Operand).Index : ci - 2;
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
			case Code.Ldarga: Nop(instruct, strb); break;
			case Code.Starg_S:
			case Code.Starg:
				int stargIndex = ((VariableReference)instruct.Operand).Index;
				PopStack(strb, lbl, stack, stack.Parameter, stargIndex);
				stack.Pop();
				break;
			case Code.Ldloca_S:
			case Code.Ldloca:
				int ldlocaIndex = ((VariableReference)instruct.Operand).Index;
				stack.Push(primPtr);
				strb.GenOp(lbl, "GET", stack.Reg(), "rO");
				strb.GenOp(lbl, "ADD", stack.Reg(), stack.Reg(), (stack.Locals.ElementOffset[ldlocaIndex] * AddrSize).ToString());
				break;
			case Code.Ldnull:
				stack.Push(primPtr);
				strb.GenOp(lbl, "SET", stack.Reg(), "0");
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
				strb.GenOp(lbl, "SET", stack.Reg(), instruct.Operand.ToString());
				break;
			case Code.Ldc_I8:
				stack.Push(primI64);
				strb.GenOp(lbl, "SET", stack.Reg(), instruct.Operand.ToString()); // todo: only 16 bit, make static global when >2^16
				break;
			case Code.Ldc_R4: Nop(instruct, strb); break;
			case Code.Ldc_R8: Nop(instruct, strb); break;
			case Code.Dup:
				var dupTypeSize = GetSize(stack.DynStack.Peek().type);
				StackMove(strb, stack.RegNum(), stack.RegNum() + dupTypeSize.Octets, dupTypeSize.Octets);
				break;
			case Code.Pop:
				strb.GenNop(lbl);
				stack.Pop();
				break;
			case Code.Jmp: Nop(instruct, strb); break;
			case Code.Call:
				var callMethod = (MethodReference)instruct.Operand;
				int callParamSize = callMethod.Parameters.Sum(x => GetSize(x.ParameterType).Octets);
				int callStart = stack.RegNum() - callParamSize;
				StackMove(strb, callStart + 1, callStart + 2, callParamSize);
				reg1 = (callStart + 1).Reg();
				for (int i = 0; i < callMethod.Parameters.Count; i++)
					stack.Pop();
				strb.GenOp(lbl, "PUSHJ", reg1, callMethod.Name);
				stack.Push(callMethod.ReturnType);
				break;
			case Code.Calli: Nop(instruct, strb); break;
			case Code.Ret:
				if (stack.Return.Count <= 0)
				{
					if (method.Name == "Main")
					{
						strb.GenOp(lbl, "SET", 255.Reg(), "0");
						strb.GenOp("", "TRAP", "0", "Halt", "0");
					}
					else
					{
						strb.GenOp(lbl, "POP");
					}
				}
				else
				{
					strb.GenNop(lbl);
					// we have to copy the return registers this way (s = stackptr)
					// addr: value <- from
					//    0:     1 <- s-3
					//    1:     2 <- s-2
					//    2:     3 <- s-1
					//    3:     0 <- s-0

					// move // 3: 0 <- s-0
					StackMove(strb, stack.DynEndOffset.Octets - 1, stack.Return.BlockOffset + stack.Return.BlockSize - 1, 1);
					StackMove(strb, stack.DynEndOffset.Octets - stack.Return.BlockSize - 1, stack.Return.BlockOffset, stack.Return.BlockSize - 1);
					strb.GenOp("", "POP", stack.Return.ElementSize[0].ToString(), "0");
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
			case Code.Switch: Nop(instruct, strb); break;
			case Code.Ldind_I1:
			case Code.Ldind_U1:
			case Code.Ldind_I2:
			case Code.Ldind_U2:
			case Code.Ldind_I4:
			case Code.Ldind_U4:
			case Code.Ldind_I8:
				reg1 = stack.PopReg();
				stack.Push(GetFamiliarType(code));
				strb.GenOp(lbl, "LD" + GetFamiliar(code), stack.Reg(), reg1);
				break;
			case Code.Ldind_I: goto case Code.Ldind_I8;
			case Code.Ldind_R4: Nop(instruct, strb); break;
			case Code.Ldind_R8: Nop(instruct, strb); break;
			case Code.Ldind_Ref: goto case Code.Ldind_I;
			case Code.Stind_I1:
			case Code.Stind_I2:
			case Code.Stind_I4:
			case Code.Stind_I8:
				reg1 = stack.PopReg();
				reg2 = stack.PopReg();
				strb.GenOp(lbl, "ST" + GetFamiliar(code), reg1, reg2);
				break;
			case Code.Stind_I: goto case Code.Stind_I8;
			case Code.Stind_R4: Nop(instruct, strb); break;
			case Code.Stind_R8: Nop(instruct, strb); break;
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
				strb.GenOp("", "SET", stack.Reg(1), "rR");
				stack.Pop();
				break;
			case Code.Rem_Un:
				strb.GenOp(lbl, "DIVU", stack.Reg(1), stack.Reg(1), stack.Reg());
				strb.GenOp("", "SET", stack.Reg(1), "rR");
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
			case Code.Conv_R4: Nop(instruct, strb); break;
			case Code.Conv_R8: Nop(instruct, strb); break;
			case Code.Callvirt: Nop(instruct, strb); break;
			case Code.Cpobj: Nop(instruct, strb); break;
			case Code.Ldobj: Nop(instruct, strb); break;
			case Code.Ldstr: Nop(instruct, strb); break;
			case Code.Newobj: Nop(instruct, strb); break;
			case Code.Castclass: Nop(instruct, strb); break;
			case Code.Isinst: Nop(instruct, strb); break;
			case Code.Conv_R_Un: Nop(instruct, strb); break;
			case Code.Unbox: Nop(instruct, strb); break;
			case Code.Throw: Nop(instruct, strb); break;
			case Code.Ldfld:
				{
					var fld = instruct.Operand as FieldDefinition;
					reg1 = stack.PopReg();
					stack.Push(fld.FieldType);
					stack.Push(primU64);
					reg3 = stack.Reg(); // get temporary register with address
					var declType = fld.DeclaringType;
					if (declType.IsPointer || declType.IsClass)
					{
						var octs = GetSize(fld.FieldType).Octets;
						// backup our address from the stack
						if (octs > 1)
						{
							strb.GenOp(lbl, "SET", reg3, reg1);
							reg1 = reg3;
						}
						for (int i = 0; i < octs; i++)
						{
							strb.GenOp(lbl, "LDOU", stack.Reg(1, i), reg1, ((GetFldOffset(fld).Octets + i) * AddrSize).ToString());
						}
					}
					else
					{
						Nop(instruct, strb);
					}
					stack.Pop(); // pop tmp
				}
				break;
			case Code.Ldflda: Nop(instruct, strb); break;
			case Code.Stfld:
			case Code.Stsfld:
				/* Store field
				 * (-1) value [x:size]
				 * ( 0) object
				 */
				{
					var fld = instruct.Operand as FieldDefinition;

					if (code == Code.Stsfld)
					{
						stack.Push(primU64);
						strb.GenOp(lbl, "SET", stack.Reg(), StaticHeapAllocation[fld.FullName].Bytes.ToString());
					}

					reg1 = stack.Reg();

					if (fld.DeclaringType.IsPointer || fld.DeclaringType.IsClass)
					{
						for (int i = 0; i < GetSize(fld.FieldType).Octets; i++)
						{
							strb.GenOp(lbl, "STOU", reg1, stack.Reg(1, i), ((GetFldOffset(fld).Octets + i) * AddrSize).ToString());
						}
					}
					else
					{
						Nop(instruct, strb);
					}
					// hack to pop the obj ref, then get the type back onto the stack
					stack.Pop();
					stack.Pop();
				}
				break;
			case Code.Ldsfld:
				{
					var fld = instruct.Operand as FieldDefinition;
					stack.Push(fld.FieldType);
					stack.Push(primU64);
					reg1 = stack.Reg(); // get temporary register with address
					var declType = fld.DeclaringType;
					if (declType.IsPointer || declType.IsClass)
					{
						var octs = GetSize(fld.FieldType).Octets;
						// backup our address from the stack
						strb.GenOp(lbl, "SET", reg1, StaticHeapAllocation[(instruct.Operand as FieldReference).FullName].Bytes.ToString());
						for (int i = 0; i < octs; i++)
						{
							strb.GenOp(lbl, "LDOU", stack.Reg(1, i), reg1, ((GetFldOffset(fld).Octets + i) * AddrSize).ToString());
						}
					}
					else
					{
						Nop(instruct, strb);
					}
					stack.Pop(); // pop tmp
				}
				break;
			case Code.Ldsflda:
				stack.Push(primU64);
				strb.GenOp(lbl, "SET", stack.Reg(), StaticHeapAllocation[(instruct.Operand as FieldReference).FullName].Bytes.ToString());
				break;
			case Code.Stobj: Nop(instruct, strb); break;
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
			case Code.Box: Nop(instruct, strb); break;
			/* Array read access
			* (0) length
			* ->
			* (0) array [ 0:ptr 1:len ]
			*/
			case Code.Newarr:
				reg1 = stack.Reg();
				stack.Push(primU64);
				strb.GenOp("", "SET", stack.Reg(), reg1);
				stack.Push(primU64);
				strb.GenOp(lbl, "MUL", stack.Reg(0), stack.Reg(1), GetSize(instruct.Operand as TypeReference).Bytes.ToString());
				strb.GenOp("", "PUSHJ", stack.Reg(1), "malloc");
				strb.GenOp("", "SET", reg1, stack.Reg());
				stack.Pop(); stack.Pop(); stack.Pop();
				stack.Push(primArr);
				break;
			case Code.Ldlen:
				reg1 = stack.PopReg();
				stack.Push(primU64);
				strb.GenOp(lbl, "SUB", reg1, reg1, "8");
				strb.GenOp("", "LDO", reg1, reg1);
				break;
			case Code.Ldelema:
				reg1 = stack.PopReg();
				//reg2 = stack.PopReg(); // array
				//stack.PushManual(1); // TODO

				//TODO
				//int ldelemaSize = GetSize((TypeReference)instruct.Operand);
				break;
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
				reg1 = stack.PopReg(); // index
				(reg2, typ2) = stack.PopRegTyp(); // array
				stack.Push(typ2.GetElementType());
				strb.GenOp(lbl, "LD" + GetFamiliar(code), stack.Reg(), reg2, reg1);
				break;
			case Code.Ldelem_I: goto case Code.Ldelem_I8;
			case Code.Ldelem_R4: Nop(instruct, strb); break;
			case Code.Ldelem_R8: Nop(instruct, strb); break;
			case Code.Ldelem_Ref: Nop(instruct, strb); break;
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
				reg1 = stack.PopReg(); // value
				reg2 = stack.PopReg(); // index
				reg3 = stack.PopReg(); // array [ 0:ptr 1:len ]
				strb.GenOp(lbl, "ST" + GetFamiliar(code), reg1, reg3, reg2);
				break;
			case Code.Stelem_R4: Nop(instruct, strb); break;
			case Code.Stelem_R8: Nop(instruct, strb); break;
			case Code.Stelem_Ref: Nop(instruct, strb); break;
			case Code.Ldelem_Any: Nop(instruct, strb); break;
			case Code.Stelem_Any: Nop(instruct, strb); break;
			case Code.Unbox_Any: Nop(instruct, strb); break;
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
			case Code.Refanyval: Nop(instruct, strb); break;
			case Code.Ckfinite: Nop(instruct, strb); break;
			case Code.Mkrefany: Nop(instruct, strb); break;
			case Code.Ldtoken: Nop(instruct, strb); break;
			case Code.Add_Ovf: Nop(instruct, strb); break;
			case Code.Add_Ovf_Un: Nop(instruct, strb); break;
			case Code.Mul_Ovf: Nop(instruct, strb); break;
			case Code.Mul_Ovf_Un: Nop(instruct, strb); break;
			case Code.Sub_Ovf: Nop(instruct, strb); break;
			case Code.Sub_Ovf_Un: Nop(instruct, strb); break;
			case Code.Endfinally: Nop(instruct, strb); break;
			case Code.Leave: Nop(instruct, strb); break;
			case Code.Leave_S: Nop(instruct, strb); break;
			case Code.Arglist: Nop(instruct, strb); break;
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
			case Code.Ldftn: Nop(instruct, strb); break;
			case Code.Ldvirtftn: Nop(instruct, strb); break;
			case Code.Localloc: Nop(instruct, strb); break;
			case Code.Endfilter: Nop(instruct, strb); break;
			case Code.Unaligned: Nop(instruct, strb); break;
			case Code.Volatile: Nop(instruct, strb); break;
			case Code.Tail: Nop(instruct, strb); break;
			case Code.Initobj: Nop(instruct, strb); break;
			case Code.Constrained: Nop(instruct, strb); break;
			case Code.Cpblk: Nop(instruct, strb); break;
			case Code.Initblk: Nop(instruct, strb); break;
			case Code.No: Nop(instruct, strb); break;
			case Code.Rethrow: Nop(instruct, strb); break;
			case Code.Sizeof:
				var _sizeof = GetSize(instruct.Operand as TypeReference);
				stack.Push(primU64);
				strb.GenOp(lbl, "SET", stack.Reg(), _sizeof.Bytes8.ToString());
				break;
			case Code.Refanytype: Nop(instruct, strb); break;
			case Code.Readonly: Nop(instruct, strb); break;
			default: Nop(instruct, strb); break;
			}
		}

		public static void PushStack(StringBuilder strb, string lbl, Stack stack, StackBlock block, int index)
		{
			int size = block.ElementSize[index];
			for (int i = 0; i < size; i++)
			{
				strb.GenOp(i == 0 ? lbl : "", "SET",
					stack.Reg(0, i),
					block.Reg(index, i));
			}
		}

		public static void PopStack(StringBuilder strb, string lbl, Stack stack, StackBlock block, int index)
		{
			int size = block.ElementSize[index];
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

		private Stack CalcStack(MethodDefinition method)
		{
			var stack = new Stack
			{
				Return = new StackBlock(),
				Parameter = new StackBlock(),
				Locals = new StackBlock(),
			};

			//stack.Return.Count = Math.Sign(GetSize(method.ReturnType)); // TODO
			stack.Return.IndexStart = 0;
			stack.Return.ElementSize = GetSize(method.ReturnType) == Size.Zero ? new int[0] : new[] { method.ReturnType }.Select(x => GetSize(x).Octets).ToArray();
			stack.Return.ElementOffset = new int[stack.Return.Count];

			//stack.Parameter.Count = method.Parameters.Count;
			stack.Parameter.IndexStart = stack.Return.IndexStart + stack.Return.Count;
			stack.Parameter.ElementSize = method.Parameters.Select(x => x.ParameterType).Select(x => GetSize(x).Octets).ToArray();
			stack.Parameter.ElementOffset = new int[stack.Parameter.Count];

			//stack.Locals.Count = method.Body.Variables.Count;
			stack.Locals.IndexStart = stack.Parameter.IndexStart + stack.Parameter.Count;
			stack.Locals.ElementSize = method.Body.Variables.Select(x => x.VariableType).Select(x => GetSize(x).Octets).ToArray();
			stack.Locals.ElementOffset = new int[stack.Locals.Count];

			//stack.End.Count = 0;
			stack.FixedEndIndex = stack.Locals.IndexStart + stack.Locals.Count;

			stack.CumulativeOffset = new int[stack.FixedEndIndex];
			stack.AbsElementSize = new int[stack.FixedEndIndex];

			int gOff = 0;
			int cum = 0;
			AccumulateStack(ref gOff, ref cum, stack, stack.Return);
			AccumulateStack(ref gOff, ref cum, stack, stack.Parameter);
			AccumulateStack(ref gOff, ref cum, stack, stack.Locals);

			stack.FixedEndOffset = cum;

			return stack;
		}

		private static void AccumulateStack(ref int gOff, ref int cum, Stack stack, StackBlock block)
		{
			block.BlockOffset = gOff;
			int localSize = 0;
			for (int i = 0; i < block.ElementOffset.Length; i++)
			{
				block.ElementOffset[i] = cum;
				stack.CumulativeOffset[gOff] = cum;
				cum += block.ElementSize[i];
				localSize += block.ElementSize[i];
				stack.AbsElementSize[gOff] = block.ElementSize[i];
				gOff++;
			}
			block.BlockSize = localSize;
		}

		public static Size GetSize(TypeReference type)
		{
			if (type.FullName == "System.Void")
				return Size.Zero;
			if (type.IsArray)
				return Size.FromBytes(16); // 2x long
			if (type.IsPointer)
				return Size.FromBytes(8);
			if (type.IsPinned)
			{
				var ptype = (PinnedType)type;
				return GetSize(ptype.ElementType);
			}
			if (type.IsByReference)
				return Size.FromBytes(8);
			if (type.FullName == "System.Boolean")
				return Size.FromBytes(1);
			if (type.FullName == "System.Byte")
				return Size.FromBytes(1);
			if (type.FullName == "System.SByte")
				return Size.FromBytes(1);
			if (type.FullName == "System.Int16")
				return Size.FromBytes(2);
			if (type.FullName == "System.Int16")
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
				if (!TypeSizes.TryGetValue(type.FullName, out var size))
				{
					var dtype = (TypeDefinition)type;
					size = GetClassSize(dtype);
					TypeSizes[type.FullName] = size;
				}
				return size;
			}
			return Size.FromOctets(1);
		}

		public static Size GetClassSize(TypeDefinition type)
		{
			var size = Size.Zero;
			foreach (var fld in type.Fields)
			{
				size += GetSize(fld.FieldType);
			}
			return size;
		}

		public static Size GetFldOffset(FieldDefinition fld)
		{
			if (!FieldOffsets.TryGetValue(fld.FullName, out var size))
			{
				var offset = Size.Zero;
				foreach (var tfld in fld.DeclaringType.Fields)
				{
					FieldOffsets.Add(tfld.FullName, offset);
					offset += GetSize(tfld.FieldType);
				}
				size = FieldOffsets[fld.FullName];
			}
			return size;
		}

		private void Nop(Instruction instruction, StringBuilder strb)
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
				strb.GenNop("cread"); // 0: arrptr, 1: buflen
									  //strb.GenOp("", "STO", 0.Reg(), 255.Reg(), "0");
									  //strb.GenOp("", "STO", 1.Reg(), 255.Reg(), "8");
				strb.GenOp("", "SET", 255.Reg(), "rS");
				strb.GenOp("", "TRAP", "0", "Fgets", "StdIn");
				strb.GenOp("", "POP", "1", "0");
				break;

			case "cwrite":
				strb.GenNop("cwrite"); // 0: arrptr
				strb.GenOp("", "SET", 255.Reg(), 0.Reg());
				strb.GenOp("", "TRAP", "0", "Fputs", "StdOut");
				strb.GenOp("", "POP");
				break;

			case "delarr":
				strb.GenNop("delarr"); // 0: ptr
				strb.GenOp("", "GET", 2.Reg(), "rJ");
				strb.GenOp("", "SUB", 4.Reg(), 4.Reg(), "8");
				strb.GenOp("", "PUSHJ", 3.Reg(), "free");
				strb.GenOp("", "SET", 0.Reg(), 3.Reg());
				strb.GenOp("", "PUT", "rJ", 2.Reg());
				strb.GenOp("", "POP");
				break;

			default:
				throw new InvalidOperationException();
			}
			strb.AppendLine();
		}

		private static void GenerateMethodStart(StringBuilder strb, Stack stack)
		{
			var returnSize = stack.Return.BlockSize;
			if (returnSize == 0)
				return;

			// moves incomming parameter (offset 0) into the reserved parameter block
			StackMove(strb, 0, stack.Parameter.BlockOffset, stack.Parameter.BlockSize);
		}
	}

	// Array: (ptr, len)
	// MEM:
	//	0: ptr
	//	1: len
	// STACK:
	//	0: ptr
	//	1: len

	internal readonly struct Size
	{
		public ulong Bytes { get; }
		public int Octets => (int)((Bytes + 7) / 8);
		public int Bytes8 => Octets * 8;

		public static readonly Size Zero = new Size(0);
		public static readonly Size Oct = new Size(8);

		private Size(ulong bytes)
		{
			Bytes = bytes;
		}

		public static Size FromBytes(ulong bytes) => new Size(bytes);
		public static Size FromOctets(int octs) => new Size((ulong)(octs * 8));

		public static Size operator +(Size a, Size b) => FromOctets(a.Octets + b.Octets);
		public static bool operator ==(Size a, Size b) => a.Bytes == b.Bytes;
		public static bool operator !=(Size a, Size b) => a.Bytes != b.Bytes;

		public override string ToString() => $"{Octets} ({Bytes})";
	}

	internal class Stack
	{
		public int Index => DynStack.Count + FixedEndIndex - 1;

		public int[] AbsElementSize { get; set; }
		public int[] CumulativeOffset { get; set; }
		public Stack<StackElem> DynStack = new Stack<StackElem>();

		public StackBlock Return { get; set; }
		public StackBlock Parameter { get; set; }
		public StackBlock Locals { get; set; }

		public int FixedEndIndex { get; set; }
		public int FixedEndOffset { get; set; }

		public Size DynEndOffset => DynStack.Count > 0 ? (DynStack.Peek().offset + Compiler.GetSize(DynStack.Peek().type)) : Size.FromOctets(FixedEndOffset);

		public void Push(TypeReference type)
		{
			var size = Compiler.GetSize(type);
			if (size.Octets == 0)
				return;

			var currentOff = DynEndOffset;
			DynStack.Push(new StackElem { offset = currentOff, type = type });
		}

		public TypeReference Pop() => PopRegTyp().typ;

		public string PopReg() => PopRegTyp().reg;

		public (string reg, TypeReference typ) PopRegTyp()
		{
			if (DynStack.Count <= 0)
				throw new InvalidOperationException("Stack empty!");

			var reg = Reg(0);
			var elem = DynStack.Pop();
			return (reg, elem.type);
		}

		public int RegNum(int sub = 0, int elem = 0)
		{
			int acc = Index - sub;
			if (acc < FixedEndIndex)
				throw new InvalidOperationException("Stack accessing fixed registers");
			else
				return DynStack.Reverse().ElementAt(acc - FixedEndIndex).offset.Octets + elem;
		}

		public string Reg(int sub = 0, int elem = 0) => $"${RegNum(sub, elem)}";

		public struct StackElem
		{
			public Size offset;
			public TypeReference type;
		}

		public StackElem[] Save() => DynStack.ToArray();
		public void Load(StackElem[] stack) => DynStack = new Stack<StackElem>(stack);
	}

	internal class StackBlock
	{
		public int IndexStart { get; set; }
		public int Count => ElementSize.Length;

		public int BlockOffset { get; set; }
		public int BlockSize { get; set; }

		// Absolute offset
		public int[] ElementOffset { get; set; }
		public int[] ElementSize { get; set; }

		public int RegNum(int index = 0, int elem = 0)
		{
			if (index >= Count)
				throw new InvalidOperationException("Access out of block");
			else
				return ElementOffset[index] + elem;
		}
		public string Reg(int index = 0, int elem = 0) => $"${RegNum(index, elem)}";
	}

	internal static class Ext
	{
		public static void GenOp(this StringBuilder strb, string label, string op, string pX = null, string pY = null, string pZ = null)
		{
			strb.Append(label).Append(" ").Append(op);
			if (pX != null)
				strb.Append(" ").Append(pX);
			if (pY != null)
				strb.Append(",").Append(pY);
			if (pZ != null)
				strb.Append(",").Append(pZ);
			strb.AppendLine();
		}

		public static void GenNop(this StringBuilder strb, string label)
		{
			if (!string.IsNullOrEmpty(label))
			{
				strb.Append(label).Append(" ").Append("SWYM");
				strb.AppendLine();
			}
		}

		public static void GenNopCom(this StringBuilder strb, string label, string text = null)
		{
			strb.Append(label).Append(" ").Append("SWYM");
			if (text != null)
				strb.Append(" % ").Append(text);
			strb.AppendLine();
		}

		public static void GenCom(this StringBuilder strb, string text)
		{
			strb.Append(" % ").AppendLine(text);
		}

		public static string Reg(this int num) => $"${num}";

		public static Size Size(this TypeReference tref) => Compiler.GetSize(tref);

		public static bool IsCondJump(this Code code) => false
			|| code == Code.Brfalse
			|| code == Code.Brfalse_S
			|| code == Code.Brtrue
			|| code == Code.Brtrue_S
			|| code == Code.Beq
			|| code == Code.Beq_S
			|| code == Code.Bge
			|| code == Code.Bge_S
			|| code == Code.Bge_Un
			|| code == Code.Bge_Un_S
			|| code == Code.Bgt
			|| code == Code.Bgt_S
			|| code == Code.Bgt_Un
			|| code == Code.Bgt_Un_S
			|| code == Code.Ble
			|| code == Code.Ble_S
			|| code == Code.Ble_Un
			|| code == Code.Ble_Un_S
			|| code == Code.Blt
			|| code == Code.Blt_S
			|| code == Code.Blt_Un
			|| code == Code.Blt_Un_S
			|| code == Code.Bne_Un
			|| code == Code.Bne_Un_S;
		public static bool IsUnCondJump(this Code code) => code == Code.Br || code == Code.Br_S;
		public static bool IsJump(this Code code) => code.IsCondJump() || code.IsUnCondJump();
	}
}
