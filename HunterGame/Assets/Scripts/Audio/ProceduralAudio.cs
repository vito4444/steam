using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hunter.Audio
{
    /// Synthesised sound effects.
    ///
    /// The project has no audio assets and no way to record any. In an extraction game
    /// that is not a cosmetic gap: positional sound is load-bearing gameplay information —
    /// which direction a rival is searching from, whether the bell just rang, how close
    /// something heavy is. Silence would remove a whole channel of decision input.
    ///
    /// So the clips are generated from oscillators and noise at first use. They are not
    /// as good as recorded foley, but they carry direction, distance and event type, which
    /// is what the systems actually need from them.
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        static readonly Dictionary<string, AudioClip> Cache = new();

        static AudioClip GetOrCreate(string name, float seconds, Func<float, int, float> sample)
        {
            if (Cache.TryGetValue(name, out var cached) && cached != null) return cached;

            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];

            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                data[i] = Mathf.Clamp(sample(t, i), -1f, 1f);
            }

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            Cache[name] = clip;
            return clip;
        }

        /// Deterministic white noise. A seeded LCG rather than UnityEngine.Random so clips
        /// are byte-identical between runs and can be asserted on in tests.
        sealed class Noise
        {
            uint _state;
            public Noise(uint seed) => _state = seed | 1u;

            public float Next()
            {
                _state = _state * 1664525u + 1013904223u;
                return (_state >> 8) / 8388608f - 1f;
            }
        }

        /// One-pole low pass. Cheap way to turn white noise into something with a body.
        sealed class LowPass
        {
            float _value;
            readonly float _alpha;
            public LowPass(float cutoffHz)
            {
                float rc = 1f / (2f * Mathf.PI * Mathf.Max(cutoffHz, 1f));
                _alpha = (1f / SampleRate) / (rc + 1f / SampleRate);
            }
            public float Process(float input) => _value += _alpha * (input - _value);
        }

        static float Envelope(float t, float duration, float attack, float power = 2.2f)
        {
            if (t >= duration) return 0f;
            if (t < attack) return t / Mathf.Max(attack, 1e-5f);
            float decay = 1f - (t - attack) / Mathf.Max(duration - attack, 1e-5f);
            return Mathf.Pow(Mathf.Clamp01(decay), power);
        }

        /// Blade through air: filtered noise whose brightness sweeps up then down, which is
        /// what gives a whoosh its sense of travel.
        public static AudioClip Swing()
        {
            const float duration = 0.34f;
            var noise = new Noise(0x5EED01);
            var low = new LowPass(1400f);
            var high = new LowPass(280f);

            return GetOrCreate("swing", duration, (t, i) =>
            {
                float n = noise.Next();
                float body = low.Process(n) - high.Process(n);   // crude band pass
                float sweep = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration));
                return body * sweep * Envelope(t, duration, 0.03f, 1.4f) * 2.6f;
            });
        }

        /// Steel into flesh: a low thud with a short bright transient on top.
        public static AudioClip Impact()
        {
            const float duration = 0.42f;
            var noise = new Noise(0xB00D17);
            var low = new LowPass(900f);

            return GetOrCreate("impact", duration, (t, i) =>
            {
                float thud = Mathf.Sin(2f * Mathf.PI * Mathf.Lerp(150f, 48f, Mathf.Clamp01(t / 0.14f)) * t);
                float crack = low.Process(noise.Next()) * Envelope(t, 0.10f, 0.001f, 3.6f);
                return (thud * Envelope(t, duration, 0.004f, 2.8f) * 0.72f + crack * 0.85f);
            });
        }

        /// The bell. Inharmonic partials are what separate a bell from an organ note, and
        /// the long tail is why ringing it is a commitment: everything gets to hear it.
        public static AudioClip Bell()
        {
            const float duration = 4.2f;
            // Ratios roughly following a struck bell's hum, prime, tierce, quint, nominal.
            float[] partials = { 1f, 2.0f, 2.4f, 3.0f, 4.16f, 5.43f };
            float[] gains = { 0.9f, 0.55f, 0.42f, 0.30f, 0.19f, 0.12f };
            float[] decays = { 0.55f, 0.9f, 1.4f, 1.7f, 2.6f, 3.4f };
            const float fundamental = 174f;

            return GetOrCreate("bell", duration, (t, i) =>
            {
                float sum = 0f;
                for (int p = 0; p < partials.Length; p++)
                {
                    float frequency = fundamental * partials[p];
                    // Slight detune per partial gives the tone a live, beating quality.
                    frequency *= 1f + Mathf.Sin(t * (0.7f + p * 0.31f)) * 0.0016f;
                    sum += Mathf.Sin(2f * Mathf.PI * frequency * t) * gains[p] * Mathf.Exp(-t * decays[p]);
                }
                float strike = Mathf.Exp(-t * 46f) * 0.5f;
                return (sum * 0.42f + strike);
            });
        }

        /// Boot on wet stone. Randomised per index so consecutive steps are not identical.
        public static AudioClip Footstep(int variant)
        {
            const float duration = 0.19f;
            var noise = new Noise((uint)(0xF007 + variant * 7919));
            var low = new LowPass(700f + variant * 130f);

            return GetOrCreate($"footstep{variant}", duration, (t, i) =>
            {
                float scuff = low.Process(noise.Next());
                float body = Mathf.Sin(2f * Mathf.PI * 92f * t) * 0.35f;
                return (scuff * 1.7f + body) * Envelope(t, duration, 0.002f, 3.2f);
            });
        }

        /// Looping wind through the ruin. Two detuned noise beds so the loop point is not
        /// obvious to the ear.
        public static AudioClip Wind()
        {
            const float duration = 8f;
            var a = new Noise(0x1177);
            var b = new Noise(0x9931);
            var lowA = new LowPass(180f);
            var lowB = new LowPass(70f);

            return GetOrCreate("wind", duration, (t, i) =>
            {
                float gust = 0.55f + 0.45f * Mathf.Sin(t * 0.31f) * Mathf.Sin(t * 0.13f + 1.7f);
                // Cross-fade the ends so the loop does not click.
                float edge = Mathf.Min(1f, Mathf.Min(t, duration - t) / 0.6f);
                return (lowA.Process(a.Next()) * 0.7f + lowB.Process(b.Next()) * 1.3f) * gust * edge * 0.5f;
            });
        }

        /// The sovereign's presence: a slow, dissonant drone that rises as it closes.
        public static AudioClip SovereignDrone()
        {
            const float duration = 6f;
            return GetOrCreate("sovereign", duration, (t, i) =>
            {
                float edge = Mathf.Min(1f, Mathf.Min(t, duration - t) / 0.8f);
                float low = Mathf.Sin(2f * Mathf.PI * 41f * t);
                // A tritone above the root: unresolved on purpose.
                float tritone = Mathf.Sin(2f * Mathf.PI * 58f * t) * 0.55f;
                float breath = Mathf.Sin(2f * Mathf.PI * 0.7f * t) * 0.5f + 0.5f;
                return (low + tritone) * (0.25f + breath * 0.35f) * edge * 0.6f;
            });
        }

        /// Metallic chime when loot enters the bag: pure positive feedback.
        public static AudioClip LootPickup()
        {
            const float duration = 0.55f;
            return GetOrCreate("loot", duration, (t, i) =>
            {
                float a = Mathf.Sin(2f * Mathf.PI * 1180f * t) * Mathf.Exp(-t * 9f);
                float b = Mathf.Sin(2f * Mathf.PI * 1770f * t) * Mathf.Exp(-t * 13f) * 0.6f;
                float c = Mathf.Sin(2f * Mathf.PI * 2360f * t) * Mathf.Exp(-t * 18f) * 0.35f;
                return (a + b + c) * 0.42f;
            });
        }
    }
}
