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

        //public static int Strlen(byte[] str)
        //{
        //    for (int i = 0; ; i++)
        //    {
        //        if (str[i] == 0)
        //            return i;
        //    }
        //}

        //public static void Itoa(int num, byte[] buf)
        //{
        //    int pos = 0;
        //    do
        //    {
        //        var digit = num % 10;
        //        num /= 10;
        //        buf[pos++] = (byte)('0' + digit);
        //    } while (num > 0);
        //    buf[pos] = 0;
        //}

        //internal static unsafe byte[] UInt32ToDecStr(uint value)
        //{
        //    // Intrinsified in mono interpreter
        //    int bufferLength = CountDigits(value);

        //    byte[] result = new byte[bufferLength];
        //    fixed (byte* buffer = result)
        //    {
        //        byte* p = buffer + bufferLength;
        //        do
        //        {
        //            uint remainder = value % 10;
        //            value /= 10;
        //            *(--p) = (byte)(remainder + '0');
        //        }
        //        while (value != 0);
        //    }
        //    return result;
        //}

        //public static int CountDigits(uint value)
        //{
        //    int digits = 1;
        //    if (value >= 100000)
        //    {
        //        value /= 100000;
        //        digits += 5;
        //    }

        //    if (value < 10) { /* no-op */ }
        //    else if (value < 100) { digits++; }
        //    else if (value < 1000) { digits += 2; }
        //    else if (value < 10000) { digits += 3; }
        //    else { digits += 4; }

        //    return digits;
        //}

        ////public static int Dummy(int x)
        ////{
        ////	return x * 2;
        ////}

        //public static ulong Atoi(byte[] input)
        //{
        //    ulong x = 0L;
        //    for (ulong i = 0L; input[i] >= '0'; i++)
        //    {
        //        x *= 10L;
        //        x += (ulong)(input[i] - (byte)'0');
        //    }
        //    return x;
        //}
    }
}
