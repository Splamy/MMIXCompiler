namespace MMIXCompiler.Compiler;

record StaticAllocation(
	MAddr Address,
	Size Size,
	string Label,
	ulong Value
);

