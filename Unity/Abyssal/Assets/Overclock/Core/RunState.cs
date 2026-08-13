using System;
using System.Collections.Generic;

namespace Overclock.Core
{
    /// <summary>
    /// 一局运行期间累积的全局修正。
    ///
    /// 所有升级最终都落到这几个乘数上。刻意保持数量少：
    /// 修正项一多，玩家就无法预判一次升级到底会带来什么，
    /// 构筑就退化成了猜谜。
    /// </summary>
    [Serializable]
    public sealed class RunModifiers
    {
        /// <summary>元件发热倍率。低于 1 表示更凉快。</summary>
        public double HeatMultiplier = 1.0;

        /// <summary>吞吐上限倍率。</summary>
        public double ThroughputMultiplier = 1.0;

        /// <summary>导热倍率，同时影响散热片和普通元件之间的热交换。</summary>
        public double ConductivityMultiplier = 1.0;

        /// <summary>熔点加成，单位摄氏度。</summary>
        public double MeltingPointBonus = 0.0;

        /// <summary>每层的额外预算。</summary>
        public int BudgetBonus = 0;

        /// <summary>元件成本折扣，0.2 表示便宜两成。</summary>
        public double CostDiscount = 0.0;

        public RunModifiers Clone() => (RunModifiers)MemberwiseClone();

        /// <summary>把修正应用到元件的静态属性上。</summary>
        public ComponentSpec Apply(ComponentSpec spec)
        {
            return new ComponentSpec(
                spec.Throughput * ThroughputMultiplier,
                spec.HeatPerLoad * HeatMultiplier,
                spec.IdleHeat * HeatMultiplier,
                spec.Conductivity * ConductivityMultiplier,
                spec.MeltingPoint + MeltingPointBonus,
                Math.Max(1, (int)Math.Round(spec.Cost * (1.0 - CostDiscount))));
        }
    }

    /// <summary>一个可选的架构升级。</summary>
    public sealed class Upgrade
    {
        public string Id;
        public string Name;
        public string Description;

        /// <summary>所属构筑轴线。玩家应该能感觉到自己这一局在往哪个方向走。</summary>
        public BuildAxis Axis;

        public Action<RunModifiers> Apply;
    }

    /// <summary>
    /// 构筑轴线。每条轴线上的升级互相协同，
    /// 玩家在三选一时实际上是在决定这一局要成为什么样的芯片。
    /// </summary>
    public enum BuildAxis
    {
        /// <summary>超导：极致压低发热，代价是吞吐上限低。</summary>
        Superconductor,

        /// <summary>暴力：无视发热，靠散热和快速重建硬撑。</summary>
        BruteForce,

        /// <summary>压缩：减少总流量但抬高单点负载。</summary>
        Compression,

        /// <summary>并行：大量低效但分散的线路。</summary>
        Parallel,
    }

    /// <summary>升级池。</summary>
    public static class UpgradeLibrary
    {
        public static readonly Upgrade[] All =
        {
            new Upgrade
            {
                Id = "cryo_etch",
                Name = "低温蚀刻",
                Description = "所有元件发热降低 18%",
                Axis = BuildAxis.Superconductor,
                Apply = m => m.HeatMultiplier *= 0.82,
            },
            new Upgrade
            {
                Id = "lattice_align",
                Name = "晶格对齐",
                Description = "发热降低 12%，吞吐降低 5%",
                Axis = BuildAxis.Superconductor,
                Apply = m => { m.HeatMultiplier *= 0.88; m.ThroughputMultiplier *= 0.95; },
            },
            new Upgrade
            {
                Id = "refractory",
                Name = "耐火封装",
                Description = "所有元件熔点提高 28 度",
                Axis = BuildAxis.BruteForce,
                Apply = m => m.MeltingPointBonus += 28.0,
            },
            new Upgrade
            {
                Id = "wide_rail",
                Name = "宽轨总线",
                Description = "吞吐上限提高 15%，发热提高 8%",
                Axis = BuildAxis.BruteForce,
                Apply = m => { m.ThroughputMultiplier *= 1.15; m.HeatMultiplier *= 1.08; },
            },
            new Upgrade
            {
                Id = "vapor_chamber",
                Name = "均热腔",
                Description = "导热效率提高 30%",
                Axis = BuildAxis.BruteForce,
                Apply = m => m.ConductivityMultiplier *= 1.30,
            },
            new Upgrade
            {
                Id = "dense_pack",
                Name = "高密封装",
                Description = "元件成本降低 25%",
                Axis = BuildAxis.Compression,
                Apply = m => m.CostDiscount = Math.Min(0.6, m.CostDiscount + 0.25),
            },
            new Upgrade
            {
                Id = "batch_encode",
                Name = "批量编码",
                Description = "吞吐提高 10%，熔点降低 10 度",
                Axis = BuildAxis.Compression,
                Apply = m => { m.ThroughputMultiplier *= 1.10; m.MeltingPointBonus -= 10.0; },
            },
            new Upgrade
            {
                Id = "extra_mask",
                Name = "追加掩模",
                Description = "每层额外获得 12 点预算",
                Axis = BuildAxis.Parallel,
                Apply = m => m.BudgetBonus += 12,
            },
            new Upgrade
            {
                Id = "redundant_path",
                Name = "冗余通路",
                Description = "每层额外 8 点预算，发热降低 6%",
                Axis = BuildAxis.Parallel,
                Apply = m => { m.BudgetBonus += 8; m.HeatMultiplier *= 0.94; },
            },
        };

        /// <summary>
        /// 抽三个不重复的升级。已经拿过的不再出现——
        /// 重复的选项会让三选一变成「没得选」。
        /// </summary>
        public static List<Upgrade> Draw(int seed, ICollection<string> taken, int count = 3)
        {
            var rng = new Random(seed);
            var pool = new List<Upgrade>();
            foreach (var u in All) if (!taken.Contains(u.Id)) pool.Add(u);

            var result = new List<Upgrade>();
            while (result.Count < count && pool.Count > 0)
            {
                int i = rng.Next(pool.Count);
                result.Add(pool[i]);
                pool.RemoveAt(i);
            }
            return result;
        }
    }

    /// <summary>
    /// 一整局的进度：第几层、拿了哪些升级、当前的修正是什么。
    /// </summary>
    public sealed class RunState
    {
        public int Seed { get; }
        public int LayerIndex { get; private set; } = 1;
        public RunModifiers Modifiers { get; } = new RunModifiers();
        public List<string> TakenUpgrades { get; } = new List<string>();

        /// <summary>本局要通关需要打穿的层数。</summary>
        public int TotalLayers { get; }

        public bool RunComplete => LayerIndex > TotalLayers;

        public RunState(int seed, int totalLayers = 8)
        {
            Seed = seed;
            TotalLayers = totalLayers;
        }

        /// <summary>难度随层数线性上升，最后一层接近满难度。</summary>
        public double Difficulty =>
            TotalLayers <= 1 ? 1.0 : (LayerIndex - 1) / (double)(TotalLayers - 1);

        /// <summary>生成当前层。修正会同时影响预算和元件属性。</summary>
        public SiliconLayer CreateLayer(int width, int height)
        {
            var layer = LayerGenerator.Generate(Seed * 7919 + LayerIndex, width, height, Difficulty);
            layer.Modifiers = Modifiers;
            layer.Budget += Modifiers.BudgetBonus;
            return layer;
        }

        /// <summary>本层通关后抽升级。</summary>
        public List<Upgrade> DrawUpgrades()
            => UpgradeLibrary.Draw(Seed * 104729 + LayerIndex, TakenUpgrades);

        public void TakeUpgrade(Upgrade upgrade)
        {
            if (upgrade == null) return;
            upgrade.Apply?.Invoke(Modifiers);
            TakenUpgrades.Add(upgrade.Id);
        }

        public void AdvanceLayer() => LayerIndex++;

        /// <summary>玩家这一局的构筑倾向，取拿到最多升级的那条轴线。</summary>
        public BuildAxis DominantAxis()
        {
            var tally = new Dictionary<BuildAxis, int>();
            foreach (var id in TakenUpgrades)
            {
                foreach (var u in UpgradeLibrary.All)
                {
                    if (u.Id != id) continue;
                    tally.TryGetValue(u.Axis, out int n);
                    tally[u.Axis] = n + 1;
                    break;
                }
            }

            var best = BuildAxis.Superconductor;
            int bestCount = -1;
            foreach (var pair in tally)
            {
                if (pair.Value <= bestCount) continue;
                best = pair.Key;
                bestCount = pair.Value;
            }
            return best;
        }
    }
}
