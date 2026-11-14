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
    /// <para><see cref="Start"/> / <see cref="Restart"/> は必ず <see cref="SaveGuard.Suppressed"/> を立てる (ランの中断セーブ・メタ進行・設定・手引きの印を書かない)。
    /// 一度立てたら、 Play モードを抜けるまで立ったまま (次の Play で切に戻る)。</para>
    ///
    /// <para>演出の契機は、 なるべく本物の戦闘の経路で踏ませる: <see cref="Go"/> (おまかせで挿して決定) で 1 ターン進め、
    /// <see cref="ChargeNextTurn"/> で大技の周期を寄せる。 Demo* は戦闘の流れを通さず演出だけを直接流す (キーの K / J / H / Space と同じ)。</para>
    /// </summary>
    public static class QaBattle
    {
        private static Game2DBattleSim Sim => Object.FindObjectOfType<Game2DBattleSim>(true);
        private static Game2DBattleFx Fx => Object.FindObjectOfType<Game2DBattleFx>(true);

        /// <summary>確認で使う HP の既定 (最大 HP・今の HP)。 ボス相手に負けないよう大きくしてある。</summary>
        public const int DefaultHp = 9999;

        /// <summary>セーブを止め、 ランが無ければ (タイトル・ゲームオーバーなら) 新しいランを始めて、 指定した敵との戦闘の画面に入る。
        /// ランがあれば作り直さない (戦闘中なら打ち切って始め直す = <see cref="Restart"/> と同じ)。
        /// enemyId = 敵の id (<c>boss_layer6</c> など・ふつうの敵も可)。 family = 武器の家系 sword / axe / dagger / shield (段 2 の品を持たせる)・"none" か null = 素手。
        /// hp = 最大 HP と今の HP (0 以下ならランの値のまま)。 例: <c>UI.Game2D.Battle.QaBattle.Start("boss_layer6", "axe")</c></summary>
        public static bool Start(string enemyId, string family = "sword", int hp = DefaultHp)
        {
            SaveGuard.Suppressed = true;
            if (!Application.isPlaying) { Debug.LogWarning("[QaBattle] Play モードで呼ぶ"); return false; }
            var gm = GameManager.Instance;
            if (gm == null) { Debug.LogWarning("[QaBattle] GameManager が無い"); return false; }
            MarkAllTutorialsSeen();
            if (gm.Run == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver)
                gm.StartNewRun();
            return Restart(enemyId, family, hp);
        }

        /// <summary>ランを作り直さずに、 敵と武器の家系を替えて戦闘を始め直す (戦闘中なら、 決着の処理を通さずに打ち切ってから)。 ランが無ければ false (先に <see cref="Start"/>)。
        /// 例: <c>UI.Game2D.Battle.QaBattle.Restart("boss_layer7_p4", "dagger")</c></summary>
        public static bool Restart(string enemyId, string family = "sword", int hp = DefaultHp)
        {
            SaveGuard.Suppressed = true;
            var gm = GameManager.Instance; var run = gm?.Run;
            if (run == null) { Debug.LogWarning("[QaBattle] ランが無い (先に Start)"); return false; }
            MarkAllTutorialsSeen();
            CombatManager.Instance?.QaAbortCombat();
            if (string.IsNullOrEmpty(family) || family == "none") run.equippedWeaponId = null;
            else
            {
                var w = ItemSystem.ItemDatabase.Instance?.GetWeapon(family, 2);
                if (w == null) { Debug.LogWarning($"[QaBattle] 家系 '{family}' の武器が無い"); return false; }
                if (run.ownedPassiveItems == null || !run.ownedPassiveItems.Contains(w.id)) ItemSystem.Helpers.PassiveAddHelper.AddPassiveItem(run, w.id);
                run.equippedWeaponId = w.id;
            }
            if (hp > 0) { run.playerMaxHP = hp; run.playerHP = hp; }      // SaveGuard を立てた後 (ランの値を直接書く・確認専用)
            Runner.Cancel(); Runner.State = "idle";      // 前の戦闘の go= を残さない
            Fx?.QaClear();                               // 前のボスの待機・暗幕・流れている絵を消す
            Game2DBattleFx.QaMoveLog.Clear();
            bool ok = gm.QaStartCombat(enemyId);
            Sim?.QaNewCombat();                          // 戦闘の画面を入り直させる (HP・武器・敵を読み直す。 戦闘開始の知らせの購読に頼らない)
            Game2DLoadingScreen.QaDismiss();      // 新しいランの「第 N 層」のロード画面を出さない (戦闘の画面を覆い、 閉じるまで Go が効かない)
            Debug.Log($"[QaBattle] 戦闘 {enemyId} / 武器 {run.equippedWeaponId ?? "なし"} / HP {run.playerHP}/{run.playerMaxHP} / セーブ停止 {SaveGuard.Suppressed} → {(ok ? "開始" : "失敗")}");
            return ok;
        }

        /// <summary>手引きを全部「見た」にする (手元だけ・SaveGuard で書かない)。 戦闘の画面の上に手引きの窓が出て止まらないように。</summary>
        private static void MarkAllTutorialsSeen()
        {
            foreach (var f in typeof(TutorialTopics).GetFields(BindingFlags.Public | BindingFlags.Static))
                if (f.IsLiteral && f.FieldType == typeof(string)) TutorialSeen.Mark((string)f.GetValue(null));
        }

        /// <summary>1 ターン進める (おまかせで挿して決定)。 判断待ちになるまで待ってから、 決まるまで 1 フレームに 1 回押す (振り直しの判断・
        /// 予告の写し直しで止まった時も押し直す)。 決着の画面 (勝ち / 負け) なら「次へ」を 1 回。 進んだかは <see cref="Status"/> の go= を見る
        /// (pending = 待ち中 / done = 進んだ / timeout = 20 秒待っても進まなかった)。 戻り値は受け付けたか。</summary>
        public static bool Go() => Runner.Request(false);

        /// <summary>そのターンの自分の攻撃を会心にしてから <see cref="Go"/> (戦闘の結果が変わる・確認専用)。</summary>
        public static bool GoCrit() => Runner.Request(true);

        /// <summary>「次のターンの敵の行動がチャージ、 その次のターンが大技」になるよう、 大技の周期の起点 (Escalation.HeavyCycleOffset) を寄せる。
        /// 判断待ち (ターン T) に呼ぶと T+1 がチャージ・T+2 が大技 (Go 1 回の後のターンの敵の行動でチャージが出る)。
        /// 今のターン T は変えない: 敵の攻撃値と予告 (チャージか) はターンの頭に決まっているので、 T をチャージにすると画面と本体が食い違う。
        /// 前にも後ろにも寄せて探し、 T の周期の位置 (大技か・チャージか) が変わる寄せ方は選ばない。
        /// 戻り値: 何ターン先がチャージになったか (1 = 次のターン・2 以上 = 次は無理だった時の一番近い所) / −1 = 大技を持たない敵か、 寄せられない。
        /// 無理な条件: 灰燼の王は起点を正の値にしか動かせない数え方なので、 戦闘の最初の数ターン (次のターン &lt; 周期 4 か 3) は 2 以上が返る
        /// ── その数 − 1 回 Go してから呼び直す。 周期 2 の敵は、 今のターンが大技になってしまうので次のターンへは寄せず 2 以上が返る。</summary>
        public static int ChargeNextTurn()
        {
            var cm = CombatManager.Instance; var e = cm?.CurrentEnemy; if (e == null || e.heavyPeriod <= 0) return -1;
            int cur = Mathf.Max(1, cm.CurrentCombatTurn), keep = Escalation.HeavyCycleOffset;
            int curPos = Escalation.TurnsToHeavy(e, cur);
            int bestK = int.MaxValue, bestOff = keep;
            for (int off = keep - 64; off <= cur + 64; off++)
            {
                Escalation.HeavyCycleOffset = off;
                int pos = Escalation.TurnsToHeavy(e, cur);
                if (pos != curPos && (pos == 0 || pos == 1 || curPos == 0 || curPos == 1)) continue;      // 今のターンの大技・チャージを変えない
                for (int k = 1; k <= 8 && k < bestK; k++)
                    if (Escalation.TurnsToHeavy(e, cur + k) == 1 && Escalation.TurnsToHeavy(e, cur + k + 1) == 0) { bestK = k; bestOff = off; break; }
                if (bestK == 1) break;
            }
            if (bestK == int.MaxValue) { Escalation.HeavyCycleOffset = keep; return -1; }
            Escalation.HeavyCycleOffset = bestOff;
            Debug.Log($"[QaBattle] 大技の起点 {keep} → {bestOff} (T{cur + bestK} がチャージ・T{cur + bestK + 1} が大技)");
            return bestK;
        }

        /// <summary>流れで流した技・武器・大技の絵の履歴 (「T3 move-relic-guard」の形・古い順・最大 40 件)。 turn を渡すとそのターンの分だけ (0 以下 = 全部)。
        /// 例: <c>UI.Game2D.Battle.QaBattle.MoveLog()</c></summary>
        public static string MoveLog(int turn = 0)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in Game2DBattleFx.QaMoveLog)
                if (turn <= 0 || e.StartsWith("T" + turn + " ")) sb.Append(e).Append(" | ");
            return sb.Length > 0 ? sb.ToString(0, sb.Length - 3) : "(なし)";
        }

        /// <summary>ヴェスカが次のターンに使う遺物の 1 枚目の種類を決める: "攻撃" / "防御" / "妨害" (英語 strike / guard / hex も可)。
        /// その段のプールの、 その種類の先頭の遺物に差し替える (1 ターン限り・効果も本物)。 判断待ちの間に呼ぶと、 次のターンの頭に効く。</summary>
        public static bool ForceRelic(string role)
        {
            string r = role == "strike" ? "攻撃" : role == "guard" ? "防御" : role == "hex" ? "妨害" : role;
            if (r != "攻撃" && r != "防御" && r != "妨害") return false;
            ItemSystem.PassiveSkills.Effects.VescaRelicPool.QaForceRole = r;
            return true;
        }

        /// <summary>GUI に大技を掛ける時の重みの地図を PNG (480×270・白 = 1 = 窓に接した所・黒 = 0) で書き出す。 地図は大技の区間 (チャージ・大技) の頭で作られる。
        /// 例: <c>UI.Game2D.Battle.QaBattle.DumpGuiWeight("C:/tmp/w.png")</c>。 地図がまだ無ければ false。</summary>
        public static bool DumpGuiWeight(string path) => Game2DGuiRt.QaDumpWeight(path);

        /// <summary>いま読み込んである絵の一覧: 名前・持っている量の見積もり (バイト)・帯にしなかった時の量・読み込みに掛かった時間 (ミリ秒)。 大きい順・最後に合計。
        /// 例: <c>UI.Game2D.Battle.QaBattle.FxMemory()</c></summary>
        public static string FxMemory()
        {
            var l = new System.Collections.Generic.List<FxSheet>(FxSheet.Loaded);
            l.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            var sb = new System.Text.StringBuilder(); long sum = 0, plain = 0; float ms = 0f;
            foreach (var s in l) { sb.Append($"{s.Name}\t{s.Bytes}\t(帯なし {s.BytesPlain})\t{s.LoadMs:0.0} ms\n"); sum += s.Bytes; plain += s.BytesPlain; ms += s.LoadMs; }
            sb.Append($"合計 {l.Count} 枚\t{sum} B ({sum / 1048576f:0.0} MB)\t(帯なし {plain / 1048576f:0.0} MB)\t読み込み {ms:0} ms");
            return sb.ToString();
        }

        /// <summary>直近の大技の間の、 1 フレームの最大時間 (ミリ秒) と、 戦闘演出の部品の処理の最大時間。 例: "frame=33.4 fx=6.1"。</summary>
        public static string HeavyFrameMs() => $"frame={Game2DBattleFx.QaHeavyMaxFrameMs:0.0} fx={Game2DBattleFx.QaHeavyMaxFxMs:0.0}";

        /// <summary>今の状態を 1 行で (フェーズ・敵・ターン・予告・演出が空いているか・Go の進み)。</summary>
        public static string Status()
        {
            var gm = GameManager.Instance; var s = Sim; var f = Fx; var cm = CombatManager.Instance;
            return $"phase={gm?.CurrentPhase} enemy={cm?.CurrentEnemy?.id} turn={cm?.CurrentCombatTurn} sim={s?.phase} heavyIn={s?.heavyIn} charging={s?.charging} down={s?.down} " +
                   $"hp={s?.playerHp}/{s?.playerMax} fxIdle={f?.IsIdle} bossIdling={f?.BossIdling} go={Runner.State} loading={Game2DLoadingScreen.Blocking} saveSuppressed={SaveGuard.Suppressed} " +
                   $"gui=[on={Game2DGuiRt.FxOn} weight={Game2DGuiRt.FxWeightReady} dark={Game2DGuiRt.FxDark:0.00} red={Game2DGuiRt.FxRed:0.00} white={Game2DGuiRt.FxWhite:0.00} shake={Game2DGuiRt.FxShake}]";
        }

        // ── Go を次のフレーム以降で押し切る係 ──
        private sealed class Runner : MonoBehaviour
        {
            private static Runner _inst;
            public static string State = "idle";
            private bool _pending, _crit, _endPhase; private int _frames, _presses;

            private static Runner Get()
            {
                if (_inst == null) { var go = new GameObject("QaBattleRunner") { hideFlags = HideFlags.HideAndDontSave }; _inst = go.AddComponent<Runner>(); }
                return _inst;
            }
            public static void Cancel() { if (_inst != null) _inst._pending = false; if (State == "pending") State = "canceled"; }

            public static bool Request(bool crit)
            {
                var s = Sim; if (s == null || !Application.isPlaying) { State = "no-sim"; return false; }
                var r = Get(); r._pending = true; r._crit = crit; r._frames = 0; r._presses = 0;
                r._endPhase = s.phase == Game2DBattleSim.P.Won || s.phase == Game2DBattleSim.P.Lost;
                State = "pending";
                return true;
            }

            private void Update()
            {
                if (!_pending) return;
                var s = Sim; var f = Fx;
                if (s == null || f == null) { _pending = false; State = "no-sim"; return; }
                if (++_frames > 1200) { _pending = false; State = $"timeout (sim={s.phase} fxIdle={f.IsIdle})"; return; }
                if (Game2DLoadingScreen.Blocking) Game2DLoadingScreen.QaDismiss();
                if (_endPhase)
                {
                    if (s.phase == Game2DBattleSim.P.Won || s.phase == Game2DBattleSim.P.Lost) { s.Button(); _pending = false; State = "done (次へ)"; }
                    else { _pending = false; State = "done"; }
                    return;
                }
                if (_presses > 0 && s.phase != Game2DBattleSim.P.Wire) { _pending = false; State = $"done ({_presses} 回押した)"; return; }
                if (s.phase != Game2DBattleSim.P.Wire || !f.IsIdle) return;      // 判断待ちになるまで待つ
                s.AutoWire(false);
                if (_crit) { var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context; if (ctx != null) ctx.forceCritical = true; }
                s.Button(); _presses++;
                if (s.phase != Game2DBattleSim.P.Wire) { _pending = false; State = $"done ({_presses} 回押した)"; }
            }
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
