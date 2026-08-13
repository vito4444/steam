using System;
using System.Collections;
using Decoder.Signal;
using Decoder.UI;
using UnityEngine;

namespace Decoder.Gameplay
{
    /// <summary>
    /// 自动演练。在没有人操作的环境里把一个班次从头走到尾，
    /// 用来验证"搜频、收到信号、抄下电码、查表出译文、上报得到判定"这条链路是通的。
    ///
    /// 这不是给玩家的功能，是给自动化验证用的。手工点一遍只能证明当时能玩，
    /// 每次改完代码都自动走一遍，才能在链路断掉的那一刻就发现。
    ///
    /// 用命令行参数 -playtest 激活，-playtestSpeed 调整节奏。
    /// </summary>
    public sealed class PlaytestDriver : MonoBehaviour
    {
        [Tooltip("勾选后无论有没有命令行参数都会自动演练")]
        public bool alwaysRun;

        [Tooltip("每个步骤之间停留多少秒，留出时间给截图")]
        public float stepSeconds = 2.5f;

        public RadioReceiver receiver;
        public StationHud hud;

        private void Start()
        {
            if (!alwaysRun && !HasCommandLineFlag("-playtest"))
            {
                return;
            }

            StartCoroutine(RunScript());
        }

        private static bool HasCommandLineFlag(string flag)
        {
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (string.Equals(arg, flag, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private IEnumerator RunScript()
        {
            if (receiver == null || hud == null)
            {
                Debug.LogError("[PlaytestDriver] 缺少接线，无法演练");
                yield break;
            }

            var shift = ShiftLibrary.FirstShift();
            var primary = shift.Primary;
            var telegraph = ChineseTelegraphCode.Shared;

            hud.SetStatusBanner("自动演练 · 正在扫描频段");
            Debug.Log("[PlaytestDriver] 开始演练");

            // 第一步：从频段低端扫到主线电台，模拟玩家搜频的过程。
            var from = shift.bandLowKHz;
            var to = primary.frequencyKHz;
            var scanSeconds = stepSeconds;
            for (var t = 0f; t < scanSeconds; t += Time.deltaTime)
            {
                receiver.tunedKHz = Mathf.Lerp(from, to, t / scanSeconds);
                yield return null;
            }

            receiver.tunedKHz = to;
            yield return new WaitForSeconds(stepSeconds);

            Debug.Log($"[PlaytestDriver] 已调到 {receiver.tunedKHz:F2} kHz，" +
                      $"信号强度 {receiver.SignalLevel:P0}，" +
                      $"呼号 {receiver.CurrentStation?.Callsign ?? "无"}");
            hud.SetStatusBanner($"自动演练 · 截获 {receiver.CurrentStation?.Callsign}");

            // 第二步：逐字抄下电码，模拟玩家一边听一边敲。
            var digits = primary.ResolveAirText(telegraph);
            var perChar = Mathf.Max(0.04f, stepSeconds / Mathf.Max(1, digits.Length));
            foreach (var c in digits)
            {
                hud.AppendCopiedCharacter(c);
                yield return new WaitForSeconds(perChar);
            }

            yield return new WaitForSeconds(stepSeconds);
            Debug.Log($"[PlaytestDriver] 抄收完成: {hud.CopiedBuffer}");

            // 第三步：逐组查电码表。这是玩家真正要做的动作，
            // 一组一组查出来才知道电文说的是什么。
            hud.SetStatusBanner("自动演练 · 查电码表");
            var groups = digits.Length / ChineseTelegraphCode.CodeLength;
            for (var g = 0; g < groups; g++)
            {
                hud.LookUpOneGroup();
                yield return new WaitForSeconds(stepSeconds * 0.5f);
            }

            // 第四步：填上报单。呼号和频率都要玩家自己记，系统不代填。
            hud.SetStatusBanner("自动演练 · 填写上报单");
            hud.FillForm(primary.callsign, primary.frequencyKHz.ToString("F2"));
            yield return new WaitForSeconds(stepSeconds);

            // 第五步：把威胁等级调到这条电文应有的等级。
            while (hud.SelectedLevel != primary.correctLevel)
            {
                hud.CycleLevel(hud.SelectedLevel < primary.correctLevel ? 1 : -1);
                yield return null;
            }

            hud.SetStatusBanner("自动演练 · 送出上报");
            yield return new WaitForSeconds(stepSeconds * 0.5f);

            // 第六步：翻一下档案，确认听过的电台都记下来了。
            hud.SetStatusBanner("自动演练 · 翻阅电台档案");
            hud.ToggleArchive(true);
            yield return new WaitForSeconds(stepSeconds * 1.5f);
            hud.ToggleArchive(false);

            // 第七步：送出，看判定。
            hud.SubmitReport();
            Debug.Log("[PlaytestDriver] 已送出上报");
            hud.SetStatusBanner("自动演练 · 完成");
        }
    }
}
