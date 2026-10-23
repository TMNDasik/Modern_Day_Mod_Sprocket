using System;
using System.IO;
using System.Text;

namespace SoundOverhaul;


internal static class Wav
{
    internal sealed class Data
    {
        public string Path;
        public string Name;
        public float[] Samples;
        public int Channels, Rate, Frames;
    }

    public static Data Read(string path)
    {
        var b = File.ReadAllBytes(path);
        if (b.Length < 12 || Encoding.ASCII.GetString(b, 0, 4) != "RIFF" || Encoding.ASCII.GetString(b, 8, 4) != "WAVE")
            throw new InvalidDataException("not a RIFF/WAVE file");
        int format = 0, channels = 0, rate = 0, bits = 0, dataOff = -1, dataLen = 0;
        var pos = 12;
        while (pos + 8 <= b.Length)
        {
            var id = Encoding.ASCII.GetString(b, pos, 4);
            var len = BitConverter.ToInt32(b, pos + 4);
            var body = pos + 8;
            if (len < 0 || body + len > b.Length) len = b.Length - body; 
            if (id == "fmt " && len >= 16)
            {
                format = BitConverter.ToUInt16(b, body);
                channels = BitConverter.ToUInt16(b, body + 2);
                rate = BitConverter.ToInt32(b, body + 4);
                bits = BitConverter.ToUInt16(b, body + 14);
                if (format == 0xFFFE && len >= 26) format = BitConverter.ToUInt16(b, body + 24);
            }
            else if (id == "data")
            {
                dataOff = body;
                dataLen = len;
                break;
            }
            pos = body + len + (len & 1);
        }
        if (dataOff < 0 || channels <= 0 || rate <= 0) throw new InvalidDataException("no fmt/data chunk");
        var bytesPer = bits / 8;
        if (bytesPer <= 0) throw new InvalidDataException("bad bit depth");
        var count = dataLen / bytesPer;
        count -= count % channels;
        var s = new float[count];
        if (format == 3 && bits == 32)
            for (var i = 0; i < count; i++) s[i] = BitConverter.ToSingle(b, dataOff + i * 4);
        else if (format == 1 || format == 0xFFFE || format == 0)
        {
            switch (bits)
            {
                case 8:
                    for (var i = 0; i < count; i++) s[i] = (b[dataOff + i] - 128) / 128f;
                    break;
                case 16:
                    for (var i = 0; i < count; i++) s[i] = BitConverter.ToInt16(b, dataOff + i * 2) / 32768f;
                    break;
                case 24:
                    for (var i = 0; i < count; i++)
                    {
                        var o = dataOff + i * 3;
                        var v = (b[o] | (b[o + 1] << 8) | (b[o + 2] << 16)) << 8 >> 8;
                        s[i] = v / 8388608f;
                    }
                    break;
                case 32:
                    for (var i = 0; i < count; i++) s[i] = BitConverter.ToInt32(b, dataOff + i * 4) / 2147483648f;
                    break;
                default:
                    throw new InvalidDataException($"unsupported PCM bit depth {bits}");
            }
        }
        else throw new InvalidDataException($"unsupported WAV format {format}");
        return new Data
        {
            Path = path,
            Name = System.IO.Path.GetFileNameWithoutExtension(path),
            Samples = s,
            Channels = channels,
            Rate = rate,
            Frames = count / channels
        };
    }

    
    public static void ToMono(Data d)
    {
        if (d.Channels <= 1) return;
        var c = d.Channels;
        var m = new float[d.Frames];
        for (var f = 0; f < d.Frames; f++)
        {
            var sum = 0f;
            for (var k = 0; k < c; k++) sum += d.Samples[f * c + k];
            m[f] = sum / c;
        }
        d.Samples = m;
        d.Channels = 1;
    }

    
    
    public static void TrimLoop(Data d, float maxSeconds, float crossfadeSeconds)
    {
        var keep = (int)(maxSeconds * d.Rate);
        var x = (int)(crossfadeSeconds * d.Rate);
        if (keep <= x * 2 || d.Frames < keep + x) return;
        var c = d.Channels;
        var o = new float[keep * c];
        Array.Copy(d.Samples, o, keep * c);
        for (var f = 0; f < x; f++)
        {
            var t = (f + 0.5f) / x * MathF.PI * 0.5f;
            var a = MathF.Sin(t);   
            var b = MathF.Cos(t);   
            for (var k = 0; k < c; k++)
                o[f * c + k] = d.Samples[f * c + k] * a + d.Samples[(keep + f) * c + k] * b;
        }
        d.Samples = o;
        d.Frames = keep;
    }
}
