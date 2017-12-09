namespace TestCode
{
	public unsafe class MMIX
	{
		public static void Main()
		{
			/*byte* fLine = null;
			long iter = Atoi(fLine);
			for(long i = 0L; i < 3; i++)
			{
				fLine = null;
				byte* cur = fLine;
				while(*cur >= (long)'a')
				{
					*cur = (byte)((*cur - (byte)'a' + 13L) % 26L);
					cur++;
				}
			}*/

			Dummy(42);

			byte[] buf = MMIXSTD.newarr<byte>(32, sizeof(byte));
			fixed (byte* bufp = &buf[0])
			{
				MMIXSTD.cread(bufp, (ulong)buf.Length);
				MMIXSTD.cwrite(bufp);
			}
			MMIXSTD.delarr(buf);
		}

		public static int Dummy(int x)
		{
			return x;
		}

		/*public static ulong Atoi(byte[] input)
		{
			ulong x = 0L;
			for (ulong i = 0L; input[i] >= '0'; i++)
			{
				x *= 10L;
				x += (ulong)(input[i] - (byte)'0');
			}
			return x;
		}*/
	}

#pragma warning disable CS0626, IDE1006
	public static class MMIXSTD
	{
		public static extern unsafe void cread(byte* buf, ulong buflen);
		public static extern unsafe void cwrite(byte* buf);

		public static unsafe void* malloc(ulong size) { return null; }
		public static unsafe void free(void* mem) { }
		public static extern T[] newarr<T>(ulong size, ulong elemSize);
		public static extern void delarr<T>(T[] mem);
	}
#pragma warning restore CS0626, IDE1006
}
