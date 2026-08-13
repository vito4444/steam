using System;
using System.Globalization;
using System.IO;
using System.Text;
using Monster.Audio;
using UnityEditor;
using UnityEngine;

namespace Monster.EditorTools
{
    /// <summary>Writes every synthesised clip to disk as a WAV.
    ///
    /// The booth's audio has been generated and structurally tested since M2 and has never
    /// once been heard, because this machine has no audio device. Tests can prove a clip is
    /// mono, unclipped, free of DC offset and deterministic; they cannot tell anyone whether
    /// the heater sounds like a heater.
    ///
    /// Exporting turns that from an unclosable gap into a reviewable artifact: the files can
    /// be listened to anywhere. It also prints the measurements worth knowing before
    /// listening, so an obviously broken clip is caught without ears.</summary>
    public static class AudioExport
    {
        private const string OutputFolder = "artifacts/audio";

        [MenuItem("Monster/Export Synthesised Audio")]
        public static void ExportAll()
        {
            var root = Directory.GetParent(Application.dataPath)?.Parent?.FullName
                       ?? Directory.GetCurrentDirectory();
            var directory = Path.Combine(root, OutputFolder);
            Directory.CreateDirectory(directory);

            var clips = new (string name, AudioClip clip)[]
            {
                ("room_tone", ProceduralAudio.RoomTone()),
                ("wind", ProceduralAudio.Wind()),
                ("heater", ProceduralAudio.Heater()),
                ("crt_whine", ProceduralAudio.CrtWhine()),
                ("switch", ProceduralAudio.SwitchThrow()),
                ("stamp", ProceduralAudio.Stamp()),
                ("paper", ProceduralAudio.PaperRustle()),
                ("telephone", ProceduralAudio.TelephoneRing()),
                ("vehicle", ProceduralAudio.VehicleArrival()),
            };

            var manifest = new StringBuilder();
            manifest.AppendLine("name,seconds,samples,rate,peak,rms,zero_crossings_per_second");

            foreach (var (name, clip) in clips)
            {
                if (clip == null)
                {
                    Debug.LogError($"[AudioExport] {name} came back null");
                    continue;
                }

                var samples = new float[clip.samples * clip.channels];
                clip.GetData(samples, 0);

                var path = Path.Combine(directory, name + ".wav");
                File.WriteAllBytes(path, EncodeWav(samples, clip.frequency, clip.channels));

                var peak = 0f;
                var sumOfSquares = 0.0;
                var crossings = 0;

                for (var i = 0; i < samples.Length; i++)
                {
                    peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
                    sumOfSquares += samples[i] * (double)samples[i];

                    if (i > 0 && Mathf.Sign(samples[i]) != Mathf.Sign(samples[i - 1]))
                    {
                        crossings++;
                    }
                }

                var seconds = clip.length;
                var rms = Math.Sqrt(sumOfSquares / Math.Max(1, samples.Length));

                manifest.AppendLine(string.Join(",", new[]
                {
                    name,
                    seconds.ToString("F2", CultureInfo.InvariantCulture),
                    clip.samples.ToString(CultureInfo.InvariantCulture),
                    clip.frequency.ToString(CultureInfo.InvariantCulture),
                    peak.ToString("F3", CultureInfo.InvariantCulture),
                    rms.ToString("F4", CultureInfo.InvariantCulture),
                    (crossings / Math.Max(0.001f, seconds)).ToString("F0", CultureInfo.InvariantCulture),
                }));

                Debug.Log($"[AudioExport] {name}: {seconds:F2}s, peak {peak:F3}, rms {rms:F4} -> {path}");
            }

            var manifestPath = Path.Combine(directory, "clips.csv");
            File.WriteAllText(manifestPath, manifest.ToString());
            Debug.Log($"[AudioExport] wrote {manifestPath}");
        }

        /// <summary>16-bit PCM. Written by hand because Unity has no runtime WAV encoder and
        /// pulling in a package for nine short clips would be worse.</summary>
        private static byte[] EncodeWav(float[] samples, int sampleRate, int channels)
        {
            var dataBytes = samples.Length * 2;

            using var stream = new MemoryStream(44 + dataBytes);
            using var writer = new BinaryWriter(stream);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataBytes);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(sampleRate);
            writer.Write(sampleRate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            foreach (var sample in samples)
            {
                writer.Write((short)(Mathf.Clamp(sample, -1f, 1f) * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }
}
