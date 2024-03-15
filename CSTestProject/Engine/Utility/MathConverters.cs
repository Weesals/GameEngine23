using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.Engine.Converters {
    public class Int2Converter : TypeConverter {
        public override bool CanConvertFrom(ITypeDescriptorContext? context, Type sourceType) {
            return sourceType == typeof(string) || base.CanConvertFrom(context, sourceType);
        }
        public override object? ConvertFrom(ITypeDescriptorContext? context, CultureInfo? culture, object value) {
            if (value is string strValue) {
                var chrs = strValue.AsSpan().TrimStart('<').TrimEnd('>');
                var commaI = chrs.IndexOf(',');
                if (commaI == -1) commaI = chrs.Length;
                var item1 = chrs.Slice(0, commaI);
                var item2 = chrs.Slice(commaI < chrs.Length ? commaI + 1 : commaI);
                while (item1.Length > 0 && char.IsWhiteSpace(item1[0])) item1 = item1.Slice(1);
                while (item2.Length > 0 && char.IsWhiteSpace(item2[0])) item2 = item2.Slice(1);
                while (item1.Length > 0 && char.IsWhiteSpace(item1[^1])) item1 = item1.Slice(0, item1.Length - 2);
                while (item2.Length > 0 && char.IsWhiteSpace(item2[^1])) item2 = item2.Slice(0, item2.Length - 2);
                Int2 outValue = default;
                int.TryParse(item1, out outValue.X);
                int.TryParse(item2, out outValue.Y);
                return outValue;
            }
            return base.ConvertFrom(context, culture, value);
        }
    }

    public static class NumberConverter {
        public static object? ConvertTo(double value, Type type) {
            if (type == typeof(sbyte)) return (sbyte)Math.Clamp(value, sbyte.MinValue, sbyte.MaxValue);
            if (type == typeof(byte)) return (byte)Math.Clamp(value, byte.MinValue, byte.MaxValue);
            if (type == typeof(short)) return (short)Math.Clamp(value, short.MinValue, short.MaxValue);
            if (type == typeof(ushort)) return (ushort)Math.Clamp(value, ushort.MinValue, ushort.MaxValue);
            if (type == typeof(int)) return (int)Math.Clamp(value, int.MinValue, int.MaxValue);
            if (type == typeof(uint)) return (uint)Math.Clamp(value, uint.MinValue, uint.MaxValue);
            if (type == typeof(long)) return (long)Math.Clamp(value, long.MinValue, long.MaxValue);
            if (type == typeof(ulong)) return (ulong)Math.Clamp(value, ulong.MinValue, ulong.MaxValue);
            if (type == typeof(float)) return (float)value;
            if (type == typeof(double)) return value;
            return null;
        }
    }
}
