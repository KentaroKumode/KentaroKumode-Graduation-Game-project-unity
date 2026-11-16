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
        /// <summary>戦闘の GUI の格納のばねの値 (0.00 = 出ている / 1.00 = 画面の外)。</summary>
        private static string StowNow() { var h = Game2DBattleLcd.Instance; return h != null ? h.StowValue.ToString("0.00") : "-"; }

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
            FxLayerPreload.LoadForEnemyNow(enemyId, run.equippedWeaponId);      // 層を経ないので、 その敵と武器の絵をここで止まって読む (戦闘中に読まないように)
            bool ok = gm.QaStartCombat(enemyId);
            Sim?.QaNewCombat();                          // 戦闘の画面を入り直させる (HP・武器・敵を読み直す。 戦闘開始の知らせの購読に頼らない)
            Game2DLoadingScreen.QaDismiss();      // 新しいランの「第 N 層」のロード画面を出さない (戦闘の画面を覆い、 閉じるまで Go が効かない)
            Debug.Log($"[QaBattle] 戦闘 {enemyId} / 武器 {run.equippedWeaponId ?? "なし"} / HP {run.playerHP}/{run.playerMaxHP} / セーブ停止 {SaveGuard.Suppressed} → {(ok ? "開始" : "失敗")}");
            return ok;
        }

        /// <summary>ランの見せる層の番号 (<c>RunState.currentFloor</c>・0 = 序層 / 1〜5 = 第一〜第五層) を替える (確認専用・セーブを止める)。 戦闘の背景はこの番号で選ぶ
        /// (<see cref="BattleBackdrops"/>)。 <see cref="Start"/> の新しいランは 0 層なので、 層の背景を見る時は Start の後にこれを呼んでから <see cref="Restart"/>。
        /// 例: <c>UI.Game2D.Battle.QaBattle.SetFloor(3); UI.Game2D.Battle.QaBattle.Restart("boss_layer5")</c>。 ランが無ければ false。</summary>
        public static bool SetFloor(int floor)
        {
            SaveGuard.Suppressed = true;
            var run = GameManager.Instance?.Run; if (run == null) { Debug.LogWarning("[QaBattle] ランが無い (先に Start)"); return false; }
            run.currentFloor = Mathf.Clamp(floor, MapSystem.FreeMove.FloorPlan.FarmFloor, MapSystem.FreeMove.FloorPlan.LastOrdinal);
            Debug.Log($"[QaBattle] 層 {run.currentFloor} (深さ {run.Depth}) → 背景 {BattleBackdrops.ForFloor(run.currentFloor)}");
            return true;
        }

        /// <summary>今の戦闘の背景 (Resources/Game2DBackdrops の名前・"-" = 仮の背景) と、 ヴェスカ 4 段への移行で替える予定の背景。 例: "bg_vesca (次 bg_vesca_p4)"。</summary>
        public static string Backdrop() { var f = Fx; if (f == null) return "(戦闘の画面が無い)"; return (f.BackdropName ?? "-") + (f.PendingBackdropName != null ? " (次 " + f.PendingBackdropName + ")" : ""); }

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
        /// <summary>ヴェスカ (3・4 段目) が次のチャージのターンに選ぶ天与を決める: "sword" / "shield" / "ring" (1 回限り)。 判断待ちの間に呼ぶ。</summary>
        public static bool ForceTenyo(string gift)
        {
            var g = gift == "sword" ? CombatSystem.TenyoHeavy.Gift.Sword : gift == "shield" ? CombatSystem.TenyoHeavy.Gift.Shield : gift == "ring" ? CombatSystem.TenyoHeavy.Gift.Ring : CombatSystem.TenyoHeavy.Gift.None;
            if (g == CombatSystem.TenyoHeavy.Gift.None) return false;
            CombatSystem.TenyoHeavy.QaForce = g; return true;
        }

        /// <summary>敵の HP を変える (1 以上・最大まで・確認専用)。 ヴェスカなら HP 1 にして <see cref="Go"/> すると、 次の段へ移り、 形態移行の演出が流れる。</summary>
        public static void SetEnemyHp(int hp) => CombatManager.Instance?.QaSetEnemyHp(hp);

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

        /// <summary>いま読み込んである絵の一覧: 名前・持っている量の見積もり (バイト)・帯にしなかった時の量・読み込みに掛かった時間 (ミリ秒・頼んでから使えるまで) と、 そのうち主スレッドで使った時間。 大きい順・最後に合計。
        /// 例: <c>UI.Game2D.Battle.QaBattle.FxMemory()</c></summary>
        public static string FxMemory()
        {
            var l = new System.Collections.Generic.List<FxSheet>(FxSheet.Loaded);
            l.Sort((a, b) => b.Bytes.CompareTo(a.Bytes));
            var sb = new System.Text.StringBuilder(); long sum = 0, plain = 0; float ms = 0f, main = 0f;
            foreach (var s in l) { sb.Append($"{s.Name}\t{s.Bytes}\t(帯なし {s.BytesPlain})\t{s.LoadMs:0.0} ms (主スレッド {s.MainMs:0.0} ms)\n"); sum += s.Bytes; plain += s.BytesPlain; ms += s.LoadMs; main += s.MainMs; }
            sb.Append($"合計 {l.Count} 枚\t{sum} B ({sum / 1048576f:0.0} MB)\t(帯なし {plain / 1048576f:0.0} MB)\t読み込み {ms:0} ms (頼んでから使えるまでの和・並行に進む)\t主スレッド {main:0} ms");
            return sb.ToString();
        }

        /// <summary>直近の大技の間の、 1 フレームの最大時間 (ミリ秒) と、 戦闘演出の部品の処理の最大時間。 例: "frame=33.4 fx=6.1"。</summary>
        public static string HeavyFrameMs() => $"frame={Game2DBattleFx.QaHeavyMaxFrameMs:0.0} fx={Game2DBattleFx.QaHeavyMaxFxMs:0.0}";

        /// <summary>戦闘の入り (切り替わりと敵の出現) が終わってから今までの、 1 フレームの最大時間 (ミリ秒) と、 その時に使う瞬間に読んだ絵 (sheet=- なら絵の読み込みではない) と、
        /// 入りの終わりからの秒。 チャージのターンの入りの引っかかりもここに出る。 次の戦闘の入りの終わりで 0 に戻る。 例: "frame=17.2 sheet=- at=12.4s"。</summary>
        public static string BattleMaxFrame() => $"frame={Game2DBattleFx.QaBattleMaxFrameMs:0.0} sheet={Game2DBattleFx.QaBattleMaxSheet ?? "-"} at={Game2DBattleFx.QaBattleMaxAt:0.0}s";

        /// <summary>直近の層の頭の読み込み: 層・枚数・頼んでから全部読めるまでの時間 (ミリ秒・読みかけなら読みかけの数)。 例: "floor 7 (depth 7) 64 枚 1830 ms"。</summary>
        public static string LayerLoad() => FxLayerPreload.Busy ? $"{FxLayerPreload.QaLayerName} {FxLayerPreload.QaLayerCount} 枚 読みかけ {FxSheet.PendingCount}" : $"{FxLayerPreload.QaLayerName} {FxLayerPreload.QaLayerCount} 枚 {FxLayerPreload.QaLayerMs:0} ms";

        /// <summary>GUI の格納を手で決める (2026-11-15): 1 = 格納したまま / 0 = 出したまま / −1 = 自動 (ボスの大技の発動とヴェスカの形態移行の間だけ・既定)。
        /// <c>Start</c> / <c>Restart</c> で自動に戻る。 例: <c>UI.Game2D.Battle.QaBattle.ForceStow(1)</c></summary>
        public static void ForceStow(int mode) => Game2DBattleFx.QaStow = mode < 0 ? -1 : mode > 0 ? 1 : 0;

        /// <summary>エンディングの画面を直接出す (#328・<see cref="Game2DEndingScreen.QaShow"/>・セーブを止める・フェーズは変えない)。
        /// id = end1 / end2 / end3 / end_true / end_good。 例: <c>UI.Game2D.Battle.QaBattle.ShowEnding("end_true")</c></summary>
        public static bool ShowEnding(string id) => Game2DEndingScreen.QaShow(id);

        /// <summary>本物と同じ経路でエンディングを出す (#328): セーブを止め、 ランが無ければ始め、 最後に倒したボスと裂け目の選択を id に合わせて入れて踏破 (RunClear) のフェーズへ入る。
        /// 戦闘中なら打ち切ってから。 id = end1 / end2 / end3 / end_true / end_good。 閉じると「踏破」の窓 → タイトルへ (ランの記録・実績・解放は通さない)。
        /// 例: <c>UI.Game2D.Battle.QaBattle.ShowEndingInRunClear("end_true")</c>。 無い id なら false。</summary>
        public static bool ShowEndingInRunClear(string id)
        {
            SaveGuard.Suppressed = true;
            var gm = GameManager.Instance; if (gm == null || !Application.isPlaying) return false;
            string boss; bool open = false;
            switch (id)
            {
                case "end1": boss = BossIds.Layer5Judgment; break;
                case "end2": boss = BossIds.Layer5Hidden; break;
                case "end3": boss = BossIds.Layer6Ash; break;
                case Endings.EndTrueId: boss = BossIds.VescaP1; break;
                case Endings.EndGoodId: boss = BossIds.VescaP1; open = true; break;
                default: return false;
            }
            MarkAllTutorialsSeen();
            if (gm.Run == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver) gm.StartNewRun();
            CombatManager.Instance?.QaAbortCombat();
            Game2DLoadingScreen.QaDismiss();
            gm.QaEnterRunClear(boss, open);
            return gm.CurrentPhase == GameManager.GamePhase.RunClear;
        }

        /// <summary>今の状態を 1 行で (フェーズ・敵・ターン・予告・演出が空いているか・Go の進み)。</summary>
        public static string Status()
        {
            var gm = GameManager.Instance; var s = Sim; var f = Fx; var cm = CombatManager.Instance;
            return $"phase={gm?.CurrentPhase} floor={gm?.Run?.currentFloor} bg={Backdrop()} enemy={cm?.CurrentEnemy?.id} turn={cm?.CurrentCombatTurn} sim={s?.phase} heavyIn={s?.heavyIn} charging={s?.charging} down={s?.down} " +
                   $"hp={s?.playerHp}/{s?.playerMax} vescaShield={ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.vescaShield} fxIdle={f?.IsIdle} bossIdling={f?.BossIdling} go={Runner.State} loading={Game2DLoadingScreen.Blocking} ending={Game2DEndingScreen.Blocking} modal={Game2DModal.Active} saveSuppressed={SaveGuard.Suppressed} " +
                   $"stow=[{(f != null && f.GuiStowed ? "格納" : "出")} hud={StowNow()} force={Game2DBattleFx.QaStow}] gui=[on={Game2DGuiRt.FxOn} weight={Game2DGuiRt.FxWeightReady} dark={Game2DGuiRt.FxDark:0.00} red={Game2DGuiRt.FxRed:0.00} white={Game2DGuiRt.FxWhite:0.00} shake={Game2DGuiRt.FxShake}]";
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
        private static int _demoTenyo;

        /// <summary>試験のボスの天与 (ヴェスカで流す時): "sword" / "shield" / "ring" / "none" (時計の絵)。</summary>
        public static void DemoTenyo(string gift) => _demoTenyo = gift == "sword" ? 1 : gift == "shield" ? 2 : gift == "ring" ? 3 : 0;
        /// <summary>形態移行の演出だけを流す (final = 3→4)。</summary>
        public static void DemoTransition(bool final) => Fx?.VescaTransition(final);

        /// <summary>試験のボスを選ぶ (キー K): "King" / "Judge" / "Chevalier" / "Vesca" / "VescaFinal"。</summary>
        public static void DemoBoss(string kind) { if (System.Enum.TryParse(kind, out BossFx.Kind k)) _demo = k; }
        /// <summary>チャージ → 待機 (キー J の 1 回目)。</summary>
        public static void DemoCharge(int ash = 3) => Fx?.BossCharge(_demo, ash, 0, _demoTenyo);
        /// <summary>大技 (キー J の 2 回目・待機が出ていれば、 入ってよいコマまで回してから)。</summary>
        public static void DemoHeavy(int ash = 3, int taken = 12) => Fx?.BossHeavy(_demo, ash, taken, _demoTenyo);
        /// <summary>待機を止める (ダウンで取り消した時と同じ)。</summary>
        public static void DemoCancel() => Fx?.BossCancel();
        /// <summary>ボスの通常の技の絵を 1 回 (名前は Resources/Game2DFx の move-*: "move-king-attack" など)。</summary>
        public static void DemoMove(string name) => Fx?.PlayMove(name);
        /// <summary>任意の絵 (Resources/Game2DFx の名前) を 1 回流す (戦闘の流れを通さない)。 画面全体の絵は左上に合わせる。 小さい絵は基準点を、
        /// ヴェスカの時計 (vesca-*) なら玉の中心 (240,130)・それ以外は敵の足もと (240,196) に置く (見本の舞台と同じ)。 ループの絵は loopSeconds 秒。</summary>
        public static void DemoSheet(string name, float loopSeconds = 4f)
        {
            bool vesca = name != null && name.StartsWith("vesca-");
            Fx?.QaPlaySheet(name, 240, vesca ? 130 : 196, loopSeconds);
        }

        /// <summary>演出の速さ (1 = 普段・0.5・0.25・0 = 一時停止)。 戦闘の演出の時計だけ (Time.timeScale は触らない)。</summary>
        public static void SetSpeed(float speed) => Game2DBattleFx.QaSpeed = Mathf.Max(0f, speed);
        /// <summary>1 コマ (1/8 秒) だけ進める (一時停止の間に使う)。</summary>
        public static void StepFrame() => Game2DBattleFx.QaStepSeconds += 0.125f;

        // ── イベントの画面 (2026-11-16・EventScreenView) ──

        /// <summary>名前を指定したイベントの画面を出す (セーブを止める・ランが無ければ新しいランを始める・戦闘中なら打ち切る)。
        /// floor = 見せる層の番号 (背景の絵が替わる・-1 = 今のまま)・coins = 所持金 (-1 = 今のまま・0 で「ゴールドが足りない」の箱を見る)・
        /// eta = 補足されていて N ターンで捕まる見込みにする (-1 = 本物の見込み・1 でターンの札の警告と箱の下の説明を見る)。
        /// 例: <c>UI.Game2D.Battle.QaBattle.ShowEvent("老婆の商店", 2, 0, 1)</c>・<c>UI.Game2D.Battle.QaBattle.ShowEvent("さびれた観測所・再訪・写し", 4)</c></summary>
        public static bool ShowEvent(string eventName, int floor = -1, int coins = -1, int eta = -1)
        {
            SaveGuard.Suppressed = true;
            if (!Application.isPlaying) { Debug.LogWarning("[QaBattle] Play モードで呼ぶ"); return false; }
            var gm = GameManager.Instance;
            if (gm == null) { Debug.LogWarning("[QaBattle] GameManager が無い"); return false; }
            MarkAllTutorialsSeen();
            if (gm.Run == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver)
                gm.StartNewRun();
            if (gm.CurrentPhase == GameManager.GamePhase.Combat) CombatManager.Instance?.QaAbortCombat();
            var run = gm.Run;
            if (floor >= 0) run.currentFloor = Mathf.Clamp(floor, MapSystem.FreeMove.FloorPlan.FarmFloor, MapSystem.FreeMove.FloorPlan.LastOrdinal);
            if (coins >= 0) run.coins = coins;
            EventSystem.EventEncounter.QaCatchEta = eta;
            bool ok = gm.QaStartEvent(eventName);
            Game2DLoadingScreen.QaDismiss();
            UI.Game2D.Game2DPhaseScreens.RequestRebuild();      // 同じフェーズ・同じイベントのまま層や所持金を替えて出し直した時も、 最初の頁から描き直す
            Debug.Log($"[QaBattle] イベント {eventName} / 層 {run.currentFloor} (背景 {UI.Game2D.EventScreenView.BackdropName(EventSystem.EventEncounter.Instance?.Current)}) / 所持金 {run.coins} / 見込み {eta} → {(ok ? "開始" : "失敗")}");
            return ok;
        }

        /// <summary>イベントの画面の板と箱の濃さを試しに替える (0〜255・-1 で定数 EventScreenView.PlateAlpha に戻す)。 液晶のブルーム・自動露出の上での見え方を見る用。
        /// 決めた値は EventScreenView.PlateAlpha に書く。 例: <c>UI.Game2D.Battle.QaBattle.EventPlateAlpha(245)</c></summary>
        public static void EventPlateAlpha(int a)
        {
            UI.Game2D.EventScreenView.QaPlateAlpha = a;
            UI.Game2D.Game2DPhaseScreens.RequestRebuild();
        }

        // ── 液晶の湾曲 (2026-11-16・見比べ用。 既定は外形を湾曲に合わせる f = 1) ──

        /// <summary>液晶の板の材質 (Game2D/LcdScreen・設定が作った複製)。 無ければ null。</summary>
        private static Material LcdMat()
        {
            var g = Object.FindObjectOfType<Game2DLcdGhost>();
            var m = g != null && g.surface != null ? g.surface.sharedMaterial : null;
            return m != null && m.HasProperty("_CurveFit") ? m : null;
        }

        /// <summary>マウスの当たりが読む材質 (Game2DScreen.lcdQuad の材質) が液晶の板の材質と別なら、 同じ値を写す (見た目と当たりがずれないように)。</summary>
        private static void SyncPointerMat(Material m)
        {
            var sc = Object.FindObjectOfType<Game2DScreen>();
            var mr = sc != null && sc.lcdQuad != null ? sc.lcdQuad.GetComponent<MeshRenderer>() : null;
            var pm = mr != null ? mr.sharedMaterial : null;
            if (pm == null || pm == m || !pm.HasProperty("_CurveFit")) return;
            pm.SetFloat("_Curve", m.GetFloat("_Curve")); pm.SetFloat("_CurveFit", m.GetFloat("_CurveFit"));
            Debug.LogWarning("[QaBattle] マウスの当たりが読む材質と液晶の板の材質が別だった (値を写した)");
        }

        /// <summary>湾曲の外形の合わせ方 <c>_CurveFit</c> を実行中に替える (1 = 縁の中央を合わせる・絵の外形が樽型・既定 / 0 = 旧い合わせ方: 四隅を絵の端に合わせる・間は線形)。 設定を変えると既定 (Game2DSettingsApply.CurveFit) へ戻る。
        /// マウスの当たり (Game2DPointer.LcdDot) も同じ材質から読むので一緒に変わる。 保存はしない (Play を抜けると戻る)。 戻り値 = 今の値の説明。
        /// 例: <c>UI.Game2D.Battle.QaBattle.LcdCurveFit(1)</c></summary>
        public static string LcdCurveFit(float fit)
        {
            var m = LcdMat(); if (m == null) return "(液晶の材質が無い)";
            m.SetFloat("_CurveFit", Mathf.Clamp01(fit)); SyncPointerMat(m);
            return LcdCurveNow();
        }

        /// <summary>湾曲の強さ <c>_Curve</c> を実行中に替える (0〜0.2・材質の既定 0.07 × 設定の倍率)。 設定を変えると設定の値で上書きされる。 保存はしない。
        /// 例: <c>UI.Game2D.Battle.QaBattle.LcdCurve(0.1f)</c></summary>
        public static string LcdCurve(float k)
        {
            var m = LcdMat(); if (m == null) return "(液晶の材質が無い)";
            m.SetFloat("_Curve", Mathf.Clamp(k, 0f, 0.2f)); SyncPointerMat(m);
            return LcdCurveNow();
        }

        /// <summary>GUI (計器盤・帯・窓・手引き) の合成の形を替える (2026-11-16・見比べ用・保存しない)。 true = 液晶の中身として合成 (既定・湾曲と液晶の効果が全部掛かる) /
        /// false = 旧い提示の板 (湾曲しない層で液晶の上に重ね、 影と上下の色調を付ける)。 押す所の変換 (Game2DPointer.TryDot) も一緒に切り替わる。
        /// 例: <c>UI.Game2D.Battle.QaBattle.GuiInLcd(false)</c></summary>
        public static string GuiInLcd(bool on)
        {
            Game2DGuiRt.InLcd = on;
            return "guiInLcd=" + Game2DGuiRt.InLcd;
        }

        /// <summary>走査線の試しの形 (2026-11-16・作者「走査線で文章が読みづらい」の改善案の見比べ用・保存しない)。 液晶のシェーダのグローバル <c>_Game2DScanMode</c>。
        /// <b>ビットの組 (0〜7・2026-11-17・重ねて見比べる)</b>: 1 = 縞を dot の行の半分の細かさに (どの dot の行も同じ明るさ) / 2 = 明るい所 (字) ほど走査線を弱める /
        /// 4 = GUI の覆っている所だけ弱める。 2 と 4 を重ねると弱め方は掛け合わせ。 0 = 旧い縞 (形なし)。
        /// 既定は 3 = 1+2 (2026-11-17・作者の決定・本体の <see cref="Game2DSettingsApply.ScanMode"/> が起動時と設定の変更時に入れる)。 ここで替えた値は設定を変えると既定へ戻る。
        /// 例: 3 = 1+2・6 = 2+4・7 = 1+2+4。 走査線の濃さそのものは設定の「走査線」(0 で切)。 例: <c>UI.Game2D.Battle.QaBattle.ScanMode(3)</c></summary>
        public static string ScanMode(int mode)
        {
            mode = Mathf.Clamp(mode, 0, 7);
            Shader.SetGlobalFloat("_Game2DScanMode", mode);
            return "scanMode=" + mode + " (" + (mode == 0 ? "今のまま" : ((mode & 1) != 0 ? "1 半ドットの縞 " : "") + ((mode & 2) != 0 ? "2 明るい所を弱める " : "") + ((mode & 4) != 0 ? "4 GUI の所を弱める" : "")).TrimEnd() + ")";
        }

        /// <summary>今の湾曲 (「curve=0.070 fit=0.00」)。</summary>
        public static string LcdCurveNow() { var m = LcdMat(); return m == null ? "(液晶の材質が無い)" : $"curve={m.GetFloat("_Curve"):0.000} fit={m.GetFloat("_CurveFit"):0.00}"; }

        /// <summary>イベントの画面の背景を、 どのイベントでも名指しの絵にする (Resources/Game2DBackdrops の名前・"ev_wave" など・null か "" で対応表どおりに戻す)。
        /// 絵ごとに板と列に隠れる具合を撮る用。 先に <see cref="ShowEvent"/> で画面を出しておく。 戻り値 = 今出している絵の名前 (読めない名前なら層の絵になり、 警告が出る)。
        /// 例: <c>UI.Game2D.Battle.QaBattle.EventBackdrop("ev_wave")</c></summary>
        public static string EventBackdrop(string name)
        {
            UI.Game2D.EventScreenView.QaBackdrop = string.IsNullOrEmpty(name) ? null : name;
            UI.Game2D.Game2DPhaseScreens.RequestRebuild();
            var ev = EventSystem.EventEncounter.Instance != null ? EventSystem.EventEncounter.Instance.Current : null;
            string bg = UI.Game2D.EventScreenView.BackdropName(ev);
            if (BattleBackdrops.Load(bg) == null) Debug.LogWarning($"[QaBattle] 背景 {bg} を読めない");
            return bg;
        }

        /// <summary>イベントの名前の一覧 (定義ファイルの順・「、」区切り)。 例: <c>UI.Game2D.Battle.QaBattle.EventNames()</c></summary>
        public static string EventNames()
        {
            var sb = new System.Text.StringBuilder();
            foreach (var e in EventSystem.EventDatabase.All) sb.Append(sb.Length > 0 ? "、" : "").Append(e.name);
            return sb.ToString();
        }

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
