using System;
using System.Globalization;
using Worker.Core;

namespace Worker.Preview
{
    /// <summary>
    /// Headless entry point for the visual self-test.
    ///
    /// Usage:
    ///   dotnet run --project Tools/Preview -- [options]
    ///
    ///   --seed N          simulation seed (default 1)
    ///   --workers N       starting staff (default 4)
    ///   --ticks N         how many ticks to simulate (default 3 in-game days)
    ///   --interval N      ticks between captured frames (default quarter day)
    ///   --out PATH        output directory (default Artifacts/selftest/local)
    ///   --no-events       skip writing events.tsv
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            var settings = new SelfTestSettings();

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--seed":
                        settings.Seed = uint.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--workers":
                        settings.WorkerCount = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--ticks":
                        settings.Ticks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--interval":
                        settings.FrameIntervalTicks = int.Parse(Next(args, ref i), CultureInfo.InvariantCulture);
                        break;
                    case "--out":
                        settings.OutputDirectory = Next(args, ref i);
                        break;
                    case "--scenario":
                        settings.Scenario = Next(args, ref i);
                        break;
                    case "--no-events":
                        settings.WriteEventLog = false;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;
                    default:
                        Console.Error.WriteLine("unknown option: " + args[i]);
                        PrintUsage();
                        return 2;
                }
            }

            if (settings.FrameIntervalTicks <= 0) settings.FrameIntervalTicks = SimConfig.TicksPerDay / 4;

            Console.WriteLine("worker self-test");
            Console.WriteLine("  scenario " + settings.Scenario + ", seed " + settings.Seed
                              + ", workers " + settings.WorkerCount
                              + ", ticks " + settings.Ticks + ", frame every " + settings.FrameIntervalTicks);

            return SelfTestRun.Execute(settings);
        }

        private static string Next(string[] args, ref int index)
        {
            index++;
            if (index >= args.Length) throw new ArgumentException("missing value for " + args[index - 1]);
            return args[index];
        }

        private static void PrintUsage()
        {
            Console.WriteLine("usage: dotnet run --project Tools/Preview -- [--seed N] [--workers N]");
            Console.WriteLine("       [--ticks N] [--interval N] [--out PATH] [--no-events]");
        }
    }
}
