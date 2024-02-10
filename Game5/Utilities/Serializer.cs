using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Weesals.Engine;

namespace Game5 {
    public abstract class Serializer {

        public bool IsWriting { get { return !IsReading; } }
        public abstract bool IsReading { get; }
        public abstract long Position { get; }
        public abstract bool HasMore { get; }

        public int Version;

        public virtual void HintNewline() { }

        protected abstract Stream ReplaceStream(Stream stream);

        public abstract bool Serialize(ref bool data);
        public abstract bool Serialize(ref char data);
        public abstract bool Serialize(ref short data);
        public abstract bool Serialize(ref ushort data);
        public abstract bool Serialize(ref int data);
        public abstract bool Serialize(ref uint data);
        public abstract bool Serialize(ref long data);
        public abstract bool Serialize(ref float data);
        public abstract bool Serialize(ref string data);
        public abstract bool Serialize(ref byte data);
        public abstract bool Serialize(ref sbyte data);
        public abstract bool Serialize(ref byte[] data);
        public abstract bool Serialize(ref byte[] data, ref int len);
        public bool Serialize(ref int[] data) {
            int scount = (data != null ? data.Length : -1);
            bool res = Serialize(ref scount);
            if (scount >= 0) {
                if (scount != (data != null ? data.Length : 0)) data = new int[scount];
                for (int s = 0; s < scount; ++s) {
                    res |= Serialize(ref data[s]);
                }
            } else data = null;
            return res;
        }
        public bool Serialize(ref string[] data) {
            int scount = (data != null ? data.Length : -1);
            bool res = Serialize(ref scount);
            if (scount >= 0) {
                if (scount != (data != null ? data.Length : 0)) data = new string[scount];
                for (int s = 0; s < scount; ++s) {
                    res |= Serialize(ref data[s]);
                }
            } else data = null;
            return res;
        }

        public bool Serialize(ref DateTime tmpDate) {
            long ticks = tmpDate.Ticks;
            if (Serialize(ref ticks)) {
                tmpDate = new DateTime(ticks);
                return true;
            }
            return false;
        }
        public bool SerializeLQ(ref DateTime date) {
            var lqEpoch = new DateTime(1990, 1, 1);
            var elapsed = date > lqEpoch ? date - lqEpoch : TimeSpan.Zero;
            var ticks = (uint)(elapsed.Ticks / TimeSpan.TicksPerSecond);
            if (Serialize(ref ticks)) {
                date = lqEpoch + new TimeSpan(ticks * TimeSpan.TicksPerSecond);
                return true;
            }
            return false;
        }
        public bool SerializeLQ(ref TimeSpan timeSpan) {
            const long Stride = TimeSpan.TicksPerSecond / 2;
            var s2L = (timeSpan.Ticks / Stride);
            var s2 = s2L >= uint.MaxValue ? uint.MaxValue : (uint)s2L;
            if (Serialize(ref s2)) {
                if (s2 == uint.MaxValue) timeSpan = TimeSpan.MaxValue;
                timeSpan = TimeSpan.FromTicks(s2 * Stride);
                return true;
            }
            return false;
        }
        public bool SerializeLQ(ref Vector3 pos) {
            SerializeLQ(ref pos.X);
            SerializeLQ(ref pos.Y);
            return SerializeLQ(ref pos.Z);
        }
        public bool SerializeLQ(ref Vector2 pos) {
            SerializeLQ(ref pos.X);
            return SerializeLQ(ref pos.Y);
        }
        // Precision: 0.25, Range: [0, 64]
        public bool SerializeLQ(ref float v) {
            const float LQConv = 4.0f;
            v = (float)SerializeInline((byte)(v * LQConv + 0.5f)) / LQConv;
            return IsReading;
        }
        // Precision: 0.05, Range: [-1638.4, 1638.35]
        public bool SerializeMQ(ref float v) {
            const float LQConv = 20.0f;
            v = (short)SerializeInline((short)Math.Round(v * LQConv)) / LQConv;
            return IsReading;
        }

        public bool Serialize(ref Quaternion rot) {
            Serialize(ref rot.X);
            Serialize(ref rot.Y);
            Serialize(ref rot.Z);
            return Serialize(ref rot.W);
        }
        public bool Serialize(ref Vector2 pos) {
            Serialize(ref pos.X);
            return Serialize(ref pos.Y);
        }
        public bool Serialize(ref Vector3 pos) {
            Serialize(ref pos.X);
            Serialize(ref pos.Y);
            return Serialize(ref pos.Z);
        }
        public bool Serialize(ref Color col) {
            Serialize(ref col.R);
            Serialize(ref col.G);
            Serialize(ref col.B);
            return Serialize(ref col.A);
        }
        public bool Serialize(ref Int2 pos) {
            pos.X = SerializeInline(pos.X);
            pos.Y = SerializeInline(pos.Y);
            return IsReading;
        }

        public bool SerializeMQ(ref Int2 pos) {
            pos.X = SerializeInline((short)pos.X);
            pos.Y = SerializeInline((short)pos.Y);
            return IsReading;
        }

        public bool SerializeInline(bool value) { Serialize(ref value); return value; }
        public string SerializeInline(string value) { Serialize(ref value); return value; }
        public char SerializeInline(char value) { Serialize(ref value); return value; }
        public byte SerializeInline(byte value) { Serialize(ref value); return value; }
        public sbyte SerializeInline(sbyte value) { Serialize(ref value); return value; }
        public short SerializeInline(short value) { Serialize(ref value); return value; }
        public ushort SerializeInline(ushort value) { Serialize(ref value); return value; }
        public int SerializeInline(int value) { Serialize(ref value); return value; }
        public uint SerializeInline(uint value) { Serialize(ref value); return value; }
        public float SerializeInline(float value) { Serialize(ref value); return value; }
        public long SerializeInline(long value) { Serialize(ref value); return value; }
        public Vector2 SerializeInline(Vector2 value) { Serialize(ref value); return value; }
        public Vector3 SerializeInline(Vector3 value) { Serialize(ref value); return value; }
        public Int2 SerializeInline(Int2 value) { Serialize(ref value); return value; }
        public Quaternion SerializeInline(Quaternion value) { Serialize(ref value); return value; }
        public Color SerializeInline(Color value) { Serialize(ref value); return value; }

        public bool Guard(string dbgstr) {
            string input = dbgstr;
            try { Serialize(ref dbgstr); } catch { dbgstr = ""; }
            if (dbgstr == input) return true;
            Debug.WriteLine($"Guard violated {input}");
            return false;
        }

        public struct Scope : IDisposable {
            public readonly Serializer Serializer;
            public readonly string Name;
            private Stream baseStream;
            private MemoryStream stream;
            public Scope(string name, Serializer serializer) {
                Name = name;
                Serializer = serializer;
                if (serializer.IsWriting) {
                    baseStream = serializer.ReplaceStream(stream = new MemoryStream());
                } else if (serializer.IsReading) {
                    byte[] data = null;
                    serializer.Serialize(ref data);
                    baseStream = serializer.ReplaceStream(stream = new MemoryStream(data));
                } else {
                    throw new NotImplementedException("Serializer is not supported by Scope");
                }
            }
            public void Dispose() {
                Serializer.ReplaceStream(baseStream);
                if (Serializer.IsWriting) { var data = stream.ToArray(); Serializer.Serialize(ref data); }
            }
        }
        public Scope BeginScope(string name = "") { return new Scope(name, this); }
    }

    public class SerialWriter : Serializer {
        internal BinaryWriter writer;

        public SerialWriter(Stream stream) : this(new BinaryWriter(stream, System.Text.Encoding.UTF8)) { }
        public SerialWriter(BinaryWriter _writer) {
            writer = _writer;
        }

        public void Flush() { writer.Flush(); }

        public override bool IsReading { get { return false; } }
        public override long Position { get { return writer.BaseStream.Position; } }
        public override bool HasMore { get { return true; } }

        protected override Stream ReplaceStream(Stream stream) { writer.Flush(); var oldStream = writer.BaseStream; writer = new BinaryWriter(stream); return oldStream; }
        public override bool Serialize(ref bool data) { writer.Write(data); return false; }
        public override bool Serialize(ref char data) { writer.Write(data); return false; }
        public override bool Serialize(ref byte data) { writer.Write(data); return false; }
        public override bool Serialize(ref sbyte data) { writer.Write(data); return false; }
        public override bool Serialize(ref short data) { writer.Write(data); return false; }
        public override bool Serialize(ref ushort data) { writer.Write(data); return false; }
        public override bool Serialize(ref int data) { writer.Write(data); return false; }
        public override bool Serialize(ref uint data) { writer.Write(data); return false; }
        public override bool Serialize(ref long data) { writer.Write(data); return false; }
        public override bool Serialize(ref float data) { writer.Write(data); return false; }
        public override bool Serialize(ref string data) { writer.Write(data != null ? data : ""); return false; }
        public override bool Serialize(ref byte[] data) { if (data == null) writer.Write((Int16)(-1)); else { writer.Write(data.Length); writer.Write(data); } return false; }
        public override bool Serialize(ref byte[] data, ref int len) { if (data == null) writer.Write((Int16)(-1)); else { writer.Write((Int16)len); writer.Write(data, 0, len); } return false; }

    }

    public class SerialReader : Serializer {

        internal BinaryReader reader;

        public SerialReader(byte[] data) : this(new MemoryStream(data)) { }
        public SerialReader(Stream stream) : this(new BinaryReader(stream, System.Text.Encoding.UTF8)) { }
        public SerialReader(BinaryReader _reader) {
            reader = _reader;
        }

        public override bool IsReading { get { return true; } }
        public override long Position { get { return reader.BaseStream.Position; } }
        public override bool HasMore { get { return reader.BaseStream.Position < reader.BaseStream.Length; } }

        protected override Stream ReplaceStream(Stream stream) { var oldStream = reader.BaseStream; reader = new BinaryReader(stream); return oldStream; }
        public override bool Serialize(ref bool data) { data = reader.ReadBoolean(); return true; }
        public override bool Serialize(ref char data) { data = reader.ReadChar(); return true; }
        public override bool Serialize(ref byte data) { data = reader.ReadByte(); return true; }
        public override bool Serialize(ref sbyte data) { data = reader.ReadSByte(); return true; }
        public override bool Serialize(ref short data) { data = reader.ReadInt16(); return true; }
        public override bool Serialize(ref ushort data) { data = reader.ReadUInt16(); return true; }
        public override bool Serialize(ref int data) { data = reader.ReadInt32(); return true; }
        public override bool Serialize(ref uint data) { data = reader.ReadUInt32(); return true; }
        public override bool Serialize(ref long data) { data = reader.ReadInt64(); return true; }
        public override bool Serialize(ref float data) { data = reader.ReadSingle(); return true; }
        public override bool Serialize(ref string data) { data = reader.ReadString(); return true; }
        public override bool Serialize(ref byte[] data) { int len = reader.ReadInt16(); if (len >= 0) data = reader.ReadBytes(len); return true; }
        public override bool Serialize(ref byte[] data, ref int len) { len = reader.ReadInt16(); if (len >= 0) data = reader.ReadBytes(len); return true; }

    }


    public abstract class SerialText : Serializer {

        public enum States { Ready, Block, Word, };
        protected States state = States.Ready;
        protected void BeginState(States _state) {
            bool reqWS = false;
            switch (_state) {
                case States.Word: reqWS = true; break;
                case States.Block: reqWS = _state != States.Block; break;
            }
            if (reqWS && state != States.Ready) RequireWhitespace();
            state = _state;
        }

        public override void HintNewline() {
            if (state != States.Ready) {
                RequireWhitespace('\r');
                RequireWhitespace('\n');
            }
        }

        protected abstract void RequireWhitespace(char ws = ' ');

    }

    public class SerialWriterText : SerialText, IDisposable {
        internal StreamWriter writer;

        void WriteWord(string word) {
            BeginState(States.Word);
            writer.Write(word);
        }
        protected override void RequireWhitespace(char ws) {
            writer.Write(ws);
            state = States.Ready;
        }

        public SerialWriterText(Stream stream) : this(new StreamWriter(stream, System.Text.Encoding.UTF8)) { }
        public SerialWriterText(StreamWriter _writer) { writer = _writer; }

        public override bool IsReading { get { return false; } }
        public override long Position { get { return writer.BaseStream.Position; } }
        public override bool HasMore { get { return true; } }

        protected override Stream ReplaceStream(Stream stream) { writer.Flush(); var oldStream = writer.BaseStream; writer = new StreamWriter(stream); return oldStream; }

        public override bool Serialize(ref bool data) { WriteWord(data ? "true" : "false"); return false; }
        public override bool Serialize(ref char data) { BeginState(States.Block); writer.Write(data); return false; }
        public override bool Serialize(ref byte data) { BeginState(States.Block); writer.Write(data.ToString("X2")); return false; }
        public override bool Serialize(ref sbyte data) { BeginState(States.Block); writer.Write(data.ToString("X2")); return false; }
        public override bool Serialize(ref short data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref ushort data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref int data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref uint data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref long data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref float data) { WriteWord(data.ToString()); return false; }
        public override bool Serialize(ref string data) { WriteWord(data != null ? data : ""); return false; }
        public override bool Serialize(ref byte[] data) { if (data == null) WriteWord(""); else { WriteWord(Convert.ToBase64String(data)); } return false; }
        public override bool Serialize(ref byte[] data, ref int len) { throw new NotImplementedException(); }

        public void Dispose() {
            writer.Flush();
        }
    }

    public class SerialReaderText : SerialText {

        internal StreamReader reader;

        public SerialReaderText(byte[] data) : this(new MemoryStream(data)) { }
        public SerialReaderText(Stream stream) : this(new StreamReader(stream, System.Text.Encoding.UTF8)) { }
        public SerialReaderText(StreamReader _reader) { reader = _reader; }

        public override bool IsReading { get { return true; } }
        public override long Position { get { return reader.BaseStream.Position; } }
        public override bool HasMore { get { return reader.BaseStream.Position < reader.BaseStream.Length; } }

        static StringBuilder strBuilder = new StringBuilder();
        private string ReadWord() {
            BeginState(States.Word);
            for (char c; !char.IsWhiteSpace(c = (char)reader.Peek());) { reader.Read(); strBuilder.Append(c); }
            var str = strBuilder.ToString();
            strBuilder.Length = 0;
            return str;
        }
        private int ToHex(char c) {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            throw new ArgumentOutOfRangeException();
        }
        private int ReadHex(int len) {
            int r = 0;
            for (int l = 0; l < len; ++l) r = r * 16 + ToHex((char)reader.Read());
            return r;
        }

        protected override void RequireWhitespace(char _ws) {
            var ws = (char)reader.Read();
            Debug.Assert(char.IsWhiteSpace(ws), "Required whitespace, not " + ws);
            state = States.Ready;
        }

        protected override Stream ReplaceStream(Stream stream) { var oldStream = reader.BaseStream; reader = new StreamReader(stream); return oldStream; }

        public override bool Serialize(ref bool data) { data = ReadWord() == "true"; return true; }
        public override bool Serialize(ref char data) { BeginState(States.Block); data = (char)reader.Read(); return true; }
        public override bool Serialize(ref byte data) { BeginState(States.Block); data = (byte)ReadHex(2); return true; }
        public override bool Serialize(ref sbyte data) { BeginState(States.Block); data = (sbyte)ReadHex(2); return true; }
        public override bool Serialize(ref short data) { data = short.Parse(ReadWord()); return true; }
        public override bool Serialize(ref ushort data) { data = ushort.Parse(ReadWord()); return true; }
        public override bool Serialize(ref int data) { data = int.Parse(ReadWord()); return true; }
        public override bool Serialize(ref uint data) { data = uint.Parse(ReadWord()); return true; }
        public override bool Serialize(ref long data) { data = long.Parse(ReadWord()); return true; }
        public override bool Serialize(ref float data) { data = float.Parse(ReadWord()); return true; }
        public override bool Serialize(ref string data) { data = ReadWord(); return true; }
        public override bool Serialize(ref byte[] data) { data = Convert.FromBase64String(ReadWord()); return true; }
        public override bool Serialize(ref byte[] data, ref int len) { throw new NotImplementedException(); }

    }
}
