using UnityEngine;

namespace TheInternet
{
    public class BrowserAudioOutput : MonoBehaviour
    {
        private const int TargetSamples = 24576; // 256 ms stereo reserve at 48 kHz
        private readonly StreamingPcmBuffer buffer = new StreamingPcmBuffer(96000, TargetSamples);
        private AudioSource output;
        private AudioClip clip;
        private volatile bool accepting;
        public int BufferedSamples { get { return buffer.Available; } }
        public long UnderrunCount { get { return buffer.Underruns; } }

        public void Enqueue(float[] samples) { if (accepting) buffer.Write(samples); }
        public void ResetAudio() { accepting = false; buffer.Clear(); if (output != null) output.Stop(); }

        public void Apply(Vector3 position, bool enabled, bool muted, float screenWidth = WindowSettings.DefaultWidth)
        {
            transform.position = position;
            if (!enabled)
            {
                accepting = false;
                if (output != null) output.Stop();
                buffer.Clear();
                return;
            }
            accepting = true;
            if (output == null)
            {
                output = gameObject.AddComponent<AudioSource>();
                output.playOnAwake = false; output.loop = true;
                output.spatialBlend = 1; output.dopplerLevel = 0; output.spread = 0;
                output.rolloffMode = AudioRolloffMode.Logarithmic;
                // A short streaming loop avoids Unity requesting a long clip's
                // worth of audio ahead of a live source at startup.
                clip = AudioClip.Create("The Internet live audio", 4096, 2, 48000, true, buffer.Read);
                output.clip = clip;
            }
            double scale = System.Math.Max(0.05, screenWidth) / WindowSettings.DefaultWidth;
            output.minDistance = (float)System.Math.Min(float.MaxValue, 0.5 * scale);
            output.maxDistance = (float)System.Math.Min(float.MaxValue, 30.0 * scale);
            output.mute = muted;
            output.pitch = Mathf.Clamp(1f + (buffer.Available - TargetSamples) * 0.0000001f, 0.9975f, 1.0025f);
            if (!output.isPlaying && buffer.Available >= TargetSamples) output.Play();
        }

        private void OnDestroy()
        {
            accepting = false; buffer.Clear();
            if (output != null) output.Stop();
            if (clip != null) Destroy(clip);
        }
    }
}
