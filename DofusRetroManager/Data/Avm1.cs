using System.IO;
using System.IO.Compression;
using System.Text;

namespace DofusRetroManager.Data;

/// <summary>Objet ActionScript : un simple dictionnaire de membres.</summary>
public sealed class AsObject : Dictionary<string, object?>
{
    public AsObject() : base(StringComparer.Ordinal) { }
}

/// <summary>
/// Interpréteur AVM1 minimal, suffisant pour exécuter les fichiers de langue de Dofus Retro
/// (lang/swf/*.swf) qui ne contiennent que des affectations de données
/// (ex: CR[44] = [[303, 1], [312, 2]]; I.u[44] = {n: "...", l: 10}).
/// Le résultat est l'ensemble des variables globales.
/// </summary>
public static class Avm1
{
    public static AsObject Run(byte[] swf)
    {
        var globals = new AsObject();
        foreach (var code in ExtractDoActions(swf))
            Execute(code, globals);
        return globals;
    }

    static IEnumerable<byte[]> ExtractDoActions(byte[] swf)
    {
        byte[] data;
        if (swf[0] == (byte)'C')
        {
            using var z = new ZLibStream(new MemoryStream(swf, 8, swf.Length - 8), CompressionMode.Decompress);
            using var ms = new MemoryStream();
            ms.Write(swf, 0, 8);
            z.CopyTo(ms);
            data = ms.ToArray();
        }
        else if (swf[0] == (byte)'F') data = swf;
        else throw new InvalidDataException("Format SWF non supporté");

        int p = 8;
        int nbits = data[p] >> 3;
        p += (5 + nbits * 4 + 7) / 8 + 4; // RECT + frame rate + frame count
        while (p + 2 <= data.Length)
        {
            int h = BitConverter.ToUInt16(data, p); p += 2;
            int code = h >> 6, len = h & 0x3F;
            if (len == 0x3F) { len = BitConverter.ToInt32(data, p); p += 4; }
            if (code == 12) yield return data.AsSpan(p, len).ToArray(); // DoAction
            if (code == 0) yield break;
            p += len;
        }
    }

    static void Execute(byte[] c, AsObject globals)
    {
        var stack = new Stack<object?>();
        var pool = new List<string>();
        var regs = new object?[256];
        int p = 0;

        object? Pop() => stack.Count > 0 ? stack.Pop() : null;

        while (p < c.Length)
        {
            byte op = c[p++];
            if (op == 0) break;
            int len = 0, start = p;
            if (op >= 0x80) { len = BitConverter.ToUInt16(c, p); p += 2; start = p; }

            switch (op)
            {
                case 0x88: // ConstantPool
                {
                    pool.Clear();
                    int n = BitConverter.ToUInt16(c, start), q = start + 2;
                    for (int i = 0; i < n; i++) pool.Add(ReadString(c, ref q));
                    break;
                }
                case 0x96: // Push
                {
                    int q = start, end = start + len;
                    while (q < end)
                    {
                        byte t = c[q++];
                        switch (t)
                        {
                            case 0: stack.Push(ReadString(c, ref q)); break;
                            case 1: stack.Push((double)BitConverter.ToSingle(c, q)); q += 4; break;
                            case 2: case 3: stack.Push(null); break;
                            case 4: stack.Push(regs[c[q++]]); break;
                            case 5: stack.Push(c[q++] != 0); break;
                            case 6:
                            {
                                // Double AVM1 : les deux mots de 32 bits sont inversés
                                var tmp = new byte[8];
                                Array.Copy(c, q + 4, tmp, 0, 4);
                                Array.Copy(c, q, tmp, 4, 4);
                                stack.Push(BitConverter.ToDouble(tmp, 0)); q += 8; break;
                            }
                            case 7: stack.Push((double)BitConverter.ToInt32(c, q)); q += 4; break;
                            case 8: stack.Push(pool[c[q++]]); break;
                            case 9: stack.Push(pool[BitConverter.ToUInt16(c, q)]); q += 2; break;
                            default: q = end; break;
                        }
                    }
                    break;
                }
                case 0x17: Pop(); break; // Pop
                case 0x1C: // GetVariable
                {
                    var name = ToStr(Pop());
                    stack.Push(ResolvePath(globals, name));
                    break;
                }
                case 0x1D: // SetVariable
                {
                    var v = Pop(); var name = ToStr(Pop());
                    globals[name] = v;
                    break;
                }
                case 0x3C: // DefineLocal
                {
                    var v = Pop(); var name = ToStr(Pop());
                    globals[name] = v;
                    break;
                }
                case 0x41: Pop(); break; // DefineLocal2
                case 0x4E: // GetMember
                {
                    var key = Pop(); var obj = Pop();
                    stack.Push(GetMember(obj, key));
                    break;
                }
                case 0x4F: // SetMember
                {
                    var v = Pop(); var key = Pop(); var obj = Pop();
                    SetMember(obj, key, v);
                    break;
                }
                case 0x42: // InitArray
                {
                    int n = (int)ToNum(Pop());
                    var list = new List<object?>(n);
                    for (int i = 0; i < n; i++) list.Add(Pop());
                    stack.Push(list);
                    break;
                }
                case 0x43: // InitObject
                {
                    int n = (int)ToNum(Pop());
                    var o = new AsObject();
                    for (int i = 0; i < n; i++) { var v = Pop(); var k = ToStr(Pop()); o[k] = v; }
                    stack.Push(o);
                    break;
                }
                case 0x40: // NewObject
                {
                    Pop(); int n = (int)ToNum(Pop());
                    for (int i = 0; i < n; i++) Pop();
                    stack.Push(new AsObject());
                    break;
                }
                case 0x3D: // CallFunction
                {
                    Pop(); int n = (int)ToNum(Pop());
                    for (int i = 0; i < n; i++) Pop();
                    stack.Push(null);
                    break;
                }
                case 0x52: // CallMethod
                {
                    Pop(); Pop(); int n = (int)ToNum(Pop());
                    for (int i = 0; i < n; i++) Pop();
                    stack.Push(null);
                    break;
                }
                case 0x4C: stack.Push(stack.Count > 0 ? stack.Peek() : null); break; // PushDuplicate
                case 0x4D: { var a = Pop(); var b = Pop(); stack.Push(a); stack.Push(b); break; } // StackSwap
                case 0x87: regs[c[start]] = stack.Count > 0 ? stack.Peek() : null; break; // StoreRegister
                case 0x0A: case 0x47: // Add / Add2
                {
                    var b = Pop(); var a = Pop();
                    if (a is string || b is string) stack.Push(ToStr(a) + ToStr(b));
                    else stack.Push(ToNum(a) + ToNum(b));
                    break;
                }
                case 0x0B: { var b = ToNum(Pop()); var a = ToNum(Pop()); stack.Push(a - b); break; }
                case 0x0C: { var b = ToNum(Pop()); var a = ToNum(Pop()); stack.Push(a * b); break; }
                case 0x0D: { var b = ToNum(Pop()); var a = ToNum(Pop()); stack.Push(a / b); break; }
                case 0x12: stack.Push(!Truthy(Pop())); break; // Not
                case 0x99: p = start + len + (short)BitConverter.ToUInt16(c, start); continue; // Jump
                case 0x9D: // If
                {
                    if (Truthy(Pop())) { p = start + len + (short)BitConverter.ToUInt16(c, start); continue; }
                    break;
                }
                case 0x9B: case 0x8E: // DefineFunction(2) : on ignore le corps
                {
                    int codeSize = BitConverter.ToUInt16(c, start + len - 2);
                    p = start + len + codeSize;
                    stack.Push(null);
                    continue;
                }
                default:
                    break; // opérations non utilisées par les fichiers de données
            }
            p = start + len;
        }
    }

    static object? ResolvePath(AsObject globals, string name)
    {
        object? cur = null;
        var parts = name.Split('.');
        for (int i = 0; i < parts.Length; i++)
            cur = i == 0 ? (globals.TryGetValue(parts[0], out var g) ? g : null) : GetMember(cur, parts[i]);
        return cur;
    }

    static object? GetMember(object? obj, object? key)
    {
        if (obj is AsObject o) return o.TryGetValue(KeyStr(key), out var v) ? v : null;
        if (obj is List<object?> l)
        {
            if (KeyStr(key) == "length") return (double)l.Count;
            int i = (int)ToNum(key);
            return i >= 0 && i < l.Count ? l[i] : null;
        }
        return null;
    }

    static void SetMember(object? obj, object? key, object? v)
    {
        if (obj is AsObject o) o[KeyStr(key)] = v;
        else if (obj is List<object?> l)
        {
            int i = (int)ToNum(key);
            if (i < 0) return;
            while (l.Count <= i) l.Add(null);
            l[i] = v;
        }
    }

    static string ReadString(byte[] c, ref int q)
    {
        int s = q;
        while (q < c.Length && c[q] != 0) q++;
        var str = Encoding.UTF8.GetString(c, s, q - s);
        q++;
        return str;
    }

    static string KeyStr(object? k) => k is double d && d == Math.Floor(d) ? ((long)d).ToString() : ToStr(k);

    public static string ToStr(object? v) => v switch
    {
        null => "",
        string s => s,
        double d => d == Math.Floor(d) ? ((long)d).ToString() : d.ToString(System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        _ => v.ToString() ?? ""
    };

    public static double ToNum(object? v) => v switch
    {
        double d => d,
        bool b => b ? 1 : 0,
        string s when double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) => d,
        _ => 0
    };

    static bool Truthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0 && !double.IsNaN(d),
        string s => s.Length > 0,
        _ => true
    };
}
