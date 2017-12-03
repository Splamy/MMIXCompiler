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

			byte* buf = null;
			MMIXSTD.ConsoleRead(buf, 2);
			MMIXSTD.ConsoleWrite(buf);
		}

		public static unsafe long Atoi(byte[] input)
		{
			long x = 0L;
			for (long i = 0L; input[i] >= '0'; i++)
			{
				x *= 10L;
				x += (input[i] - '0');
			}
			return x;
		}
	}

#pragma warning disable CS0626
	public static class MMIXSTD
	{
		public extern static unsafe void ConsoleRead(byte* buf, ulong len);
		public extern static unsafe void ConsoleWrite(byte* buf);
	}
#pragma warning restore CS0626
}
