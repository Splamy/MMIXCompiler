using Mono.Cecil.Cil;
using System;
using System.Collections.Generic;
using System.Text;

namespace MMIXCompiler.Compiler;

internal static class Extensions
{
	public static void GenOp(this StringBuilder strb, string label, string op, string? pX = null, string? pY = null, string? pZ = null)
	{
		strb.Lbl(label).GenOpClear(op, pX, pY, pZ);
	}

	public static void GenOpClear(this StringBuilder strb, string op, string? pX = null, string? pY = null, string? pZ = null)
	{
		strb.Append(' ').Append(op);
		if (pX != null)
			strb.Append(' ').Append(pX);
		if (pY != null)
			strb.Append(',').Append(pY);
		if (pZ != null)
			strb.Append(',').Append(pZ);
		strb.AppendLine();
	}

	public static void GenNop(this StringBuilder strb, string label, string? text = null)
	{
		if (!string.IsNullOrEmpty(label) || !string.IsNullOrEmpty(text))
		{
			strb.Append(label).Append(' ').Append("SWYM");
			if (!string.IsNullOrEmpty(text))
				strb.Append(" % ").Append(text);
			strb.AppendLine();
		}
	}

	public static void GenComment(this StringBuilder strb, string text)
	{
		strb.Append(" % ").AppendLine(text);
	}

	public static void GenLoadConst(this StringBuilder strb, string label, Reg reg, ulong num)
	{
		strb.GenNop(label);
		if (unchecked((long)num) is < 0 and >= -0xFFFF)
		{
			strb.GenOp("", "SETL", reg, (-(long)num).ToString());
			strb.GenOp("", "NEG", reg, reg);
			return;
		}

		bool firstSet = false;
		ulong numH = num >> 48 & 0xFFFF;
		ulong numMH = num >> 32 & 0xFFFF;
		ulong numML = num >> 16 & 0xFFFF;
		ulong numL = num >> 00 & 0xFFFF;
		if (numL != 0)
		{
			strb.GenOp("", "SETL", reg, numL.ToString());
			firstSet = true;
		}
		if (numML != 0)
		{
			strb.GenOp("", firstSet ? "ORML" : "SETML", reg, numML.ToString());
			firstSet = true;
		}
		if (numMH != 0)
		{
			strb.GenOp("", firstSet ? "ORMH" : "SETMH", reg, numMH.ToString());
			firstSet = true;
		}
		if (numH != 0)
		{
			strb.GenOp("", firstSet ? "ORH" : "SETH", reg, numH.ToString());
			firstSet = true;
		}

		if (!firstSet)
		{
			strb.GenOp("", "SET", reg, "0");
		}
	}

	public static void GenWriteObject<T>(this StringBuilder strb, string label, Reg reg, T obj) where T : struct
	{
		
	}

	public static StringBuilder Lbl(this StringBuilder strb, string label)
	{
		if (!string.IsNullOrEmpty(label))
			strb.Append(label);
		return strb;
	}

	public static Reg Reg(this int num) { if (num > 255) throw new ArgumentOutOfRangeException(nameof(num)); return ((byte)num).Reg(); }
	public static Reg Reg(this byte num) => new(num);

	private static readonly HashSet<Code> ConditionalJumpOpcodes = new()
	{
		Code.Brfalse,
		Code.Brfalse_S,
		Code.Brtrue,
		Code.Brtrue_S,
		Code.Beq,
		Code.Beq_S,
		Code.Bge,
		Code.Bge_S,
		Code.Bge_Un,
		Code.Bge_Un_S,
		Code.Bgt,
		Code.Bgt_S,
		Code.Bgt_Un,
		Code.Bgt_Un_S,
		Code.Ble,
		Code.Ble_S,
		Code.Ble_Un,
		Code.Ble_Un_S,
		Code.Blt,
		Code.Blt_S,
		Code.Blt_Un,
		Code.Blt_Un_S,
		Code.Bne_Un,
		Code.Bne_Un_S,
	};
	public static bool IsCondJump(this Code code) => ConditionalJumpOpcodes.Contains(code);
	public static bool IsUnCondJump(this Code code) => code is Code.Br or Code.Br_S;
	public static bool IsJump(this Code code) => code.IsCondJump() || code.IsUnCondJump();
}

