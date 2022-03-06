namespace TestCode
{
    using static MMIXSTD;
    public static unsafe class MMIX
    {
        public static void Main(int argc, byte** argv)
        {
            byte[] readbuf = new byte[256];

            cwrite("Hello World");
        }

        public static int Strlen(byte[] str)
        {
            for (int i = 0; ; i++)
            {
                if (str[i] == 0)
                    return i;
            }
        }

        public static void Itoa(int num, byte[] buf)
        {
            int pos = 0;
            do
            {
                var digit = num % 10;
                num /= 10;
                buf[pos++] = (byte)('0' + digit);
            } while (num > 0);
            buf[pos] = 0;
        }

        internal static unsafe byte[] UInt32ToDecStr(uint value)
        {
            // Intrinsified in mono interpreter
            int bufferLength = CountDigits(value);

            byte[] result = new byte[bufferLength];
            fixed (byte* buffer = result)
            {
                byte* p = buffer + bufferLength;
                do
                {
                    uint remainder = value % 10;
                    value /= 10;
                    *(--p) = (byte)(remainder + '0');
                }
                while (value != 0);
            }
            return result;
        }

        public static int CountDigits(uint value)
        {
            int digits = 1;
            if (value >= 100000)
            {
                value /= 100000;
                digits += 5;
            }

            if (value < 10) { /* no-op */ }
            else if (value < 100) { digits++; }
            else if (value < 1000) { digits += 2; }
            else if (value < 10000) { digits += 3; }
            else { digits += 4; }

            return digits;
        }

        //public static int Dummy(int x)
        //{
        //	return x * 2;
        //}

        public static ulong Atoi(byte[] input)
        {
            ulong x = 0L;
            for (ulong i = 0L; input[i] >= '0'; i++)
            {
                x *= 10L;
                x += (ulong)(input[i] - (byte)'0');
            }
            return x;
        }
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
            var prev = node->prev;
            var next = node->next;
            prev->next = next;
            next->prev = prev;
        }

        public static extern void cread(byte[] buf);
        public static extern void cwrite(MMIXString buf);
        public static extern void delarr<T>(T[] mem);
        public static extern void read_file(byte[] filename);
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

    public struct MMIXString
    {
        public static extern implicit operator MMIXString(string fromstr);
        public static extern implicit operator MMIXString(byte[] frombyte);
        public static extern implicit operator string(MMIXString fromstr);
        public static extern implicit operator byte[](MMIXString frombyte);
    }

    public unsafe struct MemAllocNode
    {
        public MemAllocNode* prev;
        public MemAllocNode* next;
        public ulong size;
    }
#pragma warning restore CS0626, IDE1006
}
