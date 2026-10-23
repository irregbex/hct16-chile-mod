using System;
using System.IO;
using UnityEngine;

namespace HCT15
{
    /// <summary>
    /// 2.0.17 - a WAV song played straight from the disk (2026-10-09: the Agregados songs now last 6 minutes).
    /// MusicPlayer loaded every song whole through UnityWebRequest (streamAudio keeps the whole download anyway): 47 songs,
    /// ~1.3 GB, in a 32-bit game - the next big allocation (the Chile map image) crashed it. Now a .wav is an AudioClip made
    /// with AudioClip.Create(stream: true) whose reader callback reads only the samples the mixer asks for (a 64 KB buffer
    /// per song); the header is parsed once (PCM 8/16/24/32-bit and 32-bit float). Anything it cannot read falls back to the
    /// old loader.
    /// </summary>
    internal sealed class WavStream
    {
        private readonly object _lock = new object();
        private FileStream _fs;
        private long _data;
        private int _channels, _rate, _bits, _format, _frames, _pos;
        private byte[] _buf = new byte[0];
        private string _name = "";
        private bool _logged;

        public static AudioClip Open(string path, string name)
        {
            var w = new WavStream();
            if (!w.Parse(path)) { w.Close(); return null; }
            w._name = name;
            return AudioClip.Create(name, w._frames, w._channels, w._rate, true, w.Read, w.SetPosition);
        }

        private bool Parse(string path)
        {
            _fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536);
            var br = new BinaryReader(_fs);
            if (System.Text.Encoding.ASCII.GetString(br.ReadBytes(4)) != "RIFF") return false;
            br.ReadUInt32();
            if (System.Text.Encoding.ASCII.GetString(br.ReadBytes(4)) != "WAVE") return false;
            bool fmt = false;
            while (_fs.Position + 8 <= _fs.Length)
            {
                string id = System.Text.Encoding.ASCII.GetString(br.ReadBytes(4));
                long size = br.ReadUInt32();
                long next = _fs.Position + size + (size & 1);
                if (id == "fmt ")
                {
                    _format = br.ReadUInt16(); _channels = br.ReadUInt16(); _rate = (int)br.ReadUInt32();
                    br.ReadUInt32(); br.ReadUInt16(); _bits = br.ReadUInt16();
                    if (_format == 0xFFFE && size >= 26) { br.ReadUInt16(); br.ReadUInt16(); br.ReadUInt32(); _format = br.ReadUInt16(); }   // WAVE_FORMAT_EXTENSIBLE: the sub-format
                    fmt = true;
                }
                else if (id == "data")
                {
                    if (!fmt) return false;
                    _data = _fs.Position;
                    long bytes = Math.Min(size == 0 ? long.MaxValue : size, _fs.Length - _data);
                    int frame = _channels * (_bits / 8);
                    if (frame <= 0) return false;
                    _frames = (int)Math.Min(int.MaxValue, bytes / frame);
                    bool ok = (_format == 1 && (_bits == 8 || _bits == 16 || _bits == 24 || _bits == 32)) || (_format == 3 && _bits == 32);
                    return ok && _channels > 0 && _rate > 0 && _frames > 0;
                }
                _fs.Position = next;
            }
            return false;
        }

        private void Close() { try { if (_fs != null) _fs.Close(); } catch (Exception) { } _fs = null; }

        private void SetPosition(int frame)
        {
            lock (_lock) _pos = Mathf.Clamp(frame, 0, _frames);
        }

        private void Read(float[] data)
        {
            lock (_lock)
            {
                int n = 0;
                try
                {
                    int bps = _bits / 8;
                    int samples = Math.Min(data.Length, (_frames - _pos) * _channels);
                    if (samples > 0 && _fs != null)
                    {
                        int bytes = samples * bps;
                        if (_buf.Length < bytes) _buf = new byte[bytes];
                        _fs.Position = _data + (long)_pos * _channels * bps;
                        int got = 0;
                        while (got < bytes) { int r = _fs.Read(_buf, got, bytes - got); if (r <= 0) break; got += r; }
                        n = got / bps;
                        for (int i = 0; i < n; i++)
                        {
                            int o = i * bps;
                            switch (_bits)
                            {
                                case 8: data[i] = (_buf[o] - 128) / 128f; break;
                                case 16: data[i] = (short)(_buf[o] | (_buf[o + 1] << 8)) / 32768f; break;
                                case 24: data[i] = ((_buf[o] << 8 | _buf[o + 1] << 16 | _buf[o + 2] << 24) >> 8) / 8388608f; break;
                                default: data[i] = _format == 3 ? BitConverter.ToSingle(_buf, o) : BitConverter.ToInt32(_buf, o) / 2147483648f; break;
                            }
                        }
                        _pos += n / _channels;
                        if (!_logged && n > 0) { _logged = true; float peak = 0f; for (int i = 0; i < n; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i])); Debug.Log("[HCT15] Music: streaming '" + _name + "' from the disk (" + _rate + " Hz, " + _bits + " bit, " + (_frames / (float)_rate).ToString("0") + " s; first block peak " + peak.ToString("0.00") + ")"); }
                    }
                }
                catch (Exception) { }
                for (int i = n; i < data.Length; i++) data[i] = 0f;
            }
        }
    }
}
