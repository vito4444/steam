using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Worker.Core;

namespace Worker.Preview
{
    public sealed class SelfTestSettings
    {
        public uint Seed = 1;
        public int WorkerCount = 4;
        public int Ticks = SimConfig.TicksPerDay * 3;
        public int FrameIntervalTicks = SimConfig.TicksPerDay / 4;
        public string OutputDirectory = "Artifacts/selftest/local";
        public bool WriteEventLog = true;

        /// <summary>"starter" for the opening factory, "automated" for the fully belted one.</summary>
        public string Scenario = "starter";
    }

    public sealed class FrameRecord
    {
        public int Tick;
        public string File;
        public int Shipped;
        public int Crafts;
        public int Balance;
        public int IdleWorkers;
        public ulong Hash;
    }

    /// <summary>
    /// Runs the simulation headlessly, captures frames and writes machine-readable
    /// metrics next to them.
    ///
    /// Performance numbers here are deliberately CPU-side only (simulation tick cost,
    /// allocations). This machine has no GPU, so any frame rate measured here would say
    /// nothing about what a player sees; render performance has to be measured on real
    /// hardware and is tracked separately.
    /// </summary>
    public static class SelfTestRun
    {
        public static int Execute(SelfTestSettings settings)
        {
            Directory.CreateDirectory(settings.OutputDirectory);
            string shotsDirectory = Path.Combine(settings.OutputDirectory, "shots");
            Directory.CreateDirectory(shotsDirectory);

            var world = settings.Scenario == "automated"
                ? Scenarios.AutomatedFactory(settings.Seed, settings.WorkerCount)
                : Scenarios.StarterFactory(settings.Seed, settings.WorkerCount);
            var order = Scenarios.AddOpeningOrder(world);
            Scenarios.AddContractSeries(world, 4);

            var events = new List<string>();
            if (settings.WriteEventLog)
            {
                world.EventRaised += evt => events.Add(evt.ToString());
            }

            var renderer = new WorldRenderer(new RenderOptions());
            var frames = new List<FrameRecord>();
            var tickMicros = new List<long>(settings.Ticks);

            long allocationsBefore = GC.GetTotalAllocatedBytes(precise: false);
            var stopwatch = new Stopwatch();
            var totalWatch = Stopwatch.StartNew();

            int idleSamples = 0;
            int workerSamples = 0;

            CaptureFrame(world, renderer, shotsDirectory, frames, settings);

            for (int i = 0; i < settings.Ticks; i++)
            {
                stopwatch.Restart();
                world.Step();
                stopwatch.Stop();
                tickMicros.Add(stopwatch.ElapsedTicks * 1_000_000 / Stopwatch.Frequency);

                for (int w = 0; w < world.Workers.Count; w++)
                {
                    workerSamples++;
                    if (world.Workers[w].IsIdle) idleSamples++;
                }

                if (world.Tick % settings.FrameIntervalTicks == 0)
                {
                    CaptureFrame(world, renderer, shotsDirectory, frames, settings);
                }
            }

            totalWatch.Stop();
            long allocated = GC.GetTotalAllocatedBytes(precise: false) - allocationsBefore;

            CaptureFrame(world, renderer, shotsDirectory, frames, settings);

            tickMicros.Sort();
            var metrics = new StringBuilder();
            metrics.Append("{\n");
            AppendField(metrics, "seed", settings.Seed.ToString());
            AppendField(metrics, "workerCount", settings.WorkerCount.ToString());
            AppendField(metrics, "ticks", settings.Ticks.ToString());
            AppendField(metrics, "wallClockMs", totalWatch.ElapsedMilliseconds.ToString());
            AppendField(metrics, "tickMicrosP50", Percentile(tickMicros, 50).ToString());
            AppendField(metrics, "tickMicrosP95", Percentile(tickMicros, 95).ToString());
            AppendField(metrics, "tickMicrosMax", tickMicros.Count > 0 ? tickMicros[tickMicros.Count - 1].ToString() : "0");
            AppendField(metrics, "allocatedBytes", allocated.ToString());
            AppendField(metrics, "allocatedBytesPerTick", (settings.Ticks > 0 ? allocated / settings.Ticks : 0).ToString());
            AppendField(metrics, "realtimeBudgetUsedPercent",
                (Percentile(tickMicros, 95) * SimConfig.TicksPerSecond * 100 / 1_000_000).ToString());
            AppendField(metrics, "idleWorkerRatioPercent",
                (workerSamples > 0 ? idleSamples * 100 / workerSamples : 0).ToString());
            AppendField(metrics, "unitsShipped", world.TotalUnitsShipped.ToString());
            AppendField(metrics, "craftsCompleted", world.TotalCraftsCompleted.ToString());
            AppendField(metrics, "finalBalanceCents", world.Ledger.Balance.ToString());
            AppendField(metrics, "openingOrderState", "\"" + order.State + "\"");
            AppendField(metrics, "openingOrderDelivered", order.Delivered.ToString());
            AppendField(metrics, "finalStateHash", "\"" + StateHash.ToHex(StateHash.Compute(world)) + "\"");

            metrics.Append("  \"frames\": [\n");
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                metrics.Append("    {");
                metrics.Append("\"tick\": ").Append(frame.Tick).Append(", ");
                metrics.Append("\"file\": \"").Append(frame.File).Append("\", ");
                metrics.Append("\"shipped\": ").Append(frame.Shipped).Append(", ");
                metrics.Append("\"crafts\": ").Append(frame.Crafts).Append(", ");
                metrics.Append("\"balanceCents\": ").Append(frame.Balance).Append(", ");
                metrics.Append("\"idleWorkers\": ").Append(frame.IdleWorkers).Append(", ");
                metrics.Append("\"hash\": \"").Append(StateHash.ToHex(frame.Hash)).Append("\"}");
                if (i < frames.Count - 1) metrics.Append(',');
                metrics.Append('\n');
            }
            metrics.Append("  ]\n}\n");

            File.WriteAllText(Path.Combine(settings.OutputDirectory, "metrics.json"), metrics.ToString());

            if (settings.WriteEventLog)
            {
                File.WriteAllLines(Path.Combine(settings.OutputDirectory, "events.tsv"), events);
            }

            Console.WriteLine("self-test complete");
            Console.WriteLine("  output          " + Path.GetFullPath(settings.OutputDirectory));
            Console.WriteLine("  frames          " + frames.Count);
            Console.WriteLine("  ticks           " + settings.Ticks + " in " + totalWatch.ElapsedMilliseconds + " ms");
            Console.WriteLine("  tick p50/p95    " + Percentile(tickMicros, 50) + " / " + Percentile(tickMicros, 95) + " us");
            Console.WriteLine("  alloc per tick  " + (settings.Ticks > 0 ? allocated / settings.Ticks : 0) + " B");
            Console.WriteLine("  shipped         " + world.TotalUnitsShipped);
            Console.WriteLine("  crafts          " + world.TotalCraftsCompleted);
            Console.WriteLine("  opening order   " + order.State + " " + order.Delivered + "/" + order.Quantity);
            Console.WriteLine("  idle ratio      " + (workerSamples > 0 ? idleSamples * 100 / workerSamples : 0) + "%");
            Console.WriteLine("  state hash      " + StateHash.ToHex(StateHash.Compute(world)));

            // A run that never shipped anything means the factory stalled; treat that as
            // a failed self-test rather than a green run with a sad screenshot.
            return world.TotalUnitsShipped > 0 ? 0 : 1;
        }

        private static void CaptureFrame(SimWorld world, WorldRenderer renderer, string directory,
            List<FrameRecord> frames, SelfTestSettings settings)
        {
            string fileName = "tick_" + world.Tick.ToString("D6", CultureInfo.InvariantCulture) + ".png";
            var options = new RenderOptions
            {
                Caption = "DAY " + (world.Tick / SimConfig.TicksPerDay + 1)
                          + "   TICK " + world.Tick
                          + "   SHIPPED " + world.TotalUnitsShipped
                          + "   SEED " + settings.Seed
            };

            var frameRenderer = new WorldRenderer(options);
            var raster = frameRenderer.Render(world);
            raster.SavePng(Path.Combine(directory, fileName));

            int idle = 0;
            for (int i = 0; i < world.Workers.Count; i++)
            {
                if (world.Workers[i].IsIdle) idle++;
            }

            frames.Add(new FrameRecord
            {
                Tick = world.Tick,
                File = fileName,
                Shipped = world.TotalUnitsShipped,
                Crafts = world.TotalCraftsCompleted,
                Balance = world.Ledger.Balance,
                IdleWorkers = idle,
                Hash = StateHash.Compute(world)
            });
        }

        private static long Percentile(List<long> sortedValues, int percentile)
        {
            if (sortedValues.Count == 0) return 0;
            int index = sortedValues.Count * percentile / 100;
            if (index >= sortedValues.Count) index = sortedValues.Count - 1;
            return sortedValues[index];
        }

        private static void AppendField(StringBuilder builder, string name, string value)
            => builder.Append("  \"").Append(name).Append("\": ").Append(value).Append(",\n");
    }
}
