
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
		if (curNode->next == null)
		{
			if (curNode->prev == null)
			{
				curNode->size = (ulong)sizeof(MemAllocNode);
				newNodePtr += 1;
			}

			curNode->next = newNodePtr;
			newNodePtr->prev = curNode;
			newNodePtr->next = null;
		}
		else
		{
			newNodePtr->prev = curNode;
			newNodePtr->next = curNode->next;

			curNode->next->prev = newNodePtr;
			curNode->next = newNodePtr;
		}
		newNodePtr->size = toAlloc;
		return newNodePtr + 1;
	}

	public static unsafe void free(void* mem)
	{
		var node = ((MemAllocNode*)mem) - 1;
		node->prev->next = node->next;

	}

#if DEBUG
	public static void cread(byte[] buf) { }
	public static void cwrite(byte[] buf) { }
	public static void delarr<T>(T[] mem) { }
#else
	public static extern void cread(byte[] buf);
	public static extern void cwrite(byte[] buf);
	public static extern void delarr<T>(T[] mem);
#endif
}

public static unsafe class MMIXTYP
{
	public static readonly bool pBool;
	public static readonly sbyte pI8;
	public static readonly short pI16;
	public static readonly int pI32;
	public static readonly long pI64;
	public static readonly byte pU8;
	public static readonly ushort pU16;
	public static readonly uint pU32;
	public static readonly ulong pU64;
	public static readonly void* pPtr;
	public static readonly System.Array pArr;
}

public unsafe struct MemAllocNode
{
	public MemAllocNode* prev;
	public MemAllocNode* next;
	public ulong size;
}
#pragma warning restore CS0626, IDE1006