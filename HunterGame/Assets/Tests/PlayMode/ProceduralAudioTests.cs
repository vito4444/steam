using System.Collections;
using Hunter.Audio;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Hunter.Tests
{
    /// Nobody can listen to these clips in CI, so the tests assert on the waveform
    /// properties that decide whether a sound does its job: that it is not silence, that
    /// it decays like a struck object rather than droning, and that a bell rings longer
    /// than a footstep. Runs in PlayMode because AudioClip.Create needs the audio module.
    public class ProceduralAudioTests
    {
        static float[] Read(AudioClip clip)
        {
            var data = new float[clip.samples * clip.channels];
            clip.GetData(data, 0);
            return data;
        }

        static float Rms(float[] data, int from, int to)
        {
            double sum = 0d;
            int count = 0;
            for (int i = Mathf.Max(0, from); i < Mathf.Min(data.Length, to); i++)
            {
                sum += data[i] * (double)data[i];
                count++;
            }
            return count == 0 ? 0f : Mathf.Sqrt((float)(sum / count));
        }

        static float Peak(float[] data)
        {
            float peak = 0f;
            foreach (var s in data) peak = Mathf.Max(peak, Mathf.Abs(s));
            return peak;
        }

        [UnityTest]
        public IEnumerator EveryClipHasSignalAndStaysInsideFullScale()
        {
            var clips = new[]
            {
                ProceduralAudio.Swing(), ProceduralAudio.Impact(), ProceduralAudio.Bell(),
                ProceduralAudio.Footstep(0), ProceduralAudio.Wind(),
                ProceduralAudio.SovereignDrone(), ProceduralAudio.LootPickup(),
            };
            yield return null;

            foreach (var clip in clips)
            {
                Assert.IsNotNull(clip);
                Assert.Greater(clip.samples, 1000, $"{clip.name} is too short to be audible");

                var data = Read(clip);
                Assert.Greater(Rms(data, 0, data.Length), 0.001f, $"{clip.name} is silence");
                Assert.LessOrEqual(Peak(data), 1.0001f, $"{clip.name} clips past full scale");
            }
        }

        [UnityTest]
        public IEnumerator StruckSoundsDecay()
        {
            // A hit that does not fall away reads as a drone, not as an impact.
            foreach (var clip in new[] { ProceduralAudio.Impact(), ProceduralAudio.Swing(),
                                         ProceduralAudio.Footstep(1), ProceduralAudio.LootPickup() })
            {
                var data = Read(clip);
                int third = data.Length / 3;

                float head = Rms(data, 0, third);
                float tail = Rms(data, data.Length - third, data.Length);

                Assert.Greater(head, tail * 2f,
                    $"{clip.name} should be much quieter at the end than at the start");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator BellRingsFarLongerThanAFootstep()
        {
            var bell = ProceduralAudio.Bell();
            var step = ProceduralAudio.Footstep(0);
            yield return null;

            Assert.Greater(bell.length, step.length * 8f,
                "the bell has to hang in the air; that is why ringing it is a commitment");
            Assert.Greater(bell.length, 3f);
        }

        [UnityTest]
        public IEnumerator AmbientBedsLoopWithoutAClick()
        {
            // A discontinuity between the last and first sample is an audible click on
            // every loop, which over a whole raid is unbearable.
            foreach (var clip in new[] { ProceduralAudio.Wind(), ProceduralAudio.SovereignDrone() })
            {
                var data = Read(clip);
                Assert.Less(Mathf.Abs(data[0]), 0.02f, $"{clip.name} starts away from zero");
                Assert.Less(Mathf.Abs(data[^1]), 0.02f, $"{clip.name} ends away from zero");
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator FootstepVariantsDifferFromEachOther()
        {
            var a = Read(ProceduralAudio.Footstep(0));
            var b = Read(ProceduralAudio.Footstep(1));
            yield return null;

            int compared = Mathf.Min(a.Length, b.Length);
            double difference = 0d;
            for (int i = 0; i < compared; i++) difference += Mathf.Abs(a[i] - b[i]);

            Assert.Greater(difference / compared, 0.01f,
                "consecutive footsteps must not be byte-identical");
        }

        [UnityTest]
        public IEnumerator SynthesisIsDeterministic()
        {
            // Cached, so a second request must hand back the identical clip rather than
            // re-rolling noise; otherwise runs would not be reproducible.
            var first = ProceduralAudio.Impact();
            var second = ProceduralAudio.Impact();
            yield return null;

            Assert.AreSame(first, second);
        }
    }
}
