using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Weesals {
    public static class ListExt {
        public static void RemoveAtSwapBack<T>(this List<T> list, int index) {
            list[index] = list[^1];
            list.RemoveAt(list.Count - 1);
        }
    }
    public static class ArrayExt {
        public static void InsertAt<T>(ref T[] array, ref int count, int index, T value) {
            if (count >= array.Length) {
                Array.Resize(ref array, (int)BitOperations.RoundUpToPowerOf2((uint)count + 4));
            }
            Array.Copy(array, index, array, index + 1, count - index);
            ++count;
            array[index] = value;
        }
    }
    public static class StringExt {
        unsafe public static ulong ComputeStringHash(this string str) {
            ulong hash = 0;
            foreach (var chr in str) {
                hash *= 3074457345618258799ul;
                hash += chr;
            }
            return hash;
        }
    }
}
