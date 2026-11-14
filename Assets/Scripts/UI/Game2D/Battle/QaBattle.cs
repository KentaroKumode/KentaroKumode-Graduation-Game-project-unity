#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Reflection;
using CombatSystem;
using GameLoop;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 実機で戦闘の演出を確かめるための口 (2026-11-14・エディタと開発ビルドだけ)。 MCP の execute_code から 1 行で呼ぶ。
    /// 使い方は docs/qa/battle-fx.md。
    ///
    /// <para><see cref="Start"/> は必ず <see cref="SaveGuard.Suppressed"/> を立てる (ランの中断セーブ・メタ進行・設定・手引きの印を書かない)。
    /// 一度立てたら、 Play モードを抜けるまで立ったまま (次の Play で切に戻る)。</para>
    ///
    /// <para>演出の契機は、 なるべく本物の戦闘の経路で踏ませる: <see cref="Go"/> (おまかせで挿して決定) で 1 ターン進め、
    /// <see cref="ChargeNextTurn"/> で大技の周期を寄せる。 Demo* は戦闘の流れを通さず演出だけを直接流す (キーの K / J / H / Space と同じ)。</para>
    /// </summary>
    public static class QaBattle
    {
        private static Game2DBattleSim Sim => Object.FindObjectOfType<Game2DBattleSim>(true);
        private static Game2DBattleFx Fx => Object.FindObjectOfType<Game2DBattleFx>(true);

        /// <summary>セーブを止め、 (ランが無ければ) 新しいランを始めて、 指定した敵との戦闘の画面に入る。
        /// enemyId = 敵の id (<c>boss_layer6</c> など・ふつうの敵も可)。 family = 武器の家系 sword / axe / dagger / shield (段 2 の品を持たせる)・"none" か null = 素手。
        /// 例: <c>UI.Game2D.Battle.QaBattle.Start("boss_layer6", "axe")</c></summary>
        public static bool Start(string enemyId, string family = "sword")
        {
            SaveGuard.Suppressed = true;
            if (!Application.isPlaying) { Debug.LogWarning("[QaBattle] Play モードで呼ぶ"); return false; }
            var gm = GameManager.Instance;
            if (gm == null) { Debug.LogWarning("[QaBattle] GameManager が無い"); return false; }
            MarkAllTutorialsSeen();
            if (gm.Run == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver || CombatManager.Instance?.IsCombatActive == true)
                gm.StartNewRun();
            var run = gm.Run;
            if (string.IsNullOrEmpty(family) || family == "none") run.equippedWeaponId = null;
            else
            {
                var w = ItemSystem.ItemDatabase.Instance?.GetWeapon(family, 2);
                if (w == null) { Debug.LogWarning($"[QaBattle] 家系 '{family}' の武器が無い"); return false; }
                if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains(w.id)) ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, w.id);
                run.equippedWeaponId = w.id;
            }
            bool ok = gm.QaStartCombat(enemyId);
            Debug.Log($"[QaBattle] 戦闘 {enemyId} / 武器 {run.equippedWeaponId ?? "なし"} / セーブ停止 {SaveGuard.Suppressed} → {(ok ? "開始" : "失敗")}");
            return ok;
        }

        /// <summary>手引きを全部「見た」にする (手元だけ・SaveGuard で書かない)。 戦闘の画面の上に手引きの窓が出て止まらないように。</summary>
        private static void MarkAllTutorialsSeen()
        {
            foreach (var f in typeof(TutorialTopics).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) TutorialSeen.Mark((string)f.GetValue(null));
        }

        /// <summary>今のターンを、 おまかせで挿して決定する (本物の戦闘の経路で 1 ターン進む)。 判断待ちでない時は何もしない。</summary>
        public static void Go()
        {
            var s = Sim; if (s == null) return;
            if (s.phase == Game2DBattleSim.P.Wire) s.AutoWire();
            s.Button();
        }

        /// <summary>このターンの自分の攻撃を会心にしてから <see cref="Go"/> (戦闘の結果が変わる・確認専用)。</summary>
        public static void GoCrit()
        {
            var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.forceCritical = true;
            Go();
        }

        /// <summary>次のターンを大技のチャージにする (その次のターンが大技)。 大技の周期の起点 (Escalation.HeavyCycleOffset) を寄せる。
        /// 判断待ちの間に呼ぶ。 大技を持たない敵は false。</summary>
        public static bool ChargeNextTurn()
        {
            var cm = CombatManager.Instance; var e = cm?.CurrentEnemy; if (e == null || e.heavyPeriod <= 0) return false;
            int next = cm.CurrentCombatTurn + 1, keep = Escalation.HeavyCycleOffset;
            for (int d = 0; d < 40; d++)
                foreach (int off in new[] { keep + d, keep - d })
                {
                    Escalation.HeavyCycleOffset = off;
                    if (Escalation.TurnsToHeavy(e, next) == 1) { Debug.Log($"[QaBattle] 大技の起点 {keep} → {off} (T{next} がチャージ・T{next + 1} が大技)"); return true; }
                }
            Escalation.HeavyCycleOffset = keep;
            return false;
        }

        /// <summary>今の状態を 1 行で (フェーズ・敵・ターン・予告・演出が空いているか)。</summary>
        public static string Status()
        {
            var gm = GameManager.Instance; var s = Sim; var f = Fx; var cm = CombatManager.Instance;
            return $"phase={gm?.CurrentPhase} enemy={cm?.CurrentEnemy?.id} turn={cm?.CurrentCombatTurn} sim={s?.phase} heavyIn={s?.heavyIn} charging={s?.charging} down={s?.down} " +
                   $"fxIdle={f?.IsIdle} bossIdling={f?.BossIdling} saveSuppressed={SaveGuard.Suppressed}";
        }

        // ── 演出だけを直接流す (戦闘の流れを通さない・キーと同じ) ──

        private static BossFx.Kind _demo = BossFx.Kind.King;

        /// <summary>試験のボスを選ぶ (キー K): "King" / "Judge" / "Chevalier" / "Vesca" / "VescaFinal"。</summary>
        public static void DemoBoss(string kind) { if (System.Enum.TryParse(kind, out BossFx.Kind k)) _demo = k; }
        /// <summary>チャージ → 待機 (キー J の 1 回目)。</summary>
        public static void DemoCharge(int ash = 3) => Fx?.BossCharge(_demo, ash, 0);
        /// <summary>大技 (キー J の 2 回目・待機が出ていれば、 入ってよいコマまで回してから)。</summary>
        public static void DemoHeavy(int ash = 3, int taken = 12) => Fx?.BossHeavy(_demo, ash, taken);
        /// <summary>待機を止める (ダウンで取り消した時と同じ)。</summary>
        public static void DemoCancel() => Fx?.BossCancel();
        /// <summary>ボスの通常の技の絵を 1 回 (名前は Resources/Game2DFx の move-*: "move-king-attack" など)。</summary>
        public static void DemoMove(string name) => Fx?.PlayMove(name);
        /// <summary>自分の攻撃 (キー Space)。 family = sword / axe / dagger / shield (絵を流す) か null (手描き)。</summary>
        public static void DemoAttack(string family, bool crit, int dealt = 20)
        {
            var f = Fx; if (f == null) return;
            int wi = Game2DBattleFx.IndexOf(family ?? "sword", 2, false);
            f.WeaponSheetFamily = family; f.PlayerAttack(wi >= 0 ? wi : 0, crit, dealt);
        }
    }
}
#endif
