using System;
using System.Numerics;
using Monster.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Monster.Tests
{
    /// <summary>Tests for what each clip is made of, frequency by frequency.
    ///
    /// This is how audio gets reviewed on a machine with no sound card. The existing tests
    /// check that a clip is mono, unclipped, free of DC offset and deterministic; the mix
    /// tests check how loud each one is against the others. Neither can tell that the paper
    /// rustle was white noise with three quarters of its energy above 5 kHz and an
    /// 85-percent rolloff at 18.7 kHz, which is a hiss generator rather than a sheet of
    /// paper. This can, and did.
    ///
    /// The bands are deliberately wide. The point is to catch a clip that has become the
    /// wrong kind of sound, not to freeze a mix decision.</summary>
    public sealed class AudioSpectrumTests
    {
        private const int Window = 16384;

        /// <summary>Fraction of the clip's energy between two frequencies.</summary>
        private static double EnergyBetween(AudioClip clip, double lowHz, double highHz)
        {
            var spectrum = Spectrum(clip, out var binHz);

            var total = 0.0;
            var inBand = 0.0;

            for (var i = 1; i < spectrum.Length; i++)
            {
                var frequency = i * binHz;
                total += spectrum[i];

                if (frequency >= lowHz && frequency < highHz)
                {
                    inBand += spectrum[i];
                }
            }

            return total <= 0.0 ? 0.0 : inBand / total;
        }

        private static double[] Spectrum(AudioClip clip, out double binHz)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            // Taken from the middle, because the ends of a one-shot are its attack and its
            // fade and neither is representative of the body of the sound.
            var start = Math.Max(0, (samples.Length - Window) / 2);
            var buffer = new Complex[Window];

            for (var i = 0; i < Window; i++)
            {
                var sample = start + i < samples.Length ? samples[start + i] : 0f;

                // Hann, so a partial that does not complete a whole number of cycles in the
                // window does not smear across the whole spectrum.
                var hann = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (Window - 1)));
                buffer[i] = new Complex(sample * hann, 0.0);
            }

            Fft(buffer);
            binHz = ProceduralAudio.SampleRate / (double)Window;

            var magnitudes = new double[Window / 2];
            for (var i = 0; i < magnitudes.Length; i++)
            {
                magnitudes[i] = buffer[i].Magnitude * buffer[i].Magnitude;
            }

            return magnitudes;
        }

        /// <summary>Iterative radix-2. Sixteen thousand points runs in a few milliseconds
        /// and saves taking a dependency for nine short clips.</summary>
        private static void Fft(Complex[] buffer)
        {
            var n = buffer.Length;

            for (int i = 1, j = 0; i < n; i++)
            {
                var bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1)
                {
                    j ^= bit;
                }

                j ^= bit;

                if (i < j)
                {
                    (buffer[i], buffer[j]) = (buffer[j], buffer[i]);
                }
            }

            for (var length = 2; length <= n; length <<= 1)
            {
                var angle = -2.0 * Math.PI / length;
                var step = new Complex(Math.Cos(angle), Math.Sin(angle));

                for (var i = 0; i < n; i += length)
                {
                    var w = Complex.One;

                    for (var j = 0; j < length / 2; j++)
                    {
                        var u = buffer[i + j];
                        var v = buffer[i + j + length / 2] * w;
                        buffer[i + j] = u + v;
                        buffer[i + j + length / 2] = u - v;
                        w *= step;
                    }
                }
            }
        }

        // ------------------------------------------------------------------ the beds --

        [Test]
        public void TheAmbientBedsAreLowAndDark()
        {
            var beds = new (string name, AudioClip clip, double floor)[]
            {
                ("room tone", ProceduralAudio.RoomTone(), 0.80),
                ("wind", ProceduralAudio.Wind(), 0.80),
                ("heater", ProceduralAudio.Heater(), 0.65),
            };

            foreach (var (name, clip, floor) in beds)
            {
                var low = EnergyBetween(clip, 0, 1000);
                Assert.Greater(low, floor,
                    $"{name} has only {low:P0} of its energy below 1 kHz; a bed that bright is hiss " +
                    "sitting on top of the game rather than a room the game happens in");
            }
        }

        /// <summary>The one bed that is supposed to be bright. A flyback whine that has
        /// drifted down into the midrange is a hum, and the booth already has one.</summary>
        [Test]
        public void TheCrtWhineIsUpWhereAFlybackIs()
        {
            var high = EnergyBetween(ProceduralAudio.CrtWhine(), 5000, 22050);
            Assert.Greater(high, 0.85, $"the CRT whine has only {high:P0} of its energy above 5 kHz");
        }

        // --------------------------------------------------------------- the one-shots --

        [Test]
        public void AStampIsAThudAndAVehicleIsARumble()
        {
            var stamp = EnergyBetween(ProceduralAudio.Stamp(), 0, 1000);
            Assert.Greater(stamp, 0.80, $"the stamp has only {stamp:P0} of its energy below 1 kHz");

            var vehicle = EnergyBetween(ProceduralAudio.VehicleArrival(), 0, 1000);
            Assert.Greater(vehicle, 0.80, $"the vehicle has only {vehicle:P0} of its energy below 1 kHz");
        }

        [Test]
        public void ASwitchIsAClickRatherThanAThump()
        {
            var high = EnergyBetween(ProceduralAudio.SwitchThrow(), 1000, 22050);
            Assert.Greater(high, 0.50,
                $"a thrown switch has only {high:P0} of its energy above 1 kHz, which is a thump");
        }

        /// <summary>The test that found the bug this file exists for. Paper needs hiss and
        /// body both: all hiss is a noise generator, all body is cloth.</summary>
        [Test]
        public void PaperHasHissAndBodyBoth()
        {
            var clip = ProceduralAudio.PaperRustle();
            var hiss = EnergyBetween(clip, 5000, 22050);
            var body = EnergyBetween(clip, 0, 2000);

            Assert.Greater(hiss, 0.10, $"paper has only {hiss:P0} above 5 kHz; it is cloth, not paper");
            Assert.Less(hiss, 0.55, $"paper has {hiss:P0} above 5 kHz; it is a noise generator, not paper");
            Assert.Greater(body, 0.10, $"paper has only {body:P0} below 2 kHz; the sheet has no weight");
        }

        [Test]
        public void TheTelephoneIsABellInTheMidrange()
        {
            var band = EnergyBetween(ProceduralAudio.TelephoneRing(), 500, 5000);
            Assert.Greater(band, 0.70,
                $"the telephone has only {band:P0} of its energy between 500 Hz and 5 kHz, which is " +
                "not where a bell lives");
        }
    }
}
