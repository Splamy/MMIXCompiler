namespace TestCode
{
	public static unsafe class MMIX
	{
		public static void Main(int argc, byte** argv)
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

			byte[] buf = new byte[13];// MMIXSTD.newarr<byte>(32, sizeof(byte));

			MMIXSTD.cread(buf);

			fixed (byte* bufp = &buf[0])
			{
				//MMIXSTD.cread(bufp, (ulong)buf.Length);
				MMIXSTD.cwrite(bufp);
			}
			MMIXSTD.delarr(buf);
		}

		public static int Dummy(int x)
		{
			return x * 2;
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
	public static unsafe class MMIXSTD
	{
		public const ulong TEXT_SEGMENT = 0x0000_0000_0000_0000;
		public const ulong DATA_SEGMENT = 0x2000_0000_0000_0000;
		public const ulong POOL_SEGMENT = 0x4000_0000_0000_0000;
		public const ulong STACK_SEGMENT = 0x6000_0000_0000_0000;
		private const ulong Align8Mask = 0xFFFF_FFFF_FFFF_FFF8;

		public static unsafe void* malloc(ulong size)
		{
			size = (size + 0x7) & Align8Mask;
			var toAlloc = size + (ulong)sizeof(MemAllocNode);

			// find the next free slot
			var curNode = (MemAllocNode*)DATA_SEGMENT;
			while (curNode->next != null && (ulong)curNode + curNode->size + toAlloc > (ulong)curNode->next)
				curNode = curNode->next;

			var newNodePtr = (MemAllocNode*)((ulong)curNode + curNode->size);
			newNodePtr->next = (MemAllocNode*)0;
			newNodePtr->size = toAlloc;
			if (newNodePtr != curNode)
				curNode->next = newNodePtr;

			return newNodePtr + 1;
		}

		public static unsafe void free(void* mem) { }
		//public static extern T[] newarr<T>(ulong size, ulong elemSize);

#if DEBUG
		public static void cread(byte[] buf) { }
		public static void cwrite(byte* buf) { }
		public static void delarr<T>(T[] mem) { }
#else
		public static extern void cread(byte[] buf);
		public static extern void cwrite(byte* buf);
		public static extern void delarr<T>(T[] mem);
#endif
	}

	public static unsafe class MMIXTYP
	{
		public static bool pBool;
		public static sbyte pI8;
		public static short pI16;
		public static int pI32;
		public static long pI64;
		public static byte pU8;
		public static ushort pU16;
		public static uint pU32;
		public static ulong pU64;
		public static void* pPtr;
		public static System.Array pArr;
	}

	public unsafe struct MemAllocNode
	{
		public MemAllocNode* next;
		public ulong size;
	}
#pragma warning restore CS0626, IDE1006
}
