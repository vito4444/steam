using System;
using UnityEngine;

namespace Monster.Audio
{
    /// <summary>Synthesises the booth's sounds in code.
    ///
    /// Nothing here is a recorded asset. Three reasons, in order of how much they matter.
    /// The concept's audio is almost entirely small mechanical noises and room tone, which
    /// synthesise convincingly and would otherwise be a licensing or recording cost. Code
    /// is reviewable in a diff where a .wav is not. And a synthesised clip is deterministic
    /// from a seed, so a test can assert its length, level and DC offset instead of
    /// somebody having to listen to it.
    ///
    /// That last point carries a caveat worth stating plainly: the build machine has no
    /// audio device, so none of this has been heard. It is verified to be structurally
    /// correct, not verified to sound good.</summary>
    public static class ProceduralAudio
    {
        public const int SampleRate = 44100;

        /// <summary>Fluorescent tube hum, mains-frequency harmonics over a floor of
        /// filtered noise. Loops seamlessly because every partial completes a whole number
        /// of cycles in the clip.</summary>
        public static AudioClip RoomTone(float seconds = 4f, int seed = 101)
        {
            return Build("BoothRoomTone", seconds, seed, (t, phase, random, noise) =>
            {
                var hum = 0f;
                hum += 0.055f * Mathf.Sin(phase * 100f);
                hum += 0.030f * Mathf.Sin(phase * 200f);
                hum += 0.013f * Mathf.Sin(phase * 300f);
                hum += 0.006f * Mathf.Sin(phase * 400f);

                // A slow beat, so the tone is never perfectly static.
                hum *= 1f + 0.14f * Mathf.Sin(phase * 0.5f);

                return hum + noise * 0.022f;
            });
        }

        /// <summary>Wind against the window: low-passed noise with a slow swell.</summary>
        public static AudioClip Wind(float seconds = 6f, int seed = 202)
        {
            var low = 0f;
            return Build("BoothWind", seconds, seed, (t, phase, random, noise) =>
            {
                low += (noise - low) * 0.012f;
                var swell = 0.55f + 0.45f * Mathf.Sin(phase * 0.17f) * Mathf.Sin(phase * 0.061f);
                return low * 6.5f * swell;
            });
        }

        /// <summary>The heater cycling: a rattling low rumble that fades in and out.</summary>
        public static AudioClip Heater(float seconds = 5f, int seed = 303)
        {
            var low = 0f;
            return Build("BoothHeater", seconds, seed, (t, phase, random, noise) =>
            {
                low += (noise - low) * 0.045f;
                var rattle = 0.35f * Mathf.Sin(phase * 47f) * (0.5f + 0.5f * Mathf.Sin(phase * 3.1f));
                var envelope = Mathf.Clamp01(Mathf.Sin(t / seconds * Mathf.PI));
                return (low * 3.2f + rattle * 0.25f) * envelope * 0.5f;
            });
        }

        /// <summary>A brass toggle being thrown. Two transients: the finger on the metal
        /// and the mechanism reaching its stop.</summary>
        public static AudioClip SwitchThrow(int seed = 404) =>
            Build("SwitchThrow", 0.16f, seed, (t, phase, random, noise) =>
            {
                var first = Transient(t, 0f, 0.0035f) * (noise * 0.6f + 0.4f * Mathf.Sin(phase * 2600f));
                var second = Transient(t, 0.045f, 0.011f) * (noise * 0.45f + 0.55f * Mathf.Sin(phase * 1250f));
                return (first * 0.55f + second * 0.85f) * 0.7f;
            });

        /// <summary>A rubber stamp on paper on a steel desk: a soft impact with a short
        /// woody ring under it.</summary>
        public static AudioClip Stamp(int seed = 505) =>
            Build("Stamp", 0.30f, seed, (t, phase, random, noise) =>
            {
                var impact = Transient(t, 0f, 0.010f) * noise;
                var body = Transient(t, 0.002f, 0.055f) *
                           (Mathf.Sin(phase * 168f) * 0.6f + Mathf.Sin(phase * 254f) * 0.3f);
                return (impact * 0.8f + body * 0.55f) * 0.85f;
            });

        /// <summary>Paper being moved: a burst of high noise with a fast irregular
        /// envelope.</summary>
        public static AudioClip PaperRustle(int seed = 606)
        {
            // A one-pole across the noise before it is shaped. Unfiltered, this clip put
            // 75 percent of its energy above 5 kHz with an 85-percent rolloff at 18.7 kHz,
            // which is white noise rather than paper -- the measurement that found it is
            // the same one the spectral tests now make every run. Paper has body: the
            // sheet moves as well as hisses, but it still hisses.
            var body = 0f;

            return Build("PaperRustle", 0.42f, seed, (t, phase, random, noise) =>
            {
                body += (noise - body) * 0.34f;

                var envelope = Mathf.Exp(-t * 7f) * (0.45f + 0.55f * Mathf.Abs(Mathf.Sin(phase * 21f)));
                var crackle = body * (0.6f + 0.4f * Mathf.Sin(phase * 380f + body * 6f));
                return crackle * envelope * 1.15f;
            });
        }

        /// <summary>An electromechanical bell, struck twice.</summary>
        public static AudioClip TelephoneRing(int seed = 707) =>
            Build("TelephoneRing", 1.4f, seed, (t, phase, random, noise) =>
            {
                var strike = Mathf.Repeat(t, 0.05f);
                var burst = t < 0.55f || (t > 0.75f && t < 1.30f) ? 1f : 0f;
                var hammer = Mathf.Exp(-strike * 45f);
                var bell = Mathf.Sin(phase * 1046f) * 0.6f
                           + Mathf.Sin(phase * 1420f) * 0.3f
                           + Mathf.Sin(phase * 2612f) * 0.12f;
                return bell * hammer * burst * 0.42f;
            });

        /// <summary>A diesel vehicle pulling up: rumble that rises, then idles.</summary>
        public static AudioClip VehicleArrival(int seed = 808)
        {
            var low = 0f;
            return Build("VehicleArrival", 3.2f, seed, (t, phase, random, noise) =>
            {
                low += (noise - low) * 0.02f;
                var approach = Mathf.Clamp01(t / 1.8f);
                var idle = Mathf.Sin(phase * 34f) * 0.35f + Mathf.Sin(phase * 68f) * 0.15f;
                var knock = Mathf.Sin(phase * 11f) > 0.7f ? 0.25f : 0f;
                return (low * 5.5f + idle + knock) * approach * 0.30f;
            });
        }

        /// <summary>The high whine of a CRT flyback. Quiet, and the kind of sound a player
        /// only notices when it stops.</summary>
        public static AudioClip CrtWhine(float seconds = 2f, int seed = 909) =>
            Build("CrtWhine", seconds, seed, (t, phase, random, noise) =>
                0.045f * Mathf.Sin(phase * 9450f) + 0.012f * Mathf.Sin(phase * 18900f) + noise * 0.004f);

        // -------------------------------------------------------------------- machinery --

        private delegate float Voice(float time, float phase, System.Random random, float noise);

        private static float Transient(float t, float start, float decay) =>
            t < start ? 0f : Mathf.Exp(-(t - start) / decay);

        /// <summary>Renders a voice into a clip and normalises it so no sound in the booth
        /// can clip or arrive wildly louder than its neighbours.</summary>
        private static AudioClip Build(string name, float seconds, int seed, Voice voice)
        {
            var count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var samples = new float[count];
            var random = new System.Random(seed);

            for (var i = 0; i < count; i++)
            {
                var t = (float)i / SampleRate;
                var phase = t * 2f * Mathf.PI;
                var noise = (float)(random.NextDouble() * 2.0 - 1.0);
                samples[i] = voice(t, phase, random, noise);
            }

            RemoveDcOffset(samples);
            Normalise(samples, 0.82f);
            FadeEdges(samples, Mathf.Min(0.004f, seconds * 0.1f));

            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }

        private static void RemoveDcOffset(float[] samples)
        {
            var sum = 0.0;
            foreach (var sample in samples)
            {
                sum += sample;
            }

            var mean = (float)(sum / samples.Length);
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] -= mean;
            }
        }

        private static void Normalise(float[] samples, float peak)
        {
            var max = 0f;
            foreach (var sample in samples)
            {
                max = Mathf.Max(max, Mathf.Abs(sample));
            }

            if (max < 1e-5f)
            {
                return;
            }

            var gain = peak / max;
            for (var i = 0; i < samples.Length; i++)
            {
                samples[i] *= gain;
            }
        }

        /// <summary>Ramps the first and last few milliseconds to zero. Without it every
        /// looping clip ticks audibly once per loop.</summary>
        private static void FadeEdges(float[] samples, float seconds)
        {
            var fade = Mathf.Clamp(Mathf.RoundToInt(seconds * SampleRate), 1, samples.Length / 2);
            for (var i = 0; i < fade; i++)
            {
                var gain = (float)i / fade;
                samples[i] *= gain;
                samples[samples.Length - 1 - i] *= gain;
            }
        }

        /// <summary>Reads a clip back for inspection. Used by tests, which is the only way
        /// any of this gets checked on a machine with no audio device.</summary>
        public static float[] ReadSamples(AudioClip clip)
        {
            if (clip == null)
            {
                throw new ArgumentNullException(nameof(clip));
            }

            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);
            return samples;
        }
    }
}
