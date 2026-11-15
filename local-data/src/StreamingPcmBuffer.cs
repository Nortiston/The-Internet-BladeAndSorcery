using System;

namespace TheInternet
{
    // Bounded, memory-only audio queue shared by the main and Unity audio threads.
    public sealed class StreamingPcmBuffer
    {
        private readonly float[] samples;
        private readonly object gate = new object();
        private int read, write, count;
        private readonly int prebuffer;
        private bool primed;
        private long underruns;
        public StreamingPcmBuffer(int capacity, int prebuffer = 0)
        {
            if (capacity <= 0 || prebuffer < 0 || prebuffer > capacity) throw new ArgumentOutOfRangeException();
            samples = new float[capacity]; this.prebuffer = prebuffer; primed = prebuffer == 0;
        }
        public int Available { get { lock (gate) return count; } }
        public long Underruns { get { lock (gate) return underruns; } }
        public void Write(float[] input)
        {
            lock (gate)
            {
                foreach (float value in input)
                {
                    if (count == samples.Length) { read = (read + 1) % samples.Length; count--; }
                    samples[write] = float.IsNaN(value) || float.IsInfinity(value) ? 0 : Math.Max(-1, Math.Min(1, value));
                    write = (write + 1) % samples.Length; count++;
                }
            }
        }
        public void Read(float[] output)
        {
            lock (gate)
            {
                // After a real stall, refill a reserve instead of alternating
                // between one tiny packet of sound and one chunk of silence.
                if (!primed)
                {
                    if (count < Math.Max(prebuffer, output.Length)) { Array.Clear(output, 0, output.Length); return; }
                    primed = true;
                }
                int n = Math.Min(output.Length, count);
                for (int i = 0; i < n; i++) { output[i] = samples[read]; read = (read + 1) % samples.Length; }
                count -= n;
                Array.Clear(output, n, output.Length - n);
                if (n < output.Length && prebuffer > 0) { underruns++; primed = false; }
            }
        }
        public void Clear() { lock (gate) { read = write = count = 0; primed = prebuffer == 0; Array.Clear(samples, 0, samples.Length); } }
    }
}
