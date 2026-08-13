using System;
using System.Collections.Generic;
using System.Linq;

namespace Abyssal.Core
{
    /// <summary>
    /// 一口井从上到下的地层序列。
    ///
    /// 地层序列决定了一个班次的难度曲线：什么时候安全窗口会突然收窄、
    /// 什么时候会撞上高研磨性的钻头杀手、什么时候会遇到含气层。
    /// </summary>
    public sealed class WellProfile
    {
        readonly List<Formation> _formations;

        public WellProfile(IEnumerable<Formation> formations)
        {
            _formations = formations?.OrderBy(f => f.BottomDepth).ToList()
                          ?? throw new ArgumentNullException(nameof(formations));
            if (_formations.Count == 0)
                throw new ArgumentException("井身剖面至少要有一段地层", nameof(formations));
        }

        public IReadOnlyList<Formation> Formations => _formations;

        /// <summary>井底深度，m。钻到这里就算完钻。</summary>
        public double TotalDepth => _formations[_formations.Count - 1].BottomDepth;

        public Formation FormationAt(double depth)
        {
            for (int i = 0; i < _formations.Count; i++)
                if (depth <= _formations[i].BottomDepth)
                    return _formations[i];
            return _formations[_formations.Count - 1];
        }

        /// <summary>下一次地层变化的深度，m。没有下一段时返回 null。</summary>
        public double? NextBoundaryBelow(double depth)
        {
            for (int i = 0; i < _formations.Count; i++)
                if (_formations[i].BottomDepth > depth)
                    return _formations[i].BottomDepth;
            return null;
        }
    }

    /// <summary>
    /// 八种地层模板。这些模板是玩法的词汇表——
    /// 玩家最终要学会的是「看到这种岩屑，就该往哪个方向调参数」。
    /// </summary>
    public static class FormationLibrary
    {
        /// <summary>松散砂岩。好钻，渗透性强，压不住就会涌水。</summary>
        public static Formation LooseSandstone(double bottom) => new Formation
        {
            Name = "松散砂岩",
            BottomDepth = bottom,
            Hardness = 0.65,
            Abrasiveness = 0.8,
            PorePressureGradient = 10.2,
            FracturePressureGradient = 16.8,
            Permeability = 0.85,
            CuttingsDescription = "浅黄色粗砂，颗粒松散",
        };

        /// <summary>致密页岩。硬，慢，但压力规矩，是最省心的一段。</summary>
        public static Formation TightShale(double bottom) => new Formation
        {
            Name = "致密页岩",
            BottomDepth = bottom,
            Hardness = 1.55,
            Abrasiveness = 0.9,
            PorePressureGradient = 10.4,
            FracturePressureGradient = 18.2,
            Permeability = 0.12,
            CuttingsDescription = "深灰色薄片状碎屑",
        };

        /// <summary>石灰岩。可能有溶洞，破裂压力低，稍微压重一点就开始漏。</summary>
        public static Formation Limestone(double bottom) => new Formation
        {
            Name = "石灰岩",
            BottomDepth = bottom,
            Hardness = 1.25,
            Abrasiveness = 1.15,
            PorePressureGradient = 10.0,
            FracturePressureGradient = 14.6,
            Permeability = 0.55,
            CuttingsDescription = "米白色棱角状碎块，遇酸起泡",
        };

        /// <summary>盐层。不渗透所以不会涌不会漏，但会蠕变缩径，停转就卡。</summary>
        public static Formation SaltDome(double bottom) => new Formation
        {
            Name = "盐岩",
            BottomDepth = bottom,
            Hardness = 0.80,
            Abrasiveness = 0.45,
            PorePressureGradient = 11.8,
            FracturePressureGradient = 21.0,
            Permeability = 0.02,
            CuttingsDescription = "半透明结晶颗粒，咸味",
        };

        /// <summary>高压页岩。安全窗口只有约 55 kg/m³，这一段基本没有容错空间。</summary>
        public static Formation OverpressuredShale(double bottom) => new Formation
        {
            Name = "异常高压页岩",
            BottomDepth = bottom,
            Hardness = 1.70,
            Abrasiveness = 1.0,
            PorePressureGradient = 16.4,
            FracturePressureGradient = 17.0,
            Permeability = 0.30,
            CuttingsDescription = "黑色致密碎屑，掉块明显",
        };

        /// <summary>含气砂岩。高压加含气，井涌上返时会膨胀，处理时间窗口极短。</summary>
        public static Formation GasSand(double bottom) => new Formation
        {
            Name = "含气砂岩",
            BottomDepth = bottom,
            Hardness = 0.90,
            Abrasiveness = 0.85,
            PorePressureGradient = 15.2,
            FracturePressureGradient = 16.4,
            Permeability = 0.95,
            GasBearing = true,
            CuttingsDescription = "灰砂夹气泡，返出物有轻微嘶声",
        };

        /// <summary>砾岩。钻头杀手，研磨性是普通地层的两倍多。</summary>
        public static Formation Conglomerate(double bottom) => new Formation
        {
            Name = "砾岩",
            BottomDepth = bottom,
            Hardness = 2.10,
            Abrasiveness = 2.30,
            PorePressureGradient = 10.6,
            FracturePressureGradient = 18.8,
            Permeability = 0.25,
            CuttingsDescription = "杂色磨圆砾石，粒径不均",
        };

        /// <summary>破碎带。破裂压力极低，几乎压一下就漏。</summary>
        public static Formation FracturedZone(double bottom) => new Formation
        {
            Name = "构造破碎带",
            BottomDepth = bottom,
            Hardness = 1.10,
            Abrasiveness = 1.30,
            PorePressureGradient = 10.1,
            FracturePressureGradient = 12.9,
            Permeability = 1.20,
            CuttingsDescription = "碎裂岩块，含方解石脉",
        };

        public static readonly Func<double, Formation>[] All =
        {
            LooseSandstone, TightShale, Limestone, SaltDome,
            OverpressuredShale, GasSand, Conglomerate, FracturedZone,
        };
    }

    /// <summary>井身剖面的构造器。既有手工剧本，也有程序化生成。</summary>
    public static class WellProfiles
    {
        /// <summary>
        /// 培训井段。只有两种温和的地层，用来教玩家基本的钻压和排量配合，
        /// 全程不会出现井涌或漏失。
        /// </summary>
        public static WellProfile Training() => new WellProfile(new[]
        {
            FormationLibrary.LooseSandstone(1900),
            FormationLibrary.TightShale(2100),
        });

        /// <summary>
        /// 第一个正式班次。在砂岩和页岩之后插入一段石灰岩，
        /// 让玩家第一次体验「窗口上限突然降下来」的感觉。
        /// </summary>
        public static WellProfile ShiftOne() => new WellProfile(new[]
        {
            FormationLibrary.LooseSandstone(1920),
            FormationLibrary.TightShale(2050),
            FormationLibrary.Limestone(2140),
            FormationLibrary.TightShale(2260),
        });

        /// <summary>盐层班次。重点是「不能停转」——停下来就被盐岩缩径卡住。</summary>
        public static WellProfile SaltShift() => new WellProfile(new[]
        {
            FormationLibrary.TightShale(2280),
            FormationLibrary.SaltDome(2470),
            FormationLibrary.TightShale(2560),
        });

        /// <summary>高压班次。窗口收窄到几十 kg/m³，是难度的第一个台阶。</summary>
        public static WellProfile OverpressureShift() => new WellProfile(new[]
        {
            FormationLibrary.TightShale(2600),
            FormationLibrary.OverpressuredShale(2760),
            FormationLibrary.Conglomerate(2830),
        });

        /// <summary>含气班次。一旦判断失误就是井喷，这是主线的高潮段。</summary>
        public static WellProfile GasShift() => new WellProfile(new[]
        {
            FormationLibrary.Conglomerate(2880),
            FormationLibrary.FracturedZone(2950),
            FormationLibrary.GasSand(3080),
            FormationLibrary.TightShale(3200),
        });

        /// <summary>
        /// 程序化生成的井段，用于主线之后的无尽模式。
        /// <paramref name="difficulty"/> 从 0 到 1，越高越容易抽到窄窗口和含气层。
        /// </summary>
        public static WellProfile Procedural(int seed, double startDepth, double difficulty)
        {
            var rng = new Random(seed);
            difficulty = Physics.Clamp01(difficulty);

            var safe = new Func<double, Formation>[]
            {
                FormationLibrary.LooseSandstone,
                FormationLibrary.TightShale,
                FormationLibrary.SaltDome,
            };
            var risky = new Func<double, Formation>[]
            {
                FormationLibrary.Limestone,
                FormationLibrary.OverpressuredShale,
                FormationLibrary.GasSand,
                FormationLibrary.Conglomerate,
                FormationLibrary.FracturedZone,
            };

            var list = new List<Formation>();
            double depth = startDepth;
            int segments = 3 + rng.Next(0, 3);

            for (int i = 0; i < segments; i++)
            {
                depth += 70.0 + rng.NextDouble() * 130.0;
                bool pickRisky = rng.NextDouble() < 0.25 + 0.5 * difficulty;
                var pool = pickRisky ? risky : safe;
                list.Add(pool[rng.Next(pool.Length)](depth));
            }

            // 最后一段永远是页岩，给玩家一个能喘气的收尾。
            list.Add(FormationLibrary.TightShale(depth + 90.0));
            return new WellProfile(list);
        }
    }
}
