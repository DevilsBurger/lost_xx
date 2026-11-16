using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace LostRegalia.Sim
{
    /// <summary>
    /// 의존성 없는 JSON 읽기. data/ 의 규칙 파일을 읽는 데 쓴다.
    /// 객체는 Dictionary&lt;string, object&gt;, 배열은 List&lt;object&gt;, 수는 double, 그 밖에 string·bool·null.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Ws(s, ref i);
            if (i != s.Length) throw new FormatException("JSON 끝에 남은 글자가 있다: " + i);
            return v;
        }

        static void Ws(string s, ref int i)
        {
            while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
        }

        static object Value(string s, ref int i)
        {
            Ws(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON이 중간에 끝났다");
            char c = s[i];
            if (c == '{')
            {
                i++;
                var d = new Dictionary<string, object>();
                Ws(s, ref i);
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    Ws(s, ref i);
                    string k = Str(s, ref i);
                    Ws(s, ref i);
                    if (s[i] != ':') throw new FormatException("':'이 와야 한다: " + i);
                    i++;
                    d[k] = Value(s, ref i);
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("',' 또는 '}'이 와야 한다: " + i);
                }
            }
            if (c == '[')
            {
                i++;
                var l = new List<object>();
                Ws(s, ref i);
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value(s, ref i));
                    Ws(s, ref i);
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("',' 또는 ']'이 와야 한다: " + i);
                }
            }
            if (c == '"') return Str(s, ref i);
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            if (string.CompareOrdinal(s, i, "null", 0, 4) == 0) { i += 4; return null; }
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            if (i == start) throw new FormatException("알 수 없는 값: " + i);
            return double.Parse(s.Substring(start, i - start), CultureInfo.InvariantCulture);
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("문자열이 와야 한다: " + i);
            i++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: sb.Append(e); break;
                }
            }
        }
    }

    /// <summary>파싱한 JSON을 꺼내 쓰는 짧은 도우미.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o) { return (Dictionary<string, object>)o; }
        public static List<object> Arr(object o) { return (List<object>)o; }
        public static float Num(object o) { return o == null ? 0f : (float)(double)o; }
        public static int Int(object o) { return o == null ? 0 : (int)Math.Round((double)o); }
        public static string Str(object o) { return (string)o; }

        public static float[] Nums(object o)
        {
            var l = Arr(o);
            var r = new float[l.Count];
            for (int i = 0; i < r.Length; i++) r[i] = Num(l[i]);
            return r;
        }
    }
}
