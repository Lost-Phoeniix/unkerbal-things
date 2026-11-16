// Dread soundscape: the recorded clips. A set is every "<set>-<kind>-NN.wav" in the sound folder (vac: airless, uw: water).
// Loaded on a pool thread when a set is first wanted and dropped again later, so the sound costs memory only where it is heard.
// Pure C# (no UnityEngine): the offline tests load the same files through the same code.
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace UnkerbalThings.Dread
{
    /// <summary>16-bit samples: the same memory as on disk. Right is the same array as left for a mono file.</summary>
    public sealed class DreadClip
    {
        public short[] L, R;
        public int Rate;
        public int Frames { get { return L.Length; } }
    }

    public sealed class DreadSoundBank
    {
        readonly Dictionary<string, DreadClip[]> kinds = new Dictionary<string, DreadClip[]>();
        volatile bool ready;

        public bool Ready { get { return ready; } }
        public int Total { get; private set; }

        /// <summary>The clips of a kind ("groan", "bed-a", ...), never null.</summary>
        public DreadClip[] Clips(string kind)
        {
            DreadClip[] c;
            return ready && kinds.TryGetValue(kind, out c) ? c : Empty;
        }
        static readonly DreadClip[] Empty = new DreadClip[0];

        /// <summary>Starts loading on a pool thread; Ready turns true when done (an empty bank if the folder has nothing).</summary>
        public static DreadSoundBank Begin(string folder, string set)
        {
            DreadSoundBank b = new DreadSoundBank();
            ThreadPool.QueueUserWorkItem(_ => b.LoadAll(folder, set));
            return b;
        }

        /// <summary>Loads on the calling thread (tools and tests).</summary>
        public static DreadSoundBank LoadNow(string folder, string set)
        {
            DreadSoundBank b = new DreadSoundBank();
            b.LoadAll(folder, set);
            return b;
        }

        void LoadAll(string folder, string set)
        {
            try
            {
                if (Directory.Exists(folder))
                {
                    string[] files = Directory.GetFiles(folder, set + "-*.wav");
                    Array.Sort(files, StringComparer.Ordinal);
                    Dictionary<string, List<DreadClip>> found = new Dictionary<string, List<DreadClip>>();
                    foreach (string f in files)
                    {
                        string kind = KindOf(Path.GetFileNameWithoutExtension(f), set);
                        DreadClip c = LoadClip(f);
                        if (kind == null || c == null) continue;
                        List<DreadClip> list;
                        if (!found.TryGetValue(kind, out list)) found[kind] = list = new List<DreadClip>();
                        list.Add(c);
                        Total++;
                    }
                    foreach (KeyValuePair<string, List<DreadClip>> kv in found) kinds[kv.Key] = kv.Value.ToArray();
                }
            }
            catch (Exception) { kinds.Clear(); Total = 0; }
            ready = true;
        }

        // "vac-groan-03" gives "groan"; "vac-bed-a" gives "bed-a"
        internal static string KindOf(string name, string set)
        {
            if (!name.StartsWith(set + "-", StringComparison.Ordinal)) return null;
            string rest = name.Substring(set.Length + 1);
            int dash = rest.LastIndexOf('-');
            if (dash > 0)
            {
                string tail = rest.Substring(dash + 1);
                int number;
                if (int.TryParse(tail, out number)) return rest.Substring(0, dash);
            }
            return rest;
        }

        /// <summary>Reads a 16-bit PCM WAV, mono or stereo. Null when missing or in another format.</summary>
        public static DreadClip LoadClip(string path)
        {
            try
            {
                byte[] b = File.ReadAllBytes(path);
                int p = 12, ch = 0, bits = 0, rate = 0, off = -1, len = 0;
                while (p + 8 <= b.Length)
                {
                    int n = BitConverter.ToInt32(b, p + 4);
                    string id = System.Text.Encoding.ASCII.GetString(b, p, 4);
                    if (id == "fmt ") { ch = BitConverter.ToInt16(b, p + 10); rate = BitConverter.ToInt32(b, p + 12); bits = BitConverter.ToInt16(b, p + 22); }
                    else if (id == "data") { off = p + 8; len = Math.Min(n, b.Length - off); break; }
                    p += 8 + n + (n & 1);
                }
                if (off < 0 || bits != 16 || ch < 1 || ch > 2 || rate < 8000) return null;
                int frames = len / (2 * ch);
                if (frames < 16) return null;
                DreadClip c = new DreadClip { Rate = rate, L = new short[frames] };
                c.R = ch == 2 ? new short[frames] : c.L;
                for (int i = 0; i < frames; i++)
                {
                    c.L[i] = BitConverter.ToInt16(b, off + i * ch * 2);
                    if (ch == 2) c.R[i] = BitConverter.ToInt16(b, off + i * 4 + 2);
                }
                return c;
            }
            catch (Exception) { return null; }
        }
    }
}
