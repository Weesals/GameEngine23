using UnityEngine;
using System.Collections;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;

/// <summary>
/// An allocation-less and fast streaming JSON parser
/// Using the provided enumerators, it will only parse
/// as much as is needed to fulfil the current request
/// </summary>
public struct SJson : IEnumerable<SJson> {

    // Does this Json element have valid data
    public bool IsValid { get { return Data.Length > 0; } }

    // Get the type of data contained in this element
    public bool IsObject { get { return Data[Start] == '{'; } }
    public bool IsArray { get { return Data[Start] == '['; } }
    public bool IsString { get { return Data[Start] == '"'; } }
    public bool IsNumeric {
        get {
            bool num = false;
            int s = Start;
            while (s < End && char.IsWhiteSpace(Data[s])) ++s;
            if (s < End && Data[s] == '-') ++s;
            while (s < End && char.IsNumber(Data[s])) { num = true; ++s; }
            if (s < End && Data[s] == '.') ++s;
            while (s < End && char.IsNumber(Data[s])) ++s;
            while (s < End && char.IsWhiteSpace(Data[s])) ++s;
            return s == End && num;
        }
    }

    // Data fields; this json relates to Data.Substring(Start, End-Start)
    public readonly string Data;
    public readonly int Start;
    public readonly int End;

    public SJson(string data) : this(data, 0, data.Length) { }
    public SJson(string data, int start, int end) {
        Data = data;
        Start = start;
        End = end;
    }

    // Only if IsObject. Gets (the first of) a named field
    public SJson this[string field] {
        get {
            foreach (var (name, value) in GetFields()) {
                if (name.Equals(field)) return value;
            }
            return Invalid;
        }
    }

    // Gets the value of the element. Must be compatible type.
    public static implicit operator string (SJson json) {
        if (json.Start + 2 < json.Data.Length && json.IsString) {
            return json.ToString();
        } else if (json.Start + 1 < json.Data.Length && json.IsNumeric) {
            return json.ToString();
        } else {
            switch(json.ToString()) {
                case "null": return null;
                case "unknown": return null;
                default: throw new FormatException(json.ToString());
            }
        }
    }
    public static implicit operator float (SJson json) { return float.Parse(json.AsSpanRaw(), provider: CultureInfo.InvariantCulture); }
    public static implicit operator int (SJson json) { return int.Parse(json.AsSpanRaw(), provider: CultureInfo.InvariantCulture); }
    public static implicit operator long(SJson json) { return long.Parse(json.AsSpanRaw(), provider: CultureInfo.InvariantCulture); }
    public static implicit operator bool (SJson json) { return bool.Parse(json.AsSpanRaw()); }

    // Get the value, or return an error if value could not be parsed
    // (to avoid throwing exceptions)
    public int OrDefault(int defValue) {
        int value; return int.TryParse(AsSpanRaw(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : defValue;
    }
    public float OrDefault(float defValue) {
        float value; return float.TryParse(AsSpanRaw(), NumberStyles.AllowThousands | NumberStyles.Float, CultureInfo.InvariantCulture, out value) ? value : defValue;
    }
    public bool OrDefault(bool defValue) {
        bool value; return bool.TryParse(AsSpanRaw(), out value) ? value : defValue;
    }

    // Compare with target after cleaning up string
    public bool Equals(string other) {
        var it = new StringIterator(Data, Start, End);
        var i = 0;
        for (; it.MoveNext(); ++i) if (i >= other.Length || it.Current != other[i]) return false;
        return i == other.Length;
    }
    // Remove quotes and special characaters
    public override string ToString() {
        var it = new StringIterator(Data, Start, End);
        if (!it.IsQuoted) return Data.Substring(Start, End - Start);
        var builder = new System.Text.StringBuilder();
        while (it.MoveNext()) builder.Append(it.Current);
        return builder.ToString();
    }
    public ReadOnlySpan<char> AsSpanRaw() { return Data.AsSpan(Start, End - Start); }

    // Helper methods to parse easily
    private void SkipWhitespace(ref int i) {
        while (true) {
            while (i < End && char.IsWhiteSpace(Data[i])) ++i;
            if (i + 2 >= End || Data[i] != '/' || Data[i + 1] != '*') break;
            i = Data.IndexOf("*/", i + 2, End - (i + 2));
            if (i == -1) i = End; else i += 2;
        }
    }
    private int SkipString(int i) {
        Debug.Assert(Data[i] == '"', "JSON strings must begin with quotes '\"'");
        for (++i; i < End; ++i) {
            switch (Data[i]) {
                case '\\': ++i; break;
                case '"': return i + 1;
            }
        }
        return i;
    }
    private int SkipItem(int i) {
        switch (Data[i]) {
            case '"': return SkipString(i);
            case '{':
            case '[': {
                ++i;
                for (; i < End;) {
                    switch (Data[i]) {
                        case '"': i = SkipString(i); break;
                        case '{': case '[': i = SkipItem(i); break;
                        case '}': case ']': return i + 1;
                        default: i++; break;
                    }
                }
            }
            break;
            default: {
                while (char.IsLetterOrDigit(Data[i]) || Data[i] == '_' || Data[i] == '-' || Data[i] == '.')
                    ++i;
            }
            break;
        }
        return i;
    }
    private int GetArrayItemEnd(ref int i) {
        int iend = i = SkipItem(i);
        SkipWhitespace(ref i);
        if (i < End && Data[i] == ',') ++i;
        SkipWhitespace(ref i);
        return iend;
    }

    /// <summary>
    /// Helper iterator to decode strings
    /// </summary>
    public struct StringIterator : IEnumerator<char> {
        public readonly string Str;
        private int it;
        private int end;
        public bool IsQuoted { get; private set; }
        public char Current { get; private set; }
        object IEnumerator.Current => Current;
        public StringIterator(string source, int strStart, int strEnd) {
            Str = source;
            it = strStart;
            end = strEnd;
            IsQuoted = it < end && source[it] == '"' && source[end - 1] == '"';
            if (IsQuoted) { ++it; --this.end; }
            Current = default;
        }
        public void Dispose() { }
        public void Reset() { throw new NotImplementedException(); }
        public bool MoveNext() {
            if (it >= end) return false;
            Current = Str[it++];
            if (IsQuoted && Current == '\\') {
                if (it >= end) return false;
                Current = Str[it++];
                switch (Current) {
                    case '"':
                    case '\\':
                    case '/': break;
                    case 'b': Current = '\b'; break;
                    case 'f': Current = '\f'; break;
                    case 'n': Current = '\n'; break;
                    case 'r': Current = '\r'; break;
                    case 't': Current = '\t'; break;
                    case 'u': {
                        int hex = 0;
                        for (int h = 0; h < 4 && it < end; h++) hex = (Str[it++] - '0') + hex * 16;
                        Current = (char)hex;
                    }
                    break;
                }
                return true;
            }
            return true;
        }
    }

    IEnumerator IEnumerable.GetEnumerator() { return GetEnumerator(); }
    IEnumerator<SJson> IEnumerable<SJson>.GetEnumerator() { return GetEnumerator(); }
    public ChildEnumerator GetEnumerator() { return new ChildEnumerator(this); }
    public FieldEnumerator GetFields() { return new FieldEnumerator(this); }

    /// <summary>
    /// Allows iterating all children, only valid if IsArray
    /// </summary>
    public struct ChildEnumerator : IEnumerator<SJson> {
        public SJson Parent;
        private SJson child;
        private int it;
        public SJson Current => child;
        object IEnumerator.Current => Current;
        public ChildEnumerator(SJson parent) {
            Parent = parent;
            child = default;
            it = Parent.Start + 1;
        }
        public void Dispose() { }
        public void Reset() { it = Parent.Start + 1; }
        public bool MoveNext() {
            Parent.SkipWhitespace(ref it);
            int istart = it;
            int iend = Parent.GetArrayItemEnd(ref it);
            if (iend == istart) return false;
            child = new SJson(Parent.Data, istart, iend);
            return true;
        }
    }

    /// <summary>
    /// Allows iterating all fields, only valid if IsObject
    /// </summary>
    public struct FieldEnumerator : IEnumerator<KeyValuePair<SJson, SJson>> {
        public SJson Parent;
        private SJson fieldName;
        private SJson fieldValue;
        private int it;
        public KeyValuePair<SJson, SJson> Current => new KeyValuePair<SJson, SJson>(fieldName, fieldValue);
        object IEnumerator.Current => Current;
        public FieldEnumerator(SJson parent) : this() {
            Parent = parent;
            fieldName = fieldValue = default;
            it = Parent.Start + 1;
        }
        public void Dispose() { }
        public void Reset() { it = Parent.Start + 1; }
        public bool MoveNext() {
            for (; it < Parent.End;) {
                Parent.SkipWhitespace(ref it);
                // Parse name
                int lstart = it;
                int lend = it = Parent.SkipItem(it);
                if (lend == lstart) break;
                Parent.SkipWhitespace(ref it);
                if (Parent.Data[it] == ':') ++it;
                Parent.SkipWhitespace(ref it);
                // Parse value
                int vstart = it;
                int vend = it = Parent.SkipItem(it);
                Parent.SkipWhitespace(ref it);
                if (it < Parent.End && Parent.Data[it] == ',') ++it;
                // Success!
                fieldName = new SJson(Parent.Data, lstart, lend);
                fieldValue = new SJson(Parent.Data, vstart, vend);
                return true;
            }
            return false;
        }
        // Only here so can use code foreach(var field in item.GetFields())
        public FieldEnumerator GetEnumerator() { return new FieldEnumerator(Parent); }
    }

    public static readonly SJson Invalid = new SJson("");
}
