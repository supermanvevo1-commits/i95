using UnityEngine;

namespace SurviveI95.Game
{
    /// <summary>
    /// The roar of rumble strips, made from the recorded tire roar. Florida mills its shoulder strips 7 in wide every
    /// 12 in (0.305 m), so the tires hit Speed / 0.305 grooves a second: about 100 a second at 70 mph. Every groove
    /// strikes the tire the same way, so the result is a tone, not a hiss: each hit here replays the same few
    /// milliseconds of the real tire recording, dying away before the next. The slice drifts slowly through the
    /// recording so the tone stays alive, and a little of the roar, pulsed groove by groove, gives it grit. Dulled
    /// the way the car's body passes it to your ears.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class RumbleStripSound : MonoBehaviour
    {
        public const float GrooveSpacing = 0.305f;
        /// <summary>Grooves hit per second and loudness 0-1; set from the game thread, read on the audio thread.</summary>
        public volatile float Rate, Level;
        float[] rec; int sampleRate, grain, hits, at; double phase; float lowL, lowR, level, ring;

        public void Init(AudioClip clip)
        {
            sampleRate = AudioSettings.outputSampleRate;
            var all = new float[clip.samples * clip.channels];
            if (!clip.GetData(all, 0)) return;
            var mono = new float[clip.samples];
            for (int i = 0; i < clip.samples; i++) mono[i] = all[i * clip.channels];
            grain = clip.samples / 3; rec = mono;
        }

        void OnAudioFilterRead(float[] data, int channels)
        {
            var r = rec; if (r == null || sampleRate <= 0) return;
            float rate = Rate, want = Level;
            double step = rate / sampleRate;
            float k = 1f - Mathf.Exp(-2f * Mathf.PI * 900f / sampleRate), decay = Mathf.Exp(-1f / (.0035f * sampleRate));
            for (int i = 0; i < data.Length; i += channels)
            {
                level += (want - level) * .0015f;
                phase += step;
                if (phase >= 1.0)
                {
                    phase -= 1.0; at = 0; ring = 1f;
                    // Every 24 hits the slice moves on a little through the recording.
                    if (++hits % 24 == 0) grain = (grain + 997) % (r.Length - 4096);
                }
                else ring *= decay;
                float p = (float)phase, pulse = p < 7f / 12f ? Mathf.Exp(-p * 6f) : .05f;
                float hit = r[grain + Mathf.Min(at, 4000)] * ring * 3.2f;
                at++;
                float l = hit + data[i] * pulse * .5f; lowL += (l - lowL) * k;
                float rr = channels > 1 ? hit + data[i + 1] * pulse * .5f : l; lowR += (rr - lowR) * k;
                data[i] = lowL * level;
                if (channels > 1) data[i + 1] = lowR * level;
                for (int c = 2; c < channels; c++) data[i + c] = 0f;
            }
        }
    }
}
