using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MMIXCompiler
{
	class Compiler
	{
		const int AddrSize = 8;

		public string Compile(string code)
		{
			using (var stream = File.OpenRead("TestCode.dll"))
			{
				return LoadBinData(stream);
			}
		}

		public string LoadBinData(Stream data)
		{
			var strb = new StringBuilder();

			strb.GenOp("", "LOC", "#100");

			var asm = AssemblyDefinition.ReadAssembly(data);
			var module = asm.MainModule;
			foreach (var type in module.Types)
			{
				if (!type.IsClass)
					continue;

				if (type.Name == "MMIX")
				{
					foreach (var method in type.Methods)
					{
						if (!method.HasBody
							|| method.IsConstructor
							|| !method.IsIL
							|| !method.IsStatic)
							continue;

						strb.Append(GenerateMethod(method));
					}
				}
				else if (type.Name == "MMIXSTD")
				{
					foreach (var method in type.Methods)
					{
						if (method.IsConstructor
							|| !method.IsIL
							|| !method.IsStatic
							|| !method.HasBody)
							continue;

						if (method.Body.CodeSize == 0)
							GenerateStdLib(strb, method.Name);
						else
							strb.Append(GenerateMethod(method));
					}
				}
			}

			return strb.ToString();
		}

		public string GenerateMethod(MethodDefinition method)
		{
			var strb = new StringBuilder();

			strb.GenNopCom(method.Name, "() [] = ?");

			var stack = CalcStack(method);

			GenerateMethodStart(strb, stack);

			bool doesCall = false;

			var hasInJump = new HashSet<Instruction>();
			foreach (var instruct in method.Body.Instructions)
			{
				if (instruct.Operand is Instruction target)
				{
					hasInJump.Add(target);
				}

				if (instruct.OpCode.Code == Code.Call)
					doesCall = true;
			}

			foreach (var instruct in method.Body.Instructions)
			{
				string reg1, reg2, reg3;
				string lbl = "";
				if (hasInJump.Contains(instruct))
					lbl = Label(method, instruct);

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
					stack.PushManual(1); // TODO
					strb.GenOp(lbl, "GET", stack.Reg(), "rO");
					strb.GenOp(lbl, "ADD", stack.Reg(), stack.Reg(), (stack.Locals.ElementOffset[ldlocaIndex] * AddrSize).ToString());
					break;
				case Code.Ldnull:
					stack.PushManual(1); // TODO
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
					stack.PushManual(1); // TODO
					strb.GenOp(lbl, "SET", stack.Reg(), (ci - 22).ToString());
					break;
				case Code.Ldc_I4_S:
				case Code.Ldc_I4:
				case Code.Ldc_I8:
					stack.PushManual(1); // TODO
					strb.GenOp(lbl, "SET", stack.Reg(), instruct.Operand.ToString());
					break;
				case Code.Ldc_R4: Nop(instruct, strb); break;
				case Code.Ldc_R8: Nop(instruct, strb); break;
				case Code.Dup: Nop(instruct, strb); break;
				case Code.Pop:
					strb.GenNop(lbl);
					stack.Pop();
					break;
				case Code.Jmp: Nop(instruct, strb); break;
				case Code.Call:
					var callMethod = (MethodReference)instruct.Operand;
					int callParamSize = callMethod.Parameters.Sum(x => GetSize(x.ParameterType));
					int callStart = stack.RegNum() - callParamSize;
					StackMove(strb, callStart + 1, callStart + 2, callParamSize);
					reg1 = (callStart + 1).Reg();
					stack.Pop(callMethod.Parameters.Count);
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
						StackMove(strb, stack.DynEndOffset - 1, stack.Return.BlockOffset + stack.Return.BlockSize - 1, 1);
						StackMove(strb, stack.DynEndOffset - stack.Return.BlockSize - 1, stack.Return.BlockOffset, stack.Return.BlockSize - 1);
						strb.GenOp("", "POP", stack.Return.ElementSize[0].ToString(), "0");
					}
					stack.Pop(stack.Return.Count);
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
					stack.Pop(2);
					break;
				case Code.Bge_S:
				case Code.Bge:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BNN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Bgt_S:
				case Code.Bgt:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Ble_S:
				case Code.Ble:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BNP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Blt_S:
				case Code.Blt:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Bne_Un_S:
				case Code.Bne_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BNZ", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Bge_Un_S:
				case Code.Bge_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BNN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Bgt_Un_S:
				case Code.Bgt_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Ble_Un_S:
				case Code.Ble_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BNP", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
					break;
				case Code.Blt_Un_S:
				case Code.Blt_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "BN", stack.Reg(1), Label(method, (Instruction)instruct.Operand));
					stack.Pop(2);
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
					stack.PushManual(1); // TODO
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
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Sub:
					strb.GenOp(lbl, "SUB", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Mul:
					strb.GenOp(lbl, "MUL", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Div:
					strb.GenOp(lbl, "DIV", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Div_Un:
					strb.GenOp(lbl, "DIVU", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Rem:
					strb.GenOp(lbl, "DIV", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "SET", stack.Reg(1), "rR");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Rem_Un:
					strb.GenOp(lbl, "DIVU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "SET", stack.Reg(1), "rR");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.And:
					strb.GenOp(lbl, "AND", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Or:
					strb.GenOp(lbl, "OR", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Xor:
					strb.GenOp(lbl, "XOR", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Shl:
					strb.GenOp(lbl, "SL", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Shr:
					strb.GenOp(lbl, "SR", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Shr_Un:
					strb.GenOp(lbl, "SRU", stack.Reg(1), stack.Reg(1), stack.Reg());
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Neg:
					strb.GenOp(lbl, "NEG", stack.Reg(), "0", stack.Reg());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Not:
					strb.GenOp(lbl, "XOR", stack.Reg(), stack.Reg(), 0xFFFF_FFFF_FFFF_FFFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_I1:
					strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_I2:
					strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_I4:
					//strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFF_FFFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_I8:
					strb.GenNop(lbl);
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_U1:
					strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_U2:
					strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_U4:
					//strb.GenOp(lbl, "AND", stack.Reg(), stack.Reg(), 0xFFFF_FFFFL.ToString());
					stack.Pop(1);
					stack.PushManual(1); // TODO
					break;
				case Code.Conv_U8:
					strb.GenNop(lbl);
					stack.Pop(1);
					stack.PushManual(1); // TODO
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
				case Code.Ldfld: Nop(instruct, strb); break;
				case Code.Ldflda: Nop(instruct, strb); break;
				case Code.Stfld: Nop(instruct, strb); break;
				case Code.Ldsfld: Nop(instruct, strb); break;
				case Code.Ldsflda: Nop(instruct, strb); break;
				case Code.Stsfld: Nop(instruct, strb); break;
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
				case Code.Newarr: Nop(instruct, strb); break;
				case Code.Ldlen:
					reg1 = stack.PopReg();
					stack.PushManual(1); // TODO
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
				 * ( 0) index
				 * (-1) array [ 0:ptr 1:len ]
				 */
				case Code.Ldelem_I1:
				case Code.Ldelem_U1:
				case Code.Ldelem_I2:
				case Code.Ldelem_U2:
				case Code.Ldelem_I4:
				case Code.Ldelem_U4:
				case Code.Ldelem_I8:
					reg1 = stack.PopReg(); // index
					reg2 = stack.PopReg(); // array
					stack.PushManual(1); // TODO
					strb.GenOp(lbl, "LD" + GetFamiliar(code), stack.Reg(), reg2, reg1);
					break;
				case Code.Ldelem_I: goto case Code.Ldelem_I8;
				case Code.Ldelem_R4: Nop(instruct, strb); break;
				case Code.Ldelem_R8: Nop(instruct, strb); break;
				case Code.Ldelem_Ref: Nop(instruct, strb); break;
				/* Array write access
				 * ( 0) value
				 * (-1) index
				 * (-2) array
				 */
				case Code.Stelem_I: goto case Code.Stelem_I8;
				case Code.Stelem_I1:
				case Code.Stelem_I2:
				case Code.Stelem_I4:
				case Code.Stelem_I8:
					reg1 = stack.PopReg(); // value
					reg2 = stack.PopReg(); // index
					reg3 = stack.PopReg(); // array
					strb.GenOp(lbl, "ST" + GetFamiliar(code), reg1, reg3, reg2);
					stack.Pop(3);
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
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "ZSZ", stack.Reg(1), stack.Reg(1), "1");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Cgt:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "ZSP", stack.Reg(1), stack.Reg(1), "1");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Cgt_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "ZSP", stack.Reg(1), stack.Reg(1), "1");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Clt:
					strb.GenOp(lbl, "CMP", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "ZSN", stack.Reg(1), stack.Reg(1), "1");
					stack.Pop(2);
					stack.PushManual(1); // TODO
					break;
				case Code.Clt_Un:
					strb.GenOp(lbl, "CMPU", stack.Reg(1), stack.Reg(1), stack.Reg());
					strb.GenOp("", "ZSN", stack.Reg(1), stack.Reg(1), "1");
					stack.Pop(2);
					stack.PushManual(1); // TODO
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
				case Code.Sizeof: Nop(instruct, strb); break;
				case Code.Refanytype: Nop(instruct, strb); break;
				case Code.Readonly: Nop(instruct, strb); break;
				default: Nop(instruct, strb); break;
				}
			}

			strb.AppendLine();
			return strb.ToString();
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
			stack.Return.ElementSize = GetSize(method.ReturnType) == 0 ? new int[0] : new[] { method.ReturnType }.Select(GetSize).ToArray();
			stack.Return.ElementOffset = new int[stack.Return.Count];

			//stack.Parameter.Count = method.Parameters.Count;
			stack.Parameter.IndexStart = stack.Return.IndexStart + stack.Return.Count;
			stack.Parameter.ElementSize = method.Parameters.Select(x => x.ParameterType).Select(GetSize).ToArray();
			stack.Parameter.ElementOffset = new int[stack.Parameter.Count];

			//stack.Locals.Count = method.Body.Variables.Count;
			stack.Locals.IndexStart = stack.Parameter.IndexStart + stack.Parameter.Count;
			stack.Locals.ElementSize = method.Body.Variables.Select(x => x.VariableType).Select(GetSize).ToArray();
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
			stack.Index = stack.FixedEndIndex - 1;

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

		public static int GetSize(TypeReference type)
		{
			if (type.FullName == "System.Void")
				return 0;
			return 1;
		}

		private void Nop(Instruction instruction, StringBuilder strb)
		{
			//strb.AppendLine("% Unknown: " + instruction.OpCode.Name);
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

		private static void GenerateStdLib(StringBuilder strb, string name)
		{
			switch (name)
			{
			case "cread":
				strb.GenNop("cread"); // 0: arrptr, 1: buflen
				strb.GenOp("", "STO", 0.Reg(), 255.Reg(), "0");
				strb.GenOp("", "STO", 1.Reg(), 255.Reg(), "8");
				strb.GenOp("", "TRAP", "0", "Fgets", "StdIn");
				strb.GenOp("", "POP", "1", "0");
				break;

			case "cwrite":
				strb.GenNop("cwrite"); // 0: arrptr
				strb.GenOp("", "GET", 255.Reg(), 0.Reg());
				strb.GenOp("", "TRAP", "0", "Fputs", "StdOut");
				strb.GenOp("", "POP");
				break;

			case "newarr":
				strb.GenNop("newarr"); // 0: size 1: elemsz
				strb.GenOp("", "GET", 2.Reg(), "rJ");
				strb.GenOp("", "MUL", 4.Reg(), 0.Reg(), 1.Reg());
				strb.GenOp("", "ADD", 4.Reg(), 4.Reg(), "8");
				strb.GenOp("", "PUSHJ", 3.Reg(), "malloc");
				strb.GenOp("", "STO", 0.Reg(), 3.Reg(), "0");
				strb.GenOp("", "ADD", 0.Reg(), 3.Reg(), "8");
				strb.GenOp("", "PUT", "rJ", 2.Reg());
				strb.GenOp("", "POP", "1", "0");
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

	class Stack
	{
		public int Index { get; set; }

		public int[] AbsElementSize { get; set; }
		public int[] CumulativeOffset { get; set; }
		public Stack<int> DynStackSize = new Stack<int>();
		public Stack<int> DynStackOffset = new Stack<int>();

		public StackBlock Return { get; set; }
		public StackBlock Parameter { get; set; }
		public StackBlock Locals { get; set; }

		public int FixedEndIndex { get; set; }
		public int FixedEndOffset { get; set; }

		public int DynEndOffset => DynStackOffset.Count > 0 ? (DynStackOffset.Peek() + DynStackSize.Peek()) : FixedEndOffset;

		public void Push(TypeReference type)
		{
			PushManual(Compiler.GetSize(type));
		}

		public void PushManual(int itemSize)
		{
			if (itemSize == 0)
				return;

			DynStackSize.Push(itemSize);
			DynStackOffset.Push(DynEndOffset);
			Index++;
		}

		public void Pop(int size = 1)
		{
			while (size > 0 && DynStackSize.Count > 0)
			{
				DynStackSize.Pop();
				DynStackOffset.Pop();
				size--;
				Index--;
			}
			if (size > 0)
				throw new InvalidOperationException("Stack empty!");
		}

		public string PopReg(int elem = 0)
		{
			var reg = Reg(0, elem);
			Pop();
			return reg;
		}

		public int RegNum(int sub = 0, int elem = 0)
		{
			int acc = Index - sub;
			if (acc < FixedEndIndex)
				throw new InvalidOperationException("Stack accessing fixed registers");
			else
				return DynStackOffset.Reverse().ElementAt(acc - FixedEndIndex) + elem;
		}
		public string Reg(int sub = 0, int elem = 0) => $"${RegNum(sub, elem)}";
	}

	class StackBlock
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

	static class Ext
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

		public static string Reg(this int num) => $"${num}";
	}
}
