using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Minimal real-input radix-2 (Cooley-Tukey) FFT with a built-in Blackman-Harris window,
    /// used by <see cref="PianoAudioDetector"/>'s Stage 2 native path to compute the magnitude
    /// spectrum ourselves instead of routing the mic through an AudioSource + GetSpectrumData.
    ///
    /// Doing the transform ourselves removes the playback-buffering latency of the AudioSource
    /// bridge and lets us window the FRESHEST captured samples each frame. The window, the FFT,
    /// and the output bins (0..N/2-1 spanning [0, sampleRate/2]) match what GetSpectrumData
    /// produced, so the downstream peak-detection / parabolic-interpolation / harmonic-suppression
    /// code is reused unchanged.
    ///
    /// All tables (bit-reversal, twiddles, window) are precomputed for the fixed size N, so a
    /// transform is just the windowing pass + butterflies + magnitudes — cheap enough to run on
    /// the main thread every analysis frame (N=8192 is well under 1 ms on Quest).
    ///
    /// Output magnitudes are amplitude-normalised (2 / sum(window)); the absolute scale differs
    /// from Unity's GetSpectrumData, so peakThreshold may need re-tuning on the native path —
    /// watch the health-check peakMag for the right value.
    /// </summary>
    public sealed class SimpleFFT
    {
        private readonly int _n;
        private readonly int _half;
        private readonly int[] _rev;       // bit-reversal permutation
        private readonly float[] _cos;     // twiddle real part:  cos(2pi k / N)
        private readonly float[] _sin;     // twiddle imag part: -sin(2pi k / N)
        private readonly float[] _window;  // Blackman-Harris
        private readonly float[] _re;
        private readonly float[] _im;
        private readonly float _norm;

        public int Size => _n;

        /// <param name="n">FFT length in samples. Must be a power of two (>= 2).</param>
        public SimpleFFT(int n)
        {
            _n = n;
            _half = n >> 1;

            // Bit-reversal permutation.
            int bits = 0;
            while ((1 << bits) < n) bits++;
            _rev = new int[n];
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++)
                    if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                _rev[i] = r;
            }

            // Twiddle factors W_N^k = cos(2pi k/N) - i sin(2pi k/N)  (forward transform).
            _cos = new float[_half];
            _sin = new float[_half];
            for (int k = 0; k < _half; k++)
            {
                double a = 2.0 * Mathf.PI * k / n;
                _cos[k] = (float)System.Math.Cos(a);
                _sin[k] = (float)-System.Math.Sin(a);
            }

            // Blackman-Harris window + its sum (for amplitude normalisation).
            _window = new float[n];
            const double a0 = 0.35875, a1 = 0.48829, a2 = 0.14128, a3 = 0.01168;
            double sum = 0.0;
            for (int i = 0; i < n; i++)
            {
                double x = 2.0 * Mathf.PI * i / (n - 1);
                double w = a0 - a1 * System.Math.Cos(x) + a2 * System.Math.Cos(2 * x) - a3 * System.Math.Cos(3 * x);
                _window[i] = (float)w;
                sum += w;
            }
            _norm = (float)(2.0 / sum);

            _re = new float[n];
            _im = new float[n];
        }

        /// <summary>
        /// Window <paramref name="time"/> (length N), transform it, and write magnitudes for bins
        /// 0..N/2-1 into <paramref name="mags"/> (length N/2). Bin k = k * sampleRate / N Hz.
        /// </summary>
        public void MagnitudeSpectrum(float[] time, float[] mags)
        {
            // Apply window + load in bit-reversed order in one pass.
            for (int i = 0; i < _n; i++)
            {
                int s = _rev[i];
                _re[i] = time[s] * _window[s];
                _im[i] = 0f;
            }

            // Iterative radix-2 DIT butterflies.
            for (int len = 2; len <= _n; len <<= 1)
            {
                int half = len >> 1;
                int step = _n / len;
                for (int i = 0; i < _n; i += len)
                {
                    int tw = 0;
                    for (int j = 0; j < half; j++)
                    {
                        float wr = _cos[tw];
                        float wi = _sin[tw];
                        int a = i + j;
                        int b = a + half;
                        float br = _re[b], bi = _im[b];
                        float xr = br * wr - bi * wi;
                        float xi = br * wi + bi * wr;
                        _re[b] = _re[a] - xr;
                        _im[b] = _im[a] - xi;
                        _re[a] += xr;
                        _im[a] += xi;
                        tw += step;
                    }
                }
            }

            int outN = Mathf.Min(mags.Length, _half);
            for (int k = 0; k < outN; k++)
            {
                float re = _re[k], im = _im[k];
                mags[k] = Mathf.Sqrt(re * re + im * im) * _norm;
            }
        }
    }
}
