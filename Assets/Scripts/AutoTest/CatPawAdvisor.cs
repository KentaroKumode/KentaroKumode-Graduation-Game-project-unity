using System;
using UnityEngine;
using GameLoop;
using CombatSystem;

namespace AutoTest
{
    /// <summary>
    /// 挑戦カード〈猫の手〉(#5) の評価を本体の口 (<see cref="GameHooks.CatPawPick"/>) へ差す。 <b>人が遊ぶ時も BOT が遊ぶ時も同じ物</b>。
    ///
    /// <para>猫の評価は「Super の山札の挿し方の評価」(<see cref="AutoRunner"/> の ChooseDeckWiring と効用) そのもの。 その評価は AutoRunner の
    /// 技量・実験スイッチ (V3Flags)・型の重みに結びついているので、 本体へ移さず (二重に書かず)、 ここで <b>猫専用の AutoRunner の写し</b> を 1 つ持って呼ぶ。
    /// 写しは非アクティブの GameObject に付ける ── Awake / Start / OnDestroy が走らないので、 BOT の口 (BotHooks.Install)・自動開始・
    /// FindObjectOfType (非アクティブを拾わない) には現れない。 設定は既定の Super (技量 Super・V3Flags の既定) で固定し、
    /// 走っている BOT の腕 (技量や実験スイッチ) には左右されない ── 札の効果が BOT の設定で変わらないように。</para>
    ///
    /// <para>写しは最初に猫が挿す時に作る (札を受けていないランでは作らない)。 写しが作れない・評価で例外が出た時は null を返し、
    /// 本体は既定の挿し方 (<see cref="TerminalWiring.CatPawFallback"/>) で選ぶ。</para>
    /// </summary>
    public static class CatPawAdvisor
    {
        private static AutoRunner _runner;
        private static GameObject _host;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _shuttingDown = false; _runner = null; _host = null; GameHooks.CatPawPick = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Register()
        {
            GameHooks.CatPawPick = Pick;
            Application.quitting -= OnQuit; Application.quitting += OnQuit;
        }

        private static void OnQuit() { _shuttingDown = true; }

        private static int[] Pick(int[] dice, MutualTurnTelegraph tele)
        {
            var r = Runner();
            if (r == null) return null;
            try { return r.CatPawPick(dice, tele); }
            catch (Exception e) { Debug.LogWarning("[猫の手] Super の評価に失敗: " + e.Message); return null; }
        }

        /// <summary>猫専用の AutoRunner の写し (非アクティブ)。 シーンを移っても残す。 終了中は作らない。</summary>
        private static AutoRunner Runner()
        {
            if (_runner != null) return _runner;
            if (_shuttingDown) return null;
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = new GameObject("CatPawAdvisor") { hideFlags = HideFlags.HideInHierarchy };
            _host.SetActive(false);      // 先に止めておく: 付けた AutoRunner の Awake (BotHooks.Install) を走らせない
            UnityEngine.Object.DontDestroyOnLoad(_host);
            _runner = _host.AddComponent<AutoRunner>();
            _runner.autoStart = false;
            _runner.wiringSkill = AutoRunner.WiringSkill.Super;
            return _runner;
        }
    }
}
