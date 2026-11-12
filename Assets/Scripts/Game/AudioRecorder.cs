using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>For tests: records what the player hears (the listener's mixed output) to a WAV file.</summary>
    public class AudioRecorder : MonoBehaviour
    {
        readonly List<float> data = new List<float>();
        int channels = 2; float until; string file; public bool Done;

        public static AudioRecorder Start(string path, float seconds)
        {
            var cam = Camera.main; var r = cam.gameObject.AddComponent<AudioRecorder>();
            r.file = path; r.until = Time.realtimeSinceStartup + seconds; return r;
        }

        void OnAudioFilterRead(float[] buf, int ch)
        {
            if (Done) return;
            channels = ch; lock (data) data.AddRange(buf);
        }

        void Update()
        {
            if (Done || Time.realtimeSinceStartup < until) return;
            Done = true;
            float[] d; lock (data) d = data.ToArray();
            int rate = AudioSettings.outputSampleRate, n = d.Length;
            using (var w = new BinaryWriter(File.Create(file)))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray());
                w.Write(16); w.Write((short)1); w.Write((short)channels); w.Write(rate); w.Write(rate * channels * 2); w.Write((short)(channels * 2)); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(n * 2);
                foreach (var s in d) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
            Destroy(this);
        }
    }
}
