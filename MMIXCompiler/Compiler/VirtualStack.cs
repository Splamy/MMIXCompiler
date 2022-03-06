using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.Linq;

namespace MMIXCompiler.Compiler;

internal class VirtualStack
{
	public int Index => DynStack.Count + FixedEndIndex - 1;

	public VirtualStackElem[] FixedStack { get; set; } // only for debug
	public Stack<VirtualStackElem> DynStack { get; set; } = new();
	public IEnumerable<VirtualStackElem> DebugFullStackView => FixedStack.Concat(DynStack.Reverse());

	public VirtualStackBlock Return { get; } = new();
	public VirtualStackBlock Parameter { get; } = new();
	public VirtualStackBlock Backup { get; } = new();
	public VirtualStackBlock Locals { get; } = new();

	public IReadOnlyList<VirtualStackBlock> BlockList;

	public int FixedEndIndex { get; set; }
	public int FixedEndOffset { get; set; }
	public bool DoesCall { get; set; } = false;

	public Size DynEndOffset => DynStack.Count > 0 ? DynStack.Peek().Offset + CodeGenerator.GetSize(DynStack.Peek().Type) : Size.FromOctets(FixedEndOffset);

	public VirtualStack()
	{
		BlockList = new List<VirtualStackBlock>() { Return, Parameter, Backup, Locals };
	}

	public void Push(TypeReference type)
	{
		var size = CodeGenerator.GetSize(type);
		if (size.Octets == 0)
			return;

		var currentOff = DynEndOffset;
		DynStack.Push(new(Offset: currentOff, Size: size, type));
	}

	public TypeReference Pop() => PopRegTyp().typ;

	public Reg PopReg() => PopRegTyp().reg;

	public (Reg reg, TypeReference typ) PopRegTyp()
	{
		if (DynStack.Count <= 0)
			throw new InvalidOperationException("Stack empty!");

		var reg = Reg(0);
		var elem = DynStack.Pop();
		return (reg, elem.Type);
	}

	public byte RegNum(int sub = 0, int elem = 0)
	{
		int acc = Index - sub;
		if (acc < FixedEndIndex)
			throw new InvalidOperationException("Stack accessing fixed registers");
		else
			return checked((byte)(DynStack.Reverse().ElementAt(acc - FixedEndIndex).Offset.Octets + elem));
	}

	public Reg Reg(int sub = 0, int elem = 0) => new(RegNum(sub, elem));

	public VirtualStackElem[] Save() => DynStack.ToArray();
	public void Load(VirtualStackElem[] stack) => DynStack = new Stack<VirtualStackElem>(stack);
}

