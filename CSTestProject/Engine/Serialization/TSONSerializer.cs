using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Weesals.Utility;

/* 
 * Name{Name:Value,BName!BINARY}BName!BINARYName:Value~~Name:Value
 * Name[!:]Data (!=Binary, :=Text/Value)
 * Binary: @:8-bit-zero, =:Terminator
 */

namespace Weesals.Engine.Serialization {
    public interface ISerializable {
        void Serialize(ref TSONNode node);
    }
    public class DataBuffer {
        public byte[] Data;
        public int Position;
        public int DataLen;

        public DataBuffer() : this(Array.Empty<byte>(), 0, 0) { }
        public DataBuffer(int capacity) : this(new byte[capacity], 0, 0) { }
        public DataBuffer(byte[] data) : this(data, 0, data.Length) { }
        public DataBuffer(byte[] data, int offset, int length) {
            Data = data;
            Position = offset;
            DataLen = length;
        }
        public void RequireWriteLength(int size) {
            size += DataLen;
            if (Data.Length < size)
                Array.Resize(ref Data, (int)BitOperations.RoundUpToPowerOf2((uint)size + 200));
        }
        public void RequireReadLength(int size) {
            Debug.Assert(Position + size <= DataLen);
        }
        public byte TryPeekByte() {
            return Position >= DataLen ? default : Data[Position];
        }
        public byte ReadByte() {
            return Data[Position++];
        }
        public void WriteByte(byte v) {
            Data[DataLen++] = v;
        }
        public Span<byte> WriteLength(int size) {
            RequireWriteLength(size);
            var begin = DataLen;
            DataLen += size;
            return Data.AsSpan(begin, size);
        }
        public Span<byte> ReadLength(int size) {
            RequireReadLength(size);
            var begin = Position;
            Position += size;
            return Data.AsSpan(begin, size);
        }
        public Span<byte> AsSpan() {
            return Data.AsSpan(0, DataLen);
        }
        public override string ToString() {
            return Encoding.UTF8.GetString(Data.AsSpan(0, DataLen));
        }
    }
    public interface ISerialFormatter {
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref byte value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref sbyte value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref short value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref ushort value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref int value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref uint value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref long value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref ulong value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref float value);
        void Serialize(ref SerialFormatter.SerialBuffer buffer, ref double value);
    }
    public interface IIntegerSerializer {
        void Serialize(ref long value);
        void Serialize(ref ulong value);
    }
    public interface IBinarySerializer {
        bool IsReading { get; }
        bool IsWriting { get; }
        void Serialize(ref Span<byte> data);
        void Serialize(scoped Span<byte> data);
    }

    public class SerialFormatter {
        public enum SymbolMask : byte {
            Numbers = 1 << 0,
            Letters = 1 << 1,
            Symbols = 1 << 2,
            Terminator = 1 << 4,
            Text = Numbers | Letters,
            Base64 = Numbers | Letters | Symbols,
            Anything = 0xff,
        }
        public struct CharacterSet {
            public SymbolMask AllowedSymbols;
            public byte Terminator;
            public CharacterSet(SymbolMask symbols, byte terminator) {
                AllowedSymbols = symbols;
                Terminator = terminator;
            }
            public static readonly CharacterSet Number = new CharacterSet(SymbolMask.Numbers | SymbolMask.Symbols, (byte)',');
            public static readonly CharacterSet Base64 = new CharacterSet(SymbolMask.Base64, (byte)'=');
            public static readonly CharacterSet Anything = new CharacterSet(SymbolMask.Anything, (byte)',');
        }
        public class SerialBuffer {
            public DataBuffer Buffer;
            public CharacterSet TerminatorSet;
            public bool IsWriting;
            public SerialBuffer(DataBuffer buffer, bool isWriting) {
                Buffer = buffer;
                IsWriting = isWriting;
            }
            public void RequireWriteLength(int size) => Buffer.RequireWriteLength(size);
            public void RequireReadLength(int size) => Buffer.RequireReadLength(size);
            public byte TryPeekByte() => Buffer.TryPeekByte();
            public byte ReadByte() => Buffer.ReadByte();
            public bool TryReadMatch(byte v) {
                if (Buffer.TryPeekByte() != v) return false;
                Buffer.ReadByte(); return true;
            }
            public void BeginWriting(SymbolMask symbol) {
                if (TerminatorSet.AllowedSymbols != default) {
                    if ((TerminatorSet.AllowedSymbols & symbol) != default) {
                        if (IsWriting) {
                            Buffer.RequireWriteLength(1);
                            Buffer.WriteByte(TerminatorSet.Terminator);
                        } else {
                            if (Buffer.TryPeekByte() == TerminatorSet.Terminator)
                                Buffer.ReadByte();
                        }
                    }
                    TerminatorSet = default;
                }
            }
            public void WriteByte(byte v) {
                Buffer.WriteByte(v);
            }
            public void RequireTerminator(CharacterSet set) {
                if (!IsWriting) {
                    if (Buffer.TryPeekByte() == set.Terminator) Buffer.ReadByte();
                } else {
                    Debug.Assert(TerminatorSet.AllowedSymbols == default,
                        "IDK what to do here (yet)");
                }
                TerminatorSet = set;
            }
            public void ClearTerminator() {
                TerminatorSet = default;
            }
            public Span<byte> WriteLength(int size) => Buffer.WriteLength(size);
            public ReadOnlySpan<byte> ReadLength(int size) => Buffer.ReadLength(size);
        }
        public virtual void Begin(ref SerialBuffer buffer) { }
        public virtual void End(ref SerialBuffer buffer) { }
    }
    public class SerialTextFormatter : SerialFormatter, ISerialFormatter {
        public void Serialize(ref SerialBuffer buffer, ref byte value) {
            if (buffer.IsWriting) WriteULong(ref buffer, value); else value = (byte)ReadULong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref sbyte value) {
            if (buffer.IsWriting) WriteLong(ref buffer, value); else value = (sbyte)ReadLong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref short value) {
            if (buffer.IsWriting) WriteLong(ref buffer, value); else value = (short)ReadLong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref ushort value) {
            if (buffer.IsWriting) WriteULong(ref buffer, value); else value = (ushort)ReadULong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref int value) {
            if (buffer.IsWriting) WriteLong(ref buffer, value); else value = (int)ReadLong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref uint value) {
            if (buffer.IsWriting) WriteULong(ref buffer, value); else value = (uint)ReadULong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref long value) {
            if (buffer.IsWriting) WriteLong(ref buffer, value); else value = (long)ReadLong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref ulong value) {
            if (buffer.IsWriting) WriteULong(ref buffer, value); else value = (ulong)ReadULong(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref float value) {
            if (buffer.IsWriting) WriteDbl(ref buffer, value); else value = (float)ReadDbl(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref double value) {
            if (buffer.IsWriting) WriteDbl(ref buffer, value); else value = ReadDbl(ref buffer);
        }
        public void Serialize(ref SerialBuffer buffer, ref string name) {
            if (buffer.IsWriting) WriteText(ref buffer, name); else name = ReadText(ref buffer);
        }

        public bool SerializeMatch(ref SerialBuffer buffer, SymbolMask symbol, string value) {
            buffer.BeginWriting(symbol);
            if (buffer.IsWriting) WriteMatch(ref buffer, value); else return ReadMatch(ref buffer, value);
            buffer.ClearTerminator();
            return true;
        }

        private void WriteLong(ref SerialBuffer buffer, long value) {
            var uvalue = (ulong)value;
            if (value < 0) {
                buffer.BeginWriting(SymbolMask.Symbols);
                buffer.RequireWriteLength(2);
                buffer.WriteByte((byte)'-');
                uvalue = (ulong)-value;
            }
            WriteULong(ref buffer, uvalue);
        }
        private long ReadLong(ref SerialBuffer buffer) {
            buffer.BeginWriting(SymbolMask.Symbols | SymbolMask.Numbers);
            bool negative = buffer.TryReadMatch((byte)'-');
            var uvalue = ReadULong(ref buffer);
            return negative ? -(long)uvalue : (long)uvalue;
        }
        private void WriteULong(ref SerialBuffer buffer, ulong value) {
            buffer.BeginWriting(SymbolMask.Numbers);
            int start = buffer.Buffer.DataLen;
            do {
                var next = value / 10;
                buffer.RequireWriteLength(1);
                buffer.WriteByte((byte)('0' + (value - next * 10)));
                value = next;
            } while (value > 0);
            Array.Reverse(buffer.Buffer.Data, start, buffer.Buffer.DataLen - start);
            buffer.RequireTerminator(CharacterSet.Number);
        }
        private ulong ReadULong(ref SerialBuffer buffer) {
            buffer.BeginWriting(SymbolMask.Numbers);
            ulong value = 0;
            while (true) {
                var c = buffer.TryPeekByte();
                if (c == 0 || !char.IsNumber((char)c)) break;
                buffer.ReadByte();
                value = value * 10 + (c - (ulong)'0');
            }
            buffer.RequireTerminator(CharacterSet.Number);
            return value;
        }

        private void WriteDbl(ref SerialBuffer buffer, double value) {
            buffer.BeginWriting(SymbolMask.Numbers);
            WriteMatch(ref buffer, value.ToString());
            buffer.RequireTerminator(CharacterSet.Number);
        }
        private double ReadDbl(ref SerialBuffer buffer) {
            buffer.BeginWriting(SymbolMask.Numbers);
            var start = buffer.Buffer.Position;
            buffer.TryReadMatch((byte)'-');
            while (char.IsNumber((char)buffer.TryPeekByte())) buffer.ReadByte();
            buffer.TryReadMatch((byte)'.');
            while (char.IsNumber((char)buffer.TryPeekByte())) buffer.ReadByte();
            if (buffer.TryReadMatch((byte)'e')) {
                buffer.ReadByte();
                buffer.TryReadMatch((byte)'-');
                while (char.IsNumber((char)buffer.TryPeekByte())) buffer.ReadByte();
            }
            buffer.RequireTerminator(CharacterSet.Number);
            return float.Parse(buffer.Buffer.Data.AsSpan(start, buffer.Buffer.Position - start));
        }

        private bool GetIsTextChar(char c) {
            return char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.' || c == '\\';
        }
        public void WriteText(ref SerialBuffer buffer, string name) {
            buffer.BeginWriting(SymbolMask.Text | SymbolMask.Symbols);
            buffer.RequireWriteLength(name.Length);
            for (int i = 0; i < name.Length; i++) {
                var c = name[i];
                if (!GetIsTextChar(c)) buffer.WriteByte((byte)'\\');
                buffer.WriteByte((byte)c);
            }
        }
        private string ReadText(ref SerialBuffer buffer) {
            buffer.BeginWriting(SymbolMask.Text | SymbolMask.Symbols);
            using var output = new PooledList<char>();
            while (true) {
                var c = (char)buffer.TryPeekByte();
                if (!GetIsTextChar(c)) break;
                buffer.ReadByte();
                if (c == '\\') { c = (char)buffer.ReadByte(); }
                output.Add(c);
            }
            if (output.Count == 0) return string.Empty;
            return new string(output.AsSpan());
        }
        private void WriteMatch(ref SerialBuffer buffer, string str) {
            buffer.RequireWriteLength(str.Length);
            for (int i = 0; i < str.Length; i++) buffer.WriteByte((byte)str[i]);
        }
        private bool ReadMatch(ref SerialBuffer buffer, string str) {
            var pos = buffer.Buffer.Position;
            buffer.RequireReadLength(str.Length);
            for (int i = 0; i < str.Length; i++) {
                if (buffer.ReadByte() == str[i]) continue;
                buffer.Buffer.Position = pos;
                return false;
            }
            return true;
        }

        public static readonly SerialTextFormatter Default = new();
    }
    public class SerialBinaryFormatter : SerialFormatter, ISerialFormatter {
        public void Serialize(ref SerialBuffer buffer, ref byte value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref sbyte value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref short value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref ushort value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref int value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref uint value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref long value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref ulong value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref float value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref double value) { SerializeT(ref buffer, ref value); }

        public void Serialize(ref SerialBuffer buffer, ref DateTime value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref TimeSpan value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref Span<byte> value) {
            var len = value.Length;
            Serialize(ref buffer, ref len);
            if (buffer.IsWriting) {
                value.CopyTo(buffer.WriteLength(len));
            } else {
                value = buffer.Buffer.ReadLength(len);
            }
        }
        public void Serialize(ref SerialBuffer buffer, Span<byte> value) {
            if (buffer.IsWriting) {
                value.CopyTo(buffer.WriteLength(value.Length));
            } else {
                buffer.ReadLength(value.Length).CopyTo(value);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SerializeT<T>(ref SerialBuffer buffer, ref T value) where T : unmanaged {
            if (buffer.IsWriting) WriteBytes(ref buffer, value);
            else ReadBytes(ref buffer, ref value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe static private void ReadBytes<T>(ref SerialBuffer buffer, ref T value) where T : unmanaged {
            value = MemoryMarshal.Read<T>(buffer.ReadLength(sizeof(T)));
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe static private void WriteBytes<T>(ref SerialBuffer buffer, T value) where T : unmanaged {
            MemoryMarshal.Write(buffer.WriteLength(sizeof(T)), value);
        }

        public static readonly SerialBinaryFormatter Default = new();
    }
    public class SerialB64Formatter : SerialFormatter, ISerialFormatter {
        public static readonly byte[] Base64Characters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_"u8.ToArray();

        [ThreadStatic] private static ulong bitBuffer;
        [ThreadStatic] private static int bitCount;

        public override void Begin(ref SerialBuffer buffer) {
            bitBuffer = 0;
            bitCount = 0;
            base.Begin(ref buffer);
        }
        public override void End(ref SerialBuffer buffer) {
            base.End(ref buffer);
            Flush(ref buffer, 1);
            buffer.RequireTerminator(CharacterSet.Base64);
        }
        public void Serialize(ref SerialBuffer buffer, ref byte value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref sbyte value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref short value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref ushort value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref int value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref uint value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref long value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref ulong value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref float value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref double value) { SerializeT(ref buffer, ref value); }

        public void Serialize(ref SerialBuffer buffer, ref DateTime value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref TimeSpan value) { SerializeT(ref buffer, ref value); }
        public void Serialize(ref SerialBuffer buffer, ref Span<byte> value) {
            var len = value.Length;
            Serialize(ref buffer, ref len);
            if (buffer.IsWriting) {
                WriteBytes(ref buffer, value);
            } else {
                if (value.Length < len) value = new byte[len];
                else value = value.Slice(0, len);
                ReadBytes(ref buffer, value);
            }
        }
        public void Serialize(ref SerialBuffer buffer, Span<byte> value) {
            if (buffer.IsWriting) {
                WriteBytes(ref buffer, value);
            } else {
                ReadBytes(ref buffer, value);
            }
        }

        // Flushes until there are less than `flushBits` remaining
        private void Flush(ref SerialBuffer buffer, int flushBits) {
            if (!buffer.IsWriting) return;
            while (bitCount >= flushBits) {
                int zeroBits = Math.Min(BitOperations.LeadingZeroCount(bitBuffer << (64 - bitCount)), bitCount);
                char chr = '\0';
                if (zeroBits >= 24) { bitCount -= 24; chr = '@'; } else if (zeroBits >= 20) { bitCount -= 20; chr = '$'; } else if (zeroBits >= 16) { bitCount -= 16; chr = '~'; } else if (zeroBits >= 10) { bitCount -= 10; chr = '^'; } else if (zeroBits >= 8) { bitCount -= 8; chr = '.'; }
                if (chr != '\0') {
                    buffer.BeginWriting(SymbolMask.Symbols);
                    buffer.RequireWriteLength(1);
                    buffer.WriteByte((byte)chr);
                    continue;
                }
                bitCount -= 6;
                var b64 = (bitBuffer >> bitCount) & 63;
                buffer.BeginWriting(
                    b64 < 26 * 2 ? SymbolMask.Letters :
                    b64 < 26 * 2 + 10 ? SymbolMask.Numbers :
                    SymbolMask.Symbols);
                buffer.RequireWriteLength(1);
                buffer.WriteByte(Base64Characters[b64]);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void SerializeT<T>(ref SerialBuffer buffer, ref T value) where T : unmanaged {
            var valueBytes = MemoryMarshal.Cast<T, byte>(new Span<T>(ref value));
            if (buffer.IsWriting) WriteBytes(ref buffer, valueBytes);
            else ReadBytes(ref buffer, valueBytes);
        }
        private static readonly byte[] B64Lookup = new byte[] {
            255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // 00
            255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, // 16
            255, 255, 255, 255, 120, 255, 255, 255, 255, 255, 255, 062, 255, 062, 108, 063, // 32
            052, 053, 054, 055, 056, 057, 058, 059, 060, 061, 255, 255, 255, 255, 255, 255, // 48
            124, 000, 001, 002, 003, 004, 005, 006, 007, 008, 009, 010, 011, 012, 013, 014, // 64
            015, 016, 017, 018, 019, 020, 021, 022, 023, 024, 025, 255, 255, 255, 110, 063, // 80
            255, 026, 027, 028, 029, 030, 031, 032, 033, 034, 035, 036, 037, 038, 039, 040, // 96
            041, 042, 043, 044, 045, 046, 047, 048, 049, 050, 051, 255, 255, 255, 116, 255, // 128
        };
        unsafe private void ReadBytes(ref SerialBuffer buffer, Span<byte> value) {
            Debug.Assert(buffer.Buffer.Position < buffer.Buffer.DataLen);
            for (int i = 0; ;) {
                int reqBits = Math.Min(40, (value.Length - i) << 3);
                while (bitCount < reqBits) {
                    var b = buffer.TryPeekByte();
                    var v = B64Lookup[b];
                    if (v < 100) {
                        bitBuffer = (bitBuffer << 6) | v;
                        bitCount += 6;
                    } else if (v < 255) {
                        int zeroBits = v - 100;
                        bitBuffer <<= zeroBits;
                        bitCount += zeroBits;
                    } else break;
                    buffer.ReadByte();
                }
                for (; ; ) {
                    bitCount -= 8;
                    value[i++] = (byte)(bitBuffer >> bitCount);
                    if (i >= value.Length) {
                        if (bitCount < 0) bitCount = 0;
                        return;
                    }
                    if (bitCount < 8) break;
                }
            }
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        unsafe private void WriteBytes(ref SerialBuffer buffer, ReadOnlySpan<byte> value) {
            buffer.RequireWriteLength(value.Length);
            for (int i = 0; i < value.Length; i++) {
                bitBuffer = (bitBuffer << 8) + value[i];
                bitCount += 8;
                Flush(ref buffer, 16);
            }
        }

        public static int GetBase64Index(char c) {
            const int AlphabetSize = ('Z' - 'A' + 1);
            var v = (c - 'A') & 0xdf;
            if ((uint)v < AlphabetSize) return v + (c >= 'a' ? AlphabetSize : 0);
            return
                //c >= 'A' && c <= 'Z' ? c - 'A' :
                //c >= 'a' && c <= 'z' ? c - 'a' + AlphabetSize :
                c >= '0' && c <= '9' ? c - '0' + AlphabetSize * 2 :
                c == '+' || c == '-' ? AlphabetSize * 2 + 10 :
                c == '/' || c == '_' ? AlphabetSize * 2 + 11 :
                -1;
        }

        public static readonly SerialB64Formatter Default = new();
    }

    public struct TSONNode : IDisposable, IIntegerSerializer {
        public readonly string? Name;
        private SerialFormatter.SerialBuffer buffer;
        private SerialFormatter? formatter;

        public bool IsValid => Name != null;

        public bool IsWriting => buffer.IsWriting;
        public bool IsReading => !buffer.IsWriting;

        private TSONNode(string? name, SerialFormatter.SerialBuffer buffer) {
            this.Name = name;
            this.buffer = buffer;
            this.formatter = default;
        }
        public void Dispose() {
            if (IsValid) {
                var textFormatter = RequireText();
                if (!buffer.IsWriting) {
                    while (buffer.TryPeekByte() != '}') buffer.ReadByte();
                }
                textFormatter.SerializeMatch(ref buffer, SerialFormatter.SymbolMask.Terminator, "}");
            }
        }
        public TSONNode CreateChild(string? name) {
            if (IsWriting && name == null) return default;
            var textFormatter = RequireText();
            textFormatter.Serialize(ref buffer, ref name);
            if (string.IsNullOrEmpty(name)) name = null;
            textFormatter.SerializeMatch(ref buffer, SerialFormatter.SymbolMask.Terminator, "{");
            return new(name, buffer);
        }
        public B64Node CreateBinary() {
            SetFormatter<SerialFormatter>(default!);
            return new(buffer);
        }
        public BinaryNode CreateRawBinary() {
            SetFormatter<SerialFormatter>(default!);
            return new(buffer);
        }

        private SerialBinaryFormatter RequireBinary() {
            return SetFormatter(SerialBinaryFormatter.Default);
        }
        private SerialTextFormatter RequireText() {
            return SetFormatter(SerialTextFormatter.Default);
        }

        private T SetFormatter<T>(T newFormatter) where T : SerialFormatter {
            if (formatter != newFormatter) {
                if (formatter != null) formatter.End(ref buffer);
                formatter = newFormatter;
                if (formatter != null) formatter.Begin(ref buffer);
            }
            return newFormatter;
        }

        public void Serialize(ref byte value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref sbyte value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref short value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref ushort value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref int value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref uint value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref long value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref ulong value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref float value) { RequireText().Serialize(ref buffer, ref value); }
        public void Serialize(ref double value) { RequireText().Serialize(ref buffer, ref value); }

        public void Serialize(ref DateTime value) { var ticks = value.Ticks; RequireText().Serialize(ref buffer, ref ticks); value = new(ticks); }
        public void Serialize(ref TimeSpan value) { var ticks = value.Ticks; RequireText().Serialize(ref buffer, ref ticks); value = new(ticks); }

        /*public struct ListSerializer<T> {
            private List<T> items;
            public delegate T Action(ref TSONNode serializer, ListSerializer<T> value);
            public ListSerializer(List<T> items) {
                this.items = items;
            }
        }
        public void Serialize<T>(List<T> items, ListSerializer<T>.Action callback) {
            int len = items.Count;
            Serialize(ref len);
            var listSerializer = new ListSerializer<T>(items);
            for (int i = 0; i < len; i++) {
                callback(ref this, listSerializer);
            }
        }*/
        public struct SerializeHelper<T> {
            public delegate void Serialize(T value, ref TSONNode serializer);
        }
        public void Serialize<T>(List<T> items
            , Func<T, string> nameGetter
            , Func<string, T> generator
            , SerializeHelper<T>.Serialize serializer
            ) {
            for (int i = 0; ; i++) {
                if (buffer.IsWriting) {
                    if (i >= items.Count) break;
                    var item = items[i];
                    var sChild = CreateChild(nameGetter(item));
                    serializer(item, ref sChild);
                } else {
                    var sChild = CreateChild(null);
                    if (string.IsNullOrEmpty(sChild.Name)) break;
                    int i2 = i;
                    for (; i2 < items.Count; i2++) {
                        if (nameGetter(items[i2]) == sChild.Name) break;
                    }
                    if (i2 > i && i2 != items.Count) {
                        var t = items[i2];
                        items[i2] = items[i];
                        items[i] = t;
                        i2 = i;
                    }
                    var item = default(T);
                    if (i2 == i && i < items.Count) item = items[i2];
                    else items.Insert(i, item = generator(sChild.Name));
                    serializer(item, ref sChild);
                }
            }
        }

        /*public struct ListSerializer2<T> : IEnumerator<T> {
            public T Current => throw new NotImplementedException();
            object IEnumerator.Current => throw new NotImplementedException();
            public void Dispose() { }
            public void Reset() { }
            public bool MoveNext() {
                return false;
            }
            public ListSerializer2<T> GetEnumerator() => this;
        }
        public ListSerializer2<T> CreateList<T>(List<T> items, Func<T, string> nameGetter, Func<string, T> creator) {
            return new();
        }*/

        //public void Serialize(ref DateTime value) { RequireText().Serialize(ref buffer, ref value); }
        //public void Serialize(ref TimeSpan value) { RequireText().Serialize(ref buffer, ref value); }
        //public void Serialize(ref Span<byte> value) { RequireText().Serialize(ref buffer, ref value); }

        public static TSONNode CreateWrite(DataBuffer dataBuffer) {
            return new(null, new(dataBuffer, true));
        }
        public static TSONNode CreateRead(DataBuffer dataBuffer) {
            return new(null, new(dataBuffer, false));
        }
    }
    public interface ISimpleSerializer<T> where T : ISerialFormatter {
        T GetSerializer();
        ref SerialFormatter.SerialBuffer GetBuffer();
    }
    public struct B64Node : IDisposable, IBinarySerializer {
        private SerialFormatter.SerialBuffer buffer;

        public bool IsReading => !buffer.IsWriting;
        public bool IsWriting => buffer.IsWriting;

        public B64Node(SerialFormatter.SerialBuffer buffer) {
            this.buffer = buffer;
            Require().Begin(ref buffer);
        }
        public void Dispose() {
            Require().End(ref buffer);
        }

        private SerialB64Formatter Require() {
            return SerialB64Formatter.Default;
        }

        SerialB64Formatter GetSerializer() => Require();
        unsafe ref SerialFormatter.SerialBuffer GetBuffer() => ref buffer;

        public void Serialize(ref Span<byte> data) { Require().Serialize(ref buffer, ref data); }
        public void Serialize(Span<byte> data) { Require().Serialize(ref buffer, data); }
    }
    public struct BinaryNode : IDisposable, IBinarySerializer {
        private SerialFormatter.SerialBuffer buffer;

        public bool IsReading => !buffer.IsWriting;
        public bool IsWriting => buffer.IsWriting;

        public BinaryNode(SerialFormatter.SerialBuffer buffer) {
            this.buffer = buffer;
            Require().Begin(ref buffer);
        }
        public void Dispose() {
            Require().End(ref buffer);
        }

        private SerialBinaryFormatter Require() {
            return SerialBinaryFormatter.Default;
        }

        SerialBinaryFormatter GetSerializer() => Require();
        unsafe ref SerialFormatter.SerialBuffer GetBuffer() => ref buffer;

        public void Serialize(ref Span<byte> data) { Require().Serialize(ref buffer, ref data); }
        public void Serialize(Span<byte> data) { Require().Serialize(ref buffer, data); }
    }

    public static class SimpleSerializerExt {
        public static void Serialize<S>(this S serializer, ref byte v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref sbyte v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref short v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref ushort v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref int v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref uint v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref long v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref ulong v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref float v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);
        public static void Serialize<S>(this S serializer, ref double v) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref v);

        public static void Serialize<S>(this S serializer, ref DateTime value) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref value);
        public static void Serialize<S>(this S serializer, ref TimeSpan value) where S : IBinarySerializer
            => serializer.SerializeUnmanaged(ref value);

        public static void Serialize<S>(this S serializer, ref CSIdentifier v) where S : IBinarySerializer {
            var name = v.GetAscii();
            var length = serializer.SerializeInline((ushort)name.Length);
            if (serializer.IsWriting) {
                serializer.Serialize(name);
            } else {
                Span<byte> tmpData = stackalloc byte[length];
                serializer.Serialize(tmpData);
                v = new(tmpData);
            }
        }

        public static bool Require<S>(this S serializer, ReadOnlySpan<byte> data) where S : IBinarySerializer {
            Span<byte> tmp = stackalloc byte[data.Length];
            if (serializer.IsWriting) data.CopyTo(tmp);
            serializer.Serialize(tmp);
            return serializer.IsWriting || data.SequenceEqual(tmp);
        }

        public static void SerializeUnmanaged<S, T>(this S serializer, ref T v) where S : IBinarySerializer where T : unmanaged
            => serializer.Serialize(MemoryMarshal.Cast<T, byte>(new Span<T>(ref v)));

        public static T SerializeInline<S, T>(this S serializer, T v) where S : IBinarySerializer where T : unmanaged {
            serializer.Serialize(MemoryMarshal.Cast<T, byte>(new Span<T>(ref v)));
            return v;
        }

    }
}
