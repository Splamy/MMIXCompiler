using System.Runtime.CompilerServices;

public sealed class MMIXMarker { }

#pragma warning disable CS0626, IDE1006
public static unsafe class MMIXSTD
{
    public const ulong TEXT_SEGMENT = 0x0000_0000_0000_0000;
    public const ulong DATA_SEGMENT = 0x2000_0000_0000_0000;
    public const ulong POOL_SEGMENT = 0x4000_0000_0000_0000;
    public const ulong STACK_SEGMENT = 0x6000_0000_0000_0000;
    private const ulong Align8Mask = ~0x7UL;

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
        var next = node->next;
        var prev = node->prev;
        next->prev = prev;
        prev->next = next;
    }

    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void cread(MMIXSpan buf);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void cwrite(MMIXSpan buf);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern void delarr<T>(T[] mem);
}

public unsafe struct MemAllocNode
{
    public MemAllocNode* prev;
    public MemAllocNode* next;
    public ulong size;
}

public unsafe struct MMIXSpan
{
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern implicit operator MMIXSpan(byte[] array);
    [MethodImpl(MethodImplOptions.InternalCall)]
    public static extern implicit operator MMIXSpan(string array);
}
#pragma warning restore CS0626, IDE1006