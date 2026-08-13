using System;

namespace Abyssal.Core
{
    /// <summary>
    /// 一段地层的物理属性。
    ///
    /// 玩法上最重要的两个字段是 <see cref="PorePressureGradient"/> 和
    /// <see cref="FracturePressureGradient"/>：它们共同决定了这一段的「安全泥浆窗口」。
    /// 泥浆太轻会让地层流体涌进井筒（井涌），太重会压裂地层（漏失）。
    /// 玩家的核心技能就是在地层切换时察觉窗口移动并及时跟上。
    /// </summary>
    [Serializable]
    public sealed class Formation
    {
        /// <summary>无线电和岩屑分析里显示的名字。</summary>
        public string Name = "未知地层";

        /// <summary>这一段的底界深度，单位米。</summary>
        public double BottomDepth = 500.0;

        /// <summary>抗钻强度，1.0 为基准砂岩。越大越难钻。</summary>
        public double Hardness = 1.0;

        /// <summary>研磨性，直接乘进钻头磨损速率。</summary>
        public double Abrasiveness = 1.0;

        /// <summary>孔隙压力梯度，kPa/m。正常压实地层约 9.8–10.5，异常高压层可达 18–20。</summary>
        public double PorePressureGradient = 10.2;

        /// <summary>破裂压力梯度，kPa/m。低于这个值地层不会被压开。</summary>
        public double FracturePressureGradient = 17.5;

        /// <summary>渗透率系数，影响井涌和漏失的流量。0 表示致密不渗透。</summary>
        public double Permeability = 0.4;

        /// <summary>是否含气。含气层井涌时气体会在上升过程中膨胀，风险远高于含水层。</summary>
        public bool GasBearing;

        /// <summary>岩屑在返出物里的视觉描述，玩家靠它反推自己钻到了什么。</summary>
        public string CuttingsDescription = "灰色细砂";

        /// <summary>压死这一段所需的最低泥浆密度，kg/m³。</summary>
        public double MinimumMudDensity => PorePressureGradient / Physics.G * 1000.0;

        /// <summary>压裂这一段的泥浆密度上限（静态，未计环空摩阻），kg/m³。</summary>
        public double MaximumMudDensity => FracturePressureGradient / Physics.G * 1000.0;

        /// <summary>安全窗口宽度，kg/m³。低于约 60 就属于「窄窗口」，玩家几乎没有容错空间。</summary>
        public double MudWindowWidth => MaximumMudDensity - MinimumMudDensity;
    }
}
