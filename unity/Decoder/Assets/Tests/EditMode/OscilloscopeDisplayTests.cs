using Decoder.UI;
using NUnit.Framework;

namespace Decoder.Tests
{
    /// <summary>
    /// 示波器迹线的亮度分布。
    ///
    /// 这条曲线决定听障玩家看到的是什么：如果一列被均匀填满，满幅键控就是一堵实心色块，
    /// 点和划之间只剩宽度差；有了驻留加权，上下亮边把包络的轮廓勾出来，
    /// 短促的点和拖长的划在轮廓上就是两种形状。所以这里守的不只是"好看"。
    /// </summary>
    public sealed class OscilloscopeDisplayTests
    {
        [Test]
        public void DwellWeight_PeaksAtTheTurningPoints()
        {
            // 波峰波谷处电子束速度趋近零，驻留最久，亮度必须到满。
            Assert.That(OscilloscopeDisplay.DwellWeight(1f), Is.EqualTo(1f).Within(0.001f));
        }

        [Test]
        public void DwellWeight_DimsTheZeroCrossing()
        {
            // 过零点速度最快，必须明显暗于亮边，否则整列糊成一块。
            var center = OscilloscopeDisplay.DwellWeight(0f);
            Assert.That(center, Is.GreaterThan(0f), "填充不能是全黑，否则包络内部会被掏空");
            Assert.That(center, Is.LessThan(0.5f), "填充亮度过半就看不出上下亮边了");
        }

        [Test]
        public void DwellWeight_RisesMonotonicallyTowardTheEdge()
        {
            var previous = OscilloscopeDisplay.DwellWeight(0f);
            for (var i = 1; i <= 64; i++)
            {
                var current = OscilloscopeDisplay.DwellWeight(i / 64f);
                Assert.That(current, Is.GreaterThanOrEqualTo(previous),
                    $"亮度在 u={i / 64f:F3} 处回落，迹线会出现假的暗环");
                previous = current;
            }
        }

        [Test]
        public void DwellWeight_ConcentratesBrightnessNearTheEdge()
        {
            // 线性渐变（falloff=1）在半幅处正好是中点。真实的驻留分布是凸的，
            // 亮度要压在靠近边缘的那一小段里，半幅处必须明显低于中点。
            var half = OscilloscopeDisplay.DwellWeight(0.5f);
            var edge = OscilloscopeDisplay.DwellWeight(1f);
            var center = OscilloscopeDisplay.DwellWeight(0f);
            var midpoint = (center + edge) * 0.5f;
            Assert.That(half, Is.LessThan(midpoint * 0.8f),
                "亮度分布不够凸，上下亮边会摊成整列的均匀渐变");
        }

        [Test]
        public void DwellWeight_ClampsOutOfRangeInput()
        {
            // 幅度归一化时的舍入可能给出略微越界的值，不能因此炸出负亮度或超白。
            Assert.That(OscilloscopeDisplay.DwellWeight(-0.3f),
                Is.EqualTo(OscilloscopeDisplay.DwellWeight(0f)).Within(0.001f));
            Assert.That(OscilloscopeDisplay.DwellWeight(2.5f),
                Is.EqualTo(OscilloscopeDisplay.DwellWeight(1f)).Within(0.001f));
        }

        [Test]
        public void Persistence_OutlivesOneFullSweep()
        {
            // 余辉比扫描周期短太多的话，屏幕上永远只有一小段波形在游动，
            // 读不出一个字符的完整节奏。整圈迹线必须留得住。
            var scope = new UnityEngine.GameObject("scope").AddComponent<OscilloscopeDisplay>();
            try
            {
                var halfLivesPerSweep = scope.sweepSeconds / scope.persistenceHalfLife;
                var oldestTraceLeft = UnityEngine.Mathf.Pow(0.5f, halfLivesPerSweep);
                Assert.That(oldestTraceLeft, Is.GreaterThan(0.15f),
                    "扫完一圈后最旧的迹线已经衰减殆尽，屏幕上看不到完整波形");
                Assert.That(oldestTraceLeft, Is.LessThan(0.7f),
                    "余辉太长，新旧迹线亮度拉不开，看不出光点扫到哪了");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(scope.gameObject);
            }
        }
    }
}
