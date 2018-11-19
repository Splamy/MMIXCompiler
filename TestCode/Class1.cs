namespace TestCode
{
	public static unsafe class MMIX
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
	public static unsafe class MMIXSTD
	{
		public const ulong TEXT_SEGMENT = 0x0000000000000000;
		public const ulong DATA_SEGMENT = 0x2000000000000000;
		public const ulong POOL_SEGMENT = 0x4000000000000000;
		public const ulong STACK_SEGMENT = 0x6000000000000000;

		private static MemAllocNode* heap;

		public static extern void cread(byte[] buf);
		public static extern void cwrite(byte* buf);

		public static unsafe void* malloc(ulong size)
		{
			var toAlloc = size + (ulong)sizeof(MemAllocNode);
			MemAllocNode* curNode = heap;

			if (heap == null)
			{
				heap = (MemAllocNode*)DATA_SEGMENT;
				heap->size = toAlloc;
			}
			else
			{
				// find the next free slot
				while (curNode->next != null && (ulong)curNode + curNode->size + toAlloc > (ulong)curNode->next)
					curNode = curNode->next;
			}

			var endOfNode = (ulong)curNode + curNode->size;
			var newNodePtr = (MemAllocNode*)endOfNode;
			newNodePtr->next = curNode->next;
			newNodePtr->size = toAlloc;
			curNode->next = newNodePtr;
			return newNodePtr + (ulong)sizeof(MemAllocNode);
		}

		public static unsafe void free(void* mem) { }
		//public static extern T[] newarr<T>(ulong size, ulong elemSize);
		public static extern void delarr<T>(T[] mem);
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
