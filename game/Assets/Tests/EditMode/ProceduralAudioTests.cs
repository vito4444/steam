using System;
using System.Linq;
using Monster.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Monster.Tests
{
    /// <summary>Tests for the synthesised booth audio.
    ///
    /// A caveat that has to be stated rather than buried: the build machine has no audio
    /// device, so none of these sounds have been listened to. These tests establish that
    /// each clip is structurally sound -- right length, no clipping, no DC offset, actually
    /// audible, and deterministic -- which is a real and useful floor. They cannot
    /// establish that a switch sounds like a switch. That needs ears on hardware.</summary>
    public sealed class ProceduralAudioTests
    {
        private static (string name, Func<AudioClip> make)[] AllClips() => new (string, Func<AudioClip>)[]
        {
            ("room tone", () => ProceduralAudio.RoomTone()),
            ("wind", () => ProceduralAudio.Wind()),
            ("heater", () => ProceduralAudio.Heater()),
            ("switch", () => ProceduralAudio.SwitchThrow()),
            ("stamp", () => ProceduralAudio.Stamp()),
            ("paper", () => ProceduralAudio.PaperRustle()),
            ("telephone", () => ProceduralAudio.TelephoneRing()),
            ("vehicle", () => ProceduralAudio.VehicleArrival()),
            ("crt whine", () => ProceduralAudio.CrtWhine()),
        };

        [Test]
        public void EveryClipIsMonoAtTheProjectSampleRate()
        {
            foreach (var (name, make) in AllClips())
            {
                var clip = make();
                Assert.AreEqual(1, clip.channels, $"{name} is not mono");
                Assert.AreEqual(ProceduralAudio.SampleRate, clip.frequency, $"{name} has the wrong sample rate");
                Assert.Greater(clip.samples, 1000, $"{name} is suspiciously short");
            }
        }

        /// <summary>Turns red if a voice ever produces something that would distort. The
        /// synthesis normalises to 0.82 of full scale, so anything at or above 1.0 means
        /// the normalisation was bypassed.</summary>
        [Test]
        public void NoClipEverReachesFullScale()
        {
            foreach (var (name, make) in AllClips())
            {
                var samples = ProceduralAudio.ReadSamples(make());
                var peak = samples.Max(Mathf.Abs);

                Assert.Less(peak, 0.95f, $"{name} peaks at {peak:F3} and would distort");
                Assert.Greater(peak, 0.5f, $"{name} peaks at {peak:F3}, so normalisation did not run");
            }
        }

        /// <summary>A clip with a DC offset wastes headroom and can thump when it starts.</summary>
        [Test]
        public void NoClipCarriesADcOffset()
        {
            foreach (var (name, make) in AllClips())
            {
                var samples = ProceduralAudio.ReadSamples(make());
                var mean = samples.Average();

                Assert.Less(Mathf.Abs(mean), 0.02f, $"{name} has a DC offset of {mean:F4}");
            }
        }

        /// <summary>Turns red if a voice degenerates into near-silence, which is how a
        /// synthesis bug usually presents: no error, just nothing to hear.</summary>
        [Test]
        public void EveryClipIsActuallyAudible()
        {
            foreach (var (name, make) in AllClips())
            {
                var samples = ProceduralAudio.ReadSamples(make());
                var rms = Mathf.Sqrt(samples.Sum(s => s * s) / samples.Length);

                Assert.Greater(rms, 0.02f, $"{name} has an RMS of {rms:F4} and is effectively silent");
            }
        }

        /// <summary>The looping beds have to start and end at zero or they tick once per
        /// loop, which is the most noticeable possible defect in a continuous ambience.</summary>
        [Test]
        public void LoopingBedsFadeToSilenceAtBothEnds()
        {
            foreach (var (name, make) in new (string, Func<AudioClip>)[]
                     {
                         ("room tone", () => ProceduralAudio.RoomTone()),
                         ("wind", () => ProceduralAudio.Wind()),
                         ("heater", () => ProceduralAudio.Heater()),
                         ("crt whine", () => ProceduralAudio.CrtWhine()),
                     })
            {
                var samples = ProceduralAudio.ReadSamples(make());

                Assert.Less(Mathf.Abs(samples[0]), 0.02f, $"{name} starts at {samples[0]:F4}, not silence");
                Assert.Less(Mathf.Abs(samples[^1]), 0.02f, $"{name} ends at {samples[^1]:F4}, not silence");
            }
        }

        [Test]
        public void SynthesisIsDeterministic()
        {
            var first = ProceduralAudio.ReadSamples(ProceduralAudio.Stamp());
            var second = ProceduralAudio.ReadSamples(ProceduralAudio.Stamp());

            CollectionAssert.AreEqual(first, second, "the same seed produced two different clips");
        }

        /// <summary>A one-shot that runs on for seconds would pile up when several fire
        /// together, and the decision sequence fires four of them within a second.</summary>
        [Test]
        public void OneShotsAreShort()
        {
            foreach (var (name, make) in new (string, Func<AudioClip>)[]
                     {
                         ("switch", () => ProceduralAudio.SwitchThrow()),
                         ("stamp", () => ProceduralAudio.Stamp()),
                         ("paper", () => ProceduralAudio.PaperRustle()),
                     })
            {
                var clip = make();
                Assert.Less(clip.length, 0.6f, $"{name} runs for {clip.length:F2}s");
            }
        }

        /// <summary>The switch has to hit hard at the start; a soft attack reads as a
        /// different object entirely.</summary>
        [Test]
        public void TheSwitchHasASharpAttack()
        {
            var samples = ProceduralAudio.ReadSamples(ProceduralAudio.SwitchThrow());
            var attackWindow = ProceduralAudio.SampleRate / 200; // five milliseconds

            var attackPeak = samples.Take(attackWindow).Max(Mathf.Abs);
            var tailPeak = samples.Skip(samples.Length / 2).Max(Mathf.Abs);

            Assert.Greater(attackPeak, tailPeak * 3f,
                $"attack peaked at {attackPeak:F3} against a tail of {tailPeak:F3}");
        }
    }
}
