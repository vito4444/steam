using System;
using Monster.Audio;
using NUnit.Framework;
using UnityEngine;

namespace Monster.Tests
{
    /// <summary>Tests for how the ambient beds sit against each other.
    ///
    /// The existing audio tests check each clip on its own: mono, unclipped, no DC offset,
    /// audible, deterministic. All of that can be true of a mix in which the air in the room
    /// is three times louder than the weather outside, which is what the booth actually
    /// shipped with, because every clip is normalised to the same peak and the mix levels
    /// were guessed rather than measured.
    ///
    /// Nobody on this machine can hear any of it, so the balance has to be arithmetic.</summary>
    public sealed class AudioMixTests
    {
        private static double Rms(AudioClip clip)
        {
            var samples = new float[clip.samples * clip.channels];
            clip.GetData(samples, 0);

            var sum = 0.0;
            foreach (var sample in samples)
            {
                sum += sample * (double)sample;
            }

            return Math.Sqrt(sum / Math.Max(1, samples.Length));
        }

        private static double InMix(AudioClip clip, float level) => Rms(clip) * level;

        [Test]
        public void TheWeatherIsTheLoudestThingInTheRoom()
        {
            var room = InMix(ProceduralAudio.RoomTone(), BoothAudio.RoomToneLevel);
            var wind = InMix(ProceduralAudio.Wind(), BoothAudio.WindLevel);
            var heater = InMix(ProceduralAudio.Heater(), BoothAudio.HeaterLevel);
            var crt = InMix(ProceduralAudio.CrtWhine(), BoothAudio.CrtLevel);

            Assert.Greater(wind, heater, $"wind {wind:F4} is not above the heater {heater:F4}");
            Assert.Greater(heater, room, $"the heater {heater:F4} is not above the room tone {room:F4}");
            Assert.Greater(room, crt, $"the room tone {room:F4} is not above the CRT whine {crt:F4}");
        }

        /// <summary>The whole bed has to be quiet enough that a switch being thrown is an
        /// event, and loud enough that the booth is not silent.</summary>
        [Test]
        public void TheCombinedBedSitsInTheRangeAQuietRoomShould()
        {
            var beds = new[]
            {
                InMix(ProceduralAudio.RoomTone(), BoothAudio.RoomToneLevel),
                InMix(ProceduralAudio.Wind(), BoothAudio.WindLevel),
                InMix(ProceduralAudio.Heater(), BoothAudio.HeaterLevel),
                InMix(ProceduralAudio.CrtWhine(), BoothAudio.CrtLevel),
            };

            var combined = 0.0;
            foreach (var bed in beds)
            {
                combined += bed * bed;
            }

            combined = Math.Sqrt(combined);
            var dbfs = 20.0 * Math.Log10(combined);

            Assert.Greater(dbfs, -36.0, $"the ambient bed is {dbfs:F1} dBFS, which is effectively silence");
            Assert.Less(dbfs, -20.0, $"the ambient bed is {dbfs:F1} dBFS, loud enough to sit on top of " +
                                     "everything the player does");
        }

        /// <summary>No single bed may carry the whole room. If one does, the others are
        /// decoration and the booth has one sound rather than four.</summary>
        [Test]
        public void NoSingleBedDominatesTheOthers()
        {
            var beds = new[]
            {
                ("room tone", InMix(ProceduralAudio.RoomTone(), BoothAudio.RoomToneLevel)),
                ("wind", InMix(ProceduralAudio.Wind(), BoothAudio.WindLevel)),
                ("heater", InMix(ProceduralAudio.Heater(), BoothAudio.HeaterLevel)),
            };

            foreach (var (name, level) in beds)
            {
                foreach (var (otherName, otherLevel) in beds)
                {
                    Assert.Less(level / otherLevel, 3.0,
                        $"{name} at {level:F4} is more than three times {otherName} at {otherLevel:F4}");
                }
            }
        }

        /// <summary>A switch being thrown has to read as an event against the bed without
        /// being a jump-scare.</summary>
        [Test]
        public void AThrownSwitchStandsAboveTheBedWithoutStartling()
        {
            var bed = InMix(ProceduralAudio.RoomTone(), BoothAudio.RoomToneLevel)
                      + InMix(ProceduralAudio.Wind(), BoothAudio.WindLevel);

            var switchPeak = 0f;
            var samples = new float[ProceduralAudio.SwitchThrow().samples];
            ProceduralAudio.SwitchThrow().GetData(samples, 0);

            foreach (var sample in samples)
            {
                switchPeak = Mathf.Max(switchPeak, Mathf.Abs(sample));
            }

            var headroom = 20.0 * Math.Log10(switchPeak * 0.70f / bed);

            Assert.Greater(headroom, 12.0, $"a thrown switch is only {headroom:F1} dB above the bed");
            Assert.Less(headroom, 32.0, $"a thrown switch is {headroom:F1} dB above the bed, which is a " +
                                        "jump-scare rather than a switch");
        }
    }
}
