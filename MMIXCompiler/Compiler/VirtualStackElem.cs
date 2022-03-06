using Mono.Cecil;

namespace MMIXCompiler.Compiler;

internal record VirtualStackElem(Size Offset, Size Size, TypeReference Type, string? Desc = null)
{
	public override string ToString() => $"@{Offset.OctetsLong}:{Size.OctetsLong} {Desc} ({Type?.FullName})";
}

