using System;

namespace MMIXCompiler.Compiler;

internal class VirtualStackBlock
{
	public int IndexStart { get; set; }
	public int Count => Elements.Length;

	public int BlockOffset { get; set; }
	public int BlockSize { get; set; }

	// Absolute offset
	public VirtualStackElem[] Elements { get; set; }

	public byte RegNum(int index = 0, int elem = 0)
	{
		if (index >= Count)
			throw new InvalidOperationException("Access out of block");
		else
			return checked((byte)(Elements[index].Offset.Octets + elem));
	}
	public Reg Reg(int index = 0, int elem = 0) => new(RegNum(index, elem));
}

