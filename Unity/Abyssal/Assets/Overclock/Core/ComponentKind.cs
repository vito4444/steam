using System;

namespace Overclock.Core
{
    /// <summary>可以放到硅层上的元件种类。</summary>
    public enum ComponentKind
    {
        /// <summary>空格子。</summary>
        Empty = 0,

        /// <summary>数据源。每层固定若干个，是流量的起点，玩家不能放置也不能拆。</summary>
        Source,

        /// <summary>输出端口。把数据送出这一层，是流量的终点。</summary>
        Sink,

        /// <summary>导线。只负责传输，产热很低。</summary>
        Trace,

        /// <summary>缓冲器。吞吐上限更高，但产热明显。</summary>
        Buffer,

        /// <summary>总线。吞吐上限最高，产热也最高，是热量失控的常见起点。</summary>
        Bus,

        /// <summary>分流器。把一路数据拆成多路，降低单点负载。</summary>
        Splitter,

        /// <summary>压缩器。减少总流量但自身负载翻倍。</summary>
        Compressor,

        /// <summary>散热片。不传数据，把邻格的热量吸过来再摊给更远的地方。</summary>
        HeatSink,

        /// <summary>坏块。地形障碍，不能放东西，也不导热。</summary>
        DeadCell,
    }

    /// <summary>元件的静态属性。数值全部集中在这里，方便一次性看清平衡关系。</summary>
    public readonly struct ComponentSpec
    {
        /// <summary>单位时间能通过的数据量上限。0 表示不导数据。</summary>
        public readonly double Throughput;

        /// <summary>满负荷运行时每秒的产热。</summary>
        public readonly double HeatPerLoad;

        /// <summary>即使空载也存在的静态漏热。</summary>
        public readonly double IdleHeat;

        /// <summary>导热系数倍率。散热片远高于其他元件。</summary>
        public readonly double Conductivity;

        /// <summary>能承受的温度上限，超过就永久烧毁。</summary>
        public readonly double MeltingPoint;

        /// <summary>放置成本，单位是本层的蚀刻预算。</summary>
        public readonly int Cost;

        public ComponentSpec(double throughput, double heatPerLoad, double idleHeat,
                             double conductivity, double meltingPoint, int cost)
        {
            Throughput = throughput;
            HeatPerLoad = heatPerLoad;
            IdleHeat = idleHeat;
            Conductivity = conductivity;
            MeltingPoint = meltingPoint;
            Cost = cost;
        }
    }

    public static class ComponentLibrary
    {
        /// <summary>
        /// 元件数值表。
        ///
        /// 设计意图是让每种元件都有明确的取舍，没有任何一种是全面占优的：
        /// 导线便宜凉快但细，总线粗但会把自己烧掉，散热片不导数据却是唯一的降温手段，
        /// 压缩器用双倍的单点发热换取全局流量的下降。
        /// </summary>
        public static ComponentSpec Of(ComponentKind kind)
        {
            switch (kind)
            {
                case ComponentKind.Source:
                    return new ComponentSpec(12.0, 0.35, 0.60, 1.0, 260.0, 0);
                case ComponentKind.Sink:
                    return new ComponentSpec(24.0, 0.10, 0.20, 1.2, 300.0, 0);
                case ComponentKind.Trace:
                    return new ComponentSpec(6.0, 0.55, 0.05, 1.0, 150.0, 1);
                case ComponentKind.Buffer:
                    return new ComponentSpec(11.0, 0.95, 0.22, 0.8, 165.0, 3);
                case ComponentKind.Bus:
                    return new ComponentSpec(20.0, 1.70, 0.45, 0.7, 175.0, 6);
                case ComponentKind.Splitter:
                    return new ComponentSpec(9.0, 0.70, 0.15, 0.9, 160.0, 4);
                case ComponentKind.Compressor:
                    return new ComponentSpec(7.0, 2.10, 0.30, 0.6, 185.0, 7);
                case ComponentKind.HeatSink:
                    return new ComponentSpec(0.0, 0.00, 0.00, 6.5, 400.0, 4);
                case ComponentKind.DeadCell:
                    return new ComponentSpec(0.0, 0.00, 0.00, 0.05, 9999.0, 0);
                default:
                    return new ComponentSpec(0.0, 0.00, 0.00, 0.35, 9999.0, 0);
            }
        }

        /// <summary>这种元件是否参与数据传输。</summary>
        public static bool CarriesData(ComponentKind kind)
            => Of(kind).Throughput > 0.0 && kind != ComponentKind.DeadCell;

        /// <summary>玩家能不能放置这种元件。数据源、输出端口和坏块属于地形。</summary>
        public static bool IsPlaceable(ComponentKind kind)
            => kind == ComponentKind.Trace || kind == ComponentKind.Buffer
               || kind == ComponentKind.Bus || kind == ComponentKind.Splitter
               || kind == ComponentKind.Compressor || kind == ComponentKind.HeatSink;
    }
}
