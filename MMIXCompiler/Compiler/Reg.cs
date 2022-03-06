namespace MMIXCompiler.Compiler;

class Reg
{
	public byte Number { get; }

	public Reg(byte number)
	{
		Number = number;
	}

	public override string ToString() => $"${Number}";

	public static implicit operator string(Reg reg) => reg.ToString();
}

