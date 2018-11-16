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

			var test = new byte[13];

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
		private static readonly MemAllocNode heap;

		public static extern unsafe void cread(byte* buf, ulong buflen);
		public static extern unsafe void cwrite(byte* buf);

		public static unsafe void* malloc(ulong size)
		{
			var toAlloc = size + (ulong)sizeof(MemAllocNode);//sizeof(MemAllocNode);
			MemAllocNode* curNode;
			fixed (MemAllocNode* heapPtr = &heap)
				curNode = heapPtr;

			// find the next free slot
			while (curNode->next != null && curNode->next->block_pointer - curNode->block_pointer + curNode->size < toAlloc)
				curNode = curNode->next;

			var endOfNode = curNode->block_pointer + curNode->size;
			var newNodePtr = (MemAllocNode*)endOfNode;
			newNodePtr->next = curNode->next;
			newNodePtr->block_pointer = endOfNode;
			newNodePtr->size = toAlloc;
			curNode->next = newNodePtr;
			return newNodePtr;
		}

		public static unsafe void free(void* mem) { }
		public static unsafe extern T[] newarr<T>(ulong size, ulong elemSize);
		public static unsafe extern void delarr<T>(T[] mem);
	}

	public unsafe struct MemAllocNode
	{
		public MemAllocNode* next;
		public ulong block_pointer;
		public ulong size;
	}
#pragma warning restore CS0626, IDE1006
}
