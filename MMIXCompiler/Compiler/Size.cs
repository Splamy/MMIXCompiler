using System;

namespace MMIXCompiler.Compiler;

// Array: (ptr, len)
// MEM:
//	0: ptr
//	1: len
// STACK:
//	0: ptr
//	1: len

internal readonly struct Size : IEquatable<Size>
{
    public ulong Bytes { get; }
    public ulong OctetsLong => (Bytes + 7) / 8;
    public ulong Bytes8Long => OctetsLong * 8;
    public int Octets => (int)OctetsLong;
    public int Bytes8 => (int)Bytes8Long;

    public static readonly Size Zero = new(0);
    public static readonly Size Oct = new(8);

    private Size(ulong bytes)
    {
        Bytes = bytes;
    }

    public static Size FromBytes(ulong bytes) => new(bytes);
    public static Size FromOctets(ulong octs) => new(octs * 8);
    public static Size FromOctets(int octs) => FromOctets((ulong)octs);

    public static Size operator +(Size a, Size b) => FromOctets(a.OctetsLong + b.OctetsLong);
    public static bool operator ==(Size a, Size b) => a.Bytes == b.Bytes;
    public static bool operator !=(Size a, Size b) => a.Bytes != b.Bytes;

    public override string ToString() => $"{OctetsLong} ({Bytes})";

    public override bool Equals(object? other) => other is Size size && Equals(size);
    public bool Equals(Size other) => Bytes == other.Bytes;
    public override int GetHashCode() => HashCode.Combine(Bytes);
}

internal record struct MAddr(ulong Ptr)
{
    private const ulong Align8Mask = ~0x7UL;

    public static MAddr operator +(MAddr a, Size s) => new(a.Ptr + s.Bytes);

    /// <summary>Aligned down to Octets</summary>
    public MAddr AlignDown8() => new(Ptr & Align8Mask);
    /// <summary>Aligned down to Octets</summary>
    public MAddr AlignUp8() => new((Ptr + 0xF) & Align8Mask);

    public override string ToString() => $"0x{Ptr:X2}";
}