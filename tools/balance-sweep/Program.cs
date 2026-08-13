using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Overclock.Core;

// 平衡性扫描。
//
// 让三种水平的自动玩家各跑上千局，看通关率、卡关层数、烧毁量和峰值温度。
// 这些数字回答的是设计问题而不是工程问题：
//   - 最保守的策略是不是真的赢不了（如果能赢，风险就没有意义）
//   - 最激进的策略是不是真的会死（如果不会死，克制就没有意义）
//   - 合格玩家的通关率落在哪一档（太高没挑战，太低劝退）
//
// 用法：dotnet run --project tools/balance-sweep -- [每种策略的局数]

if (args.Length > 0 && args[0] == "--trace")
{
    TraceSingleRun();
    return;
}

int runsPerStrategy = args.Length > 0 && int.TryParse(args[0], out int n) ? n : 400;

Console.WriteLine($"OVERCLOCK 平衡性扫描   每种策略 {runsPerStrategy} 局");
Console.WriteLine(new string('-', 74));

var strategies = new[]
{
    AutoPlayer.Strategy.Minimal,
    AutoPlayer.Strategy.Greedy,
    AutoPlayer.Strategy.Balanced,
};

var names = new Dictionary<AutoPlayer.Strategy, string>
{
    [AutoPlayer.Strategy.Minimal] = "保守（只铺导线）",
    [AutoPlayer.Strategy.Greedy] = "激进（只铺总线）",
    [AutoPlayer.Strategy.Balanced] = "合格（主路加散热）",
};

var stopwatch = Stopwatch.StartNew();
var summaries = new List<(AutoPlayer.Strategy strategy, double winRate, double avgLayers,
                          double avgBurned, double avgPeak, int[] stuckAt)>();

foreach (var strategy in strategies)
{
    int completed = 0;
    long totalLayers = 0;
    long totalBurned = 0;
    double totalPeak = 0.0;
    var stuckAt = new int[9];

    for (int i = 0; i < runsPerStrategy; i++)
    {
        var player = new AutoPlayer(strategy, seed: i * 31 + 7);
        var result = player.PlayRun(seed: i * 977 + 13);

        if (result.Completed) completed++;
        else stuckAt[Math.Clamp(result.LayersCleared, 0, 8)]++;

        totalLayers += result.LayersCleared;
        totalBurned += result.TotalBurned;
        totalPeak += result.WorstPeak;
    }

    double winRate = completed / (double)runsPerStrategy;
    double avgLayers = totalLayers / (double)runsPerStrategy;
    double avgBurned = totalBurned / (double)runsPerStrategy;
    double avgPeak = totalPeak / runsPerStrategy;

    summaries.Add((strategy, winRate, avgLayers, avgBurned, avgPeak, stuckAt));

    Console.WriteLine($"{names[strategy],-20} 通关率 {winRate,6:P1}   " +
                      $"平均过 {avgLayers,4:F2} 层   " +
                      $"平均烧 {avgBurned,5:F1} 件   " +
                      $"峰值 {avgPeak,5:F0}C");

    var distribution = string.Join("  ",
        Enumerable.Range(0, 9).Where(k => stuckAt[k] > 0)
                  .Select(k => $"L{k}:{stuckAt[k]}"));
    if (!string.IsNullOrEmpty(distribution))
        Console.WriteLine($"{"",20} 卡关分布   {distribution}");
}

Console.WriteLine(new string('-', 74));
Console.WriteLine($"耗时 {stopwatch.ElapsedMilliseconds} 毫秒");
Console.WriteLine();

// 设计判据。这几条不是工程断言，是「这个游戏成不成立」的底线。
var minimal = summaries.First(s => s.strategy == AutoPlayer.Strategy.Minimal);
var greedy = summaries.First(s => s.strategy == AutoPlayer.Strategy.Greedy);
var balanced = summaries.First(s => s.strategy == AutoPlayer.Strategy.Balanced);

Console.WriteLine("设计判据");
Report("保守策略赢不了（否则冒险没有意义）", minimal.winRate < 0.10,
       $"通关率 {minimal.winRate:P1}");
Report("激进策略会死（否则克制没有意义）", greedy.winRate < 0.55,
       $"通关率 {greedy.winRate:P1}，平均烧 {greedy.avgBurned:F1} 件");
// 判据按自动玩家的水平来定，不是按人。这个 AI 只会「铺几条导线，热了贴散热片」，
// 不会规划拓扑、不会为瓶颈段单独升级、也不会根据升级调整打法。
// 真人玩家的通关率会明显高于它，所以这里的合格区间取 15% 到 45%。
Report("合格策略赢面在 15% 到 45% 之间", balanced.winRate is > 0.15 and < 0.45,
       $"通关率 {balanced.winRate:P1}");
Report("合格策略明显强于保守策略", balanced.avgLayers > minimal.avgLayers + 1.0,
       $"平均层数 {balanced.avgLayers:F2} 对 {minimal.avgLayers:F2}");

static void Report(string claim, bool pass, string evidence)
    => Console.WriteLine($"  [{(pass ? "通过" : "未过")}] {claim}   {evidence}");


// 逐层追踪一局，用来定位「卡在第几层、为什么卡」。
// 汇总统计只能告诉你有问题，定位得靠单局细节。
static void TraceSingleRun()
{
    foreach (var strategy in new[] { AutoPlayer.Strategy.Minimal,
                                     AutoPlayer.Strategy.Greedy,
                                     AutoPlayer.Strategy.Balanced })
    {
        Console.WriteLine($"=== {strategy} ===");
        var run = new RunState(1234, totalLayers: 8);
        var player = new AutoPlayer(strategy, seed: 99);

        while (!run.RunComplete)
        {
            var layer = run.CreateLayer(16, 10);
            int sources = 0, sinks = 0, dead = 0;
            for (int y = 0; y < layer.Height; y++)
            for (int x = 0; x < layer.Width; x++)
            {
                var k = layer.CellAt(x, y);
                if (k == ComponentKind.Source) sources++;
                else if (k == ComponentKind.Sink) sinks++;
                else if (k == ComponentKind.DeadCell) dead++;
            }

            int startBudget = layer.Budget;
            var r = player.PlayLayer(layer);

            Console.WriteLine(
                $"  L{run.LayerIndex}  目标 {r.TargetThroughput,5:F1}  达成 {r.FinalThroughput,5:F1}  " +
                $"预算 {startBudget,3} -> {r.BudgetLeft,3}  峰值 {r.PeakTemperature,5:F0}C  " +
                $"烧 {r.Burned,2}  源 {sources} 汇 {sinks} 坏块 {dead}  " +
                $"{(r.Cleared ? "通过" : "未过")}");

            if (!r.Cleared) break;

            var offers = run.DrawUpgrades();
            if (offers.Count > 0)
            {
                var pick = offers[0];
                run.TakeUpgrade(pick);
                Console.WriteLine($"        升级：{pick.Name}  {pick.Description}");
            }
            run.AdvanceLayer();
        }
        Console.WriteLine();
    }
}
