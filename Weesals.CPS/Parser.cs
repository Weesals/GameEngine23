using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Weesals.CPS {
    public struct Parser : IEquatable<string> {
        public string String;
        public int I;
        public int End;
        public bool IsValid => End > 0;

        public bool IsAtEnd => I >= End;
        public int Length => End - I;

        public char this[int index] { get { return String[I + index]; } }

        public Parser(string str, int start = 0, int end = -1) {
            if (end == -1) end = str.Length;
            String = str;
            I = start;
            End = end;
        }

        public bool Require(char chr) {
            if (!Match(chr)) {
                //Debug.LogError("Failed to match " + chr);
                return false;
            }
            return true;
        }
        public string AsString() {
            // TODO: Use SJson.StringIterator
            return String.Substring(I, End - I);
        }
        public ReadOnlySpan<char> AsSpan() { return String.AsSpan(I, End - I); }
        public override string ToString() { return AsString(); }
        public bool Equals(string? other) { return other!.Length == Length && string.CompareOrdinal(String, I, other, 0, Length) == 0; }
        public static implicit operator ReadOnlySpan<char>(Parser p) { return p.AsSpan(); }

        public char PeekChar() {
            SkipWhitespace();
            return I < String.Length ? String[I] : '\0';
        }
        public string PeekString(int maxLength = int.MaxValue, string defaultValue = "") {
            var strIt = new StringIterator(String, I, End);
            if (strBuilder == null) strBuilder = new(); else strBuilder.Clear();
            for (; strIt.MoveNext();) strBuilder.Append(strIt.Current);
            return strBuilder.ToString();
            /*Debug.Assert(I <= End, "Invalid I value");
            var len = Mathf.Min(End - I, maxLength);
            if (len == 0) return defaultValue;
            return String.Substring(I, len);*/
        }

        public bool PeekMatch(char match) {
            if (Length < 1) return false;
            return String[I] == match;
        }
        public bool PeekMatch(string match) {
            if (Length < match.Length) return false;
            return (string.CompareOrdinal(String, I, match, 0, match.Length) == 0);
        }
        public bool PeekMatch(Parser match) {
            if (Length < match.Length) return false;
            return (string.CompareOrdinal(String, I, match.String, match.I, match.Length) == 0);
        }

        public bool Match(char match) {
            if (!PeekMatch(match)) return false;
            ++I;
            SkipWhitespace();
            return true;
        }
        public bool Match(string match) {
            if (!PeekMatch(match)) return false;
            I += match.Length;
            SkipWhitespace();
            return true;
        }
        public bool Match(Parser match) {
            if (!PeekMatch(match)) return false;
            I += match.Length;
            SkipWhitespace();
            return true;
        }

        public Parser MatchWord() {
            Parser.SkipComments(ref this);
            var start = I;
            if (I >= End || !char.IsLetter(String[I])) return Parser.Empty;
            while (I < End && char.IsLetterOrDigit(String[I])) ++I;
            var r = new Parser(String, start, I);
            SkipWhitespace();
            return r;
        }

        public void SkipWhitespace() {
            while (I < End && char.IsWhiteSpace(String[I])) ++I;
        }
        public void Skip(int maxLength = int.MaxValue) {
            var len = Math.Min(End - I, maxLength);
            I += len;
            SkipWhitespace();
        }

        public Number TakeNumber() { var r = ReadNumber(String, ref I, End); SkipWhitespace(); return r; }
        public string TakeString() {
            var strIt = new StringIterator(String, I, End);
            if (strBuilder == null) strBuilder = new(); else strBuilder.Clear();
            for (; strIt.MoveNext();) strBuilder.Append(strIt.Current);
            I = strIt.Index;
            return strBuilder.ToString();
        }
        private static StringBuilder strBuilder;

        public struct Number {
            public bool IsValid { get { return Source != null; } }
            public string Source;
            public bool Negative;
            public int NumeratorI;
            public int DecimalI;
            public int EndI;
            public bool IsInteger { get { return IsValid && DecimalI == EndI; } }
            public bool IsFloat { get { return IsValid && EndI > DecimalI; } }
            public int ReadInteger() {
                int val = 0;
                for (int r = NumeratorI; r < DecimalI; ++r) val = val * 10 + (Source[r] - '0');
                return val * (Negative ? -1 : 1);
            }
            public float ReadFloat() {
                float val = 0;
                for (int r = NumeratorI; r < DecimalI - 1; ++r) val = val * 10 + (Source[r] - '0');
                float d = 1 / 10f;
                for (int r = DecimalI; r < EndI; ++r) val += d * (Source[r] - '0');
                return val * (Negative ? -1 : 1);
            }
        }
        public static Number ReadNumber(string str, ref int s, int end) {
            int r = s;
            var n = new Number() { Source = str, };
            n.Negative = r < end && str[r] == '-';
            if (n.Negative) ++r;
            n.NumeratorI = r;
            while (r < end && str[r] >= '0' && str[r] <= '9') { ++r; }
            if (r <= s + (n.Negative ? 1 : 0)) return default;
            if (r < end && str[r] == '.') {
                ++r;
                n.DecimalI = r;
                while (r < end && str[r] >= '0' && str[r] <= '9') { ++r; }
            } else {
                n.DecimalI = r;
            }
            n.EndI = r;
            s = r;
            return n;
        }

        public bool MatchFunction(string name, out Parser parameters) {
            parameters = Parser.Empty;
            if (!PeekMatch(name)) return false;
            int i = I + name.Length;
            while (i < End && char.IsWhiteSpace(String[i])) ++i;
            if (i >= End || String[i] != '(') return false;
            int e = ParseBrackets(String, i, End);
            //if (e >= End) return false;
            parameters = new Parser(String, i + 1, e);
            if (e < End) ++e;
            I = e;
            SkipWhitespace();
            return true;
        }

        public Parser TakeBrackets() {
            var e = ParseBrackets(String, I, End);
            var res = new Parser(String, I, e);
            I = e + (e < End ? 1 : 0);
            return res;
        }

        public struct Parameter {
            public Parser Named;
            public Parser Value;
            public bool IsValid { get { return Value.IsValid && Value.Length > 0; } }
            public static implicit operator Parser(Parameter param) { return param.Value; }
        }
        public Parameter TakeParameter() {
            int n = -1;
            int i = I;
            for (; i < End; ++i) {
                if (n == -1 && String[i] == ':') { n = i; continue; }
                if (String[i] == ',') break;
                // Terminate parameter list
                if (String[i] == ')' || String[i] == '}' || String[i] == ']') break;
                if (String[i] == '"') {
                    i = ParseString(String, i, End);
                    if (i >= End) break;
                }
                if (String[i] == '(' || String[i] == '{' || String[i] == '[') {
                    i = ParseBrackets(String, i, End);
                    if (i >= End) break;
                }
            }
            var param = new Parameter();
            if (n >= 0) { param.Named = new Parser(String, I, n); I = n + 1; SkipWhitespace(); }
            param.Value = i > I ? new Parser(String, I, i) : Parser.Empty;
            I = i;
            Match(',');
            return param;
        }


        public static int ParseBrackets(string path, int s) { return ParseBrackets(path, s, path.Length); }
        public static int ParseBrackets(string path, int s, int end) {
            int brack = 0;
            for (; s < end; ++s) {
                SkipComments(path, ref s, end);
                if (s >= end) break;
                switch (path[s]) {
                    case '(': case '{': case '[': brack++; break;
                    case ')':
                    case '}':
                    case ']':
                    if (--brack == 0) return s;
                    break;
                    case '"': {
                        s = ParseString(path, s, end);
                        if (brack == 0 || s >= end) return s;
                    }
                    break;
                }
            }
            return s;
        }
        public static int ParseString(string path, int s) { return ParseString(path, s, path.Length); }
        public static int ParseString(string path, int s, int end) {
            //Debug.Assert(path[s] == '"', "String must begin with '\"'");
            for (++s; s < end; ++s) {
                if (path[s] == '\\') { ++s; continue; }
                if (path[s] == '"') { break; }
            }
            return s;
        }

        public static void SkipComments(ref Parser instance) {
            SkipComments(instance.String, ref instance.I, instance.End);
        }
        public static void SkipComments(string str, ref int i, int end) {
            while (true) {
                // Skip whitespace
                while (i < end && char.IsWhiteSpace(str[i])) ++i;
                // Find first slash
                if (i >= end - 1 || str[i] != '/') break;
                if (str[i + 1] == '*') {
                    // Skip block comment
                    for (i += 4; i < end; ++i) if (str[i - 2] == '*' && str[i - 1] == '/') break;
                } else if (str[i + 1] == '/') {
                    // Skip newline comment
                    for (i += 2; i < end; ++i) if (IsNewline(str[i])) break;
                    for (; i < end; ++i) if (!IsNewline(str[i])) break;
                } else break;
            }
        }

        private static bool IsNewline(char c) { return c == '\r' || c == '\n'; }


        /// <summary>
        /// Helper iterator to decode strings
        /// </summary>
        public struct StringIterator : IEnumerator<char> {
            public readonly string Str;
            private int it;
            private int end;
            public bool IsQuoted { get; private set; }
            public char Current { get; private set; }
            public int Index => it;
            object IEnumerator.Current => Current;
            public StringIterator(string source, int strStart, int strEnd) {
                Str = source;
                it = strStart;
                end = strEnd;
                IsQuoted = it < end && source[it] == '"';
                if (IsQuoted) ++it;
                --it;
                Current = default;
            }
            public void Dispose() { }
            public void Reset() { throw new NotImplementedException(); }
            public bool MoveNext() {
                ++it;
                if (it >= end) return false;
                Current = Str[it];
                if (Current == '"') return false;
                if (IsQuoted && Current == '\\') {
                    ++it;
                    if (it >= end) return false;
                    Current = Str[it];
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


        public static readonly Parser Empty = new Parser(string.Empty, 0, 0);
    }
}
