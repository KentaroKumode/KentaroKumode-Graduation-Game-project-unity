using System;
using System.Collections.Generic;
using UnityEngine;
using InventorySystem;
using InventorySystem.PassiveSkills;
using CombatSystem.DiceLED;

namespace CombatSystem
{
    /// <summary>[計測] ボスごとの **プレイヤーの実攻撃ダメージ**。
    /// HP減少総量 (damageDealt) は自壊・DOT・固定ダメを全部含むため、
    /// 「1 発いくら出ているか」を推し量る根拠にはならない。 ここは主攻撃と固定ダメを
    /// 分けて実測する。 バッチ後に Dump() で読み出す
    /// (AutoRunner がバッチ中 Debug.unityLogger.logEnabled=false にするためログは使えない)。</summary>
    public static class BossDmgDiag
    {
        private class Row { public long turns, mainSum, fixedSum, allTurns, fights; public int mainMax; }
        private static readonly System.Collections.Generic.Dictionary<string, Row> _rows
            = new System.Collections.Generic.Dictionary<string, Row>();
        private static readonly System.Collections.Generic.Dictionary<string, string> _names
            = new System.Collections.Generic.Dictionary<string, string>();

        public static void Reset() { _rows.Clear(); _names.Clear(); _spikes.Clear();
            p4Turns = p4Blocked = p4Execute = p4MaxHpBite = p4Fights = 0;
            p4EntryHpPct = 0; _relic.Clear();
            _relicSeen.Clear(); _relicFatal.Clear(); v7Turns = v7Deaths = 0;
            _phaseN.Clear(); _phaseHp.Clear(); _phaseHpMin.Clear(); }

        // ---- 7層 p4 専用の内訳 ----
        public static long p4Turns, p4Blocked, p4Execute, p4MaxHpBite, p4Fights;
        public static double p4EntryHpPct;
        private static readonly System.Collections.Generic.Dictionary<string,int> _relic
            = new System.Collections.Generic.Dictionary<string,int>();

        public static void P4Entry(double hpPct) { p4Fights++; p4EntryHpPct += hpPct; }

        // ---- 7層 各段の突入時 残HP ----
        private static readonly System.Collections.Generic.Dictionary<string,int> _phaseN
            = new System.Collections.Generic.Dictionary<string,int>();
        private static readonly System.Collections.Generic.Dictionary<string,double> _phaseHp
            = new System.Collections.Generic.Dictionary<string,double>();
        private static readonly System.Collections.Generic.Dictionary<string,double> _phaseHpMin
            = new System.Collections.Generic.Dictionary<string,double>();

        public static void PhaseEntry(string id, double hpPct)
        {
            if (string.IsNullOrEmpty(id)) return;
            _phaseN.TryGetValue(id, out var n); _phaseN[id] = n + 1;
            _phaseHp.TryGetValue(id, out var h); _phaseHp[id] = h + hpPct;
            if (!_phaseHpMin.TryGetValue(id, out var mn) || hpPct < mn) _phaseHpMin[id] = hpPct;
        }

        /// <summary>[計装] 段 index(0=p1..3=p4) の突入数と 残HP% 合計を取り出す。
        /// <c>DumpPhaseEntry</c> は文字列しか返さず、 しかもどこからも呼ばれていなかった。</summary>
        public static void ReadPhaseEntry(long[] n, double[] hpSum)
        {
            var ids = new[]{ "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4" };
            for (int i = 0; i < ids.Length && i < n.Length && i < hpSum.Length; i++)
            {
                _phaseN.TryGetValue(ids[i], out var c); n[i] = c;
                _phaseHp.TryGetValue(ids[i], out var h); hpSum[i] = h;
            }
        }
        public static void ResetPhaseEntry() { _phaseN.Clear(); _phaseHp.Clear(); _phaseHpMin.Clear(); }

        public static string DumpPhaseEntry()
        {
            if (_phaseN.Count == 0) return "PHASE: 記録なし";
            string nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{"段",-16}{"突入数",8}{"残HP平均",11}{"残HP最小",11}").Append(nl);
            sb.Append(new string('-', 46)).Append(nl);
            foreach (var id in new[]{ "boss_layer7", "boss_layer7_p2", "boss_layer7_p3", "boss_layer7_p4" })
            {
                if (!_phaseN.TryGetValue(id, out var n) || n == 0) continue;
                var e = CombatSystem.EnemyDatabase.Get(id);
                string nm = e != null ? e.displayName : id;
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                sb.Append($"{nm,-16}{n,8}{_phaseHp[id] / n,10:P1}{_phaseHpMin[id],11:P1}").Append(nl);
            }
            return sb.ToString();
        }

        // ---- 7層 全段: 遺物の「出現数」と「その遺物が出ていたターンでプレイヤーが死んだ数」 ----
        private static readonly System.Collections.Generic.Dictionary<string,int> _relicSeen
            = new System.Collections.Generic.Dictionary<string,int>();
        private static readonly System.Collections.Generic.Dictionary<string,int> _relicFatal
            = new System.Collections.Generic.Dictionary<string,int>();
        public static long v7Turns, v7Deaths;

        /// <summary>7層のターンごとに、 そのターン引かれていた遺物を数える。</summary>
        public static void V7Turn(System.Collections.Generic.List<string> relics)
        {
            v7Turns++;
            if (relics == null) return;
            foreach (var r in relics) { _relicSeen.TryGetValue(r, out var c); _relicSeen[r] = c + 1; }
        }

        /// <summary>そのターンにプレイヤーが死んだ場合、 出ていた遺物を致死側にも数える。</summary>
        public static void V7Death(System.Collections.Generic.List<string> relics)
        {
            v7Deaths++;
            if (relics == null) return;
            foreach (var r in relics) { _relicFatal.TryGetValue(r, out var c); _relicFatal[r] = c + 1; }
        }

        /// <summary>致死率 = その遺物が出ていたターンのうち、 プレイヤーが死んだ割合。
        /// 出現数で割るので「よく出るから多い」だけの札は上位に来ない。</summary>
        public static string DumpLethal()
        {
            if (v7Turns == 0) return "V7: 記録なし";
            string nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            double baseRate = v7Deaths / (double)v7Turns;
            sb.Append($"7層 総ターン {v7Turns} / プレイヤー死亡ターン {v7Deaths} (基準致死率 {baseRate:P2})").Append(nl);
            sb.Append($"{"遺物",-16}{"出現",7}{"致死",7}{"致死率",9}{"基準比",8}").Append(nl);
            sb.Append(new string('-', 48)).Append(nl);
            var keys = new System.Collections.Generic.List<string>(_relicSeen.Keys);
            keys.Sort((x, y) =>
            {
                _relicFatal.TryGetValue(x, out var fx); _relicFatal.TryGetValue(y, out var fy);
                double rx = fx / (double)_relicSeen[x], ry = fy / (double)_relicSeen[y];
                return ry.CompareTo(rx);
            });
            foreach (var k in keys)
            {
                _relicFatal.TryGetValue(k, out var f);
                int seen = _relicSeen[k];
                double rate = f / (double)seen;
                sb.Append($"{k,-16}{seen,7}{f,7}{rate,9:P2}{(baseRate > 0 ? rate / baseRate : 0),7:F2}x").Append(nl);
            }
            return sb.ToString();
        }
        public static void P4Turn(bool blocked, bool execArmed, int bite,
                                  System.Collections.Generic.List<string> relics)
        {
            p4Turns++;
            if (blocked) p4Blocked++;
            if (execArmed) p4Execute++;
            p4MaxHpBite += bite;
            if (relics != null)
                foreach (var r in relics)
                { _relic.TryGetValue(r, out var c); _relic[r] = c + 1; }
        }
        public static string DumpP4()
        {
            if (p4Turns == 0) return "P4: 記録なし";
            string nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append($"p4 戦闘数 {p4Fights} / 総ターン {p4Turns}").Append(nl);
            sb.Append($"  突入時の残HP    平均 {(p4Fights > 0 ? p4EntryHpPct / p4Fights : 0):P1}").Append(nl);
            sb.Append($"  出目潰しターン  {p4Blocked} ({p4Blocked * 100.0 / p4Turns:F1}%)  ← 天与の指輪").Append(nl);
            sb.Append($"  処刑 armed      {p4Execute} ({p4Execute * 100.0 / p4Turns:F1}%)").Append(nl);
            sb.Append($"  最大HP削り合計  {p4MaxHpBite} (1戦あたり {(p4Fights > 0 ? p4MaxHpBite / (double)p4Fights : 0):F1})").Append(nl);
            sb.Append("  遺物の出現数:").Append(nl);
            var keys = new System.Collections.Generic.List<string>(_relic.Keys);
            keys.Sort((x, y) => _relic[y].CompareTo(_relic[x]));
            foreach (var k in keys)
                sb.Append($"    {k,-16} {_relic[k],5} ({_relic[k] * 100.0 / p4Turns:F1}%)").Append(nl);
            return sb.ToString();
        }

        /// <summary>そのボス戦で経過した「全ターン」を数える (攻撃が発生しなかったターンも含む)。
        /// Record() は攻撃解決の中でしか呼ばれないため、 turns だけでは
        /// 「1 攻撃あたりの威力」と「1 ターンあたりの威力」を取り違える。 分母を分けて持つ。</summary>
        public static void RecordTurn(string bossId, string displayName)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            if (!_rows.TryGetValue(bossId, out var r)) { r = new Row(); _rows[bossId] = r; _names[bossId] = displayName; }
            r.allTurns++;
        }

        public static void Record(string bossId, string displayName, int mainDmg, int fixedDmg)
        {
            if (string.IsNullOrEmpty(bossId)) return;
            if (!_rows.TryGetValue(bossId, out var r)) { r = new Row(); _rows[bossId] = r; _names[bossId] = displayName; }
            r.turns++;
            r.mainSum  += mainDmg;
            r.fixedSum += fixedDmg;
            if (mainDmg > r.mainMax) r.mainMax = mainDmg;
        }

        // ---- 上振れヒットの実ログ (閾値超えを先着 N 件だけ全文保存) ----
        public const int SpikeThreshold = 400;
        private const int MaxSpikes = 12;
        private static readonly System.Collections.Generic.List<string> _spikes
            = new System.Collections.Generic.List<string>();

        public static bool WantSpike(int dmg) => dmg >= SpikeThreshold && _spikes.Count < MaxSpikes;
        public static void AddSpike(string line) { if (_spikes.Count < MaxSpikes) _spikes.Add(line); }
        public static string DumpSpikes()
        {
            if (_spikes.Count == 0) return $"SPIKE: {SpikeThreshold} 以上のヒットは記録なし";
            return string.Join(System.Environment.NewLine, _spikes);
        }

        public static string Dump()
        {
            if (_rows.Count == 0) return "BOSSDMG: 記録なし";
            string nl = System.Environment.NewLine;
            var sb = new System.Text.StringBuilder();
            sb.Append($"{"ボス",-16}{"全T",7}{"攻撃T",7}{"攻撃率",8}{"主/攻撃",9}{"主/全T",9}{"最大",7}").Append(nl);
            sb.Append(new string('-', 64)).Append(nl);
            var keys = new System.Collections.Generic.List<string>(_rows.Keys);
            keys.Sort();
            foreach (var k in keys)
            {
                var r = _rows[k];
                double t = System.Math.Max(1, r.turns);
                string nm = _names.TryGetValue(k, out var v) ? v : k;
                if (nm.Length > 14) nm = nm.Substring(0, 14);
                double at = System.Math.Max(1, r.allTurns);
                sb.Append($"{nm,-16}{r.allTurns,7}{r.turns,7}{r.turns / at,8:P0}"
                        + $"{(r.mainSum + r.fixedSum) / t,9:F1}{(r.mainSum + r.fixedSum) / at,9:F1}{r.mainMax,7}").Append(nl);
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// 1ターンの戦闘結果をまとめた構造体
    /// </summary>
    public struct TurnResult
    {
        public int turnNumber;

        // ダイスロール
        public int[] playerDice;
        public int[] enemyDice;
        public int playerDiceTotal;
        public int enemyDiceTotal;

        // 勝敗
        public bool playerWon;
        public bool isDraw;

        // ダメージ
        public int mainDamage;          // メインダメージ（ダイス差）
        public int pursuitDamage;       // 追撃ダメージ（パッシブ由来）
        public int totalDamage;         // 合算ダメージ（クリティカル適用後）
        public int fixedDamage;         // 固定ダメージ（パッシブ由来）
        public int scratchDamage;       // scratch削りダメージ（threat由来）
        public bool isCritical;

        // HP経過
        public int playerHPAfter;
        public int enemyHPAfter;
    }

    /// <summary>
    /// 戦闘全体の最終結果
    /// </summary>
    public struct CombatResult
    {
        public string enemyId;
        public string enemyDisplayName;
        public bool playerWon;
        public int totalTurns;
        public int playerHPRemaining;
        public int enemyHPRemaining;
        public List<TurnResult> turnLog;
        /// <summary>検証計測: この戦闘でプレイヤーが獲得した累計回復量／シールド量。</summary>
        public int healApplied;
        public int shieldGained;
        /// <summary>L1学習: プレイヤーが敵に与えた総ダメージ (enemyMaxHP - 残HP の単純差分)。</summary>
        public int damageDealt;
        /// <summary>L1学習: プレイヤーが受けた純粋な総ダメージ (healApplied 補正済み)。</summary>
        public int damageTaken;
        /// <summary>L1学習: 敵 maxHP（撃破率計算用）。</summary>
        public int enemyMaxHP;
        /// <summary>ボス難易度オートチューナー: プレイヤー敗北時の致死メカニズム分類 (勝利時は Normal)。</summary>
        public InventorySystem.PassiveSkills.DeathCause deathCause;
        /// <summary>ボス難易度オートチューナー: この戦闘のプレイヤーロール合計と回数 (平均出目算出用)。</summary>
        public long playerRollSum;
        public int playerRollCount;
        /// <summary>ボス難易度オートチューナー: この戦闘の総被ダメの **ソース別内訳** (キル時でなく支配率診断用)。</summary>
        public Dictionary<InventorySystem.PassiveSkills.DeathCause, int> playerDamageBySource;
        /// <summary>ボス難易度オートチューナー: スタンス別の「ボスがロール勝ちしたターン数/総ターン数」(強/弱別レンジ制御用)。</summary>
        public int strongRollTurns, strongRollBossWins, weakRollTurns, weakRollBossWins;
    }

    /// <summary>
    /// 戦闘システム管理クラス
    /// 
    /// 戦闘ルール:
    /// (1) 双方のダイスをすべて振り、合計値でマッチ
    /// (2) 合計値の大きい方が勝利
    /// (3) 勝利者は [勝者合計 - 敗者合計] のメインダメージを与える
    /// (4) 追撃/反撃はパッシブスキル（PursuitI-III / CounterI-III）由来の固定値
    /// (5) scratchは特定の敵パッシブ（ScratchAura）が付与する削りダメージ
    /// (6) すべてのダメージを合算後、クリティカル判定（1回、会心率% を加算して 0〜1 へクランプ）
    ///     クリティカル時は合算ダメージに倍率適用
    /// 
    /// 使い方:
    /// <code>
    /// CombatManager.Instance.OnCombatEnd += result => { ... };
    /// CombatManager.Instance.StartCombat("goblin", playerMaxHP, playerWeaponDice);
    /// </code>
    /// </summary>
    public class CombatManager : MonoBehaviour
    {
        // ===== シングルトン =====
        private static CombatManager instance;
        private static bool isApplicationQuitting;
        public static CombatManager Instance
        {
            get
            {
                if (isApplicationQuitting)
                    return null;

                if (instance == null)
                {
                    // シーン内の既存オブジェクトを検索
                    instance = FindObjectOfType<CombatManager>();
                    
                    // 見つからない場合のみ新規作成
                    if (instance == null)
                    {
                        var go = new GameObject("[CombatManager]");
                        instance = go.AddComponent<CombatManager>();
                        DontDestroyOnLoad(go);
                    }
                }
                return instance;
            }
        }

        // ===== イベント =====
        /// <summary>戦闘開始イベント（enemyId）</summary>
        public event Action<string> OnCombatStart;
        /// <summary>ターン結果イベント</summary>
        public event Action<TurnResult> OnTurnEnd;
        /// <summary>戦闘終了イベント</summary>
        public event Action<CombatResult> OnCombatEnd;

        // ===== 戦闘状態 =====
        private EnemyData currentEnemy;
        /// <summary>戦闘開始時の HP 率 (0-100)。 行動台帳の blowout 判定に要る
        /// ── 「HP&gt;80% から 1 戦で沈んだ」は終了時の HP では判別できない。</summary>
        private int _hpPctAtCombatStart = 100;
        /// <summary>戦闘開始時の HP 実数。 **この戦闘で受けた被ダメ**を出すのに要る。
        /// <c>CombatResult.damageTaken</c> は最大HP との差なので、 削れた HP を持ち越して
        /// 始まった戦闘では前の戦闘の分が混ざる ── 台帳の激しさ判定には使えない。</summary>
        private int _hpAtCombatStart;
        private int playerHP;
        private int playerMaxHP;
        private int enemyHP;
        private bool metaLethalSurviveUsed; // メタデバフ Lv9: 敵の初回致命傷を1HPで耐える
        private bool metaAgilityDodgeUsed;  // メタデバフ Lv2 俊敏: 敵が初回被弾を回避済みか
        /// <summary>挑戦デバフ〈天変地異〉: この戦闘でボスが放った「強化対象の撃」の回数。
        /// **最初の最大 3 撃で終了する** ── 深層で恒常倍率にしないため (§15-2 v3.0)。</summary>
        private int challengeBossStrikeCount;
        private int fleeAfterTurns;          // >0 のとき、このターン数を終えても未決着なら敵が逃走（偽の商人）
        private long _fightPlayerRollSum;    // ボス難易度チューナー: この戦闘のプレイヤーロール合計の累計
        private int _fightPlayerRollCount;   // 同 ロール回数 (平均出目 = sum/count)
        // ボス難易度チューナー: スタンス別の「ボスがロール勝ちしたターン数 / そのスタンスの総ターン数」(強/弱で別レンジ管理)
        private int _fightStrongTurns, _fightStrongBossWins, _fightWeakTurns, _fightWeakBossWins;
        private bool wrathDiceOverrideArmed; // 恒久デバフ「憤怒」: 1T目のダイス最大化トリガー
        private int playerDiceCount;
        private int playerDiceMax;
        private int playerAttackPower = 2;            // #2 案A': 装備武器の素火力（勝利base = attackPower + floor(|差|/3)）
        private const int WeaponDiffPerBonus = 3;     // #2 案A': 差ボーナス＝floor(|差|/3)（差3ごとに+1）。会心には非干渉
        /// <summary>武器 + 層補正の会心率 (小数)。 パッシブ・メタ分は判定時に足す。</summary>
        private float playerCritBaseRate;
        private bool isCombatActive;

        // パリィ / オーバーロード (タイミング判定) は ADR-0010 で全廃した。
        //   反射神経を要求する技量帯を、 ダイス 5 個をどう分割して役を作るかという
        //   **判断の技量帯**へ移すのが ADR-0010 の主眼で、 両者を併存させると
        //   「上手さ」の定義が二重になる。 予見メタ軸もこれに連座して消えた (§24)。

        private List<TurnResult> turnLog = new List<TurnResult>();

        // ===== LED演出管理 =====
        private DiceLEDManager ledManager;

        /// <summary>
        /// 時限バフ（解放者）等の効果で、戦闘中の最大HPと現在HPを一時的に増やす。
        /// 戦闘終了で playerMaxHP は次戦闘で再設定されるため特別なリセット不要。
        /// </summary>
        public void GrantTemporaryHpBonus(int amount)
        {
            if (amount <= 0) return;
            playerMaxHP += amount;
            playerHP = Math.Min(playerMaxHP, playerHP + amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null)
            {
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
        }

        /// <summary>
        /// 時限バフ（使命感）等の効果で、その戦闘のプレイヤーダイス数を一時的に増やす。
        /// </summary>
        public void GrantTemporaryDiceCountBonus(int delta)
        {
            if (delta == 0) return;
            playerDiceCount = Math.Max(1, playerDiceCount + delta);
        }

        // [撤去 2026-09-13] TryTriggerOverload / OverloadFires ── 出力 r10 の旧極点。
        //   反動を HP でも 希望 でも成立させられず、 〈戦意〉(累積成長) へ差し替えた。
        //   経緯は MetaProgression.MetaPanel.BattleSpiritUnlocked。

        /// <summary>
        /// 戦闘終了時にプレイヤーHPを最大値まで回復（泉の祝福）。
        /// FinishCombat の冒頭で呼ばれた場合、CombatResult にも反映される。
        /// </summary>
        public void HealPlayerToFull()
        {
            playerHP = playerMaxHP;
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.playerCurrentHP = playerHP;
        }

        /// <summary>戦闘中にプレイヤーへ直接ダメージ（中毒等の時限デバフから呼ばれる）。</summary>
        public void DamagePlayerDirect(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            amount = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                amount, GameLoop.GameManager.Instance?.Run, playerHP);
            playerHP = Math.Max(0, playerHP - amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.playerCurrentHP = playerHP;
        }

        /// <summary>戦闘中、敵に軽減不可ダメージを直接与える（記憶の砂時計・各種パッシブ用）。</summary>
        public void DealFixedDamageToEnemy(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            enemyHP = Math.Max(0, enemyHP - amount);
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null) ctx.enemyCurrentHP = enemyHP;
        }

        /// <summary>前ターンの配線 (今ターンの起点)。 〈不器用〉の変更本数制限を
        /// 方策側が守るために要る。 配列は内部のものなので**書き換えないこと**。</summary>
        public DiceTerminal[] CurrentWiring => mutualWiring;

        public bool IsCombatActive => isCombatActive;

        /// <summary>診断: <c>StartCombatInternal</c> が敵の確定まで到達した最後の戦闘通番。
        /// <see cref="StartCombatCompleted"/> と食い違っていれば、 開始処理が途中で抜けている。</summary>
        public int StartCombatEntered { get; private set; } = -1;
        /// <summary>診断: <c>isCombatActive</c> を立てるところまで到達した最後の戦闘通番。</summary>
        public int StartCombatCompleted { get; private set; } = -1;
        public EnemyData CurrentEnemy => currentEnemy;
        public int PlayerHP => playerHP;
        /// <summary>装備武器の素火力 (ADR-0009 配線ポリシーの撃破判定用)。</summary>
        public int PlayerAttackPower => playerAttackPower;
        public int PlayerMaxHP => playerMaxHP;
        /// <summary>武器 + 層補正の会心率 (小数・0.15 = 15%)。
        /// 先読み方策 (<see cref="AutoTest.SuperCombatAI"/>) が会心の期待値を組むのに要る。</summary>
        public float PlayerCritBaseRate => playerCritBaseRate;
        public int EnemyHP => enemyHP;
        public int CurrentCombatTurn => PassiveSkillManager.Instance?.Context?.currentTurn ?? 0;
        public int EnemyMaxHP => currentEnemy != null ? currentEnemy.maxHP : 0;

        // ===========================================================
        //  外部アイテム効果用API
        // ===========================================================

        /// <summary>
        /// プレイヤーのHPを回復（戦闘外でも使用可能）
        /// </summary>
        /// <param name="amount">回復量</param>
        /// <returns>実際に回復した量</returns>
        public int HealPlayer(int amount)
        {
            if (amount <= 0) return 0;

            // 呪いの渇き: HP回復効果を半減 / 狂暴化: 回復完全封印
            var psmCtxForHeal = PassiveSkillManager.Instance?.Context;
            if (psmCtxForHeal != null && psmCtxForHeal.healBlocked)
            {
                Debug.Log("[CombatManager] 狂暴化: 回復が封じられている (回復0)");
                return 0;
            }
            if (psmCtxForHeal != null && psmCtxForHeal.healHalved)
                amount = Math.Max(1, amount / 2);
            // 挑戦デバフ〈遅い回復〉: **あらゆる回復に掛かる**ので、 戦闘中の回復
            //   (吸血・消耗品・イベント) もここで削る。 回復スポットだけを触っていた
            //   旧〈浅い眠り〉が 3 度測って一度も効かなかったのは、 対象が狭すぎたため。
            {
                float hm = MetaProgression.MetaDebuffApplicator.GetHealMultiplier();
                if (hm < 1f) amount = Math.Max(1, Mathf.RoundToInt(amount * hm));
                amount = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(
                    amount, GameLoop.GameManager.Instance?.Run);
            }
            // 〈不完全な修復〉(門・2026-09-14): 血を回さなかったので傷が塞がらない。
            //   **ここが回復の唯一の絞り込み点**なので、 消耗品・吸血・パッシブ・イベントの
            //   どの経路から来た回復にも掛かる。 シールドは加算点が 13 箇所に散っていて
            //   絞り込み点が無いため対象外 (全部に足すと必ずどれかが漏れる)。
            amount = GameLoop.GateFlaws.ApplyRepairPenalty(GameLoop.GameManager.Instance?.Run, amount);
            if (amount <= 0) return 0;

            // 〈天衣無縫〉: 獲得回復量をスタック分減衰（0未満は0）
            if (psmCtxForHeal != null && psmCtxForHeal.healShieldReduction > 0)
            {
                amount = Math.Max(0, amount - psmCtxForHeal.healShieldReduction);
                if (amount <= 0) return 0;
            }

            MetaProgression.Achievements.AchievementService.NoteHealRequested(amount);
            int oldHP = playerHP;
            int newHP = Math.Min(playerMaxHP, playerHP + amount);
            int actualHealed = newHP - oldHP;
            MetaProgression.MetaDebuffApplicator.NoteHeal(
                amount, actualHealed, GameLoop.GameManager.Instance?.Run);
            
            playerHP = newHP;
            
            // 戦闘中の場合、CombatContextも更新
            var psm = PassiveSkillManager.Instance;
            if (psm != null && psm.Context != null)
            {
                psm.Context.playerCurrentHP = playerHP;
                psm.Context.healAppliedTotal += actualHealed; // 検証計測
            }

            Debug.Log($"[CombatManager] Player healed: {actualHealed} HP ({oldHP} → {playerHP})");
            return actualHealed;
        }

        /// <summary>
        /// プレイヤーの最大HPを一時的に増加（戦闘中のみ）
        /// </summary>
        /// <param name="amount">増加量</param>
        public void BoostPlayerMaxHP(int amount)
        {
            if (amount <= 0 || !isCombatActive) return;
            
            int oldMaxHP = playerMaxHP;
            playerMaxHP += amount;
            playerHP += amount; // 増加分は即回復
            
            // CombatContextも更新
            var psm = PassiveSkillManager.Instance;
            if (psm != null && psm.Context != null)
            {
                var ctx = psm.Context;
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
            
            Debug.Log($"[CombatManager] Player MaxHP boosted: {oldMaxHP} → {playerMaxHP} (+{amount})");
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        // ===========================================================
        //  戦闘開始
        // ===========================================================

        /// <summary>
        /// モンスター名(ID)を指定して戦闘開始
        /// </summary>
        /// <param name="enemyId">enemies.json の id</param>
        /// <param name="playerMaxHP">プレイヤーの最大HP</param>
        /// <param name="playerDiceCount">プレイヤーのダイス数</param>
        /// <param name="playerDiceMax">プレイヤーのダイス最大出目</param>
        /// <param name="playerCritRate">武器と層補正による会心率 (小数・0.11 = 11%)</param>
        public void StartCombat(string enemyId, int playerMaxHP,
            int playerDiceCount, int playerDiceMax, float playerCritRate = 0f, int[] equippedDiceFaces = null,
            bool suppressTerminalRoles = false, GameLoop.DiceFaceParts.Tier[] equippedFaceTiers = null)
        {
            var enemy = EnemyDatabase.Get(enemyId);
            if (enemy == null)
            {
                Debug.LogError($"[CombatManager] Enemy not found: {enemyId}");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritRate, equippedDiceFaces, suppressTerminalRoles, equippedFaceTiers);
        }

        /// <summary>
        /// EnemyData を直接指定して戦闘開始
        /// </summary>
        public void StartCombat(EnemyData enemy, int playerMaxHP,
            int playerDiceCount, int playerDiceMax, float playerCritRate = 0f, int[] equippedDiceFaces = null,
            bool suppressTerminalRoles = false, GameLoop.DiceFaceParts.Tier[] equippedFaceTiers = null,
            EnemyData secondary = null)
        {
            if (enemy == null)
            {
                Debug.LogError("[CombatManager] Enemy data is null!");
                return;
            }

            StartCombatInternal(enemy, playerMaxHP, playerDiceCount, playerDiceMax, playerCritRate, equippedDiceFaces, suppressTerminalRoles, equippedFaceTiers, secondary);
        }

        // ============================================================
        //  2 体戦 (エンカウントプリセット・2026-08-28)
        // ============================================================

        /// <summary>2 体目の敵。 null = 単体戦。
        ///
        /// <para>与ダメージは 1 体目と同額がそのまま入り、 攻撃は独立した 2 つ目のパケットとして飛ぶ。
        /// <b>パッシブは 1 体目のものと合わせて登録するが、 同名は 1 回だけ</b>
        /// (<see cref="StartCombatInternal"/> の該当ブロック)。</para>
        ///
        /// <para><b>なぜ同名を弾くか。</b> 敵側の状態は単一の <see cref="CombatContext"/> に
        /// 平置きされている ── <c>enemyBleedStacks</c> / <c>enemyShield</c> / <c>enemyDiceTotalBonus</c> や、
        /// <see cref="SwapEnemy"/> が <c>sg_</c>・<c>ashen_</c>・<c>berserk_</c> といった<b>接頭辞で掃除している</b>
        /// <c>accumulatedValues</c> がそれ。 同名を 2 回登録すると同じキーへ二重に積み、
        /// <b>静かに 2 倍の効果になる</b>。 「2 体いるぶん濃くなる」のは意図ではないので、
        /// 登録は種類の和集合で採る。 <b>別名のパッシブは両方とも効く。</b></para>
        ///
        /// <para>シールド・出血・遺物・踏みとどまりといった<b>個体に紐づく状態は 1 体目のもの</b>で、
        /// 2 体目は独自に持たない。 個体別に持たせるなら、 先に敵側状態を敵ごとに分けること。</para></summary>
        private EnemyData secondaryEnemy;
        private int secondaryHP;
        /// <summary>[計装] SkillDiag 用: 戦闘開始時の HP / 最大 HP / 種類。</summary>
        private int _diagStartHp, _diagStartMaxHp, _diagKind;

        /// <summary>このターンの 2 体目の攻撃値。 予告 (柱3) で確定し、 解決でも同じ値を使う。</summary>
        private int _secondaryAtkThisTurn;

        /// <summary>2 体目の残り HP。 0 = 撃破済み or 単体戦。</summary>
        public int SecondaryEnemyHP => secondaryHP;
        public EnemyData SecondaryEnemy => secondaryEnemy;
        public bool IsPairEncounter => secondaryEnemy != null;

        /// <summary>2 体目を私物化して整える。 1 体目と同じ前処理 (チューナー・厚い皮膚) を通す ──
        /// 通さないと挑戦デバフが 1 体目にしか掛からず、 高難度ほどペアが相対的に軽くなる。
        /// <b>状態は書かない</b>。 呼び出し側 (<see cref="StartCombatInternal"/>) が
        /// パッシブ登録より前に据える必要があるため。</summary>
        private EnemyData PrepareSecondary(EnemyData secondary, EnemyData primary)
        {
            if (secondary == null) return null;
            var e = AutoTest.BossTuning.Apply(secondary).Clone();

            float thick = MetaProgression.MetaDebuffApplicator.GetEnemyHpMultiplier();
            if (thick > 1.001f) e.maxHP = Mathf.Max(1, Mathf.RoundToInt(e.maxHP * thick));

            // 攻撃ロールの乱数キーは "enemy.atkRoll." + id。 1 体目と同 ID だと
            //   **毎ターン完全に同じ目を出す**ので、 衝突する場合だけキーを分ける。
            if (primary != null && e.id == primary.id) e.id = e.id + "#2";
            return e;
        }

        /// <summary>偽の商人戦用: 規定ターン数を終えても未決着なら敵が逃走する。
        /// StartCombat の後に呼ぶ（StartCombat 内で 0 にリセットされるため）。</summary>
        public void SetFleeAfterTurns(int turns) => fleeAfterTurns = Mathf.Max(0, turns);

        /// <summary>敵のダイス合計値へ毎ロール加算するボーナスを足す（偽の商人「貪欲」等）。
        /// StartCombat の後（ctx 生成後）に呼ぶ。</summary>
        public void AddEnemyDiceTotalBonus(int bonus)
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            if (ctx != null && bonus > 0) ctx.enemyDiceTotalBonus += bonus;
        }

        private void StartCombatInternal(EnemyData enemy, int pMaxHP,
            int pDiceCount, int pDiceMax, float pCritRate, int[] equippedDiceFaces = null,
            bool suppressTerminalRoles = false, GameLoop.DiceFaceParts.Tier[] equippedFaceTiers = null,
            EnemyData secondary = null)
        {
            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat already active!");
                return;
            }

            // 2 体戦の持ち越し防止。 SetSecondaryEnemy は StartCombat の**後**に呼ばれるので、
            //   ここで必ず落としておかないと前の戦闘の 2 体目が居座る。
            secondaryEnemy = null;
            secondaryHP = 0;

            // ボス難易度オートチューナー: hpMul を maxHP に適用 (非ボスは素通り)
            enemy = AutoTest.BossTuning.Apply(enemy);

            // **ここから下は enemy を書き換えてよい**、 という不変条件をこの1行で作る。
            //   BossTuning.Apply が複製を返すのは署名ダイスボスだけで、 非ボスや調整値の無い
            //   ボスでは **EnemyDatabase の共有インスタンスがそのまま返る**。 それを書き換えると
            //   遭遇ごとに効果が複利で積み上がり、 ラン間でも消えない (EnemyDatabase は再読込しない)。
            //   実測: 厚い皮膚 ×1.1 の直書きでスライムの maxHP が 72 → 2,068,124,672 → int 溢れ →
            //   Mathf.Max(1,…) で 1 に固着。 挑戦を 0pt に戻しても復旧せず、 固定難易度スイープが
            //   全フェーズ全滅した (2026-08-04)。 個別の書き換え箇所で複製を思い出す設計は破綻するので、
            //   入口で一度だけ確実に私物化する。
            if (enemy != null) enemy = enemy.Clone();

            // 挑戦デバフ〈厚い皮膚〉: 敵の最大HP ×1.10 / ×1.20 / ×1.30。
            float thickSkin = MetaProgression.MetaDebuffApplicator.GetEnemyHpMultiplier();
            if (thickSkin > 1.001f && enemy != null)
            {
                int before = enemy.maxHP;
                enemy.maxHP = Mathf.Max(1, Mathf.RoundToInt(enemy.maxHP * thickSkin));
                Debug.Log($"[厚い皮膚] {enemy.displayName} 最大HP {before} → {enemy.maxHP} (×{thickSkin:F2})");
            }
            // T4-B〈鋼の皮膚〉: **ボスだけ**最大HP ×1.20。 厚い皮膚 (全敵) と対象を分ける。
            {
                float bossHp = MetaProgression.MetaDebuffApplicator.GetBossHpMultiplier();
                if (bossHp > 1.001f && enemy != null && GameLoop.BossIds.IsBoss(enemy.id))
                {
                    int before = enemy.maxHP;
                    enemy.maxHP = Mathf.Max(1, Mathf.RoundToInt(enemy.maxHP * bossHp));
                    Debug.Log($"[鋼の皮膚] {enemy.displayName} 最大HP {before} → {enemy.maxHP} (×{bossHp:F2})");
                }
            }

            // 業物廃止 (2026-08-10) の代償は **enemies.json の基礎 maxHP を直接下げる**形で入れた
            //   (4層以降の敵を ×0.90)。 ランタイム倍率にすると、 表示される敵HPと実HPが
            //   ずれて調整の基準が二重になる。 §13-3 の「隠し倍率禁止・実数値を直接調整」と同じ理由。

            currentEnemy = enemy;
            // **戦闘開始が途中で失敗したことを黙って落とさない。**
            //   ここから `isCombatActive = true` までの間で抜けると、 呼び出し側から見ると
            //   「フェーズは Combat なのに戦闘が動かない」＝ 無限ループになり、
            //   ストール検出が「phase=Combat」としか言わないので原因が残らない。
            //   実際 Ultra の resume 実装でこれを踏み、 特定に丸一日かかった。
            StartCombatEntered = _combatSeq;

            // ラン中所持パッシブと装備品を PassiveSkillManager に同期（戦闘ごとに再構築）
            var equipHandler = UnityEngine.Object.FindObjectOfType<InventorySystem.ItemEquipHandler>();
            InventorySystem.PassiveSkills.RunPassiveSync.RefreshFromRun(
                GameLoop.GameManager.Instance?.Run, equipHandler);

            // [廃止 2026-09-14] SinDebuff の敵パッシブ動的注入。
            //   〈門〉リワークで欠陥 3 種はすべて**プレイヤー側の規則**になった
            //   (充電の半減 / 端子ごとの配線上限 / ブロックの貫通)。 敵を強化するのではなく
            //   こちらの手が狭くなる形なので、 敵パッシブへ差し込む経路そのものが要らない。
            //   実装は GateFlaws 各所を参照。

            _combatSeq++;
            // **戦闘中の最大HP はランの真の最大HP を使う (2026-08-04 修正)。**
            //   旧実装は呼び出し側が渡す pMaxHP (= Run.playerHP、 つまり持ち込み現在HP) を
            //   そのまま最大HP にしていた。 その結果:
            //     ・戦闘は常に「HP 100%」で始まる ＝ HP割合を条件にするパッシブが序盤で発動しない
            //     ・回復アイテムの「最大HPの X%」が「持ち込みHPの X%」になり、
            //       **削られているときほど回復量が減る**。 完全回復薬ですら持ち込みHPまでしか戻らない
            //   pMaxHP は「この戦闘を開始する現在HP」として扱い、 最大HP とは分ける。
            var runForHp = GameLoop.GameManager.Instance?.Run;
            this.playerMaxHP = runForHp != null && runForHp.playerMaxHP > 0
                             ? runForHp.playerMaxHP : pMaxHP;
            playerHP = Math.Min(pMaxHP, this.playerMaxHP);
            _hpAtCombatStart = playerHP;
            _hpPctAtCombatStart = this.playerMaxHP > 0
                                ? Mathf.RoundToInt(100f * playerHP / this.playerMaxHP) : 100;
            // [計装 2026-08-22] **7層だけの与ダメ倍率内訳を切り出す。**
            //   7層は 4 形態が 1 戦闘なので、 戦闘の開始と終了で差分を採れば連戦全体が採れる。
            //   書き込み地点 (19 箇所) には一切触らない ── 触ると本体の計測が壊れる危険がある。
            _bd7F = enemy != null && !string.IsNullOrEmpty(enemy.id)
                 && enemy.id.StartsWith(GameLoop.BossIds.Layer7Prefix);
            if (_bd7F) System.Array.Copy(DamageBreakdown, _bdAtCombatStart, DamageBreakdown.Length);
            enemyHP = enemy.maxHP;
            // [計測] 7層連戦の 1 段目突入。 2 段目以降との比較の基準になる。
            if (enemy != null && enemy.id == "boss_layer7")
                BossDmgDiag.PhaseEntry(enemy.id, this.playerMaxHP > 0 ? (double)playerHP / this.playerMaxHP : 1.0);
            playerDiceCount = pDiceCount;
            playerDiceMax = pDiceMax;
            playerCritBaseRate = pCritRate;

            // #2 案A': 装備武器の attackPower を解決（equipHandler優先・equippedWeaponId フォールバック・既定2）
            playerAttackPower = 2;
            {
                var apW = equipHandler?.GetCurrentEquipment(InventorySystem.ItemCategory.Weapon);
                if (apW == null)
                {
                    string wid = GameLoop.GameManager.Instance?.Run?.equippedWeaponId;
                    if (!string.IsNullOrEmpty(wid)) apW = InventorySystem.ItemDatabase.Instance?.GetItem(wid);
                }
                if (apW != null && apW.attackPower > 0) playerAttackPower = apW.attackPower;
            }
            isCombatActive = true;
            // [計装] 戦闘の種類別の結果 (SkillDiag)。 開始時点の HP と種類を覚えておく。
            _diagStartHp = playerHP;
            _diagStartMaxHp = this.playerMaxHP;
            _diagKind = GameLoop.BossIds.IsBoss(enemy?.id) ? SkillDiag.Boss
                      : MapSystem.MapManager.Instance?.CurrentNode?.EffectiveType.ToEnemyKind() == EnemyKind.Elite
                        ? SkillDiag.Elite : SkillDiag.Normal;
            ShieldDiag.CurrentKind = _diagKind;
            StartCombatCompleted = _combatSeq;
            metaLethalSurviveUsed = false;
            metaAgilityDodgeUsed = false;
            challengeBossStrikeCount = 0;
            fleeAfterTurns = 0;
            turnLog.Clear();
            mutualWiring = null; // ADR-0009: 配線は戦闘単位でリセット (ラン跨ぎ既定値は W7 後段)
            _fightPlayerRollSum = 0;
            _fightPlayerRollCount = 0;
            _fightStrongTurns = _fightStrongBossWins = _fightWeakTurns = _fightWeakBossWins = 0;

            // パッシブスキルマネージャーに敵スキルを登録
            var psm = PassiveSkillManager.Instance;
            var enemySkills = enemy.passiveSkills != null
                ? new List<EnemyPassiveEntry>(enemy.passiveSkills)
                : new List<EnemyPassiveEntry>();

            // ボスノードの敵には全員「狂暴化」を付与（50T後のエンレイジ）
            var nodeForBerserk = MapSystem.MapManager.Instance?.CurrentNode;
            if (nodeForBerserk != null && nodeForBerserk.type == MapSystem.TileType.Boss
                && !enemySkills.Exists(e => e != null && e.internalName == "Berserk"))
            {
                enemySkills.Add(new EnemyPassiveEntry
                {
                    internalName = "Berserk",
                    skillName = "狂暴化",
                    description = "50ターン経過後、ダイス合計+10・回復封印・被ダメージ3倍",
                });
            }
            // ── 2 体戦 (2026-08-28): 2 体目のパッシブも登録する ──
            //   **同名パッシブは 1 回しか登録しない。** RegisterEnemySkills 側の
            //   `enemySkillNames.Contains` 判定がそのまま重複を弾くので、 単純に連結してよい。
            //   これが必要なのは、 敵側の状態が単一 CombatContext に平置き
            //   (`enemyBleedStacks` / `enemyShield` / `sg_`・`ashen_`・`berserk_` の
            //   accumulatedValues) だからで、 同名を 2 回登録すると同じキーへ二重に積んで
            //   **静かに 2 倍の効果になる**。 「2 体いるぶん濃くなる」のは意図ではない。
            //   OnBattleStart より前に登録する ── 後から足すと 2 体目固有のパッシブだけ
            //   開幕フックを取り逃がす。
            if (secondary != null)
            {
                secondaryEnemy = PrepareSecondary(secondary, enemy);
                if (secondaryEnemy != null)
                {
                    // **不変条件: 2 体目の HP は 1 体目以下。** 与ダメは両方へ同額入るので、
                    //   これが成り立つ限り 2 体目が必ず先に落ちる ＝「1 体目の撃破 = 戦闘終了」で正しい。
                    //   呼び出し側 (GameManager) が HP 順に並べているので通常は素通りするが、
                    //   ここでも締める ── 破れると 2 体目が不死身になる形の壊れ方をする。
                    secondaryHP = Mathf.Clamp(secondaryEnemy.maxHP, 1, Mathf.Max(1, enemy.maxHP));

                    if (secondaryEnemy.passiveSkills != null)
                        foreach (var sp in secondaryEnemy.passiveSkills)
                            if (sp != null) enemySkills.Add(sp);

                    Debug.Log($"[2体戦] 2体目: {secondaryEnemy.displayName} HP{secondaryHP} "
                            + $"攻{secondaryEnemy.EffectiveBaseAttack} "
                            + $"パッシブ{(secondaryEnemy.passiveSkills?.Count ?? 0)}件 (同名は1回のみ登録)");
                }
            }
            // 剣家系〈一対一〉〈果たし合い〉が読む。 **戦闘中は変わらない [persistent]**。
            //   ctx は下の BeginCombat で作られるので、 set はその後 (下記) で行う。

            psm.RegisterEnemySkills(enemySkills);
            // **this.playerMaxHP (= ランの真の最大HP) を渡す。** pMaxHP は持ち込み現在HP なので、
            // ここに渡すと CombatContext 側の最大HP まで持ち込みHP になり、
            // ctx.playerMaxHP を基準にする判定 (HP割合パッシブ・回復量) が全部ずれる (2026-08-04 修正)。
            psm.BeginCombat(this.playerMaxHP, enemy.maxHP, pDiceMax, enemy.EffectiveRollMax, enemy.threat);
            // T4-C〈凶運〉: ラン単位の封印マスクを戦闘へ持ち込む。
            {
                var kyounCtx = psm.Context;
                var kyounRun = GameLoop.GameManager.Instance?.Run;
                if (kyounCtx != null) kyounCtx.sealedRoleMask = kyounRun?.sealedRoleMask ?? 0;
            }
            // CombatContext のコンストラクタは playerCurrentHP = playerMaxHP で初期化するので、
            // 持ち込みHP へ引き直す。
            if (psm.Context != null) psm.Context.playerCurrentHP = playerHP;
            psm.Context?.playerDamageBySource.Clear(); // 被ダメ ソース別内訳を戦闘開始でリセット

            // 希望(ADR-0002) 迷妄: 絶望帯(希望≤20)以降、戦闘開始時にプレイヤーパッシブを1-3個ランダム無効化。
            // 佯狂者は PassiveItem 系統のため対象外（activeSkillNames に含まれない）。
            int delusionCount = GameLoop.HopeSystem.RollPassiveDisableCount(GameLoop.GameManager.Instance?.Run);
            if (delusionCount > 0) psm.DisableRandomPlayerSkills(delusionCount);

            // 装備ダイスの面をコンテキストに設定
            var ctx = psm.Context;
            if (ctx != null && equippedDiceFaces != null)
            {
                ctx.equippedDiceFaces = equippedDiceFaces;
                // 出目パーツ: 面添字ごとの Tier。 面と対で渡ってくる (DiceFaceParts.Build)。
                ctx.equippedFaceTiers = equippedFaceTiers;
            }
            // ADR-0010〈無銘の賽〉: このダイスでは端子役が成立しない (手札役・配線役は通常どおり)。
            if (ctx != null) ctx.suppressTerminalRoles = suppressTerminalRoles;
            // 剣家系〈一対一〉〈果たし合い〉の判定材料。 **戦闘開始時に 1 度だけ確定する [persistent]**
            //   ── secondaryEnemy は上で解決済みなので、 ここで写せば戦闘中ぶれない。
            if (ctx != null) ctx.isPairEncounter = secondaryEnemy != null;
            // 敵の基礎防御（被ダメ%軽減）を反映。エリート(EliteVigor)は OnBattleStart で +0.10 する。
            if (ctx != null)
            {
                ctx.enemyDamageReductionPct = enemy.baseDefenseRate;
                // ボス難易度オートチューナー: 軸別係数引き用にボスidを記録 (非ボスは空)
                ctx.bossId = AutoTest.BossTuning.IsBoss(enemy.id) ? enemy.id : "";
                ctx.lastDamageCause = InventorySystem.PassiveSkills.DeathCause.Normal;

                // 希望(ADR-0002) 苦悩: 悲観床(45)以下で会心倍率 -0.5。Λ「注意散漫」(会心分子上限)とは
                // 効く軸が別(倍率 vs 分子)なので非重複。
                ctx.criticalMultiplier += GameLoop.HopeSystem.GetCritMultiplierDelta(GameLoop.GameManager.Instance?.Run);
            }

            // Λ層（時間の狭間）由来の恒久デバフを ctx へ設定（戦闘スコープで保持）
            if (ctx != null)
            {
                var runForLambda = GameLoop.GameManager.Instance?.Run;
                if (runForLambda != null && runForLambda.lambdaDebuffs != null && runForLambda.lambdaDebuffs.Count > 0)
                {
                    ctx.lambdaFirstTurnDiceDelta     = GameLoop.Lambda.LambdaDebuffEffects.GetFirstTurnDiceDelta(runForLambda);
                    ctx.lambdaHeavyStepsTurns        = GameLoop.Lambda.LambdaDebuffEffects.GetHeavyStepsTurns(runForLambda);
                    ctx.lambdaIrritatingInterval     = GameLoop.Lambda.LambdaDebuffEffects.GetIrritatingInterval(runForLambda);
                    ctx.lambdaDamageDealtMult        = GameLoop.Lambda.LambdaDebuffEffects.GetDamageDealtMult(runForLambda);
                    ctx.lambdaCritRateCap            = GameLoop.Lambda.LambdaDebuffEffects.GetCritRateCap(runForLambda);
                    ctx.lambdaNoCritBranch           = GameLoop.Lambda.LambdaDebuffEffects.NoCritBranch(runForLambda);
                    ctx.lambdaMercifulExecThreshold  = GameLoop.Lambda.LambdaDebuffEffects.GetMercifulExecThreshold(runForLambda);
                    ctx.lambdaConsumableLockUntilTurn= GameLoop.Lambda.LambdaDebuffEffects.GetConsumableLockUntilTurn(runForLambda);

                    // 迫りくる死(lv3): 戦闘開始時に HP を 1 にする（割合計算が先・ボス含む全戦闘）
                    if (GameLoop.Lambda.LambdaDebuffEffects.ImpendingDeathActive(runForLambda))
                    {
                        playerHP = 1;
                        ctx.playerCurrentHP = 1;
                        Debug.Log("[CombatManager] Λデバフ 迫りくる死(lv3): 戦闘開始時 HP=1");
                    }
                }
            }

            // 消費アイテム: マップで使用した「次戦闘バフ」を ctx へコピーし RunState 側をクリア
            if (ctx != null)
            {
                var rs = GameLoop.GameManager.Instance?.Run;
                if (rs != null)
                {
                    ctx.consAtkBurst        = rs.pendingConsAtkBurst;
                    ctx.consDiceRoll        = rs.pendingConsDiceRoll;
                    ctx.consDiceRollTurnsLeft = rs.pendingConsDiceRollTurns;
                    ctx.consShield          = rs.pendingConsShield;
                    ctx.shieldGainedTotal  += rs.pendingConsShield; // 検証計測
                    ctx.consShieldExpireTurn= rs.pendingConsShieldTurns;
                    ctx.consRegen           = rs.pendingConsRegen;
                    ctx.consCritPct         = rs.pendingConsCritPct;
                    ctx.consFlatReduce      = rs.pendingConsFlatReduce;
                    ctx.consDmgMultPct      = rs.pendingConsDmgMultPct;
                    ctx.consDmgMultTurnsLeft = rs.pendingConsDmgMultTurns;
                    ctx.consReflect         = rs.pendingConsReflect;
                    ctx.consEnemyDiceDebuff = rs.pendingConsEnemyDiceDebuff;
                    ctx.gamblerArmed        = rs.pendingGamblerDice;
                    if (rs.pendingFirstRollTotal > 0)
                    {
                        // 加速の粉: 初回ロール(turn1)のダイス合計+X。nextTurnBuffs→turn1でcurrentBuffsへ移行し適用。
                        ctx.nextTurnBuffs["diceBonus"] =
                            (ctx.nextTurnBuffs.TryGetValue("diceBonus", out var db) ? db : 0f) + rs.pendingFirstRollTotal;
                    }
                    if (rs.pendingEnemyStartHpCutPct > 0)
                    {
                        int cut = Mathf.CeilToInt(enemy.maxHP * rs.pendingEnemyStartHpCutPct / 100f);
                        enemyHP = Math.Max(1, enemyHP - cut);
                        Debug.Log($"[CombatManager] 奇襲: 敵開始HP-{cut} ({enemyHP}/{enemy.maxHP})");
                    }
                    // 2026-06-28: 職業スターター消耗品の pending → ctx コピー
                    if (rs.pendingPolishArmed) ctx.polishArmed = true;
                    if (rs.pendingOathArmed)
                    {
                        ctx.oathArmed = true;
                        // 戦闘開始時 HP が既に 80% 未満なら即 broken (装着しても効果なし)
                        if (ctx.playerMaxHP > 0 && ctx.playerCurrentHP * 5 < ctx.playerMaxHP * 4)
                            ctx.oathBroken = true;
                    }
                    if (rs.pendingDaggerArmed) ctx.daggerArmed = true;
                    rs.ClearPendingCombatConsumables();
                }
            }

            // 2026-07-25 v6: 整備パネル 防御トラック - 開幕シールド (毎戦闘付与)
            if (ctx != null)
            {
                var relicRun = GameLoop.GameManager.Instance?.Run;
                // 整備パネル分は 2026-08-15 に割合軽減へ移したのでここには居ない
                // (GetGuardDamageReductionPct)。 アイテム副次ステータスは 2026-09-15 に全廃。
                // 残るのは遺物 (§15-5) だけ。
                int openingShield = MetaProgression.Relics.RelicApplicator.GetOpeningShield(relicRun);
                // 防御 r10 (極点): 前戦闘の残りシールドを持ち越す。 使い切ったら 0。
                if (relicRun != null && relicRun.carriedShield > 0)
                {
                    openingShield += relicRun.carriedShield;
                    Debug.Log($"[MetaBuff] シールド持ち越し +{relicRun.carriedShield} (防御r10)");
                    relicRun.carriedShield = 0;
                }
                if (openingShield > 0)
                {
                    int gained = Math.Max(0, openingShield - ctx.healShieldReduction); // 天衣無縫減衰
                    ctx.consShield += gained;
                    ShieldDiag.Note("遺物・防御r10持ち越し", gained);
                    ctx.shieldGainedTotal += gained;
                    // 開幕シールドは戦闘中持続
                    if (ctx.consShieldExpireTurn == 0) ctx.consShieldExpireTurn = -1;
                    Debug.Log($"[MetaBuff] 開幕シールド +{gained} (遺物+持ち越し)");
                }

                // 遺物 (§15-5): 開幕の充電・出血・毒。 いずれも戦闘開始時に 1 回だけ。
                // **出血は短期戦専用**(N ターンで枯れる)、**毒は減衰しないので長期戦向き**という対。
                int rc = MetaProgression.Relics.RelicApplicator.GetOpeningCharge(relicRun);
                if (rc > 0) { ctx.AddCharge(rc); Debug.Log($"[遺物] 開幕充電 +{rc}"); }

                int rb = MetaProgression.Relics.RelicApplicator.GetOpeningBleed(relicRun);
                if (rb > 0) { ctx.AddEnemyBleed(rb); Debug.Log($"[遺物] 開幕出血 +{rb}"); }

                int rp = MetaProgression.Relics.RelicApplicator.GetOpeningPoison(relicRun);
                if (rp > 0)
                {
                    ctx.AddStatus(InventorySystem.PassiveSkills.StatusTarget.Enemy, "poison", rp);
                    Debug.Log($"[遺物] 開幕毒 +{rp}");
                }
            }
            // 2026-07-25 v6: 種火 (充電/臨界/毒/出血) の r2 効果 - 戦闘開始時に適用
            if (ctx != null)
            {
                // 臨界 r2 保温炉: 前戦闘のメーターを持ち越し (RunState.pendingRinkaiCarryover 経由)
                var runR = GameLoop.GameManager.Instance?.Run;
                if (runR != null && runR.pendingRinkaiCarryover > 0)
                {
                    ctx.rinkaiMeter += runR.pendingRinkaiCarryover;
                    Debug.Log($"[MetaBuff] 臨界持ち越し: メーター +{runR.pendingRinkaiCarryover}");
                    runR.pendingRinkaiCarryover = 0;
                }
            }

            // 大穴の異常現象: 戦闘開始時の発火判定 (蝕夜の双方T1スキップ・鉄を溶かす太陽の初回T決定)
            MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.OnCombatStart(GameLoop.GameManager.Instance?.Run);

            // 戦闘相手の種別を CombatContext に設定 (暗殺教団契約等で参照)
            // MapManager.CurrentNode の TileType から導出。 ノード未取得時は Normal フォールバック。
            var combatCtx = psm.Context;
            if (combatCtx != null)
            {
                var node = MapSystem.MapManager.Instance?.CurrentNode;
                combatCtx.currentEnemyKind = node != null
                    ? node.EffectiveType.ToEnemyKind()
                    : EnemyKind.Normal;
            }

            // 魔王の威圧 等の戦闘開始時スキル処理
            psm.FireTrigger(PassiveSkillTrigger.OnBattleStart);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // パッシブ刻印: 戦闘開始時の効果を適用 (HP回復・シールド付与・ダイス補正等)
            // 敵の戦闘開始スキルによるHP減少適用
            ctx = psm.Context;
            if (ctx != null)
            {
                float hpReduction = ctx.GetAccumulated("enemyMaxHPReduction");
                if (hpReduction > 0)
                {
                    playerHP = Math.Max(1, playerHP - (int)hpReduction);
                    this.playerMaxHP = Math.Max(1, this.playerMaxHP - (int)hpReduction);
                    ctx.playerMaxHP = this.playerMaxHP;
                    ctx.playerCurrentHP = playerHP;
                }
            }

            // 時限バフ・デバフ（戦闘開始時系）の適用
            EventSystem.TimedEffects.TimedEffectManager.OnCombatStart(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブアイテム（戦闘開始時系）
            InventorySystem.PassiveItems.PassiveItemManager.OnCombatStart(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            OnCombatStart?.Invoke(enemy.id);
            MetaProgression.Achievements.AchievementService.NoteCombatStarted();

            // ===== LED演出システム初期化 =====
            ledManager = DiceLEDManager.Instance;
            if (ledManager != null)
            {
                // アクティブなダイス数を設定
                ledManager.SetActiveDiceCount(playerDiceCount, enemy.EffectiveRollCount);
                
                // LED色をリセット（デフォルトカラーに戻す）
                ledManager.TurnOffAll();
                
                Debug.Log($"[CombatManager] DiceLED initialized - Player: {playerDiceCount}, Enemy: {enemy.EffectiveRollCount}");
            }
            else
            {
                Debug.LogWarning("[CombatManager] DiceLEDManager not found! LED animations will be disabled.");
            }
            
            // 恒久デバフ「コルヴェンの憤怒」: ボス戦の戦闘開始時、現在HP半減（1T目のダイス最大化は OnRoll で処理）
            var runForWrath = GameLoop.GameManager.Instance?.Run;
            var nodeForWrath = MapSystem.MapManager.Instance?.CurrentNode;
            wrathDiceOverrideArmed = false;
            if (runForWrath != null && nodeForWrath != null
                && nodeForWrath.type == MapSystem.TileType.Boss
                && MetaProgression.PermanentDebuffEffects.HasWrath(runForWrath))
            {
                int oldHp = playerHP;
                playerHP = Mathf.Max(1, playerHP / 2); // 割合計算が先
                wrathDiceOverrideArmed = true;
                Debug.Log($"[CombatManager] 恒久デバフ {MetaProgression.PermanentDebuffIds.Wrath}: HP {oldHp}→{playerHP}, 1T目ダイス最大化を予約");
            }

            Debug.Log($"[CombatManager] ===== COMBAT START: {enemy.displayName} =====");
            Debug.Log($"  Player HP: {playerHP}/{this.playerMaxHP}, Dice: {playerDiceCount}d{playerDiceMax}, Crit: {playerCritBaseRate * 100f:F1}%");
            Debug.Log($"  Enemy  HP: {enemyHP}/{enemy.maxHP}, Dice: {enemy.DiceNotation}, Threat: {enemy.threat}");
        }

        /// <summary>戦闘中にエネミーを差し替える（覚者の連戦などで使用）。
        /// プレイヤー状態(HP/ダイス/buffs/currentTurn) は完全保持。
        /// 敵側パッシブと敵固有の accumulatedValues キーは新エネミーのものに置換。</summary>
        /// <param name="newEnemyId">enemies.json の id</param>
        /// <param name="logLabel">遷移ログに使うラベル(例:"覚者第二形態へ")</param>
        /// <returns>差し替え成功なら true</returns>
        public bool SwapEnemy(string newEnemyId, string logLabel = null)
        {
            // [計測] 7層連戦の段突入時 残HP。 **ここが唯一の合流点**。
            //   旧: TryAdvanceVescaChain 内でのみ記録していたが、 あれは OnTurnEnd より前に
            //   削り切った取りこぼし用の経路なので、 正常経路 (連続実験の予約 → SwapEnemy) を
            //   通る大多数が記録から漏れていた (実測 3380 件に対し 36 件しか拾えず)。
            if (!string.IsNullOrEmpty(newEnemyId) && newEnemyId.StartsWith("boss_layer7") && playerMaxHP > 0)
            {
                BossDmgDiag.PhaseEntry(newEnemyId, playerHP / (double)playerMaxHP);
                if (newEnemyId == "boss_layer7_p4") BossDmgDiag.P4Entry(playerHP / (double)playerMaxHP);
            }

            if (!isCombatActive) { Debug.LogWarning("[CombatManager.SwapEnemy] 戦闘中でない"); return false; }
            var newEnemy = EnemyDatabase.Get(newEnemyId);
            if (newEnemy == null) { Debug.LogError($"[CombatManager.SwapEnemy] enemy not found: {newEnemyId}"); return false; }
            // ボス難易度オートチューナー: 覚者連戦の各形態にも hpMul を適用
            newEnemy = AutoTest.BossTuning.Apply(newEnemy);

            string prevName = currentEnemy?.displayName ?? "?";
            currentEnemy = newEnemy;
            enemyHP = newEnemy.maxHP;

            var psm = PassiveSkillManager.Instance;
            if (psm == null) { Debug.LogError("[CombatManager.SwapEnemy] PSM null"); return false; }

            // 敵側パッシブを差し替え（プレイヤー側パッシブは保持）。
            // ボス連戦(覚者等)でも狂暴化を維持する。
            var swapSkills = newEnemy.passiveSkills != null
                ? new List<EnemyPassiveEntry>(newEnemy.passiveSkills)
                : new List<EnemyPassiveEntry>();
            var nodeForSwapBerserk = MapSystem.MapManager.Instance?.CurrentNode;
            if (nodeForSwapBerserk != null && nodeForSwapBerserk.type == MapSystem.TileType.Boss
                && !swapSkills.Exists(e => e != null && e.internalName == "Berserk"))
            {
                swapSkills.Add(new EnemyPassiveEntry
                {
                    internalName = "Berserk", skillName = "狂暴化",
                    description = "50ターン経過後、ダイス合計+10・回復封印・被ダメージ3倍",
                });
            }
            psm.RegisterEnemySkills(swapSkills);

            // 敵側固有 accumulatedValues キーを掃除（プレイヤー側補正は保持）
            // 既知の "enemy-side" prefix を持つキーを除去
            var ctx = psm.Context;
            if (ctx != null)
            {
                var toRemove = new List<string>();
                foreach (var k in ctx.accumulatedValues.Keys)
                {
                    if (k.StartsWith("sg_") || k.StartsWith("ashen_")
                        || k.StartsWith("starfire_") || k.StartsWith("judg")
                        || k.StartsWith("ember_") || k.StartsWith("berserk_")
                        || k == "extraDice" || k == "enemyMaxHPReduction")
                        toRemove.Add(k);
                }
                foreach (var k in toRemove) ctx.accumulatedValues.Remove(k);

                // ctx 敵パラメータも更新（プレイヤー視点で書く: enemy = ボス、player = プレイヤー）
                ctx.enemyMaxHP = newEnemy.maxHP;
                ctx.enemyCurrentHP = newEnemy.maxHP;
                ctx.enemyThreat = newEnemy.threat;
                ctx.enemyDiceMax = newEnemy.EffectiveRollMax;
                ctx.enemyDiceTotalBonus = 0;
                ctx.bossDiceBonus = 0; // swapで一旦クリア。新形態の OnBattleStart パッシブ(刹那等)が再設定する
                ctx.enemyDamageReductionPct = newEnemy.baseDefenseRate; // 新形態の基礎防御%を反映
                // [計装] 出ていく段の実長を締める。 currentTurn は連戦で通算なので、
                //   開始点 (phaseStartTurn) との差でしか段の長さは出せない。
                if (!string.IsNullOrEmpty(ctx.bossId)
                    && ctx.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix))
                {
                    GameLoop.RunChronicle.NotePhaseDuration(
                        ctx.bossId.EndsWith("_p4") ? 4 : ctx.bossId.EndsWith("_p3") ? 3
                            : ctx.bossId.EndsWith("_p2") ? 2 : 1,
                        ctx.phaseStartTurn, ctx.currentTurn);
                }
                ctx.phaseStartTurn = ctx.currentTurn + 1;
                ctx.bossId = AutoTest.BossTuning.IsBoss(newEnemy.id) ? newEnemy.id : "";
                ctx.lastDamageCause = InventorySystem.PassiveSkills.DeathCause.Normal; // 形態swapで死因タグをリセット (前形態の残留防止)
                // ADR-0010: 役札は**形態ごとに戻す**。 4 連戦を通して 1 枚も戻らないと、
                //   後半の形態で手札が空のままエスカレーション段階だけが上がり、
                //   「敵が強くなるほど手段が減る」二重の締め付けになる。
                //
                // [検討中 2026-08-22] **7層だけ持ち越す**規則を A/B できるようにした
                //   (<see cref="VescaKeepRolesAcrossPhases"/>、 既定 false = 従来どおり)。
                //   持ち越すと 16 枚を 40 ターン級へ配分する問題になり、 温存が技量軸として立つ
                //   ── 上の「二重の締め付け」は、 裏返せば**配分の難しさ**そのものでもある。
                //   どちらが良いかは実測で決める。
                bool keepAcross = VescaKeepRolesAcrossPhases
                               && !string.IsNullOrEmpty(ctx.bossId)
                               && ctx.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix);
                if (!keepAcross)
                {
                    // 役エポックの終端。 **クリアする時だけ締める** ── 持ち越すなら
                    //   判断の地平は続いているので、 計装の単位も伸ばさなければ嘘になる。
                    YachtRoleEffects.FlushRoleEpoch();
                    ctx.usedRoles.Clear();
                }
            }

            // 新エネミーの OnBattleStart 発火
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            // LED アクティブダイス数も更新
            if (ledManager != null)
                ledManager.SetActiveDiceCount(playerDiceCount, newEnemy.diceCount);

            string label = string.IsNullOrEmpty(logLabel) ? newEnemy.displayName : logLabel;
            Debug.Log($"[CombatManager] ━━ エネミー差し替え: {prevName} → {label} (HP:{enemyHP}/{newEnemy.maxHP}, Dice:{newEnemy.DiceNotation}, Threat:{newEnemy.threat})");

            // 計測フック: 新フォームを「新エネミーとの遭遇」として通知
            // (AutoRunner 等がチェーン途中フォームのスタッツを取れるようにする)
            GameLoop.GameManager.Instance?.RaiseEnemyEncountered(newEnemy);
            return true;
        }

        // ===========================================================
        //  ターン実行
        // ===========================================================

        /// <summary>
        /// 1ターンを実行して結果を返す
        /// </summary>
        // ===========================================================
        //  ADR-0009 相互攻撃パイプライン (W7) — 切替式
        // ===========================================================

        /// <summary>ADR-0009 相互攻撃パイプラインで戦闘を解決するか。**既定 true** (2026-07-27)。
        /// ADR-0009 が正式仕様であり、旧ロール勝負 (false) は比較スイープ用の退避経路として残す。
        /// 仕様: docs/GAME.md §6</summary>
        public static bool UseMutualAttackPipeline = true;

        /// <summary>ADR-0009 柱5: 充電ゲージ最大値。
        /// 過充電判定 = charge >= ChargeMax。
        ///
        /// **2026-08-09 に 10 → 30** (ADR-0010)。 充電がリロールの原資も兼ねるようになり、
        /// 供給 (充電端子 2 本で +7/ターン程度) に対して 10 では窮屈すぎるため。</summary>
        public const int ChargeMax = 30;

        /// <summary>[検討中 2026-08-22] **7層ヴェスカの 4 連戦で役札を持ち越す** (既定 false)。
        ///
        /// <para>true にすると形態交代で <c>usedRoles</c> をクリアしない ── 16 枚を
        /// 40 ターン級の連戦へ**配分する**問題になり、 「いつ切るか」が初めて技量軸として立つ。
        /// 現行 (false) は形態ごとに戻すので、 1 形態 ≒ 10 ターンでは温存の余地がほとんど無い
        /// （実測: 見送ると 8〜9 割の確率で二度と成立しない）。</para>
        ///
        /// <para><b>2026-08-22 に true で確定 (設計判断)。</b> 実測では BOT のクリア率が
        /// 57.5% → 52.0% (McNemar p&lt;0.0001) と**下がる**。 だがこれは
        /// 「持ち越し規則 × 貪欲方策」の測定であって規則の評価ではない ──
        /// <see cref="AutoTest.SuperCombatAI"/> の <c>WouldFire</c> は 16役中11役が「成立即発動」で
        /// **配分をする方策を持たない**ため、 p1 で 16 枚を使い切って p2〜p4 を空で戦う。
        /// 規則が作った技量軸を登れる主体がいない状態での数字である。
        ///
        /// <para><b>この採用で 0pt 遺物あり 57.5% は基準値として無効。</b>
        /// また BOT 側に温存判断を入れる価値がここで初めて生じる ── 地平が 1 形態 (≒10T) から
        /// 連戦全体 (≒40T) へ伸び、 「見送っても取り返せる」確率が跳ね上がるため。</para></summary>
        public static bool VescaKeepRolesAcrossPhases = true;

        /// <summary>配線ポリシー注入点 (完全情報: 自出目+テレグラフ → 実体とゴーストの対)。
        /// null なら前ターン配線を維持 (初期値は全ダイス攻撃 = 未配置の自動合流)。
        /// AutoRunner / 配線 UI (ADR-0008 W5) がここへ差す。
        /// <b>戻り値が <see cref="WiringPlan"/> なのは実体とゴーストを原子的に受け取るため</b>
        /// (別々に取ると片方だけ更新された不整合が静かに通る)。</summary>
        public Func<int[], MutualTurnTelegraph, WiringPlan> WiringPolicy;

        /// <summary>ADR-0010: リロール方策の注入点。
        /// 引数 = (現在の出目, テレグラフ, そのターンで何回目の振り直しか 1..)。
        /// 戻り値 = 振り直すダイスの index 配列。 null / 空なら振り直しを打ち切る。
        ///
        /// **コストは `個数 × 回数`** で逓増する。 払えなければそこで打ち切られるので、
        /// 「全部振り直しを 2 回」は上限 30 では不可能 ── 狙った少数を刻む方が効率的になる。</summary>
        public Func<int[], MutualTurnTelegraph, int, int[]> RerollPolicy;

        /// <summary>ADR-0010: 役の発動方策の注入点。
        /// 引数 = (成立している役, テレグラフ, 使用済みの役)。 戻り値 = **今ターン切る役**。
        ///
        /// **発動は完全任意** ── 成立しても切らない自由が無いと、 1戦闘1回の希少性が
        /// 「最良の役が勝手に発火して勝手に消える」だけになり選択が消える。
        /// null なら何も切らない (見送り)。</summary>
        public Func<List<RoleKind>, MutualTurnTelegraph, HashSet<RoleKind>, List<RoleKind>> RolePolicy;

        /// <summary>現在の配線 (ダイス index → 端子)。「配線は残る」= 戦闘中は自動適用。
        /// TODO(W7): ラン跨ぎの既定値保存は RunState へ (ADR-0008 残タスク #5)。</summary>
        private DiceTerminal[] mutualWiring;

        public TurnResult ExecuteTurn()
        {
            if (!isCombatActive)
            {
                Debug.LogError("[CombatManager] No active combat!");
                return default;
            }

            if (UseMutualAttackPipeline) return ExecuteTurnMutual();

            var psm = PassiveSkillManager.Instance;
            var ctx = psm.Context;

            // 偽の商人: 規定ターン数を終えても未決着なら、敵が逃走して戦闘終了。
            // 両者生存のまま終わる（playerWon=false かつ playerHP>0 ＝ GameManager 側で「逃走」と判定）。
            if (fleeAfterTurns > 0 && ctx != null && ctx.currentTurn >= fleeAfterTurns
                && playerHP > 0 && enemyHP > 0)
            {
                MetaProgression.Achievements.AchievementService.NoteEnemyEscaped(currentEnemy?.id);
                Debug.Log($"[CombatManager] 偽の商人: {fleeAfterTurns}ターン経過 → 逃走（戦闘強制終了）");
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = playerHP,
                    enemyHPAfter = enemyHP,
                };
            }

            // --- ターン開始 ---

            psm.BeginTurn();

            // 敵スタンス（ADR-0005）: 毎ターン頭にロール前テレグラフで二択を抽選（ロール力↔ダメージのアンチ相関）。
            // プレイヤースタンス（ADR-0006）: 敵スタンス提示後にロール前選択（攻撃/防御）。強制ロール中はスキップ（=攻撃）。
            if (ctx != null)
            {
                {
                    var stance = EnemyStance.Apply(ctx);
                    int eMax = (stance == EnemyStance.Kind.LowRollHighDmg)
                        ? EnemyStance.WeakRollMax(currentEnemy.EffectiveRollMax, ctx.enemyStanceWeakRollRatio) : currentEnemy.EffectiveRollMax;
                    string rollNote = (stance == EnemyStance.Kind.LowRollHighDmg)
                        ? $"弱ロール(上限{currentEnemy.EffectiveRollMax}→{eMax})" : "強ロール(基準)";
                    Debug.Log($"[敵スタンス] {EnemyStance.Label(stance)}（{rollNote} / 被ダメ×{ctx.enemyStanceDamageMult:0.0}）");

                    // ロール前の推定勝率（正規近似・ADR-0006）→ 学習閾値でスタンス選択
                    float estWinProb = EstimateWinProbability(playerDiceCount, playerDiceMax, ctx.equippedDiceFaces,
                                                              currentEnemy.EffectiveRollCount, eMax);
                    // ロール優勢度を5段階で提示（期待値を暗算せずに優劣を読めるUX。ビジュアルは後付け）。
                    var odds = RollOddsRating.Telegraph(estWinProb);
                    Debug.Log($"[ロール優勢度] {RollOddsRating.Label(odds)}（推定勝率{estWinProb:P0}）");
                    var pStance = PlayerStance.Choose(ctx, playerHP, this.playerMaxHP, estWinProb);
                    if (pStance == PlayerStance.Kind.Defense)
                        Debug.Log($"[自スタンス] {PlayerStance.Label(pStance)} (推定勝率{estWinProb:P0}・与ダメ×{PlayerStance.DefenseWinDamageMult:0.0}/受け最終×{PlayerStance.DefenseLossDamageMult:0.0})");
                }
            }

            // 安全弁: 戦闘が異常に長引いた場合は強制決着（プレイヤー敗北）。
            // 既知の挙動: 「ボスにダメージ通らない × プレイヤー再生で死なない」の二重デッドロックで
            // AutoRunner が 75万ターン超に達した前例がある (旧 SinDebuff 時代)。
            // 戦闘単体での暴走は許容しない。300T を超えたらプレイヤーHPを0にして即座に終了。
            const int kHardTurnCap = 300;
            if (ctx != null && ctx.currentTurn > kHardTurnCap)
            {
                Debug.LogWarning($"[CombatManager] 戦闘ターン上限超過 ({ctx.currentTurn} > {kHardTurnCap}) — 強制決着（プレイヤー敗北）");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                    enemyHPAfter = enemyHP,
                };
            }

            // 消費アイテム持続効果の毎ターン再適用（BeginNewTurn でリセットされるため）
            if (ctx != null)
            {
                if (ctx.consCritPct > 0f) ctx.critRatePctAdd += ctx.consCritPct;
                if (ctx.consFlatReduce > 0) ctx.playerFlatDamageReduction += ctx.consFlatReduce;
                // 死因タグをターン頭でリセット。 このターンに致死スキルが発動すればそれが死因、
                // 何も無ければ通常ロール敗北(Normal) → ターン単位で正確に死因を帰属できる。
                ctx.lastDamageCause = InventorySystem.PassiveSkills.DeathCause.Normal;
            }

            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);

            // パッシブ刻印: T2 開始時に静寂の刻印(T1限定 -2軽減)を剥がす
            // 敵側のターン開始処理を反映（再生、夜の王 等）
            // SwapPerspective の影響で敵HP変動がplayerCurrentHPに入っている場合があるので
            // context から最新値を同期
            SyncHPFromContext(ctx);

            // フロアデバフ: 敵の毎ターンHP回復（6層 深淵の洗礼: enemy +3）
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;
            if (floorMod != null && floorMod.enemyPerTurnHeal > 0 && enemyHP > 0)
            {
                int heal = Mathf.Min(currentEnemy.maxHP - enemyHP, floorMod.enemyPerTurnHeal);
                if (heal > 0)
                {
                    enemyHP += heal;
                    ctx.enemyCurrentHP = enemyHP;
                    Debug.Log($"[CombatManager] フロアデバフ: 敵HP+{heal} ({enemyHP}/{currentEnemy.maxHP})");
                }
            }

            // 敵のextraDice確認（夜の王 等）
            int enemyExtraDice = (int)ctx.GetAccumulated("extraDice");
            ctx.accumulatedValues["extraDice"] = 0; // リセット

            // --- ダイスロール ---
            int actualPlayerDiceCount = playerDiceCount;
            int actualEnemyDiceCount;

            int[] playerDice = RollDice(actualPlayerDiceCount, playerDiceMax, ctx.equippedDiceFaces);
            NoteFirstRoll(actualPlayerDiceCount, ctx, playerDice);
            // 敵スタンス（ADR-0005）: 弱ロール時は上限を縮めて実際に引く（期待値≈0.65倍。結果の事後倍率ではない）。
            bool enemyWeakRoll = ctx != null && ctx.enemyStanceKind == (int)EnemyStance.Kind.LowRollHighDmg;
            float weakRatio = ctx != null ? ctx.enemyStanceWeakRollRatio : EnemyStance.WeakRollRatio;
            int[] enemyDice = RollEnemyAttack(currentEnemy, enemyExtraDice, enemyWeakRoll, weakRatio);
            actualEnemyDiceCount = enemyDice.Length;

            // シュヴァリエのレイピア コントラタック発動中: プレイヤーは 1d1 強制（必ず1を出す）
            if (ctx.GetAccumulated("player_contre") > 0)
            {
                actualPlayerDiceCount = 1;
                playerDice = new[] { 1 };
                Debug.Log("[CombatManager] コントラタック: プレイヤー 1d1 強制");
            }

            // シュヴァリエのレイピア 解除後の解放ターン: ダイス個数+1（クリ補正は currentBuffs 経由）
            if (ctx.GetAccumulated("rapier_release_pending") > 0)
            {
                actualPlayerDiceCount += 1;
                // 1個追加分の出目を生成して配列を拡張
                var extra = ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0
                    ? ctx.equippedDiceFaces[GameLoop.GameRng.Range(0, ctx.equippedDiceFaces.Length, "combat.rapierFace", RngIdx(40))]
                    : GameLoop.GameRng.Range(1, playerDiceMax + 1, "combat.rapier", RngIdx(40));
                var newArr = new int[playerDice.Length + 1];
                for (int i = 0; i < playerDice.Length; i++) newArr[i] = playerDice[i];
                newArr[newArr.Length - 1] = extra;
                playerDice = newArr;
                ctx.accumulatedValues["rapier_release_pending"] = 0;
                Debug.Log($"[CombatManager] レイピア解放: ダイス+1 (追加出目={extra})、会心+9 はバフ経由");
            }

            // 獣の恩義: 1ターン目の敵ロールを全て0にする（プレイヤー実質勝利確定）
            if (ctx.nullifyFirstEnemyRoll && ctx.currentTurn == 1)
            {
                for (int i = 0; i < enemyDice.Length; i++) enemyDice[i] = 0;
                ctx.nullifyFirstEnemyRoll = false;
                Debug.Log("[CombatManager] 獣の恩義発動: 敵の最初のロール無効化");
            }

            // 大穴の異常現象:
            //   - 鳴りやまない鐘 (20%でプレイヤーダイス1個 -1)
            //   - 影が落ちない正午 (T1で敵攻撃 50% 空振り = 敵ダイス全0)
            //   - 蝕夜 (T1で双方ダイス全0 = 行動無効)
            var phenRun = GameLoop.GameManager.Instance?.Run;
            MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.ApplyBellPenalty(phenRun, playerDice);
            if (MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.ShouldNoonMiss(phenRun, ctx.currentTurn))
            {
                for (int i = 0; i < enemyDice.Length; i++) enemyDice[i] = 0;
                Debug.Log("[CombatManager] 異常現象「影が落ちない正午」: 敵T1攻撃空振り");
            }
            if (MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks.IsEclipsedTurn(phenRun, ctx.currentTurn))
            {
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = 0;
                for (int i = 0; i < enemyDice.Length; i++) enemyDice[i] = 0;
                Debug.Log("[CombatManager] 異常現象「蝕夜」: 双方T1行動無効");
            }
            // 鉄を溶かす太陽: スケジュール T で発火 (プレイヤー行動無効 + HP-10 後段で適用)
            int ironSunDmg = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks
                .ApplyIronSunIfTriggered(phenRun, ctx.currentTurn, playerDice);
            if (ironSunDmg > 0 && playerHP > 0)
            {
                ironSunDmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    ironSunDmg, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - ironSunDmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] 異常現象「鉄を溶かす太陽」: HP-{ironSunDmg} (HP: {playerHP}/{playerMaxHP})");
            }

            // 影の代償: 5層ボス戦中、毎ロール50%でプレイヤーダイス全出目-1
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null
                && run.currentFloor == run.normalClearFloor
                && currentEnemy != null
                && MapSystem.MapManager.Instance?.CurrentNode != null
                && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss
                && run.permanentDebuffs.Contains("影の代償")
                && !ctx.rollPurity
                && GameLoop.GameRng.Chance(0.5f, "combat.coin", RngIdx(5)))
            {
                for (int i = 0; i < playerDice.Length; i++)
                    playerDice[i] = Math.Max(1, playerDice[i] - 1);
                Debug.Log("[CombatManager] 影の代償発動 (50%): プレイヤー全出目-1");
            }

            // ダイス振り直し。 ADR-0010 で希望消費の自動ポリシーから充電消費の方策注入へ移行。
            // 旧パイプライン (UseMutualAttackPipeline=false) でも同じ実装を通す。
            RerollPhase(ctx, playerDice, enemyDice, playerDiceMax);

            // ロール時系時限効果がダイス配列を直接書き換えるため、ctx に参照を渡しておく
            ctx.playerDice = playerDice;
            ctx.enemyDice = enemyDice;
            ctx.playerDiceMax = playerDiceMax;
            EventSystem.TimedEffects.TimedEffectManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（ロール時系）
            InventorySystem.PassiveItems.PassiveItemManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 恒久デバフ「コルヴェンの憤怒」: ボス戦1T目に自ダイスを全て最大値化
            if (!ctx.rollPurity && wrathDiceOverrideArmed && ctx.currentTurn == 1)
            {
                for (int i = 0; i < playerDice.Length; i++) playerDice[i] = playerDiceMax;
                wrathDiceOverrideArmed = false;
                Debug.Log($"[CombatManager] {MetaProgression.PermanentDebuffIds.Wrath}: 1T目ダイス全て最大値");
            }

            // メタバフ: ダイス合計値補正（一番低いダイスから +1 を順次振り分け、各ダイスは playerDiceMax 上限）
            int metaDiceBonus = ctx.rollPurity ? 0 : MetaProgression.MetaBuffApplicator.GetDiceTotalBonus();
            int safety = metaDiceBonus * playerDice.Length;
            while (metaDiceBonus > 0 && safety-- > 0)
            {
                int minIdx = -1;
                for (int j = 0; j < playerDice.Length; j++)
                {
                    if (playerDice[j] >= playerDiceMax) continue;
                    if (minIdx < 0 || playerDice[j] < playerDice[minIdx]) minIdx = j;
                }
                if (minIdx < 0) break; // 全て上限到達
                playerDice[minIdx]++;
                metaDiceBonus--;
            }

            // ===== LED演出実行 =====
            if (ledManager != null)
            {
                // アクティブなダイス数を更新（追加ダイスがある場合）
                ledManager.SetActiveDiceCount(actualPlayerDiceCount, actualEnemyDiceCount);
                
                // ローリングアニメーション開始（非同期）
                ledManager.PlayRollingAnimation(
                    playerDice, enemyDice, 
                    playerDiceMax, currentEnemy.EffectiveRollMax
                );
                
                Debug.Log($"[CombatManager] LED Animation started - P:{string.Join(",", playerDice)} E:{string.Join(",", enemyDice)}");
            }

            // パッシブスキルによるダイス処理
            psm.ProcessPostRoll(playerDice, enemyDice);


            // パッシブ刻印: per-roll 効果 (粘り・腐食) を fixedDamageToEnemy に積む
            // 敵スキルのPostRoll発火
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostRoll);

            // 勝敗トリガー（敵側）
            if (ctx.playerWonRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollLose);
            else if (ctx.playerLostRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollWin);
            else
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollDraw);

            // ボス難易度チューナー: プレイヤーの実ロール合計を累計 (平均出目の算出用)
            _fightPlayerRollSum += ctx.playerDiceTotal;
            _fightPlayerRollCount++;

            // スタンス別の「ボスがロール勝ち(=プレイヤー敗北)した割合」を計測。強/弱ロールで別レンジ制御するため分けて集計。
            if (ctx.enemyStanceKind == (int)EnemyStance.Kind.HighRollLowDmg)
            { _fightStrongTurns++; if (ctx.playerLostRoll) _fightStrongBossWins++; }
            else if (ctx.enemyStanceKind == (int)EnemyStance.Kind.LowRollHighDmg)
            { _fightWeakTurns++; if (ctx.playerLostRoll) _fightWeakBossWins++; }

            // --- ダメージ計算 ---
            var result = new TurnResult
            {
                turnNumber = ctx.currentTurn,
                playerDice = playerDice,
                enemyDice = enemyDice,
                playerDiceTotal = ctx.playerDiceTotal,
                enemyDiceTotal = ctx.enemyDiceTotal,
                playerWon = ctx.playerWonRoll,
                isDraw = !ctx.playerWonRoll && !ctx.playerLostRoll,
            };

            int diceDiff = Math.Abs(ctx.diceDifference);

            if (!result.isDraw)
            {
                // (3) メインダメージ = ダイス合計差
                int mainDmg = diceDiff;
                // 〈灰燼の烙印〉サドンデスの一撃必殺はターン終端で敗者HPを直接0にする
                // （AshArmor/ImmortalEmber/シールド/LSを全てバイパスするためここでは加算しない）

                // (4) 追撃ダメージ（パッシブ由来: ctx.pursuitDamage はスキル発火時にセット済み）
                int pursuitDmg = ctx.pursuitDamage;

                if (result.playerWon)
                {
                    // プレイヤーが勝利 → 敵にダメージ
                    // #2 案A': 勝利base = 武器attackPower + floor(|差|/3)。差は勝敗を主に決め、ダメージ寄与は小（会心には非干渉）。
                    int winBase = playerAttackPower + diceDiff / WeaponDiffPerBonus;
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        winBase, pursuitDmg, playerCritBaseRate);

                    // 与ダメージ修飾チェーン（順序厳守。詳細は ApplyWinDamageModifiers 参照）
                    int lbStage = GameLoop.GameManager.Instance?.Run?.limitBreakStage ?? 0;
                    totalDmg = ApplyWinDamageModifiers(totalDmg, ref fixedDmg, ref isCrit, lbStage, psm, ctx);

                    // 希望(ADR-0002) 疲労: 焦燥床(75)以下で、この攻撃が15%で**最終ダメージ半減**。
                    //   2026-08-09 に 0 ダメージから緩和。 **isCrit は落とさない** ──
                    //   会心の成否は判定済みの事実で、 疲労は結果の目減りでしかないため。
                    float fatigueChance = GameLoop.HopeSystem.GetFatigueChance(GameLoop.GameManager.Instance?.Run);
                    if (fatigueChance > 0f && GameLoop.GameRng.Chance(fatigueChance, "combat.fatigue", RngIdx(2)))
                    {
                        float fm = GameLoop.HopeSystem.FatigueDamageMultiplier;
                        totalDmg = totalDmg > 0 ? Math.Max(1, Mathf.RoundToInt(totalDmg * fm)) : totalDmg;
                        fixedDmg = fixedDmg > 0 ? Math.Max(1, Mathf.RoundToInt(fixedDmg * fm)) : fixedDmg;
                        Debug.Log($"[希望] 疲労: 最終ダメージ半減 → 主{totalDmg} 固{fixedDmg}");
                    }

                    // 大穴の異常現象「朱の雪」: 与ダメ -1 (主ダメから引く、 最低 0)
                    int crimsonDelta = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks
                        .CrimsonSnowDamageDelta(GameLoop.GameManager.Instance?.Run);
                    if (crimsonDelta < 0 && totalDmg > 0)
                    {
                        int before = totalDmg;
                        totalDmg = Math.Max(0, totalDmg + crimsonDelta);
                        if (totalDmg != before)
                            Debug.Log($"[CombatManager] 異常現象「朱の雪」: 与ダメ {before}→{totalDmg}");
                    }

                    result.mainDamage = winBase;
                    result.pursuitDamage = pursuitDmg;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // 敵にダメージ適用（メイン＋プレイヤー→敵固定）
                    enemyHP = Math.Max(0, enemyHP - totalDmg - fixedDmg);

                    // メタデバフ Lv9 鋼の皮膚: 敵の初回致命傷を1HPで耐える
                    if (enemyHP == 0
                        && !metaLethalSurviveUsed
                        && MetaProgression.MetaDebuffApplicator.EnemySurvivesFirstLethal())
                    {
                        enemyHP = 1;
                        metaLethalSurviveUsed = true;
                        Debug.Log("[CombatManager] メタデバフ Lv9 鋼の皮膚: 敵が初回致命傷で1HPに踏みとどまった");
                    }

                    // 出血ダメージ
                    if (ctx.enemyBleedStacks > 0)
                    {
                        int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - bleedDmg);
                    }

                    // 敵→プレイヤー固定ダメージ（TailStrike/Hellfire等）
                    if (ctx.fixedDamageToPlayer > 0)
                    {
                        int fixedTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                            ctx.fixedDamageToPlayer, GameLoop.GameManager.Instance?.Run, playerHP);
                        playerHP = Math.Max(0, playerHP - fixedTaken);
                    }

                    // オーバーダメージ計算（蝕夜スキル用）
                    if (enemyHP == 0 && totalDmg > 0)
                        ctx.overDamageAccumulated = totalDmg + fixedDmg;

                    // 貪欲のダイス/吸血: 与えたダメージの一定割合を回復（HealPlayer が負傷/回復封印を適用）
                    if (ctx.lifestealPct > 0f && totalDmg > 0)
                    {
                        int ls = Mathf.CeilToInt(totalDmg * ctx.lifestealPct);
                        if (ls > 0) HealPlayer(ls);
                    }

                    // シールドバッシュ: 与ダメの一定割合をシールド化（healShieldReduction を適用）
                    if (ctx.shieldOnWinPct > 0f && totalDmg > 0)
                    {
                        int sh = Mathf.CeilToInt(totalDmg * ctx.shieldOnWinPct) - ctx.healShieldReduction;
                        if (sh > 0)
                        {
                            ctx.consShield += sh;
                            ctx.shieldGainedTotal += sh;
                        }
                    }

                    // === Scratch計算 ===
                    // 脅威システム: ロール勝利でも勝ち幅が脅威に満たない分を削りダメージとして受ける
                    //   scratch += max(0, 脅威 − 勝ち幅)。 大差勝ち(diff≥脅威)なら0。
                    // 2026-06-21: 敵がプレイヤー攻撃で死亡した場合は削り発動しない (削り切ったのに直後の脅威で死ぬ理不尽を解消)
                    if (ctx.enemyThreat > 0 && enemyHP > 0)
                        ctx.scratchDamage += Math.Max(0, ctx.enemyThreat - diceDiff);
                    ctx.scratchDamage = BattleModifierManager.ApplyScratchModifiers(ctx, ctx.scratchDamage);
                    psm.FireTrigger(PassiveSkillTrigger.OnPreScratchDamage);
                    if (!ctx.nullifyScratchDamage && ctx.scratchDamage > 0 && enemyHP > 0)
                    {
                        int scratchTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                            ctx.scratchDamage, GameLoop.GameManager.Instance?.Run, playerHP);
                        playerHP = Math.Max(0, playerHP - scratchTaken);
                    }
                    result.scratchDamage = (ctx.nullifyScratchDamage || enemyHP <= 0) ? 0 : ctx.scratchDamage;

                    // 敵側PostDealDamageトリガー
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostReceiveDamage);
                }
                else
                {
                    // 敵が勝利 → プレイヤーにダメージ
                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreDealDamage);

                    // 脅威システム: ロール敗北時の被ダメは脅威を下回らない。
                    //   被ダメ基礎 = max(ダイス差, 脅威)。 ボスの攻撃力は敵スタンスの高火力倍率(StanceAtkMult)で表現する。
                    int lossBase = Math.Max(mainDmg, ctx.enemyThreat);
                    // 旧パイプライン側の敵攻撃。 相互攻撃モデル側と同じく会心判定を飛ばす。
                    var (totalDmg, fixedDmg, isCrit) = psm.ProcessDamage(
                        lossBase, 0, 0f, attackerIsEnemy: true);

                    result.mainDamage = lossBase;
                    result.pursuitDamage = 0;
                    result.totalDamage = totalDmg;
                    result.fixedDamage = fixedDmg;
                    result.isCritical = isCrit;

                    // 被ダメージ修飾チェーン（順序厳守。詳細は ApplyLossDamageModifiers 参照）
                    totalDmg = ApplyLossDamageModifiers(totalDmg, floorMod, ctx);

                    // 2026-06-28: 仕込み刃 (暗殺者スターター): 次のダイス敗北で被ダメ無効化 +
                    // 同値を軽減不能で敵に返す。 1 ロール限定 → 発火後 disarm。
                    if (ctx.daggerArmed && totalDmg > 0)
                    {
                        int reflect = totalDmg;
                        Debug.Log($"[仕込み刃] 敗北ダメ {totalDmg} を無効化 + 軽減不能 {reflect} で反射");
                        totalDmg = 0;
                        enemyHP = Math.Max(0, enemyHP - reflect);
                        ctx.daggerArmed = false;
                    }

                    // プレイヤーにダメージ適用（メイン＋敵→プレイヤー固定）
                    totalDmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        totalDmg, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - totalDmg);
                    ctx.playerDamageThisTurn += totalDmg; // 焦土用の被ダメ計測
                    // 支配率診断: メイン被ダメをソース帰属。 lastDamageCause=Judgment(断罪増幅) なら Judgment、 既定 Normal。
                    ctx.AddPlayerDamageSource(ctx.lastDamageCause, totalDmg);
                    if (ctx.fixedDamageToPlayer > 0)
                    {
                        int fixedTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                            ctx.fixedDamageToPlayer, GameLoop.GameManager.Instance?.Run, playerHP);
                        playerHP = Math.Max(0, playerHP - fixedTaken);
                        ctx.playerDamageThisTurn += fixedTaken;
                        ctx.AddPlayerDamageSource(ctx.lastDamageCause, fixedTaken);
                    }

                    // 2026-06-28: 痛覚遮断剤 (狂戦士スターター) post-clamp: メイン+固定ダメ後でも HP=1 を保証。
                    // ApplyLossDamageModifiers 内のメイン clamp と二重で防御 (fixedDamageToPlayer 経路カバー)。
                    if (ctx.painkillerArmedThisTurn && playerHP <= 0)
                    {
                        Debug.Log("[痛覚遮断剤] post-clamp: HP=1 復帰");
                        playerHP = 1;
                        ctx.playerCurrentHP = 1;
                    }

                    // 敗北時でも反撃・固定ダメージは敵に適用（Counter/Riposte等）
                    if (fixedDmg > 0)
                        enemyHP = Math.Max(0, enemyHP - fixedDmg);

                    // 出血ダメージ（敗北時も適用）
                    if (ctx.enemyBleedStacks > 0)
                    {
                        int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - bleedDmg);
                    }

                    // scratchは敗北時なし（メインダメージに含有）
                    result.scratchDamage = 0;

                    psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostDealDamage);
                }
            }
            else
            {
                // 引き分け: メインダメージなし、scratchなし
                result.mainDamage = 0;
                result.pursuitDamage = 0;
                result.totalDamage = 0;
                result.fixedDamage = ctx.fixedDamageToEnemy;
                result.isCritical = false;
                result.scratchDamage = 0;

                // 引き分け時も固定ダメージは双方に適用（蒼白の槍騎士: 軽減無視ダメ増幅を停戦協定にも乗せる）
                if (ctx.fixedDamageToEnemy > 0)
                {
                    int drawFixed = ctx.fixedDamageMultiplier > 1f
                        ? Mathf.CeilToInt(ctx.fixedDamageToEnemy * ctx.fixedDamageMultiplier)
                        : ctx.fixedDamageToEnemy;
                    enemyHP = Math.Max(0, enemyHP - drawFixed);
                }
                if (ctx.fixedDamageToPlayer > 0)
                {
                    int fixedTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        ctx.fixedDamageToPlayer, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - fixedTaken);
                }

                // 出血ダメージ（引き分け時も適用）。停戦協定ターンは他効果を抑止するためスキップ。
                if (!ctx.truceThisTurn && ctx.enemyBleedStacks > 0)
                {
                    int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                    enemyHP = Math.Max(0, enemyHP - bleedDmg);
                }
            }

            return FinishTurnCommon(result, ctx);
        }

        /// <summary>
        /// ターン終端の共通処理（旧 ExecuteTurn 後段から抽出・新旧パイプライン共用）。
        /// 死の宣告 → 慈悲の処刑 → HP同期 → 致死確定/影武者蘇生 → OnTurnEnd 系 →
        /// 敵スワップ → 時限効果 → フロア/異常現象 → 継続回復/シールド失効 →
        /// サドンデス決着 → 致死回復無効化 → 戦闘終了チェック。順序厳守。
        /// </summary>
        private TurnResult FinishTurnCommon(TurnResult result, CombatContext ctx)
        {
            var psm = PassiveSkillManager.Instance;
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;


            // 死の宣告チェック（敵スキル由来の即死ダメージ）
            if (ctx.fixedDamageToPlayer >= 999)
                playerHP = 0;

            // Λデバフ「慈悲の処刑」: 被弾後、HPが最大の5/10/15%以下なら即死。
            // combatLethalThisTurn 確定の前に処理し、ターン終了回復での蘇生を防ぐ。
            if (ctx.lambdaMercifulExecThreshold > 0f && playerHP > 0)
            {
                bool tookHit = ctx.playerDamageThisTurn > 0
                             || (ctx.scratchDamage > 0 && !ctx.nullifyScratchDamage)
                             || ctx.fixedDamageToPlayer > 0;
                if (tookHit && playerHP <= playerMaxHP * ctx.lambdaMercifulExecThreshold)
                {
                    Debug.Log($"[Λ] 慈悲の処刑: HP{playerHP}/{playerMaxHP} ≤ {ctx.lambdaMercifulExecThreshold:P0} → 即死");
                    playerHP = 0;
                }
            }

            // コンテキストにHP同期
            ctx.playerCurrentHP = playerHP;
            ctx.playerMaxHP = playerMaxHP;
            ctx.enemyCurrentHP = enemyHP;
            ctx.enemyMaxHP = currentEnemy.maxHP;

            // ラストスタンド: **combatLethalThisTurn の確定より前**。 ここを後ろへ動かすと
            //   「致死が確定した後で HP を戻す」形になり、 蘇生禁止の判定と噛み合わない。
            TryLastStandRevive(ctx);

            // 戦闘ダメージ（メイン/固定/scratch/死の宣告）でこのターンに致死へ至ったか。
            // これ以降のターン終了回復(活力/継続回復/剣鎧等)で蘇生させないための確定フラグ。
            // 天命/深淵は被ダメを上限化してHPを1〜2残すため（HP=0にならず）ここでは false。
            bool combatLethalThisTurn = playerHP <= 0;

            // ターン終了トリガー
            psm.FireTrigger(PassiveSkillTrigger.OnTurnEnd);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnEnd);

            // 覚者連戦: 敵パッシブが予約した SwapEnemy を perspective 復帰後に実行
            if (!string.IsNullOrEmpty(ctx.pendingEnemySwapId))
            {
                string swapId = ctx.pendingEnemySwapId;
                string swapLabel = ctx.pendingEnemySwapLabel;
                ctx.pendingEnemySwapId = null;
                ctx.pendingEnemySwapLabel = null;
                SwapEnemy(swapId, swapLabel);   // 計測は SwapEnemy 内で一元化
                // 敵HP は SwapEnemy で新形態の MaxHP に再設定済み。
                // 戦闘継続のため enemyHP <= 0 判定を回避する目的で SyncHP しなおす
                SyncHPFromContext(ctx);
            }

            // (〈不完全な修復〉の充電半減は 2026-09-14 に撤去。 Optimal 方策は充電を
            //   リロールにしか使っておらず、 半減しても毎ターン 1 回は回せるので効かなかった。
            //   罰は回復量へ移した ── GateFlaws.ApplyRepairPenalty / HealPlayer。)

            // ターン終了系時限効果（中毒等。適用のみ、消費は戦闘終了時）
            EventSystem.TimedEffects.TimedEffectManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（ターン終了時系。現状は該当なし、フック確保のため呼び出し）
            InventorySystem.PassiveItems.PassiveItemManager.OnTurnEnd(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // 〈不完全な修復〉(門・2026-09-14): 縁の直っていない身体は長く保たない。
            //   **軽減不可・毎ターン終了時**。 回復半減だけでは 1.3pt しか効かず
            //   (最終戦は回復薬で支える構造ではない)、 時間を軸にした罰へ差し替えた。
            {
                var drainRun = GameLoop.GameManager.Instance?.Run;
                int drain = GameLoop.GateFlaws.RepairDrainAmount(drainRun, playerMaxHP);
                if (drain > 0 && playerHP > 0)
                {
                    playerHP = Math.Max(0, playerHP - drain);
                    ctx.playerCurrentHP = playerHP;
                    Debug.Log($"[不完全な修復] 身体が保たない -{drain} (HP: {playerHP}/{playerMaxHP})");
                }
            }

            // フロアデバフ: 毎ターン自傷（6層 深淵の洗礼: -1）
            if (floorMod != null && floorMod.perTurnSelfDamage > 0 && playerHP > 0)
            {
                int dmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    floorMod.perTurnSelfDamage, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] フロアデバフ: 自傷-{dmg} (HP: {playerHP}/{playerMaxHP})");
            }

            // 大穴の異常現象: ターン終了時の累積効果 (鉄を溶かす太陽/崩れる地平/削る砂/間歇の崩落/燃える河/逆さ雷)
            var phenomenaRun = GameLoop.GameManager.Instance?.Run;
            var (pDelta, eDelta) = MapSystem.AbyssPhenomena.AbyssPhenomenonCombatHooks
                .ApplyTurnEnd(phenomenaRun, ctx.currentTurn, currentEnemy?.maxHP ?? 0);
            if (pDelta < 0 && playerHP > 0)
            {
                int dmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    -pDelta, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - dmg);
                ctx.playerCurrentHP = playerHP;
                Debug.Log($"[CombatManager] 異常現象: 自HP -{dmg} (HP: {playerHP}/{playerMaxHP})");
            }
            if (eDelta < 0 && enemyHP > 0)
            {
                enemyHP = Math.Max(0, enemyHP + eDelta);
                ctx.enemyCurrentHP = enemyHP;
                Debug.Log($"[CombatManager] 異常現象: 敵HP {eDelta} (HP: {enemyHP}/{currentEnemy?.maxHP})");
            }

            // ターン終了スキルによるHP変動をCombatManagerに反映（剣鎧等）
            SyncHPFromContext(ctx);

            // 消費: 継続回復（毎ターン終了時 +consRegen → consRegen--）。狂暴化中は封印。
            if (ctx.consRegen > 0 && playerHP > 0 && !ctx.healBlocked)
            {
                int regen = Math.Max(0, ctx.consRegen - ctx.healShieldReduction); // 天衣無縫減衰
                int heal = Math.Min(playerMaxHP - playerHP, regen);
                if (heal > 0) { playerHP += heal; ctx.playerCurrentHP = playerHP; ctx.healAppliedTotal += heal; }
                Debug.Log($"[CombatManager] 継続回復 +{heal} (次T {ctx.consRegen - 1})");
                ctx.consRegen--;
            }
            // 消費: シールド残ターン減算（-1=無制限 / 0で失効）
            if (ctx.consShieldExpireTurn > 0)
            {
                ctx.consShieldExpireTurn--;
                if (ctx.consShieldExpireTurn == 0)
                {
                    ctx.consShield = 0;
                    Debug.Log("[CombatManager] シールド失効");
                }
            }
            // 2026-07-21 消費: 攻撃バフ (cons_atk_*) の残ターン減算
            if (ctx.consDiceRollTurnsLeft > 0)
            {
                ctx.consDiceRollTurnsLeft--;
                if (ctx.consDiceRollTurnsLeft == 0)
                {
                    ctx.consDiceRoll = 0;
                    Debug.Log("[CombatManager] 攻撃バフ失効");
                }
            }
            // 2026-07-21 消費: 与ダメ倍率 (cons_dmg_*) の残ターン減算
            if (ctx.consDmgMultTurnsLeft > 0)
            {
                ctx.consDmgMultTurnsLeft--;
                if (ctx.consDmgMultTurnsLeft == 0)
                {
                    ctx.consDmgMultPct = 0;
                    Debug.Log("[CombatManager] 与ダメ倍率失効");
                }
            }

            result.playerHPAfter = playerHP;
            result.enemyHPAfter = enemyHP;
            turnLog.Add(result);

            OnTurnEnd?.Invoke(result);
            MetaProgression.Achievements.AchievementService.NoteTurnEnded(result);

            // ログ出力
            LogTurnResult(result);

            // 戦闘ダメージで致死に至っていたら、ターン終了回復で蘇生していても死亡を確定させる
            // （オーバーキル消失バグの修正：致死ダメージは回復で帳消しにできない）。
            if (combatLethalThisTurn && playerHP > 0)
            {
                Debug.Log($"[CombatManager] 戦闘致死確定: ターン終了回復による蘇生を無効化 (HP {playerHP}→0)");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                result.playerHPAfter = 0;
            }

            // 2026-06-28: 痛覚遮断剤 (狂戦士スターター): proc 後の攻撃終了時に死亡確定
            if (ctx.painkillerProcced && playerHP > 0)
            {
                Debug.Log($"[痛覚遮断剤] 強化攻撃終了 → 死亡 (HP {playerHP}→0)");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                ctx.painkillerProcced = false;
                result.playerHPAfter = 0;
            }

            // 7層ヴェスカ連戦: 敵 HP が 0 なら **戦闘終了より先に**次形態へ移す。
            //   〈連続実験〉は OnTurnEnd でしか発火しないため、 削り切りが OnTurnEnd より前に
            //   起きたターンは段移行が予約されず、 p1〜p3 を倒した時点で「7層クリア」に
            //   なっていた (実測: 完全クリア 197 件のうち 93 件 = 47% が p1〜p3 撃破)。
            //   固定ダメージ・虚空の刻み・反射など、 敵パッシブを経由しない撃破経路が複数あるため、
            //   パッシブの発火順に頼らずここで確定させる。
            if (enemyHP <= 0 && playerHP > 0) TryAdvanceVescaChain();

            // 2 体戦の締め。 SetSecondaryEnemy の不変条件 (2 体目の HP ≤ 1 体目) が成り立つ限り
            //   2 体目は必ず先に落ちているので、 ここは到達しない。 到達したら不変条件が破れている。
            if (enemyHP <= 0 && secondaryHP > 0)
            {
                Debug.LogWarning($"[2体戦] 不変条件違反: 1体目撃破時に2体目が残 {secondaryHP} ── 強制決着");
                secondaryHP = 0;
            }

            // 戦闘終了チェック
            if (playerHP <= 0 || enemyHP <= 0)
            {
                FinishCombat();
            }

            return result;
        }

        /// <summary>
        /// <summary>7 層ヴェスカの防御層。 正本: docs/GAME.md §13-4。
        ///
        /// 順序は **回避 → シールド吸収**。
        ///   - 〈解析演算〉の回避は **通常・固定・軽減不可を問わず全ダメージを消す**
        ///     （既存のメタ俊敏回避 EnemyDodgesFirstHit と同じ挙動に揃えてある）。
        ///   - シールドは通常ダメージから先に吸い、 余れば固定ダメージも吸う。
        ///     吸収量は反射（返しの盾/不落の城盾）の原資として記録する。
        /// </summary>
        private void ApplyVescaDefense(InventorySystem.PassiveSkills.CombatContext ctx,
                                       ref int dealtDmg, ref int dealtFixed)
        {
            if (ctx == null) return;

            // --- 解析演算: 回避 ---
            if (ctx.enemyDodgeChance > 0f && ctx.enemyDodgeCooldown <= 0
                && (dealtDmg > 0 || dealtFixed > 0)
                && GameLoop.GameRng.Chance(ctx.enemyDodgeChance, "combat.dodge", RngIdx(3)))
            {
                Debug.Log($"[解析演算] 回避 {ctx.enemyDodgeChance:P0} 成功 — 与ダメ {dealtDmg}+{dealtFixed} を無効化"
                        + " (次ターンは回避不能)");
                dealtDmg = 0; dealtFixed = 0;
                ctx.enemyDodgeCooldown = 2;   // 今ターン分 + 次ターン分
                return; // 回避したのでシールドは減らない
            }

            // --- 天与の盾: このターンの与ダメは半分 ---
            //   完全無効だと攻撃端子が死に札になり、 手番ごと消えるのと変わらなかった。
            //   半減なら「それでも押し切る」判断が残る。 端数は切り上げ (0 にはしない)。
            if (ctx.vescaHalvesDamageThisTurn && (dealtDmg > 0 || dealtFixed > 0))
            {
                int beforeDmg = dealtDmg, beforeFixed = dealtFixed;
                dealtDmg   = (dealtDmg   + 1) / 2;
                dealtFixed = (dealtFixed + 1) / 2;
                Debug.Log($"[天与の盾] 与ダメ {beforeDmg}+{beforeFixed} → {dealtDmg}+{dealtFixed} (半減)");
            }

            // --- シールド吸収 ---
            if (ctx.vescaShield <= 0) return;
            int absorbed = 0;
            int take = Math.Min(ctx.vescaShield, dealtDmg);
            dealtDmg -= take; ctx.vescaShield -= take; absorbed += take;
            if (ctx.vescaShield > 0 && dealtFixed > 0)
            {
                int takeF = Math.Min(ctx.vescaShield, dealtFixed);
                dealtFixed -= takeF; ctx.vescaShield -= takeF; absorbed += takeF;
            }
            if (absorbed > 0)
            {
                ctx.vescaShieldAbsorbedThisTurn += absorbed;
                Debug.Log($"[ヴェスカ] シールドが {absorbed} 吸収 (残 {ctx.vescaShield})");
            }
        }

        /// <summary>7 層ヴェスカの攻撃時効果。 敵攻撃の解決直後に呼ぶ。 正本: docs/GAME.md §13-4。
        ///   - 〈裂傷の刃心/殺戮〉大出血の付与。 **解除不可・減衰しない・4 連戦を跨ぐ**ため、
        ///     実際の毎ターンダメージは RelicScholar の OnTurnStart 側で処理する。
        ///   - 〈天与の剣〉最大 HP の削り。
        ///   - 〈処刑人の烙印〉攻撃終了時に HP が最大の 20% 以下なら 9999 の軽減不可ダメージ。
        /// </summary>
        private void ApplyVescaOnHitEffects(InventorySystem.PassiveSkills.CombatContext ctx, int takenApplied)
        {
            if (ctx == null) return;

            // **付随する新規状態異常は「実際に通ったか」で判定する**。
            //   パリィ廃止 (ADR-0010) 前は PERFECT の受け流しを別枠で見ていたが、
            //   役の〈大階〉〈相殺〉は被ダメを 0 にするので takenApplied == 0 で自然に弾かれる。
            //   既に溜まったスタックの tick と〈裂け目〉の自壊は通常どおり進む。
            if (ctx.vescaBleedOnHit > 0 && takenApplied > 0)
            {
                ctx.massiveBleedStacks += ctx.vescaBleedOnHit;
                Debug.Log($"[大出血] +{ctx.vescaBleedOnHit} (計 {ctx.massiveBleedStacks}・解除不可/減衰なし)");
            }

            if (ctx.vescaMaxHpBiteOnHit > 0 && takenApplied > 0)
            {
                int bite = ctx.vescaMaxHpBiteOnHit;
                playerMaxHP = Math.Max(1, playerMaxHP - bite);
                playerHP = Math.Min(playerHP, playerMaxHP);
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
                var run = GameLoop.GameManager.Instance?.Run;
                if (run != null) { run.playerMaxHP = playerMaxHP; run.playerHP = playerHP; }
                Debug.Log($"[天与の剣] 最大HP -{bite} → {playerMaxHP}");
            }

            // 処刑は「攻撃終了時」判定なので、 このターンに被弾していなくても閾値を割っていれば発動する。
            if (ctx.vescaExecuteArmed && playerHP > 0
                && playerHP <= Mathf.CeilToInt(playerMaxHP * 0.20f))
            {
                Debug.Log($"[処刑人の烙印] HP {playerHP}/{playerMaxHP} が 20% 以下 — 9999 の軽減不可ダメージ");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                // 通常被ダメと区別する (§13-3 の死因診断は「総被ダメに占める支配率」で本命レバーを選ぶため、
                // 処刑を Normal に混ぜると敵攻撃値のレバーへ誤誘導される)
                ctx.lastDamageCause = InventorySystem.PassiveSkills.DeathCause.Other;
            }
        }

        /// ADR-0009 相互攻撃パイプライン（8段ターン構造）。UseMutualAttackPipeline=true で
        /// ExecuteTurn の代わりに走る。ロール勝負は存在せず、毎ターン
        /// 自攻撃(武器+攻撃端子出目) → 敵攻撃(基礎×段階倍率+β×ロール − ブロック) を解決する。
        /// 勝敗トリガーは「収支」(与ダメ−被ダメ) に再定義され**解決後**に発火する。
        ///
        /// v1 の未実装/暫定 (W7 後段で解消。ADR-0009 / HANDOFF-OPUS47 W7 実装メモ参照):
        ///   - 柱5 電力経済は充電獲得の記録のみ (accumulatedValues["mutualCharge"])。消費/停電なし。
        ///   - 特殊端子なし (Attack/Block/Charge の 3 種)。
        ///   - 異常現象のダイス改変 (鐘/正午/蝕夜)・影の代償・コルヴェンの憤怒は未接続。
        ///   - ボス固有機構 (サドンデス/コントラタック) 未移植 → boss6 は旧法で計測する。
        ///   - リロール自動ポリシーは旧目的関数 (ロール勝率) のまま。
        ///   - ProcessPostRoll 中の playerWonRoll はロール比較値 (収支確定前) — 出目参照系は
        ///     従来どおり、勝敗参照系のみ解決後の再発火で拾う。
        /// </summary>
        private TurnResult ExecuteTurnMutual()
        {
            var psm = PassiveSkillManager.Instance;
            var ctx = psm.Context;

            // 偽の商人: 規定ターン数を終えても未決着なら逃走 (旧法と同一)
            if (fleeAfterTurns > 0 && ctx != null && ctx.currentTurn >= fleeAfterTurns
                && playerHP > 0 && enemyHP > 0)
            {
                MetaProgression.Achievements.AchievementService.NoteEnemyEscaped(currentEnemy?.id);
                Debug.Log($"[CombatManager] 偽の商人: {fleeAfterTurns}ターン経過 → 逃走（戦闘強制終了）");
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = playerHP,
                    enemyHPAfter = enemyHP,
                };
            }

            // --- 0. ターン開始処理 ---
            psm.BeginTurn();

            // 装備維持費 (2026-08-15): **所持パッシブ 10 個につき充電 1** を毎ターン消費する。
            //   §7-3 の仕様として決まっていたが実装されていなかった。
            //   狙い: 充電の収支が 1ターン 収入 4.04 / 支出 1.69 と**2.4 倍余っており**、
            //   余剰 2.35 は戦闘終了時に捨てられていた。 リロールが安いのではなく
            //   **使い道が無い**のが実態で、 結果として〈極〉を狙う刻みリロールが実質無料だった。
            //   ビルドが太るほど維持費が増えるので、 極を連発する厚いビルドほど原資が減る。
            //   **払えなければあるだけ払う**（罰は与えない）。 上限を割ることによる
            //   過充電型パッシブの失効が、 それ自体の重みになる。
            {
                // [凍結 2026-08-15] 10 個につき 1 は重すぎた ── 踏み倒し 35.8%、
                //   7層クリア 25.1% → 16.2% (−8.9pt) に対し 極は 12.7 → 8.06 回/ラン (−37%) のみ。
                //   極はリロール量に線形にしか反応しないので、 経済で殴っても届かない。
                //   PassiveUpkeepPer を 0 以外に戻せば復活する。
                const int PassiveUpkeepPer = 0;
                var upRun = GameLoop.GameManager.Instance?.Run;
                int owned = upRun?.ownedPassiveItems?.Count ?? 0;
                int upkeep = PassiveUpkeepPer > 0 ? owned / PassiveUpkeepPer : 0;
                if (upkeep > 0 && ctx != null)
                {
                    int pay = Math.Min(upkeep, ctx.GetCharge());
                    if (pay > 0) ctx.ConsumeCharge(pay);
                    PassiveUpkeepPaid += pay;
                    PassiveUpkeepDue  += upkeep;
                }
            }
            // T4-E〈最後の審判〉: ラン累計ターンの刻限。 **ターン開始時に清算する**。
            ApplyFinalJudgment();
            // 刻限の死にもラストスタンドは効く。 ここを飛ばすと「ターン開始で死んだ時だけ
            //   蘇生しない」という、 死因でしか区別できない不整合になる。
            TryLastStandRevive(ctx);
            if (playerHP <= 0)
            {
                // 刻限で力尽きた。 強制決着と同じ経路で閉じる (放置すると戦闘が続く)。
                if (ctx != null) ctx.playerCurrentHP = 0;
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx != null ? ctx.currentTurn : 0,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                };
            }

            // 安全弁: 300T 超で強制決着 (旧法と同一)
            const int kHardTurnCap = 300;
            if (ctx != null && ctx.currentTurn > kHardTurnCap)
            {
                Debug.LogWarning($"[CombatManager] 戦闘ターン上限超過 ({ctx.currentTurn} > {kHardTurnCap}) — 強制決着（プレイヤー敗北）");
                playerHP = 0;
                ctx.playerCurrentHP = 0;
                FinishCombat();
                return new TurnResult
                {
                    turnNumber = ctx.currentTurn,
                    playerWon = false,
                    isDraw = false,
                    playerHPAfter = 0,
                    enemyHPAfter = enemyHP,
                };
            }

            // 消費アイテム持続効果の毎ターン再適用 + 死因タグリセット (旧法と同一)
            if (ctx != null)
            {
                if (ctx.consCritPct > 0f)
                {
                    ctx.critRatePctAdd += ctx.consCritPct;
                    DmgSourceDiag.Note("消費〈会心率〉", DmgSourceDiag.CritRate, ctx.consCritPct);
                }
                if (ctx.consFlatReduce > 0) ctx.playerFlatDamageReduction += ctx.consFlatReduce;
                ctx.lastDamageCause = InventorySystem.PassiveSkills.DeathCause.Normal;
            }

            psm.FireEnemyTrigger(PassiveSkillTrigger.OnTurnStart);
            SyncHPFromContext(ctx);

            // フロアデバフ: 敵の毎ターンHP回復 (旧法と同一)
            var floorMod = GameLoop.GameManager.Instance?.ActiveModifier;
            if (floorMod != null && floorMod.enemyPerTurnHeal > 0 && enemyHP > 0)
            {
                int heal = Mathf.Min(currentEnemy.maxHP - enemyHP, floorMod.enemyPerTurnHeal);
                if (heal > 0)
                {
                    enemyHP += heal;
                    ctx.enemyCurrentHP = enemyHP;
                    Debug.Log($"[CombatManager] フロアデバフ: 敵HP+{heal} ({enemyHP}/{currentEnemy.maxHP})");
                }
            }

            // 2 体戦: このターンに 1 体目が受けた削りを 2 体目へ丸ごと写すための基準点。
            //   **回復の後・ダメージの前**に取る。 差分で採るのは、 通常ダメージ・固定ダメ・出血・
            //   反射・反撃固定ダメと**削る経路が 5 本ある**ため ── 個別に足すと必ずどれかを取り落とす。
            int primaryHpBeforeDamage = enemyHP;

            int enemyExtraDice = (int)ctx.GetAccumulated("extraDice");
            ctx.accumulatedValues["extraDice"] = 0;
            // [計装] BossCombatTrace 用。 TurnResult には与ダメ/被ダメが無いので自前で持つ。
            int traceAtk = 0, traceBlock = 0, traceThrough = 0;
            int traceEBase = 0, traceERoll = 0; float traceEsc = 1f;
            int traceRaw = 0, traceBonus = 0, traceBossBonus = 0;

            // --- 1. 敵ロール (スタンスなし = ADR-0005 は superseded) ---
            int actualPlayerDiceCount = playerDiceCount;
            int[] enemyDice = RollEnemyAttack(currentEnemy, enemyExtraDice, false, 0f);
            int actualEnemyDiceCount = enemyDice.Length;

            // --- 3. 自ロール ---
            //   出目パーツ (2026-08-17): 値だけでなく **どの面に止まったか** を持ち回る。
            //   以降のロール書き換え (見放された運 / 避雷針 / リロール / レイピア) でも
            //   playerFaceIdx を必ず同期すること ── 忘れると別の面の効果が発火する。
            int[] playerFaceIdx;
            int[] playerDice = RollDice(actualPlayerDiceCount, playerDiceMax, ctx.equippedDiceFaces, out playerFaceIdx);
            NoteFirstRoll(actualPlayerDiceCount, ctx, playerDice);

            // 挑戦デバフ 軸9〈見放された運〉: ダイスを N 本 1 に潰す。
            //   T1 = 1T目に 1 本 / T2 = 1T目に 2 本 / T3 = 全ターン 1 本。
            //   潰す対象は **出目が大きいものから** ── ランダムだと元から低い目を潰すだけの
            //   無害なターンが混ざり、 Tier ごとの重みが安定しない。
            int forsaken = MetaProgression.MetaDebuffApplicator.GetForsakenLuckDiceCount(CurrentCombatTurn);
            for (int k = 0; k < forsaken && k < playerDice.Length; k++)
            {
                int best = -1, bestVal = 0;
                for (int i = 0; i < playerDice.Length; i++)
                    if (playerDice[i] > bestVal) { bestVal = playerDice[i]; best = i; }
                if (best < 0 || bestVal <= 1) break;
                playerDice[best] = 1;
                // 出目パーツ: **値を上書きする効果は、そのダイスを面から降ろす**。
                //   潰された 1 はもうその面ではないので、 パーツ効果も発火しない。
                //   (加算する〈鋳造口〉は面が変わらないので添字を保つ ── 上書きと加算で扱いが違う)
                if (playerFaceIdx != null && best < playerFaceIdx.Length) playerFaceIdx[best] = -1;
                Debug.Log($"[見放された運] 出目 {bestVal} → 1 (T{CurrentCombatTurn})");
            }

            // 特殊端子〈鋳造口〉(§6-5): 前ターンまでに蓄積した恒常の出目 +N。
            //   **1 固定系デバフの後に置く** ── 先に足すと〈見放された運〉が潰す対象が
            //   底上げ後の値になり、 デバフと端子が互いを打ち消し合って両方無意味になる。
            {
                int foundry = (int)ctx.GetAccumulated(FoundryBonusKey);
                if (foundry > 0)
                    for (int i = 0; i < playerDice.Length; i++) playerDice[i] += foundry;
            }

            // 7層ヴェスカ〈天与の指輪〉: 3 以上の出目をすべて 1〜2 へ再抽選する。
            //   全出目 1 は本数ぶんの手が完全に死に、 その前の「行動不可」と重さが変わらなかった。
            //   再抽選なら低いなりに幅が残り、 どこへ挿すかの判断が生きる。
            if (ctx.playerHighDiceCrushed)
            {
                int crushed = 0;
                for (int i = 0; i < playerDice.Length; i++)
                    if (playerDice[i] >= 3)
                    {
                        playerDice[i] = GameLoop.GameRng.Range(1, 3, "vesca.divineRing", RngIdx(i));
                        crushed++;
                    }
                Debug.Log($"[天与の指輪] 3 以上の出目 {crushed} 本を 1〜2 へ再抽選 ({playerDice.Length} 本中)");
            }
            // 7層ヴェスカ〈落雷の避雷針〉: ダイス 1 本 (ランダム) を **そのダイスの最小面**に固定。
            // どの目が潰れるか読めない圧にするためランダム選択 (§13-4)。
            // 2026-08-16: 固定値 1 → 最小面へ。 面パーツ等で面配列が 1 始まりでないダイスに
            //   1 を書き込むと、 **そのダイスが出せない目**を作ってしまう。
            else if (ctx.lightningRodArmed && playerDice.Length > 0)
            {
                int minFace = 1;
                var faces = ctx.equippedDiceFaces;
                if (faces != null && faces.Length > 0)
                {
                    minFace = faces[0];
                    for (int i = 1; i < faces.Length; i++) if (faces[i] < minFace) minFace = faces[i];
                }
                int idx = GameLoop.GameRng.Range(0, playerDice.Length, "combat.lightning", RngIdx(4));
                Debug.Log($"[落雷の避雷針] ダイス{idx + 1} を {playerDice[idx]} → {minFace} (最小面) に固定");
                playerDice[idx] = minFace;
                // 上書き系なので面から降ろす (見放された運と同じ扱い)。
                if (playerFaceIdx != null && idx < playerFaceIdx.Length) playerFaceIdx[idx] = -1;
            }

            // --- 4. リロール (ADR-0010: 充電消費・方策注入・何度でも払える限り) ---
            //   **予告を先に組んでから振り直す。** 敵の攻撃値が見えていないと
            //   「何を残すか」を決められず、 完全情報テレグラフ (柱3) の意味が消える。
            //   ここでは敵ダイスの素の合計で予告を作る ── 敵ダイスはこの後変化しないので、
            //   パッシブによる enemyDiceTotal 補正のぶんだけ近似になる。
            // **リロールより前に ctx へ載せる。** リロール方策は
            //   「T1 の面に止まったダイスは振れない」を見て予算を組むので、
            //   後で代入すると方策が前ターンの添字を読む。
            //   RerollPhase は配列を **その場で** 書き換えるので、 参照を先に渡せば同期は保たれる。
            ctx.playerDiceFaceIdx = playerFaceIdx;

            RerollPhase(ctx, playerDice, enemyDice, playerDiceMax, playerFaceIdx);

            // ロール時系効果 (出目書き換え系はここまで = LED に見える値が配線に使える値)
            ctx.playerDice = playerDice;
            ctx.enemyDice = enemyDice;
            ctx.playerDiceMax = playerDiceMax;
            EventSystem.TimedEffects.TimedEffectManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);
            InventorySystem.PassiveItems.PassiveItemManager.OnRoll(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            // メタバフ: ダイス合計値補正 (旧法と同一)
            int metaDiceBonus = ctx.rollPurity ? 0 : MetaProgression.MetaBuffApplicator.GetDiceTotalBonus();
            int safety = metaDiceBonus * playerDice.Length;
            while (metaDiceBonus > 0 && safety-- > 0)
            {
                int minIdx = -1;
                for (int j = 0; j < playerDice.Length; j++)
                {
                    if (playerDice[j] >= playerDiceMax) continue;
                    if (minIdx < 0 || playerDice[j] < playerDice[minIdx]) minIdx = j;
                }
                if (minIdx < 0) break;
                playerDice[minIdx]++;
                metaDiceBonus--;
            }

            // LED演出
            if (ledManager != null)
            {
                ledManager.SetActiveDiceCount(actualPlayerDiceCount, actualEnemyDiceCount);
                ledManager.PlayRollingAnimation(playerDice, enemyDice, playerDiceMax, currentEnemy.EffectiveRollMax);
            }

            // パターン系パッシブ (ゾロ目/階段/全相異) + 合計確定
            psm.ProcessPostRoll(playerDice, enemyDice);
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostRoll);

            _fightPlayerRollSum += ctx.playerDiceTotal;
            _fightPlayerRollCount++;

            // --- 2. 予告 (柱3: 完全情報テレグラフ) ---
            // 〈冷却材〉(§6-5) の遅延を差し引いたターンで段階を引く。 予告にもそのまま出るので、
            // 「何ターン稼いだか」が盤面から読める (完全情報テレグラフ・柱3)。
            int escTurn = EscalationTurn(ctx);
            int enemyAtkValue = Escalation.EnemyAttackValue(currentEnemy, escTurn, ctx.enemyDiceTotal, ctx.currentTurn);
            // ADR-0009 D群: 敵攻撃値-N (呪縛 等の敵ダイス弱化系の転生先)
            enemyAtkValue = Math.Max(0, enemyAtkValue - ctx.mutualEnemyAttackReduction);
            // 2026-07-25 v6: 出血 r2 失血衰弱 (敵の出血 3 以上で攻撃 -2)
            enemyAtkValue = Math.Max(0, enemyAtkValue - MetaProgression.MetaBuffApplicator.GetBleedWeakenAttack(ctx.enemyBleedStacks));

            // 2 体戦: 2 体目の攻撃も**配線前に**確定させて予告へ載せる (柱3 完全情報テレグラフ)。
            //   解決時に振ると「見えていない一撃」ができ、 ADR-0009 の思想に正面から反する。
            //   ここで振った値を解決でもそのまま使う (二度振らない)。
            _secondaryAtkThisTurn = 0;
            if (secondaryEnemy != null && secondaryHP > 0)
            {
                var secDiceTele = RollEnemyAttack(secondaryEnemy, 0, false, 0f);
                int secTotalTele = 0;
                for (int i = 0; i < secDiceTele.Length; i++) secTotalTele += secDiceTele[i];
                _secondaryAtkThisTurn =
                    Math.Max(0, Escalation.EnemyAttackValue(secondaryEnemy, escTurn, secTotalTele, ctx.currentTurn));
            }

            var tele = new MutualTurnTelegraph
            {
                turn = ctx.currentTurn,
                enemyAttackValue = enemyAtkValue,
                secondaryAttackValue = _secondaryAtkThisTurn,
                escalationStage = Escalation.StageOf(escTurn),
                nextThresholdTurn = Escalation.NextThresholdTurn(escTurn),
                turnsToHeavy = Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn),
                heavyMul = currentEnemy?.heavyMul ?? 0f,
                enemyDice = enemyDice,
                enemyDiceTotal = ctx.enemyDiceTotal,

                // 7層ヴェスカ: 抽選結果を開示する (§6-2 完全情報 / §13-4)。
                // 遺物の攻撃強化は enemyAttackValue に既に含まれる (enemyDiceTotalBonus 経由・β圧縮後)。
                // ここで渡すのは攻撃値に乗らない性質だけ。
                drawnRelics = ctx.vescaDrawnRelics != null ? ctx.vescaDrawnRelics.ToArray() : null,
                enemyShield = ctx.vescaShield,
                enemyShieldReflectRate = ctx.vescaShieldReflectRate,
                enemyDodgeChance = ctx.enemyDodgeChance,
                enemyDamageTakenMul = ctx.enemyDamageTakenMultiplier,
                enemyHalvesDamageThisTurn = ctx.vescaHalvesDamageThisTurn,
                playerAttackPenalty = ctx.playerAttackPowerPenalty,
                playerHighDiceCrushed = ctx.playerHighDiceCrushed,
                playerBlockIgnored = ctx.playerBlockIgnored,
                blazeStacks = ctx.accumulatedValues.TryGetValue(
                        InventorySystem.PassiveSkills.Effects.BlazeBrand.StackKey, out var _bz)
                    ? (int)_bz : 0,
                blazePenalizesBlock = ctx.accumulatedValues.TryGetValue(
                        InventorySystem.PassiveSkills.Effects.BlazeBrand.ActiveKey, out var _bza)
                    && _bza > 0f,
                executeArmed = ctx.vescaExecuteArmed,
                // 〈綻び〉: このターン封印される端子。 **配線前に開示する** (柱3)。
                sealedTerminal = MetaProgression.MetaDebuffApplicator.GetSealedTerminal(
                    ctx.currentTurn, SpecialTerminals.EquippedLimit(GameLoop.GameManager.Instance?.Run) > 0 ? 4 : 3),
            };
            if (ctx.bossId == "boss_layer7_p4")
                BossDmgDiag.P4Turn(ctx.playerHighDiceCrushed, ctx.vescaExecuteArmed,
                                   ctx.vescaMaxHpBiteOnHit, ctx.vescaDrawnRelics);
            if (!string.IsNullOrEmpty(ctx.bossId) && ctx.bossId.StartsWith("boss_layer7"))
                BossDmgDiag.V7Turn(ctx.vescaDrawnRelics);
            Debug.Log($"[相互攻撃] T{tele.turn} 予告: 敵攻撃 {enemyAtkValue} (段階{tele.escalationStage}" +
                      $"{(tele.nextThresholdTurn > 0 ? $"・次段階T{tele.nextThresholdTurn}" : "・最終段階")})");
            if (tele.drawnRelics != null && tele.drawnRelics.Length > 0)
                Debug.Log($"[予告] 遺物学者: {string.Join(" / ", tele.drawnRelics)}"
                        + (tele.enemyShield > 0 ? $"  シールド{tele.enemyShield}" : "")
                        + (tele.enemyShieldReflectRate > 0f ? $"  反射×{tele.enemyShieldReflectRate:F1}" : "")
                        + (tele.enemyDodgeChance > 0f ? $"  回避{tele.enemyDodgeChance:P0}" : "")
                        + (tele.enemyHalvesDamageThisTurn ? "  【ダメージ半減】" : "")
                        + (tele.executeArmed ? "  【処刑】" : "")
                        + (tele.playerHighDiceCrushed ? "  【出目3以上を再抽選】" : "")
                        + (tele.playerBlockIgnored ? "  【ブロック無効】" : ""));

            // --- 5. 配線 (前ターン配線の自動適用 → ポリシー/UI による変更) ---
            if (mutualWiring == null || mutualWiring.Length != playerDice.Length)
            {
                var next = new DiceTerminal[playerDice.Length];
                for (int i = 0; i < next.Length; i++)
                    next[i] = (mutualWiring != null && i < mutualWiring.Length)
                        ? mutualWiring[i] : DiceTerminal.Attack; // 未配置は攻撃へ自動合流
                mutualWiring = next;
            }
            int[] mutualGhost = null;
            if (WiringPolicy != null)
            {
                var plan = WiringPolicy(playerDice, tele);
                if (plan.main != null && plan.main.Length == playerDice.Length)
                {
                    mutualWiring = plan.main;
                    mutualGhost = plan.ghost;
                }
            }

            // 特殊端子 (§6-5) の接続制限と、 挑戦デバフ〈不器用〉の端子上限を強制する。
            // **方策/UI を信用しない** ── 制限を破った配線が来たら攻撃へ落とす。
            SanitizeWiring(mutualWiring, playerDice.Length);
            // ゴーストも同様に検算する。 T3/T4 の面に止まっていないダイスのゴーストは剥がす。
            SanitizeGhost(ctx, mutualGhost, playerFaceIdx, playerDice.Length);

            int attackSum = 0, blockSum = 0, chargeDiceCount = 0;
            int attackDiceCount = 0, blockDiceCount = 0, chargeSum = 0;
            int specialSum = 0, specialCount = 0;
            for (int i = 0; i < playerDice.Length; i++)
            {
                switch (i < mutualWiring.Length ? mutualWiring[i] : DiceTerminal.Attack)
                {
                    case DiceTerminal.Block:   blockSum   += playerDice[i]; blockDiceCount++;  break;
                    case DiceTerminal.Charge:  chargeSum  += playerDice[i]; chargeDiceCount++; break;
                    case DiceTerminal.Special: specialSum += playerDice[i]; specialCount++;    break;
                    default:                   attackSum  += playerDice[i]; attackDiceCount++; break;
                }
            }
            int _diagAtkRaw = attackSum;

            // --- ゴースト接続 (出目パーツ T3/T4) ---
            //   **合計値にだけ乗せる。** 役判定に使う組は実体だけで作るので、
            //   ここで DiceCount を増やさない ── 増やすと端子調律メタの本数ボーナスや
            //   充電の本数系が二重取りになる。
            //   T4 が実体と同じ端子へ重ねたときだけ、 その端子に +3。
            if (mutualGhost != null)
            {
                for (int i = 0; i < playerDice.Length && i < mutualGhost.Length; i++)
                {
                    int g = mutualGhost[i];
                    if (g == WiringPlan.NoGhost) continue;
                    int add = playerDice[i];
                    bool stacked = i < mutualWiring.Length && (int)mutualWiring[i] == g;
                    if (stacked) add += GameLoop.DiceFaceParts.T4StackBonus;
                    switch ((DiceTerminal)g)
                    {
                        case DiceTerminal.Block:   blockSum   += add; break;
                        case DiceTerminal.Charge:  chargeSum  += add; break;
                        case DiceTerminal.Special: specialSum += add; break;
                        default:                   attackSum  += add; break;
                    }
                    if (stacked)
                        Debug.Log($"[出目パーツ T4] ダイス{i + 1} を同一端子へ重ね: "
                                + $"{(DiceTerminal)g} に +{playerDice[i]}+{GameLoop.DiceFaceParts.T4StackBonus}");
                }
            }
            // 端子調律メタバフ: **接続したダイス 1 本あたり** +N (2026-07-28 に「合計 +N」から変更)。
            // r3 で 3 本挿せば +9 ── 端子へ厚く配線するほど伸びるので、 §6-4 の配線判断と噛む。
            // 本数ベースなので「0 配線の端子に架空の加算が乗る」問題は構造的に起きない。
            int _diagAtkGhost = attackSum - _diagAtkRaw;
            attackSum += attackDiceCount * MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice();
            blockSum  += blockDiceCount  * MetaProgression.MetaBuffApplicator.GetBlockTerminalPerDice();

            // --- 特殊端子 (§6-5) ---
            //   端子調律メタの後・〈貫きの錐〉と充電付与の前に置く。
            //   〈完全防御〉のぶんも錐の貫通対象にしたいので、 錐より前でなければならない。
            if (specialCount > 0)
                ApplySpecialTerminal(ctx, playerDice, specialSum, specialCount,
                                     ref attackSum, ref blockSum, ref chargeSum);
            // 〈貫きの錐〉(7層ヴェスカ GOLD 遺物・2026-07-29 変更)。
            //   旧: ブロック配線を **丸ごと 0 にする**。 防御という選択肢が消えるため、
            //       予告があっても対処のしようがなく、 致死率が基準の 2.80 倍に達していた。
            //   新: **敵の攻撃値 1 につきブロックを 2 削る**。 貫通量が攻撃値に比例するので、
            //       厚く配線すれば残る ＝ 「守り切れるか」の計算問題になる。
            //   端子調律メタの本数ボーナスも貫通対象に含めるため、 加算の **後** に処理する。
            if (ctx != null && ctx.playerBlockIgnored && blockSum > 0)
            {
                int pierce = Math.Max(0, enemyAtkValue) * PierceAwlPerAttack;
                int before = blockSum;
                blockSum = Math.Max(0, blockSum - pierce);
                Debug.Log($"[貫きの錐] 敵攻撃{enemyAtkValue} × {PierceAwlPerAttack} = ブロック -{pierce} "
                        + $"({before} → {blockSum})");
            }
            // 〈不完全な転移〉(門を不完全に起動した罰・2026-09-14)。
            //   錐と同じ「ブロックを削る」枠だが、 <b>敵攻撃値ではなくブロック量に比例</b>する。
            //   厚く配線した分は必ず一定割合残るので、 守りという選択肢は消えない。
            //   錐の後に置く ── 両方掛かるときは「錐で削られた残り」へさらに貫通が乗る。
            if (blockSum > 0)
            {
                var gateRun = GameLoop.GameManager.Instance?.Run;
                int beforeGate = blockSum;
                blockSum = GameLoop.GateFlaws.ApplyTransferPierce(gateRun, blockSum);
                if (blockSum != beforeGate)
                    Debug.Log($"[不完全な転移] ブロック貫通 {beforeGate} → {blockSum}");
            }
            // 充電: **接続ダイスの出目合計**を獲得 (2026-07-28 に「個数」から変更)。
            //   旧: 1 本 = 1 充電。 だが消費側は 雷撃 3 / 火花 1 / 過充電閾値 7 と桁が合わず、
            //       雷撃を毎ターン撃つだけで 5 本中 3 本を充電に固定する必要があった。
            //   「屑目の定席」は端子固定の性質ではなくなり、 特殊端子〈蓄電池〉(1本あたり+2 を
            //   出目に上乗せ = 出目1→3 / 出目6→8) を買った者だけの性質へ移る。 §6-5。
            if (chargeSum > 0) ctx.AddCharge(chargeSum);

            // 遺物 (§15-5): 刻印〈ブロック端子に未配線〉の判定材料。 **配線集計の直後に確定させる**。
            ctx.blockWiredThisTurn = blockDiceCount > 0;
            // 盾家系〈反攻〉が読む「守った量」。 **貫通 (playerBlockIgnored) を差し引いた後の値**を渡す
            //   ── 実際に受け止めた分だけが反撃になる。 ここは解決 (step6) より前なので
            //   OnPostRoll / OnPreDealDamage の両方から安全に読める。
            ctx.blockSumThisTurn = blockSum;

            // 遺物 (§15-5) 充電の毎ターン成分 (段4 以上)。 開幕成分は通常戦・毎ターン成分はボス戦で効く。
            // 充電端子へ 1 本も挿していないターンにも入る (「毎ターン」なので配線に依存しない)。
            {
                var relicRun = GameLoop.GameManager.Instance?.Run;
                int rct = MetaProgression.Relics.RelicApplicator.GetChargePerTurn(relicRun, ctx);
                if (rct > 0) ctx.AddCharge(rct);
            }

            // --- 5b. 役 (ADR-0010) ---
            //   手札役 / 端子役 / 配線役 を判定し、 方策が「切る」と決めた役だけを適用する。
            //   **1 戦闘 1 役 1 回・発動は任意**。 オーバーロード (タイミング判定) の後継。
            var roleOutcome = ResolveRoles(ctx, playerDice, mutualWiring, tele,
                                           ref attackSum, ref blockSum, enemyAtkValue);
            // 次ターンへ持ち越す分は nextTurnBuffs へ (BeginNewTurn が currentBuffs へ移す)。
            if (roleOutcome.freeRerollNextTurn) ctx.nextTurnBuffs[YachtRoleEffects.FreeRerollKey] = 1f;
            if (roleOutcome.enemyStunned)       ctx.nextTurnBuffs[YachtRoleEffects.EnemyStunKey]  = 1f;

            // 配線結果をパッシブから参照可能に (虚空 等が「攻撃端子接続0」を判定する)
            ctx.accumulatedValues["mutualAttackDiceCount"] = attackDiceCount;
            // 2026-07-29: ブロック本数も記録する。 〈停滞する時間〉が「攻撃0本」ではなく
            // 「守りに寄せたか (攻撃 ≦ ブロック)」で判定できるようにするため。
            // 攻撃に 1 本だけ挿して残りを全部ブロックへ回す ガン守りが、 旧条件では素通りしていた。
            ctx.accumulatedValues["mutualBlockDiceCount"] = blockDiceCount;

            var result = new TurnResult
            {
                turnNumber = ctx.currentTurn,
                playerDice = playerDice,
                enemyDice = enemyDice,
                playerDiceTotal = ctx.playerDiceTotal,
                enemyDiceTotal = ctx.enemyDiceTotal,
            };

            // --- 6. 解決: プレイヤー攻撃 → 敵攻撃 (自先制・撃破時は敵攻撃なし) ---
            //   atkBase = 武器素火力 + 配線攻撃合計 + パッシブボーナス (ダイス合計加算系の転生先)
            //   ProcessDamage 内の OnPreDealDamage/OnPreReceiveDamage 分岐は playerWonRoll に依存するため、
            //   自攻撃前に true、敵攻撃前に false へ toggle する (収支トリガーは §収支計算 で最終確定)
            ctx.playerWonRoll = true; ctx.playerLostRoll = false;
            // 2026-07-25: 消費 (cons_atk_*) の攻撃バフ。旧モデルでは consDiceRoll=勝敗判定のみだったが、
            // 相互攻撃モデルでは攻撃値に直接加算する (竜閃 rollPurity 中は無効)。
            int consAtk = ctx.rollPurity ? 0 : ctx.consDiceRoll;
            // 遺物 (§15-5) 攻撃+N。 パッシブ由来の mutualAttackBonus と同じ枠へ足す。
            int relicAtk = MetaProgression.Relics.RelicApplicator
                           .GetAttackBonus(GameLoop.GameManager.Instance?.Run, ctx);
            int atkBase = playerAttackPower + attackSum + ctx.mutualAttackBonus + consAtk + relicAtk;

            // 攻撃端子への配線が 0 本なら、**このターンは攻撃を行わない** (2026-07-28)。
            //   意図: 端子への配線を「威力を足す作業」ではなく **攻撃するかどうかの宣言** にする。
            //   「このターンは殴らない」が明確な選択になり、 §6-2 のジレンマがターン単位で立つ。
            //
            //   止めるもの: 武器の素火力・パッシブの攻撃ボーナス・消費アイテムの加算・**追撃**。
            //   止めないもの: 固定ダメージ枠 (fixedDamageToEnemy)。
            //     反撃・出血・毒は「自分が攻撃したこと」と独立に成立する別チャネルであり、
            //     さらに〈虚空〉(sword_t3/t4) は **攻撃端子 0 の時に** そこへ最大HP5% を積む札なので、
            //     ここを潰すと「攻撃しない」を選ぶための札そのものが死ぬ。
            bool skipAttack = attackDiceCount <= 0;
            if (skipAttack)
            {
                if (atkBase > 0 || ctx.pursuitDamage > 0)
                    Debug.Log($"[配線] 攻撃端子が空 → **攻撃を行わない** "
                            + $"(素火力{playerAttackPower} 込み {atkBase} / 追撃 {ctx.pursuitDamage} を破棄)");
                atkBase = 0;
                ctx.pursuitDamage = 0;
            }
            // 7層ヴェスカ〈麻痺毒の小瓶〉: このターンのプレイヤー攻撃力を減算 (0 で床)。
            // 武器 attackPower は LEG でも 4〜8 なので実効は「武器素火力を消す」。 配線出目の分は残る。
            if (ctx.playerAttackPowerPenalty > 0)
            {
                int before = atkBase;
                atkBase = Math.Max(0, atkBase - ctx.playerAttackPowerPenalty);
                Debug.Log($"[麻痺毒の小瓶] 攻撃力 {before} → {atkBase} (-{ctx.playerAttackPowerPenalty}, 0で床)");
            }
            // 〈不完全な起動〉(門を不完全に起動した罰・2026-09-14)。
            //   回路が繋がりきらず出力が出ない。 **atkBase への割合**で掛ける ──
            //   攻撃端子の合計だけに掛けると梃子が弱すぎた (atkBase 60 のうち端子は 17 で、
            //   端子 25% カットでも atkBase は 7% しか減らない)。
            //   NPC 帯同と同じ位置・同じ形 (フラット減算の後・臨界爆発の前)。
            //   ブロックには掛けない (掛けると〈不完全な転移〉と軸が重なる)。
            if (atkBase > 0)
            {
                var igRun = GameLoop.GameManager.Instance?.Run;
                int beforeIg = atkBase;
                atkBase = GameLoop.GateFlaws.ApplyIgnitionCut(igRun, atkBase);
                if (atkBase != beforeIg)
                    Debug.Log($"[不完全な起動] 出力不足 atkBase {beforeIg} → {atkBase}");
            }

            // 臨界爆発の予約消費 (前T meter が閾値到達で set された)
            if (ctx.rinkaiBurstActive)
            {
                // **atkBase に足さない (2026-08-03 修正)。**
                //   旧実装は atkBase へ加算していたため、 その後の会心 ×3.0 と
                //   outgoingDamageMultiplier が丸ごと乗り、 「flat 50」のつもりの数字が
                //   実効 150〜300 になっていた。 rinkaiCritOnBurst 持ちなら会心は確定なので
                //   ×3.0 が保証されてさらに悪い。
                //   §9.2 の「爆発 flat」という設計意図どおり、 倍率の乗らない
                //   fixedDamageToEnemy (軽減無視の固定ダメ枠) へ回す。
                ctx.fixedDamageToEnemy += ctx.rinkaiBurstDamage;
                if (ctx.rinkaiCritOnBurst) ctx.forceCritical = true;
                // 遺物 (§15-5) 臨界: 爆発時に **敵の現在HP** の N% を上乗せする。
                //   最大HP 比だと削っても威力が落ちず、 1 戦闘 8 回の爆発で合計が最大HP の
                //   180% に達して遺物 1 個でボスを 2 回殺せる量になった (§24)。
                //   現在HP 比なら自己減衰し、 撃破もできない (漸近するだけ)。
                //   **これも固定ダメ枠へ。** 旧実装は atkBase 経由だったので同じ二重取りをしていた。
                float rinPct = MetaProgression.Relics.RelicApplicator
                               .GetRinkaiCurrentHpPct(GameLoop.GameManager.Instance?.Run, ctx);
                int relicBurst = rinPct > 0f && ctx.enemyCurrentHP > 0
                               ? Mathf.FloorToInt(ctx.enemyCurrentHP * rinPct) : 0;
                if (relicBurst > 0) ctx.fixedDamageToEnemy += relicBurst;
                ctx.rinkaiBurstActive = false;
                Debug.Log($"[臨界爆発] 固定ダメ +{ctx.rinkaiBurstDamage}"
                    + (relicBurst > 0 ? $" ＋遺物 {relicBurst} (現在HP {ctx.enemyCurrentHP} の {rinPct:P0})" : "")
                    + (ctx.rinkaiCritOnBurst ? " ＋会心確定" : ""));
            }
            // [診断] この呼び出しだけが「プレイヤー勝利の主攻撃」。 段別計測はここでのみ積む。
            DiagWinAttack = true;
            var (dealtDmg, dealtFixed, isCrit) = psm.ProcessDamage(
                atkBase, ctx.pursuitDamage, playerCritBaseRate);
            int lbStage = GameLoop.GameManager.Instance?.Run?.limitBreakStage ?? 0;
            dealtDmg = ApplyWinDamageModifiers(dealtDmg, ref dealtFixed, ref isCrit, lbStage, psm, ctx);
            ctx.lastPlayerDamage = dealtDmg + dealtFixed;
            DiagWinAttack = false;
            // [計装] 与ダメ出どころ別の発動率。 攻撃した回だけ数える。
            if (atkBase > 0)
                DmgSourceDiag.Commit(ctx, isCrit, _diagKind == SkillDiag.Boss, playerCritBaseRate,
                                     psm.ActivePlayerSkillIds,
                                     GameLoop.GameManager.Instance?.Run?.ownedPassiveItems,
                                     DsWeaponAlsoOwnedFlag());

            // 与ダメージの内訳を記録する (診断用・§15-5 の単位較正を実機基準へ直すため)
            DamageBreakdown[0]++;
            DamageBreakdown[1] += playerAttackPower;
            DamageBreakdown[2] += attackSum;
            DamageBreakdown[3] += ctx.mutualAttackBonus;
            DamageBreakdown[4] += consAtk;
            DamageBreakdown[5] += relicAtk;
            DamageBreakdown[6] += atkBase;
            DamageBreakdown[7] += dealtDmg;
            if (isCrit) DamageBreakdown[8]++;
            DamageBreakdown[9] += dealtFixed;
            DamageBreakdown[10] += ctx.pursuitDamage;
            RecordFloorDamage(ctx, dealtDmg, dealtFixed);

            // 希望(ADR-0002) 疲労: 攻撃が15%で**最終ダメージ半減** (2026-08-09 に 0 ダメから緩和)。
            //   **isCrit は落とさない** ── 会心の成否は判定済みの事実で、 疲労は結果の目減り。
            float fatigueChance = GameLoop.HopeSystem.GetFatigueChance(GameLoop.GameManager.Instance?.Run);
            if (fatigueChance > 0f && GameLoop.GameRng.Chance(fatigueChance, "combat.fatigue", RngIdx(2)))
            {
                float fm = GameLoop.HopeSystem.FatigueDamageMultiplier;
                dealtDmg   = dealtDmg   > 0 ? Math.Max(1, Mathf.RoundToInt(dealtDmg   * fm)) : dealtDmg;
                dealtFixed = dealtFixed > 0 ? Math.Max(1, Mathf.RoundToInt(dealtFixed * fm)) : dealtFixed;
                Debug.Log($"[希望] 疲労: 最終ダメージ半減 → 主{dealtDmg} 固{dealtFixed}");
            }

            result.mainDamage = atkBase;
            result.totalDamage = dealtDmg;
            result.fixedDamage = dealtFixed;
            result.isCritical = isCrit;
            result.pursuitDamage = ctx.pursuitDamage;
            result.scratchDamage = 0; // scratch 廃止 (threat は基礎攻撃値へ転生)

            // 7層ヴェスカ: 〈解析演算〉の回避 → 〈真守/革の手甲/不落の城盾〉のシールド吸収 の順に通す。
            ApplyVescaDefense(ctx, ref dealtDmg, ref dealtFixed);
            result.totalDamage = dealtDmg;
            result.fixedDamage = dealtFixed;
            // [計測] ボス戦のみ、 プレイヤーの主攻撃と固定ダメを分けて実測する
            if (ctx != null && !string.IsNullOrEmpty(ctx.bossId))
            {
                BossDmgDiag.Record(ctx.bossId, currentEnemy != null ? currentEnemy.displayName : ctx.bossId,
                                   dealtDmg, dealtFixed);
                // [計測] 上振れヒットの内訳を実データで残す (平均の 10 倍が出る理由の特定用)
                if (BossDmgDiag.WantSpike(dealtDmg))
                {
                    BossDmgDiag.AddSpike(
                        $"{(currentEnemy != null ? currentEnemy.displayName : ctx.bossId)} T{ctx.currentTurn} "
                      + $"主{dealtDmg} 固{dealtFixed} | crit={isCrit} critMul={ctx.criticalMultiplier:F2} "
                      + $"outMul={ctx.outgoingDamageMultiplier:F2} 臨界burst={ctx.rinkaiBurstActive} "
                      + $"burstDmg={ctx.rinkaiBurstDamage} 脆弱={ctx.enemyVulnerabilityMultiplier:F2} "
                      + $"armorPen={ctx.armorPenPct:F2} 画竜={ctx.garyoProc} "
                      + $"atkDice={(int)ctx.GetAccumulated("mutualAttackDiceCount")} "
                      + $"pRoll={ctx.playerDiceTotal} 武器={GameLoop.GameManager.Instance?.Run?.equippedWeaponId}");
                }
            }

            // [計装] 7層 4 段の端子別ダイス配分。 p4 だけ実火力が 1/4.5 になる理由を
            //   「ブロックに本数を取られているのか」で切る。 与ダメも同じ行に載せて、
            //   本数比とダメージ比が一致するか (＝本数で説明しきれるか) を見る。
            if (ctx != null && !string.IsNullOrEmpty(ctx.bossId)
                && ctx.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix))
            {
                // **段の判定は ctx.bossId から取る。** VescaRelicPool.ResolveStage() は
                //   CombatManager.Instance.CurrentEnemy を読む実装で、 取れないと既定 1 を返すため
                //   p1 が過大・p4 が過小に偏る (実測: p4 の記録ターンが実際の 1/5 しか無かった)。
                //   ctx.bossId は SwapEnemy が形態ごとに更新している。
                GameLoop.RunChronicle.NotePhaseWiring(
                    ctx.bossId.EndsWith("_p4") ? 4 : ctx.bossId.EndsWith("_p3") ? 3
                        : ctx.bossId.EndsWith("_p2") ? 2 : 1,
                    (int)ctx.GetAccumulated("mutualAttackDiceCount"),
                    (int)ctx.GetAccumulated("mutualBlockDiceCount"),
                    attackSum, blockSum, dealtDmg + dealtFixed);
            }

            enemyHP = Math.Max(0, enemyHP - dealtDmg - dealtFixed);
            if (_diagKind == SkillDiag.Boss && currentEnemy != null)
            {
                var bd = SkillDiag.BossDmgOf(currentEnemy.id);
                bd[2] += dealtDmg; bd[3] += dealtFixed;
                if (dealtDmg > bd[7]) bd[7] = dealtDmg;
                if (atkBase > 0)
                {
                    bd[9] += 1; bd[10] += atkBase; if (isCrit) bd[11] += 1;
                    int metaPer = attackDiceCount * MetaProgression.MetaBuffApplicator.GetAttackTerminalPerDice();
                    bd[12] += playerAttackPower; bd[13] += attackSum - metaPer; bd[14] += metaPer;
                    bd[15] += ctx.mutualAttackBonus; bd[16] += consAtk; bd[17] += relicAtk;
                    bd[18] += attackDiceCount; bd[19] += playerDice.Length;
                    bd[20] += _diagAtkRaw; bd[21] += _diagAtkGhost;
                    bd[22] += attackSum - metaPer - _diagAtkRaw - _diagAtkGhost;
                    int mx = 0; for (int q = 0; q < playerDice.Length; q++) if (playerDice[q] > mx) mx = playerDice[q];
                    bd[23] += mx;
                }
            }

            // **敵の「被弾した」トリガーはここで焚く (2026-09-09 修正)。**
            //   相互攻撃モデルでは、 これより下の分岐が
            //     `else if (enemyHP > 0) { 敵の攻撃 … OnPostDealDamage }`
            //     `else               {                OnPostReceiveDamage }`
            //   という形で、 **敵が死んだターンにしか OnPostReceiveDamage が鳴らなかった**。
            //   このトリガーを聴いている敵パッシブは〈鏡映の応答〉(4層ボス) だけで、
            //   反射は「次ターン開始時」に着弾する設計なので、 死亡ターンに予約しても
            //   永久に不発。 実測: 2,460 戦で被弾イベント 1,809 回 (＝ほぼ死亡ターンのみ)、
            //   反射 11,632 を積んだのに被ダメは 1 も動かなかった。
            //   ADR-0009 移行時の配線漏れで、 鈍器の attackerIsEnemy 漏れと同型。
            if (dealtDmg + dealtFixed > 0)
            {
                ctx.finalDamage = dealtDmg + dealtFixed;   // 敵視点で「受けた量」
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostReceiveDamage);
            }

            // シールドが吸収した分の反射 (返しの盾 100% / 不落の城盾 200%)。
            // 吸収量が上限なので、 シールド値の 2 倍を超える反射は原理的に発生しない。
            if (ctx.vescaShieldAbsorbedThisTurn > 0 && ctx.vescaShieldReflectRate > 0f && playerHP > 0)
            {
                int reflect = Mathf.RoundToInt(ctx.vescaShieldAbsorbedThisTurn * ctx.vescaShieldReflectRate);
                if (reflect > 0)
                {
                    reflect = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        reflect, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - reflect);
                    ctx.playerCurrentHP = playerHP;
                    Debug.Log($"[ヴェスカ] シールド反射 {ctx.vescaShieldAbsorbedThisTurn}×{ctx.vescaShieldReflectRate:F1} = {reflect} ダメージ");
                }
            }

            // メタデバフ Lv9 鋼の皮膚 (旧法と同一)
            if (enemyHP == 0 && !metaLethalSurviveUsed
                && MetaProgression.MetaDebuffApplicator.EnemySurvivesFirstLethal())
            {
                enemyHP = 1;
                metaLethalSurviveUsed = true;
                Debug.Log("[CombatManager] メタデバフ Lv9 鋼の皮膚: 敵が初回致命傷で1HPに踏みとどまった");
            }

            // --- 役 (ADR-0010) の撃破系。 **メタデバフ〈鋼の皮膚〉より後に置く** ---
            //   どちらも「即勝利」「確定キル」を謳う役なので、 1HP 踏みとどまりに吸われると
            //   役の文面が嘘になる。 1 戦闘 1 回・極は 1/1296 級の頻度なので上書きを許す。
            if (roleOutcome.instantWin && enemyHP > 0)
            {
                // [2026-08-22] **通常敵の即勝利を廃し、 出目依存の割合削りへ一本化**。
                //   旧仕様は 1,1,1,1,1 でも 6,6,6,6,6 でも即勝利で、 出目の大小が無意味だった
                //   ── BOT が期待値を捨てて低い目で揃えに行き、 発動が想定の 21 倍になっていた。
                //   いまは 6 揃いなら通常敵の 120% ＝ 実質確殺、 1 揃いなら 20% の削りに留まる。
                //   ボスは係数が半分 (60%/10%) ── 「引けたら勝ち」の宝くじにはしない。
                int chunk = Mathf.CeilToInt(ctx.enemyMaxHP * (roleOutcome.yachtChunkPct / 100f));
                if (_diagKind == SkillDiag.Boss && currentEnemy != null)
                    SkillDiag.BossDmgOf(currentEnemy.id)[4] += Math.Min(chunk, enemyHP);
                enemyHP = Math.Max(0, enemyHP - chunk);
                Debug.Log($"[役] 極: 最大HP {ctx.enemyMaxHP} の {roleOutcome.yachtChunkPct}% "
                        + $"= {chunk} を軽減無視で削る → 残 {enemyHP}");
            }

            // 出血
            if (ctx.enemyBleedStacks > 0)
            {
                int bleedDmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                if (_diagKind == SkillDiag.Boss && currentEnemy != null)
                    SkillDiag.BossDmgOf(currentEnemy.id)[5] += Math.Min(bleedDmg, enemyHP);
                enemyHP = Math.Max(0, enemyHP - bleedDmg);
            }

            // --- 〈天与の才〉踏みとどまり (2026-08-16) ---
            //   まだ通過していない閾値 (最大HP の 66% / 33%) を跨ぐダメージは、
            //   その閾値ちょうどで止める。 通過の記録は同ターン末の DivineTalent.Execute が行う。
            //   **位置**: 通常ダメージ・固定ダメ・出血をすべて解決した後、 かつ役の確定キルより後。
            //   役〈極〉が決めた削りは踏みとどまりで巻き戻さない ──
            //   「軽減・シールドを無視して削る」という文面を守るため (鋼の皮膚と同じ扱い)。
            //   〈過不足なし〉の確定キルは 2026-08-22 に〈拮抗〉(与ダメ+50%) へリワークされ、
            //   通常のダメージ経路を通るようになったので、 ここでの特別扱いは無くなった。
            //   **致死打も止める。** `enemyHP > 0` で条件を切ってはいけない ──
            //   「閾値を割る一撃」を素通しすると、 火力が高いほど踏みとどまりが起きなくなり、
            //   この仕組みそのものが無意味になる。
            if (!roleOutcome.instantWin)
            {
                enemyHP = InventorySystem.PassiveSkills.Effects.DivineTalent.ClampToThreshold(
                    ctx, enemyHP, currentEnemy != null ? currentEnemy.maxHP : ctx.enemyMaxHP);
                ctx.enemyCurrentHP = enemyHP;
            }

            if (enemyHP == 0 && dealtDmg > 0)
                ctx.overDamageAccumulated = dealtDmg + dealtFixed;

            // 吸血 / シールドバッシュ (旧法の勝利分岐と同一)
            if (ctx.lifestealPct > 0f && dealtDmg > 0)
            {
                int ls = Mathf.CeilToInt(dealtDmg * ctx.lifestealPct);
                if (ls > 0) HealPlayer(ls);
            }
            if (ctx.shieldOnWinPct > 0f && dealtDmg > 0)
            {
                int sh = Mathf.CeilToInt(dealtDmg * ctx.shieldOnWinPct) - ctx.healShieldReduction;
                if (sh > 0)
                {
                    ShieldDiag.Note("与ダメのシールド化", sh);
                    ctx.consShield += sh;
                    ctx.shieldGainedTotal += sh;
                }
            }

            int takenApplied = 0;

            // 〈大束〉(前ターン成立) — このターン敵は攻撃しない。 予告は既に出ているので
            // 「見えている一撃を丸ごと踏み倒す」形になり、 予告の価値を壊さない。
            bool enemyStunnedThisTurn =
                ctx.currentBuffs.TryGetValue(YachtRoleEffects.EnemyStunKey, out float stunFlag) && stunFlag > 0f;

            if (enemyHP > 0 && enemyStunnedThisTurn)
            {
                Debug.Log($"[役] 大束: 敵は行動不能 (予告されていた攻撃 {enemyAtkValue} は不発)");
            }
            else if (enemyHP > 0)
            {
                // --- 敵攻撃: 基礎×段階倍率 + β×ロール − ブロック (下限0) → 被ダメ修飾チェーン ---
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreDealDamage);
                if (ctx != null && !string.IsNullOrEmpty(ctx.bossId))
                    BossDmgDiag.RecordTurn(ctx.bossId, currentEnemy != null ? currentEnemy.displayName : ctx.bossId);
                int lossBase = Math.Max(0, enemyAtkValue - blockSum);
                GuardDiag.Note(enemyAtkValue, enemyAtkValue, blockSum, lossBase);
                // ラン内のブロック実績。 **GuardDiag (バッチ累計) とは別に持つ** ──
                //   航行の危険度見積りが読むので、 ラン跨ぎの値を混ぜると決定性が壊れる。
                {
                    var brun = GameLoop.GameManager.Instance?.Run;
                    if (brun != null) { brun.blockSeenSum += blockSum; brun.blockSeenCount++; }
                }
                traceAtk = enemyAtkValue; traceBlock = blockSum; traceThrough = lossBase;
                traceEBase = currentEnemy != null ? currentEnemy.EffectiveBaseAttack : 0;
                traceERoll = ctx.enemyDiceTotal;
                traceEsc = currentEnemy != null
                    ? Escalation.GetMultiplier(currentEnemy.EffectiveEscalationProfile, escTurn) : 1f;
                traceRaw = ctx.rawEnemyDiceTotal;
                traceBonus = ctx.enemyDiceTotalBonus;
                traceBossBonus = ctx.bossDiceBonus;
                // [計装] 7層p4 だけ、 攻撃値の内訳をターン帯ごとに積む。
                //   p1〜p3 が 9T で 7 しか削らないのに p4 が 14T で 90 削る理由を、
                //   素火力 / ダイス合計 / 停滞 / 遺物 / ブロック貫通 に割って見る。
                if (ctx.bossId == GameLoop.BossIds.VescaFinal)
                    GameLoop.RunChronicle.NoteP4Turn(
                        ctx.currentTurn,
                        currentEnemy != null ? currentEnemy.EffectiveBaseAttack : 0,
                        ctx.enemyDiceTotal,
                        ctx.stagnationStacks * InventorySystem.PassiveSkills.Effects.StagnantTime.DicePerStack(ctx),
                        (int)ctx.GetAccumulated(InventorySystem.PassiveSkills.Effects.VescaRelicPool.KeyDiceBonusThisTurn),
                        enemyAtkValue, blockSum, lossBase, ctx.massiveBleedStacks);
                ctx.playerWonRoll = false; ctx.playerLostRoll = true; // ProcessDamage 内で OnPreReceiveDamage を発火させる
                // attackerIsEnemy: true ＝ 会心判定を飛ばす。 これが無いと敵の攻撃が
                //   **プレイヤーの**会心率で会心し、 **プレイヤーの** criticalMultiplier で
                //   増幅される (§6-10 敵会心の全廃は enemies.json の分子 0 だけでは担保できない)。
                var (takenDmg, counterFixed, _) =
                    psm.ProcessDamage(lossBase, 0, 0f, attackerIsEnemy: true);
                takenDmg = ApplyLossDamageModifiers(takenDmg, floorMod, ctx);
                if (ctx.nullifyAllDamage) takenDmg = 0; // 虚空 等: 双方ダメ0化

                // 役 (ADR-0010) の被ダメ側。 〈虚空〉と**同じ位置・同じ対象**（敵の直接攻撃パケット）に置く。
                //   fixedDamageToPlayer (毒・出血・反射) は別チャネルなので触らない ──
                //   ここを含めると「攻撃していない敵の継続ダメージまで役で消える」ことになる。
                if (roleOutcome.damageZero)
                {
                    if (takenDmg > 0) Debug.Log($"[役] 大階/相殺: 被ダメ {takenDmg} → 0");
                    takenDmg = 0;
                }
                else if (roleOutcome.damageHalf && takenDmg > 0)
                {
                    int before = takenDmg;
                    takenDmg = Math.Max(1, Mathf.RoundToInt(takenDmg * YachtRoleEffects.BlockTripleDamageMultiplier));
                    Debug.Log($"[役] 束(防): 被ダメ {before} → {takenDmg}");
                }

                // 仕込み刃 (暗殺者): 被ダメ無効化 + 同値反射 (収支ベースでは「被弾ターン」で発火)
                if (ctx.daggerArmed && takenDmg > 0)
                {
                    int reflect = takenDmg;
                    Debug.Log($"[仕込み刃] 被ダメ {takenDmg} を無効化 + 軽減不能 {reflect} で反射");
                    takenDmg = 0;
                    enemyHP = Math.Max(0, enemyHP - reflect);
                    ctx.daggerArmed = false;
                }

                takenDmg = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                    takenDmg, GameLoop.GameManager.Instance?.Run, playerHP);
                playerHP = Math.Max(0, playerHP - takenDmg);
                takenApplied = takenDmg;
                MetaProgression.MetaDebuffApplicator.NoteDamageTaken(
                    takenDmg, GameLoop.GameManager.Instance?.Run);
                ApplyBreakdownIfNeeded();
                // 〈長引く負傷〉: ラン中の累計被ダメを積む。 **最大HP の減少はここからは行わない** ──
                //   戦闘中に最大HP を削ると、 予告に無い形でその場の生死が変わる。 精算は戦闘終了時。
                if (takenDmg > 0)
                {
                    var lwAccRun = GameLoop.GameManager.Instance?.Run;
                    if (lwAccRun != null) lwAccRun.lingeringWoundDamageAccum += takenDmg;
                }

                // 特殊端子〈反攻〉(§6-5): このターン受けた最終ダメージのうち、
                //   接続合計値までを敵へ返す。 **受けた量が上限**なので、
                //   守り切ったターンは何も返らない ── 殴られる覚悟とセットの端子。
                {
                    int cap = (int)ctx.GetAccumulated(RiposteKey);
                    if (cap > 0 && takenDmg > 0)
                    {
                        int reflect = Math.Min(cap, takenDmg);
                        enemyHP = Math.Max(0, enemyHP - reflect);
                        ctx.enemyCurrentHP = enemyHP;
                        Debug.Log($"[特殊端子/反攻] 被ダメ{takenDmg} のうち {reflect} を反射");
                    }
                }
                ctx.playerDamageThisTurn += takenDmg;
                ctx.AddPlayerDamageSource(ctx.lastDamageCause, takenDmg);
                if (ctx.fixedDamageToPlayer > 0)
                {
                    int fixedTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        ctx.fixedDamageToPlayer, GameLoop.GameManager.Instance?.Run, playerHP);
                    playerHP = Math.Max(0, playerHP - fixedTaken);
                    takenApplied += fixedTaken;
                    ctx.playerDamageThisTurn += fixedTaken;
                    ctx.AddPlayerDamageSource(ctx.lastDamageCause, fixedTaken);
                    MetaProgression.MetaDebuffApplicator.NoteDamageTaken(
                        fixedTaken, GameLoop.GameManager.Instance?.Run);
                }

                // 痛覚遮断剤 (狂戦士) post-clamp (旧法と同一)
                if (ctx.painkillerArmedThisTurn && playerHP <= 0)
                {
                    Debug.Log("[痛覚遮断剤] post-clamp: HP=1 復帰");
                    playerHP = 1;
                    ctx.playerCurrentHP = 1;
                }

                // [計測] 7層でこのターンにプレイヤーが落ちたなら、 出ていた遺物を致死側へ計上する。
                if (playerHP <= 0 && !string.IsNullOrEmpty(ctx.bossId)
                    && ctx.bossId.StartsWith("boss_layer7"))
                    BossDmgDiag.V7Death(ctx.vescaDrawnRelics);

                ctx.playerCurrentHP = playerHP;
                ApplyVescaOnHitEffects(ctx, takenApplied);

                // 反撃固定ダメ (Counter/Riposte 等)
                if (counterFixed > 0)
                    enemyHP = Math.Max(0, enemyHP - counterFixed);

                psm.FireEnemyTrigger(PassiveSkillTrigger.OnPostDealDamage);
            }
            // **旧: else で OnPostReceiveDamage を焚いていた (2026-09-09 撤去)。**
            //   この else は「敵が死んでいて攻撃しなかった」枝で、 被弾の通知には使えない
            //   (死亡ターンにしか鳴らない)。 正しい発火点は上の enemyHP 減算直後へ移した。

            // ============================================================
            //  2 体戦 (2026-08-28)
            // ============================================================
            if (secondaryEnemy != null && secondaryHP > 0)
            {
                // --- (a) 与ダメージは 2 体目にも全額入る ---
                //   標的選択は無い。 撃破ターンは和ではなく max になるので**戦闘長は伸びず**、
                //   エスカレーションも 1 体戦と同じ速さでしか進まない。 増えるのは被ダメだけ。
                int mirrored = Math.Max(0, primaryHpBeforeDamage - enemyHP);
                if (mirrored > 0)
                {
                    secondaryHP = Math.Max(0, secondaryHP - mirrored);
                    if (secondaryHP == 0)
                        Debug.Log($"[2体戦] {secondaryEnemy.displayName} 撃破 (相殺ダメージ {mirrored})");
                }

                // --- (b) 2 体目の攻撃 = 独立した 2 つ目のパケット ---
                //   **ブロックは各パケットから全額引く (倍率なし)。** 1 本のプールを分配すると
                //   防御配線が成立しなくなるため、 2 回引く形を採る (2026-08-28 決定)。
                if (secondaryHP > 0 && playerHP > 0 && !enemyStunnedThisTurn)
                {
                    // 予告で開示済みの値をそのまま使う (ここで振り直さない)。
                    int secAtkValue = _secondaryAtkThisTurn;
                    int secLossBase = Math.Max(0, secAtkValue - blockSum);

                    ctx.playerWonRoll = false; ctx.playerLostRoll = true;
                    var (secTaken, _, _) =
                        psm.ProcessDamage(secLossBase, 0, 0, attackerIsEnemy: true);
                    secTaken = ApplyLossDamageModifiers(secTaken, floorMod, ctx);
                    if (ctx.nullifyAllDamage) secTaken = 0;

                    // 役の被ダメ側は 1 体目と**同じ扱い**にする。 片方にしか効かないと
                    //   〈大階/相殺〉〈束(防)〉の価値が 2 体戦でだけ半減してしまう。
                    if (roleOutcome.damageZero) secTaken = 0;
                    else if (roleOutcome.damageHalf && secTaken > 0)
                        secTaken = Math.Max(1, Mathf.RoundToInt(secTaken * YachtRoleEffects.BlockTripleDamageMultiplier));

                    secTaken = MetaProgression.MetaDebuffApplicator.ApplyJudgmentDamageIncrease(
                        secTaken, GameLoop.GameManager.Instance?.Run, playerHP);

                    if (secTaken > 0)
                    {
                        playerHP = Math.Max(0, playerHP - secTaken);
                        takenApplied += secTaken;
                        ctx.playerCurrentHP = playerHP;
                        ctx.playerDamageThisTurn += secTaken;
                        ctx.AddPlayerDamageSource(ctx.lastDamageCause, secTaken);
                    }
                    Debug.Log($"[2体戦] {secondaryEnemy.displayName} 攻撃 {secAtkValue} − ブロック {blockSum} "
                            + $"→ 被ダメ {secTaken} (HP {playerHP})");
                }
            }

            // 踏みとどまり 2 回目 ── 反射 (返しの盾/コントラタック/鏡写し) と反撃固定ダメは
            //   敵の攻撃解決の**中**でヴェスカの HP を削るため、 1 回目の clamp より後に走る。
            //   1 回目を敵攻撃より前に置くのは、 致死打で `enemyHP > 0` が落ちて
            //   **ヴェスカがその手番を丸ごと飛ばす**のを防ぐため (踏みとどまった以上は殴り返す)。
            //   よってこの 2 点セットで 1 ターン分を覆う。 ClampToThreshold は冪等。
            if (!roleOutcome.instantWin)
            {
                enemyHP = InventorySystem.PassiveSkills.Effects.DivineTalent.ClampToThreshold(
                    ctx, enemyHP, currentEnemy != null ? currentEnemy.maxHP : ctx.enemyMaxHP);
            }
            ctx.enemyCurrentHP = enemyHP;

            RecordTurnStats(ctx, attackDiceCount, blockDiceCount, chargeDiceCount, blockSum, enemyAtkValue);

            // --- 臨界メーター (Rinkai) 更新 ---
            //   attackSum + radiation を meter に加算、閾値到達で次T爆発予約 (Afterglow で meter 残置量調整)
            //   熱伝導 (Conduction) が有効なら被ダメも meter に加算
            if (ctx.rinkaiThreshold > 0)
            {
                int add = attackSum + (attackSum > 0 ? ctx.rinkaiRadiationBonus : 0);
                if (ctx.rinkaiConductionEnabled && takenApplied > 0) add += takenApplied;
                if (add > 0)
                {
                    ctx.rinkaiMeter += add;
                    if (ctx.rinkaiMeter >= ctx.rinkaiThreshold)
                    {
                        ctx.rinkaiBurstActive = true;
                        ctx.rinkaiMeter = ctx.rinkaiAfterglow;
                        Debug.Log($"[臨界] メーター閾値{ctx.rinkaiThreshold}到達 → 次T爆発予約 (残 meter={ctx.rinkaiAfterglow})");
                    }
                }
            }

            // --- 収支トリガー (柱2: OnRollWin/Lose の再定義 = 収支±のターン。解決後に発火) ---
            int balance = (dealtDmg + dealtFixed) - takenApplied;
            // [計装] 対象層のボス戦だけターン単位で残す (BossCombatTrace)。 既定は無効。
            //   **収支が確定した後**に置く ── 与ダメ/被ダメの最終値がここで揃う。
            BossCombatTrace.NoteTurn(ctx.currentTurn, traceAtk, traceBlock, traceThrough,
                takenApplied, dealtDmg + dealtFixed, playerHP, enemyHP,
                traceEBase, traceERoll, traceEsc, attackSum, actualPlayerDiceCount,
                traceRaw, traceBonus, traceBossBonus,
                currentEnemy != null ? currentEnemy.maxHP : 0);
            ctx.playerWonRoll = balance > 0;
            ctx.playerLostRoll = balance < 0;
            result.playerWon = balance > 0;
            result.isDraw = balance == 0;
            Debug.Log($"[相互攻撃] T{ctx.currentTurn} 収支 {(balance >= 0 ? "+" : "")}{balance} " +
                      $"(与 {dealtDmg + dealtFixed} / 被 {takenApplied})");

            if (ctx.playerWonRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollLose);
            else if (ctx.playerLostRoll)
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollWin);
            else
                psm.FireEnemyTrigger(PassiveSkillTrigger.OnRollDraw);

            // --- 7. ターン終了処理 (共通終端) ---
            return FinishTurnCommon(result, ctx);
        }

        // ===========================================================
        //  ダメージ修飾チェーン（ExecuteTurn から分離。順序が結果を左右するため厳守）
        // ===========================================================

        /// <summary>与ダメージの内訳計測 (診断用)。 「BOT の実効火力がどこから来ているか」を
        /// 実測するために置く。 §15-5 の遺物単位は参照状態 (atkBase 17 / 与ダメ 21.4) を
        /// **机上で組み立てて**較正したが、 実機と 5 倍ずれていたことが 2026-08-03 に判明した。
        /// 二度と机上の参照状態で較正しないための計測点。
        /// [0]件数 [1]武器素火力 [2]攻撃端子出目 [3]パッシブ加算 [4]消費 [5]遺物
        /// [6]atkBase合計 [7]最終与ダメ合計 [8]会心回数 [9]固定ダメ合計 [10]追撃合計</summary>
        public static readonly double[] DamageBreakdown = new double[19];
        /// <summary>[11]パッシブ由来の outgoing [12]業物 [13]メタ [14]<b>欠番</b>(旧 副次・2026-09-15 全廃)
        /// [15]遺物 [16]最終 outgoing</summary>

        /// <summary>**7層 (ヴェスカ連戦) だけ**の内訳。 添字は <see cref="DamageBreakdown"/> と同じ。
        /// 7層は 4 形態が 1 戦闘なので、 **戦闘の開始/終了の差分**で連戦全体が採れる ──
        /// 書き込み地点 (19 箇所) には一切触らないので本体の計測を壊さない。
        ///
        /// <para>2026-08-22: 同一シードで Optimal が 7層 p3 の与ダメ/T を **+20%** 上回るのに、
        /// 攻撃本数も攻撃出目計もほぼ同じ (1.46/17.3 対 1.44/17.0)。 会心系の役の発動数も同一。
        /// **倍率のどこかに差がある**が特定できていない。 それを切り分けるための計装。</para></summary>
        public static readonly double[] DamageBreakdown7F = new double[19];
        private readonly double[] _bdAtCombatStart = new double[19];
        private bool _bd7F;

        public static void ResetDamageBreakdown()
        {
            for (int i = 0; i < DamageBreakdown7F.Length; i++) DamageBreakdown7F[i] = 0;
            for (int i = 0; i < DamageBreakdown.Length; i++) DamageBreakdown[i] = 0;
            for (int i = 0; i < DamageStages.Length; i++) DamageStages[i] = 0;
            ResetFloorDamage();
        }

        /// <summary>与ダメ倍率の**段別**実測 (診断用・2026-08-03)。
        /// 総倍率 ×4.98 のうち 会心 1.78 × outgoing 2.21 = 3.93 しか説明できず、
        /// 残差 ×1.27 の所在が不明だったため、 修飾チェーンの各段の**通過後合計**を積む。
        /// 隣り合う段の比 = その段の平均倍率。 憶測せずに済ませるための計測点。
        /// [0]件数
        /// [1]A atkBase                 [2]B damageBonus+OnPreDealDamage 後
        /// [3]C 追撃加算後              [4]D 会心/鈍器 後 (ProcessDamage の返り値)
        /// [5]E outgoing 乗算後         [6]F 鬼火の油+攻撃バースト後
        /// [7]G 練度不足後              [8]H 敵軽減パッシブ後
        /// [9]I 基礎防御後              [10]J 利刃 余剰貫通後
        /// [11]K Λ微妙な手応え後        [12]L 脆弱後
        /// [13]M 最低保証後             [14]N 狂暴化後
        /// [15]O 俊敏/防御スタンス後 (最終)
        /// [16]会心倍率の合計(会心時のみ) [17]会心回数
        /// [18]鈍器発動 [19]研磨剤発動 [20]脆弱発動 [21]狂暴化発動
        /// [22]乗算時点の outgoing 合計 (研磨剤込み) [23]余剰貫通率の合計</summary>
        public static readonly double[] DamageStages = new double[24];

        /// <summary>ProcessDamage は勝敗・引分・敵側でも呼ばれるので、
        /// **プレイヤー勝利の主攻撃のときだけ** DamageStages を積むためのゲート。</summary>
        public static bool DiagWinAttack;

        /// <summary>層 × 敵種別ごとの与ダメ分布 (診断用・2026-08-03)。
        /// 全層平均の 137 だけ見て敵HPを決めると序盤を壊すので、 **層別に取る**。
        /// 添字: [floor 0..7][kind 0=通常/1=エリート/2=ボス][項目]
        /// 項目 [0]攻撃回数 [1]与ダメ合計(主+固定) [2]最小 [3]最大 [4]敵maxHP合計 [5]ターン番号合計
        /// [6]戦闘数(ターン1の攻撃を数えた近似)</summary>
        public static readonly double[,,] FloorDamage = new double[8, 3, 7];
        /// <summary>分位を出すための度数分布。 バケット幅 10、 0〜790+ を 80 段。
        /// 平均だけだと「最低/最高」が答えられないので度数で持つ。</summary>
        public static readonly int[,,] FloorDamageHist = new int[8, 3, 80];

        public static void ResetFloorDamage()
        {
            System.Array.Clear(FloorDamage, 0, FloorDamage.Length);
            System.Array.Clear(FloorDamageHist, 0, FloorDamageHist.Length);
            System.Array.Clear(TurnStats, 0, TurnStats.Length);
        }

        /// <summary>層 × 敵種別ごとの **ターン収支** (診断用・2026-08-03)。
        /// 通常戦を 3〜4T にしたことで敵が初めて手番を得るようになったので、
        /// 「被ダメがどれだけ増えたか」と「BOT がブロック端子へ配線し始めたか」を同時に見る。
        /// 後者が動いていれば ADR-0009 のジレンマが起動した証拠で、 耐久側を触る必要はない。
        /// 添字: [floor][kind][項目]
        /// [0]ターン数 [1]ブロック配線したターン数 [2]攻撃ダイス本数 [3]ブロックダイス本数
        /// [4]充電ダイス本数 [5]被ダメ合計 [6]敵攻撃値合計 [7]ブロック出目合計
        /// [8]ターン終了時HP合計 [9]ctx.playerMaxHP 合計 [10]Run.playerMaxHP 合計
        /// ※ [9] と [10] を分けているのは、 道中で平均最大HPが半減して見える件が
        ///    「実際にランの最大HPが減っている」のか「戦闘コンテキストへのコピーがずれている」のかを
        ///    切り分けるため (2026-08-04)。</summary>
        public static readonly double[,,] TurnStats = new double[8, 3, 11];

        /// <summary>計装用の層添字。 **Λ層は 0 へ振り分ける** (2026-08-04)。
        ///
        /// §14-1 の通り Λ 滞在中は `currentFloor` が 5 のままで、 `LambdaRing` は
        /// `EnemyKind.Elite` として記録され**再訪で再発火**する。 そのまま 5 に混ぜると
        /// 5F エリートのバケツが Λ の周回で膨らみ、 5 層そのものの難度が読めなくなる
        /// (実測で 5F エリのターン数が 5F 通常の 1.8 倍 = 1 ラン 5 戦相当になっていた)。
        /// 添字 0 はどのレポートも 1..7 でしか回していなかったので空いている。</summary>
        private static int StatFloorIndex()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null && run.inLambda) return 0;
            return Mathf.Clamp(run?.currentFloor ?? 1, 1, 7);
        }

        private void RecordTurnStats(CombatContext ctx, int atkDice, int blkDice, int chgDice,
                                     int blockSum, int enemyAtkValue)
        {
            if (ctx == null) return;
            int f = StatFloorIndex();
            int k = ctx.currentEnemyKind == EnemyKind.Boss ? 2
                  : ctx.currentEnemyKind == EnemyKind.Elite ? 1 : 0;
            TurnStats[f, k, 0]++;
            if (blkDice > 0) TurnStats[f, k, 1]++;
            TurnStats[f, k, 2] += atkDice;
            TurnStats[f, k, 3] += blkDice;
            TurnStats[f, k, 4] += chgDice;
            TurnStats[f, k, 5] += ctx.playerDamageThisTurn;
            // ラン内の実被弾。 航行の危険度見積りが読む (RunState.AverageFightDamage)。
            //   **ターンごとに足す** ── 戦闘開始/終了の HP 差だと回復で相殺されて過小になる。
            {
                var drun = GameLoop.GameManager.Instance?.Run;
                if (drun != null) drun.fightDamageSum += ctx.playerDamageThisTurn;
                if (drun != null && k != 2)
                {
                    drun.navTurns++;
                    drun.navDealt += Math.Max(0, ctx.lastPlayerDamage);
                    drun.navTaken += Math.Max(0, ctx.playerDamageThisTurn);
                }
            }
            TurnStats[f, k, 6] += enemyAtkValue;
            TurnStats[f, k, 7] += blockSum;
            TurnStats[f, k, 8] += ctx.playerCurrentHP;
            TurnStats[f, k, 9] += ctx.playerMaxHP;
            TurnStats[f, k, 10] += GameLoop.GameManager.Instance?.Run?.playerMaxHP ?? 0;
        }

        /// <summary>層×種別の与ダメを 1 攻撃ぶん記録する。</summary>
        private void RecordFloorDamage(CombatContext ctx, int dealt, int fixedDmg)
        {
            int f = StatFloorIndex();
            int k = ctx == null ? 0
                  : ctx.currentEnemyKind == EnemyKind.Boss ? 2
                  : ctx.currentEnemyKind == EnemyKind.Elite ? 1 : 0;
            double v = dealt + fixedDmg;
            if (FloorDamage[f, k, 0] == 0) { FloorDamage[f, k, 2] = v; FloorDamage[f, k, 3] = v; }
            else
            {
                if (v < FloorDamage[f, k, 2]) FloorDamage[f, k, 2] = v;
                if (v > FloorDamage[f, k, 3]) FloorDamage[f, k, 3] = v;
            }
            FloorDamage[f, k, 0]++;
            FloorDamage[f, k, 1] += v;
            FloorDamage[f, k, 4] += currentEnemy != null ? currentEnemy.maxHP : 0;
            FloorDamage[f, k, 5] += ctx != null ? ctx.currentTurn : 0;
            if (ctx != null && ctx.currentTurn == 1) FloorDamage[f, k, 6]++;
            FloorDamageHist[f, k, Mathf.Clamp((int)(v / 10.0), 0, 79)]++;
        }

        /// <summary>
        /// プレイヤー勝利時の与ダメージ修飾（ProcessDamage後〜敵HP適用前）。適用順:
        /// 画竜点睛 → 与ダメ倍率 (パッシブ・メタ・遺物・研磨剤・鬼火の油を 1 つのプールで加算) → 攻撃バースト → メタ会心 → 向かい風
        /// → 敵被ダメ軽減(灰塵等の OnPreReceiveDamage) → 基礎防御%(利刃で相殺) → 勝利時最低保証
        /// → 狂暴化 → メタ俊敏回避。
        /// </summary>
        /// <summary>[計装] 層別集計の添字。 <b>Λ層 (時間の狭間) は 0 番に分ける</b> ──
        /// Λ中も currentFloor は 5 のままなので、 混ぜると 5 層道中の数字が Λ の周回で汚れる。</summary>
        private static int LambdaAwareFloorIndex()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return 0;
            return run.inLambda ? 0 : run.currentFloor;
        }

        /// <summary>[計装] 武器 ID が所持パッシブ (ownedPassiveItems) にも入っているか。
        /// 入っていると RunPassiveSync が武器の静的 skills を登録し、 段階式の動的付与と二重になる疑い。</summary>
        private static string DsWeaponAlsoOwnedFlag()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run?.ownedPassiveItems == null) return null;
            if (!string.IsNullOrEmpty(run.equippedWeaponId) && run.ownedPassiveItems.Contains(run.equippedWeaponId))
                return "__flag__:装備中の武器が所持パッシブにもある";
            var db = InventorySystem.ItemDatabase.Instance;
            if (db != null)
                foreach (var id in run.ownedPassiveItems)
                    if (db.GetItem(id)?.category == InventorySystem.ItemCategory.Weapon)
                        return "__flag__:別の武器が所持パッシブにある";
            return null;
        }

        private int ApplyWinDamageModifiers(int totalDmg, ref int fixedDmg, ref bool isCrit,
                                            int lbStage, PassiveSkillManager psm, CombatContext ctx)
        {
            // 画竜点睛: ダメージ＝(出目+10)×会心倍率、会心確定
            if (ctx.garyoProc)
            {
                totalDmg = Mathf.CeilToInt((ctx.garyoDieValue + 10) * ctx.criticalMultiplier);
                isCrit = true;
                Debug.Log($"[画竜点睛] 確定会心 {totalDmg} ダメ (出目{ctx.garyoDieValue}+10 ×{ctx.criticalMultiplier})");
            }

            // 与ダメ倍率の内訳計測 (診断用)。 **ここへ来る時点で outgoing にはパッシブ由来が
            // 既に積まれている** (OnPreDealDamage は ProcessDamage の中で発火するため)。
            // 以降の 業物/メタ/遺物 の増分と分けて記録する。
            double _ovPassive = ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            double _ovPrev = _ovPassive;
            DamageBreakdown[11] += _ovPassive;
            // 黄金卿の剣 (+0.01 × 消費GOLD) は **上限が無い**ので、 消費GOLDも記録して
            // パッシブ由来 outgoing のうちどれだけがこれ由来かを切り分けられるようにする。
            DamageBreakdown[17] += GameLoop.GameManager.Instance?.Run?.coinsSpent ?? 0;
            if (_ovPassive > DamageBreakdown[18]) DamageBreakdown[18] = _ovPassive;

            // 業物: 与ダメ倍率 +20%/lv（outgoingDamageMultiplier に加算）
            // **2026-08-10 廃止。** 旧: 与ダメ倍率 +20%/lv。
            //   素の倍率で天井を押し上げるだけの段で、 かつ素材の唯一の後半シンクだった。
            //   素材価格が 2^N の倍々カーブなので、 ショップ価格倍率 (搾取経済) の効きが
            //   この経路を通って **log で潰れ**、 挑戦デバフに段差を作れなくなっていた
            //   (実測: 価格 ×1.15 と ×1.35 で業物が 3.62 / 3.63 と動かない)。
            //   代償は後半層の敵HP −10% (SpawnEnemy 側の LateFloorEnemyHpMultiplier)。
            //   lbStage は常に 0 (RunState.limitBreakStage は互換のため残置)。
            DamageBreakdown[12] += ctx.outgoingDamageMultiplier - _ovPrev; _ovPrev = ctx.outgoingDamageMultiplier;

            // メタバフ: 与ダメ +5%×N (cap 50%)。 他の outgoing% と同じ pool に加算合成
            // ＝最終に純倍率を掛けるとインフレするので、 ここでは additive。
            int metaPct = MetaProgression.MetaBuffApplicator.GetOutgoingDamagePct();
            if (metaPct > 0) DmgSourceDiag.Note("整備パネル〈出力〉", DmgSourceDiag.Out, metaPct * 0.01f);
            // 出力 r10 (極点) 〈戦意〉: 勝利数 × 2%。 同じ pool に加算合成 (2026-09-13)。
            int dsSpirit = MetaProgression.MetaBuffApplicator.GetBattleSpiritPct(
                GameLoop.GameManager.Instance?.Run);
            metaPct += dsSpirit;
            if (MetaProgression.MetaBuffApplicator.IsBattleSpiritUnlocked())
                DmgSourceDiag.Note("整備パネル〈戦意〉", DmgSourceDiag.Out, dsSpirit * 0.01f);
            // 剛胆 r10 (極点): **精鋭にだけ** +10%。 通常戦には乗らない ──
            //   乗せると出力トラックの薄い複製になり、 軸の性格が消える。
            //
            //   **2026-09-14: ボスを対象から外した。** 含めていたため最終ボス勝率が 90.2%
            //   (他トラックは 67〜78%) まで跳ね、 r10−r9 +11.17pt の主因になっていた。
            //   ボスは<b>自分が呼んだ強敵ではない</b>ので、 「リスクを取って強敵を呼び、
            //   その見返りを取る」というトラックの筋から外れる。
            {
                int slayer = MetaProgression.MetaBuffApplicator.GetEliteSlayerPct();
                if (slayer > 0)
                {
                    var kindNode = MapSystem.MapManager.Instance?.CurrentNode;
                    var kind = kindNode != null
                        ? kindNode.EffectiveType.ToEnemyKind() : EnemyKind.Normal;
                    DmgSourceDiag.Held("整備パネル〈剛胆r10〉");
                    if (kind == EnemyKind.Elite)
                    {
                        metaPct += slayer;
                        DmgSourceDiag.Note("整備パネル〈剛胆r10〉", DmgSourceDiag.Out, slayer * 0.01f);
                    }
                }
            }
            if (metaPct > 0)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += metaPct * 0.01f;
            }
            DamageBreakdown[13] += ctx.outgoingDamageMultiplier - _ovPrev; _ovPrev = ctx.outgoingDamageMultiplier;

            // [14] は旧「アイテム副次ステータスの与ダメ%」。 2026-09-15 に副次を全廃したので
            //      書き手が居なくなった。 添字は他段の意味を保つため詰めない (常に 0)。

            // 遺物 (§15-5) 与ダメージ+N%。 同じ pool に additive (純倍率にするとインフレするため)。
            float relicOut = MetaProgression.Relics.RelicApplicator
                             .GetOutgoingPct(GameLoop.GameManager.Instance?.Run, ctx);
            if (relicOut > 0f)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += relicOut;
                DmgSourceDiag.Note("遺物〈与ダメ%〉", DmgSourceDiag.Out, relicOut);
            }
            DamageBreakdown[15] += ctx.outgoingDamageMultiplier - _ovPrev;
            DamageBreakdown[16] += ctx.outgoingDamageMultiplier;

            // 瞬間研磨剤 (剣士スターター・2026-07-27 リワーク): 次の 1 撃だけ 与ダメージ+150%。
            // 旧「ダイス合計+[本数×2]」は相互攻撃モデルで無効だったため倍率へ移設。
            // 無我無心 (rollPurity) 中は消費アイテム由来の補正を受けない規約に従い不発。
            if (ctx.polishArmed && !ctx.rollPurity)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += 1.5f;
                DmgSourceDiag.Note("消費〈瞬間研磨剤〉", DmgSourceDiag.Out, 1.5f);
                ctx.polishArmed = false;   // 1 撃限り
                if (DiagWinAttack) DamageStages[19]++;
                Debug.Log("[瞬間研磨剤] 与ダメージ+150%");
            }

            // 消費: 鬼火の油 与ダメ+X%（全戦闘）。 **他の与ダメ% と同じプールへ加算** (2026-09-19)。
            //   旧: 倍率を掛けた後の totalDmg に さらに ×(1+X%) を掛けており、 与ダメ% を重ねると掛け算で膨らんだ。
            if (ctx.consDmgMultPct > 0)
            {
                if (ctx.outgoingDamageMultiplier <= 0f) ctx.outgoingDamageMultiplier = 1f;
                ctx.outgoingDamageMultiplier += ctx.consDmgMultPct / 100f;
                DmgSourceDiag.Note("消費〈油・膏薬〉", DmgSourceDiag.Out, ctx.consDmgMultPct / 100f);
            }

            // 与ダメ倍率（激情の刃 等のパッシブ由来 + メタ% + 遺物 + 研磨剤 + 鬼火の油）
            if (ctx.outgoingDamageMultiplier > 0f
                && Mathf.Abs(ctx.outgoingDamageMultiplier - 1f) > 0.001f)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.outgoingDamageMultiplier);
                Debug.Log($"[CombatManager] 与ダメ補正 ×{ctx.outgoingDamageMultiplier:F2}: {orig}→{totalDmg}");
            }
            if (DiagWinAttack)
            {
                DamageStages[5] += totalDmg;   // 段 E
                DamageStages[22] += ctx.outgoingDamageMultiplier <= 0f ? 1f : ctx.outgoingDamageMultiplier;
            }

            // 消費: 攻撃力バースト（この勝利ターンのみ・単発消費）
            if (ctx.consAtkBurst > 0)
            {
                totalDmg += ctx.consAtkBurst;
                Debug.Log($"[CombatManager] 攻撃バースト +{ctx.consAtkBurst}");
                ctx.consAtkBurst = 0;
            }
            if (DiagWinAttack) DamageStages[6] += totalDmg;   // 段 F

            // [2026-07-28 撤去] メタバフ「会心ダイス」による会心時の固定ダメ加算。
            //   GetCritBonus() は精密トラックの分子補正だったが、 分子/分母モデル廃止に伴い常に 0。
            //   同じ値を PassiveSkillManager では会心率の分子、 ここでは固定ダメージ加算として
            //   二重に解釈していたため、 精密の効果は会心率(%)へ一本化した (§15-1)。

            // 挑戦デバフ 軸2〈練度不足〉: プレイヤー与ダメ ×0.94 / ×0.91 / ×0.89 (2026-08-09)。
            //   旧「向かい風 −1」の定数減算から % 乗算へ (docs/GAME.md §15-2)。 定数減算は
            //   高火力ビルドではほぼ無視でき、 低火力ビルドだけを一方的に潰していた。
            //   **最低 1 は残す** ── 0 にすると削りが完全に止まり、 耐久レースが成立しなくなる。
            //
            // **同種デバフは重ねがけしない (2026-08-09)。**
            //   〈練度不足〉(挑戦) と〈微妙な手応え〉(Λ) は **どちらも与ダメ低下**で、
            //   掛け合わせると 0.89 × 0.85 = 0.756 まで落ちていた。 Λ は 6 層へ行くための
            //   強制通過点なので、 高難易度では必ずこの二重取りを踏む。
            //   **厳しい方 1 つだけを採る** ── 会心側 (〈俊敏〉と〈注意散漫〉) は
            //   「減算 → 逓減 → 上限」の順で既に min() 相当になっており、 そちらへ合わせた形。
            //   なお **合計 (−11% −15% = −26% → 0.74) は乗算より厳しい**ので、
            //   「乗算をやめて加算に」は緩和にならない。 緩和になるのは max(severity) だけ。
            float metaDmgMul = Mathf.Min(
                MetaProgression.MetaDebuffApplicator.GetPlayerDamageMultiplier(),
                ctx.lambdaDamageDealtMult <= 0f ? 1f : ctx.lambdaDamageDealtMult);
            if (metaDmgMul < 0.999f && totalDmg > 0)
                totalDmg = Mathf.Max(1, Mathf.RoundToInt(totalDmg * metaDmgMul));
            if (DiagWinAttack) DamageStages[7] += totalDmg;   // 段 G

            // 敵側のダメージ軽減パッシブを発火（前後で ctx.finalDamage と totalDmg を同期し、
            // 敵パッシブ（AshArmor 等やシュヴァリエのシールド経由処理）が
            // 実際の与ダメに反映されるようにする）
            ctx.finalDamage = totalDmg;
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnPreReceiveDamage);
            totalDmg = System.Math.Max(0, ctx.finalDamage);
            if (DiagWinAttack) DamageStages[8] += totalDmg;   // 段 H

            // 基礎防御%軽減（灰塵の鎧の後）。利刃で軽減率(pt)を相殺。倍率が全部乗った最終値に対して%カット。
            if (ctx.enemyDamageReductionPct > 0f && totalDmg > 0)
            {
                float effRate = Mathf.Max(0f, ctx.enemyDamageReductionPct - ctx.armorPenPct);
                if (effRate > 0f)
                {
                    int beforeDef = totalDmg;
                    totalDmg = Mathf.CeilToInt(totalDmg * (1f - effRate));
                    Debug.Log($"[基礎防御] 軽減{effRate:P0}（防{ctx.enemyDamageReductionPct:P0}/利刃{ctx.armorPenPct:P0}）{beforeDef}→{totalDmg}");
                }
            }
            if (DiagWinAttack) DamageStages[9] += totalDmg;   // 段 I

            // 利刃: 敵基礎防御を超えた貫通分(armorPen − 防御率)は腐らせず与ダメ%へ転用。
            // → 無装甲の敵にも armorPen 分の与ダメ増として常時機能する（対タンク以外でも腐らない）。
            float wastedPen = Mathf.Max(0f, ctx.armorPenPct - ctx.enemyDamageReductionPct);
            if (wastedPen > 0f && totalDmg > 0)
            {
                int beforePen = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * (1f + wastedPen));
                Debug.Log($"[利刃] 余剰貫通 +{wastedPen:P0} {beforePen}→{totalDmg}");
            }
            if (DiagWinAttack) { DamageStages[10] += totalDmg; DamageStages[23] += wastedPen; }   // 段 J

            // Λデバフ「微妙な手応え」は **段 G で〈練度不足〉と min() を取って適用済み**
            // (2026-08-09・同種デバフの重ねがけ禁止)。 ここでは何もしない。
            // 段 K の計測点は残す ── 段別実測の添字がずれると過去の記録と比較できなくなる。
            if (DiagWinAttack) DamageStages[11] += totalDmg;   // 段 K

            // 脆弱 (狩猟旅団契約): armed状態で会心ダメージを与えると最終ダメージに ×(1+0.15/0.30/0.45) 倍率。
            // 適用後 consumed 状態へ遷移。 ロール勝利時の非会心ダメで armed に再点火する処理は応答側で別途。
            if (isCrit && totalDmg > 0)
            {
                float vulMult = VulnerabilityStatus.ConsumeOnCrit(ctx);
                if (vulMult > 1f)
                {
                    int beforeVul = totalDmg;
                    totalDmg = Mathf.CeilToInt(totalDmg * vulMult);
                    DmgSourceDiag.Note("契約〈脆弱〉", DmgSourceDiag.Vul, vulMult - 1f);
                    Debug.Log($"[狩猟旅団] 脆弱発動 ×{vulMult:F2} {beforeVul}→{totalDmg}");
                    if (DiagWinAttack) DamageStages[20]++;
                }
            }
            else if (!isCrit && totalDmg > 0)
            {
                // ロール勝利時の非会心ダメで脆弱を再点火 (契約継続中のみ)
                VulnerabilityStatus.RearmOnNonCritWin(ctx);
            }
            if (DiagWinAttack) DamageStages[12] += totalDmg;   // 段 L

            // 勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。「勝ったのに0」を防止。
            if (totalDmg < ctx.winMinDamage)
                totalDmg = ctx.winMinDamage;
            if (DiagWinAttack) DamageStages[13] += totalDmg;   // 段 M

            // 狂暴化(ボス50T後): エネミーが受けるダメージを倍化（軽減処理の後・適用直前）
            if (ctx.enemyDamageTakenMultiplier > 1f && totalDmg > 0)
            {
                int orig = totalDmg;
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyDamageTakenMultiplier);
                Debug.Log($"[CombatManager] 狂暴化: 敵被ダメ ×{ctx.enemyDamageTakenMultiplier:F1} ({orig}→{totalDmg})");
                if (DiagWinAttack) DamageStages[21]++;
            }
            if (DiagWinAttack) DamageStages[14] += totalDmg;   // 段 N

            // 挑戦デバフ 軸7〈俊敏〉
            //   T1 以上: 各戦闘の最初の 1 回の被弾を必ず回避
            //   T2/T3  : さらに 2 回目以降も 20% / 40% で回避
            if ((totalDmg + fixedDmg) > 0 && MetaProgression.MetaDebuffApplicator.EnemyDodgesFirstHit())
            {
                bool dodged = false;
                if (!metaAgilityDodgeUsed)
                {
                    metaAgilityDodgeUsed = true;
                    dodged = true;
                    Debug.Log($"[俊敏] 初撃を回避 (与ダメ {totalDmg}+{fixedDmg} を無効化)");
                }
                else
                {
                    float p = MetaProgression.MetaDebuffApplicator.GetEnemyDodgeChance();
                    // 決定論のため GameRng を通す。 連番は戦闘通し番号 + ターンで一意 (RngIdx)。
                    if (p > 0f && GameLoop.GameRng.Value("meta.agilityDodge", RngIdx(7)) < p)
                    {
                        dodged = true;
                        Debug.Log($"[俊敏] 2回目以降の回避 ({p:P0}) (与ダメ {totalDmg}+{fixedDmg} を無効化)");
                    }
                }
                if (dodged) { totalDmg = 0; fixedDmg = 0; }
            }

            // プレイヤー防御スタンス（ADR-0006）: 与ダメージ-90%（主ダメージのみ。fixedDmg=反撃/業火/血令 等は対象外）
            if (ctx != null && ctx.playerStanceDefense && totalDmg > 0)
                totalDmg = Mathf.CeilToInt(totalDmg * PlayerStance.DefenseWinDamageMult);

            if (DiagWinAttack) DamageStages[15] += totalDmg;   // 段 O (最終)
            return totalDmg;
        }

        /// <summary>
        /// プレイヤー敗北時の被ダメージ修飾（ProcessDamage後〜プレイヤーHP適用前）。適用順:
        /// 天変地異(×2) → メタ被ダメ軽減 → 不屈の鎧/苦難の刻印 → 地獄門 → 亡者の招待(+30%)
        /// → 共助(T1半減) → 獣の絆 → コントラタック(50%軽減+反射) → 消費シールド → 鏡写し反射。
        /// enemyHP はメンバフィールドのため反射系はここで直接削る。
        /// </summary>
        private int ApplyLossDamageModifiers(int totalDmg, MapSystem.FloorModifier floorMod, CombatContext ctx)
        {
            // 挑戦デバフ〈狂暴化〉: 敵ダメージ倍率。
            //   〈天変地異〉のボス初撃強化は 2026-08-08 に廃止し、 エスカレーション閾値の
            //   前倒し (Escalation.StageOf) へ移した。 ボス戦のみ・最初の 3 撃という形は
            //   40 ターン級の戦闘では誤差にしかならず、 単独 T3 が実質無料だったため。
            float enemyMul = MetaProgression.MetaDebuffApplicator.GetEnemyDamageMultiplier();
            if (Mathf.Abs(enemyMul - 1f) > 0.001f)
                totalDmg = Mathf.CeilToInt(totalDmg * enemyMul);

            // 敵スタンス（ADR-0005）: 被ダメ倍率（高ダメ>1/低ダメ<1）。以降の軽減はスタンス後の値に効く。
            if (ctx != null && Mathf.Abs(ctx.enemyStanceDamageMult - 1f) > 0.001f && totalDmg > 0)
                totalDmg = Mathf.CeilToInt(totalDmg * ctx.enemyStanceDamageMult);

            // (2026-06-01) ボス被ダメ倍率(dmgMul)の直接操作は廃止。 硬化/易化は Dice/HP/機構軸で行う。

            // [削除 2026-09-13] メタバフの定額被ダメ軽減 ── 2026-08-15 に割合軽減へ移して以降
            //   常に 0 を返す残骸だった。 現行は下の GetGuardDamageReductionPct の乗算段。

            // 名前付きパッシブ由来の固定被ダメ削減（不屈の鎧・苦難の刻印 等の合算）
            if (ctx.playerFlatDamageReduction > 0 && totalDmg > 0)
                totalDmg = Mathf.Max(1, totalDmg - ctx.playerFlatDamageReduction);

            // 遺物 (§15-5) 被ダメージ−N%。 **割合軽減**であって定額ではない。
            //   定額だと参照被ダメ 7.7/T に対し段6 が 78% 軽減になり、 序盤の敵 (baseAttack 2〜10) を
            //   丸ごと無力化していた。 割合なら敵が弱いほど軽減の絶対量も小さくなる (§24)。
            float relicRed = MetaProgression.Relics.RelicApplicator
                             .GetDamageReductionPct(GameLoop.GameManager.Instance?.Run, ctx);
            if (relicRed > 0f && totalDmg > 0)
                totalDmg = Mathf.Max(1, Mathf.CeilToInt(totalDmg * (1f - relicRed)));

            // [撤去 2026-09-13] 整備パネル〈防御〉の割合軽減。 このトラックは
            //   リスク軸〈剛胆〉へ完全リワークし、 被ダメには一切触れなくなった。

            // 2026-06-28: 不抜の聖紋 (騎士スターター): HP 80% 以上維持中 被ダメ -40%、
            // 一度でも 80% を割ったら以後無効 (回復しても復活しない)
            if (ctx.oathArmed && !ctx.oathBroken && playerMaxHP > 0)
            {
                // 開始時点で既に 80% 未満なら即 break
                if (playerHP * 5 < playerMaxHP * 4)
                {
                    ctx.oathBroken = true;
                    Debug.Log($"[不抜の聖紋] HP 80% 割れにより破棄 (HP {playerHP}/{playerMaxHP})");
                }
                else if (totalDmg > 0)
                {
                    int reduced = Mathf.CeilToInt(totalDmg * 0.4f);
                    totalDmg = Mathf.Max(0, totalDmg - reduced);
                    Debug.Log($"[不抜の聖紋] 被ダメ -{reduced} → {totalDmg} (HP {playerHP}/{playerMaxHP})");
                    // この被ダメ適用後に 80% を割るなら次回以降は無効化
                    if ((playerHP - totalDmg) * 5 < playerMaxHP * 4)
                    {
                        ctx.oathBroken = true;
                        Debug.Log($"[不抜の聖紋] この被ダメで HP 80% 割れ予定 → 破棄");
                    }
                }
            }

            // フロアデバフ: 敗北時の被ダメージ軽減（5層 地獄門: -2）
            if (floorMod != null && floorMod.defeatDamageReduction > 0 && totalDmg > 0)
            {
                int reduced = Mathf.Min(totalDmg, floorMod.defeatDamageReduction);
                totalDmg -= reduced;
                Debug.Log($"[CombatManager] フロアデバフ: 敗北時被ダメ-{reduced} → {totalDmg}");
            }

            // 亡者の招待: 被ダメ +30%
            if (ctx.receivedDamageBonus > 0f && totalDmg > 0)
            {
                int bonusDmg = Mathf.CeilToInt(totalDmg * ctx.receivedDamageBonus);
                totalDmg += bonusDmg;
                Debug.Log($"[CombatManager] 亡者の招待: 被ダメ+{bonusDmg} (合計{totalDmg})");
            }

            // 共助: 1ターン目のメインダメージ半減
            if (ctx.halveFirstEnemyAttack && ctx.currentTurn == 1)
            {
                totalDmg = totalDmg / 2;
                ctx.halveFirstEnemyAttack = false;
                Debug.Log("[CombatManager] 共助発動: 敵の最初の攻撃を半減");
            }

            // 獣の絆: 被弾無効化チャージ消費
            if (ctx.playerDamageNegateCharges > 0 && totalDmg > 0)
            {
                ctx.playerDamageNegateCharges--;
                Debug.Log($"[CombatManager] 獣の絆発動: 被弾{totalDmg}を無効化（残チャージ{ctx.playerDamageNegateCharges}）");
                totalDmg = 0;
            }

            // シュヴァリエのレイピア コントラタック: 被ダメ50%軽減 + 軽減量×2 を敵へ反射
            if (ctx.GetAccumulated("player_contre") > 0 && totalDmg > 0)
            {
                int mitigated = totalDmg / 2;
                totalDmg -= mitigated;
                int reflect = mitigated * 2;
                if (reflect > 0)
                {
                    enemyHP = Math.Max(0, enemyHP - reflect);
                    Debug.Log($"[コントラタック] 軽減{mitigated} → 被ダメ{totalDmg + mitigated}→{totalDmg}、反射{reflect} → 敵HP{enemyHP}");
                }
            }

            // 消費: シールド吸収（残量から差し引き）
            if (ctx.consShield > 0 && totalDmg > 0)
            {
                int absorbed = Math.Min(ctx.consShield, totalDmg);
                ctx.consShield -= absorbed;
                totalDmg -= absorbed;
                ctx.dbgShieldToNormal += absorbed;   // [計装] チップ側との配分を見る
                Debug.Log($"[CombatManager] シールド吸収 {absorbed} (残{ctx.consShield})");
            }

            // 消費: 鏡写しの水晶（吸収後の実被ダメと同量を敵へ反射）
            if (ctx.consReflect && totalDmg > 0)
            {
                enemyHP = Math.Max(0, enemyHP - totalDmg);
                Debug.Log($"[CombatManager] 鏡写し反射 {totalDmg} → 敵HP {enemyHP}");
            }

            // 〈虚空〉の殻: 攻撃端子 0 本のターンだけ立つ。 積むほど薄くなり n=6 で消える。
            // **下限を 0 にしないので完全無敵にはならない** (旧 nullifyAllDamage の抜け穴対策)。
            if (ctx != null && ctx.voidStanceDamageMul < 1f && totalDmg > 0)
            {
                int before = totalDmg;
                totalDmg = Mathf.Max(1, Mathf.CeilToInt(totalDmg * ctx.voidStanceDamageMul));
                Debug.Log($"[虚空] 殻が受け止めた {before}→{totalDmg} (×{ctx.voidStanceDamageMul:F2})");
            }

            // プレイヤー防御スタンス（ADR-0006）: 全軽減/シールドの後、最終的に受けるダメージを-50%（最後に適用）。
            // 反撃等の敗北時固定ダメ(fixedDamageToEnemy)はこの totalDmg に含まれない＝対象外。
            if (ctx != null && ctx.playerStanceDefense && totalDmg > 0)
                totalDmg = Mathf.CeilToInt(totalDmg * PlayerStance.DefenseLossDamageMult);

            // 2026-06-28: 痛覚遮断剤 (狂戦士スターター): 使用ターン中の致命ダメは HP=1 で踏みとどまる
            // (シールド/軽減すべての後の最終値で判定。 fixedDamageToPlayer 等の別経路は別途処理されるが
            //  メインダメで死なせない最低保証として機能)
            if (ctx != null && ctx.painkillerArmedThisTurn && totalDmg >= playerHP && playerHP > 0)
            {
                int clamp = playerHP - 1;
                Debug.Log($"[痛覚遮断剤] 致命ダメ {totalDmg} → {clamp} (HP=1 踏みとどまり)");
                totalDmg = clamp;
            }

            return totalDmg;
        }

        // ===========================================================
        //  自動戦闘（全ターン一括実行）
        // ===========================================================

        /// <summary>
        /// 決着がつくまで自動でターンを回す
        /// </summary>
        public CombatResult ExecuteFullCombat()
        {
            int maxTurns = 100; // 無限ループ防止
            int turn = 0;
            while (isCombatActive && turn < maxTurns)
            {
                ExecuteTurn();
                turn++;
            }

            if (isCombatActive)
            {
                Debug.LogWarning("[CombatManager] Combat exceeded max turns, forcing end.");
                FinishCombat();
            }

            return GetLastCombatResult();
        }

        // ===========================================================
        //  戦闘終了
        // ===========================================================

        private void FinishCombat()
        {
            // **冪等にする** (2026-09-20)。 呼び出し口が 7 箇所あり、 どれも
            //   「終わったから閉じる」を独立に判断している。 二度閉じると
            //   <b>計装だけが二重に積まれる</b> ── 戦闘側の死亡数がラン側より 10.3% 多い、
            //   という形で現れた (実測 10,000 ラン: 戦闘側 9,478 / ラン側 8,591)。
            //   `isCombatActive = false` は元からこの関数の末尾にあるので、 それを入口の番人にする。
            if (!isCombatActive) { SkillDiag.FinishCombatReentry++; return; }

            // [計装] 対象層のボス戦の締め。 既定は無効。
            CombatSystem.BossCombatTrace.EndFight(enemyHP <= 0 && playerHP > 0,
                PassiveSkillManager.Instance?.Context?.currentTurn ?? 0);
            SkillDiag.Note(_diagKind, playerHP <= 0, PassiveSkillManager.Instance?.Context?.currentTurn ?? 0,
                           _diagStartHp, playerHP, _diagStartMaxHp,
                           LambdaAwareFloorIndex());
            if (_diagKind == SkillDiag.Boss)
            {
                SkillDiag.NoteBoss(currentEnemy?.id, PassiveSkillManager.Instance?.Context?.currentTurn ?? 0, playerHP <= 0);
                if (currentEnemy != null)
                {
                    var bd = SkillDiag.BossDmgOf(currentEnemy.id);
                    bd[0] += 1; bd[1] += currentEnemy.maxHP; bd[6] += Math.Max(0, enemyHP);
                    if (enemyHP <= 0) bd[8] += 1;
                }
            }

            // 戦闘終了系時限効果（泉の祝福、芽吹きの祈り等）+ ロール/ターン系の消費
            var psmCtx = PassiveSkillManager.Instance?.Context;
            EventSystem.TimedEffects.TimedEffectManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            // 名前付き固有パッシブ（戦闘終了時系: 巡礼者の杖、希望の灯片）
            InventorySystem.PassiveItems.PassiveItemManager.OnCombatEnd(
                psmCtx, GameLoop.GameManager.Instance?.Run, this);

            // 2026-07-25 v6: 臨界 r2 保温炉 - メーターの N% を次戦闘へ持ち越し
            {
                var runEnd = GameLoop.GameManager.Instance?.Run;
                float ratio = MetaProgression.MetaBuffApplicator.GetRinkaiCarryoverRatio();
                if (runEnd != null && psmCtx != null && ratio > 0f && psmCtx.rinkaiMeter > 0)
                {
                    int carry = Mathf.FloorToInt(psmCtx.rinkaiMeter * ratio);
                    if (carry > 0)
                    {
                        runEnd.pendingRinkaiCarryover = carry;
                        Debug.Log($"[MetaBuff] 臨界保温炉: メーター {psmCtx.rinkaiMeter} × {ratio:P0} → 次戦闘へ {carry}");
                    }
                }
            }

            isCombatActive = false;

            // 挑戦デバフ〈長引く負傷〉(2026-08-10 リワーク): **ラン中の累計被ダメの N% だけ最大HP を失う**。
            //   蓄積量に割合を掛けた整数部が増えるたび、 その差分を最大HP から引く
            //   (例: 5% なら累計 20 ダメごとに −1)。 閾値も回数も持たないので、
            //   「削られながら勝つ」を繰り返すほど確実に効く ── 旧実装が死んでいた理由の裏返し。
            //
            //   **上限を設けない。** 旧実装の累計上限 20 は、 閾値が現在最大HP 比だったころの
            //   自己加速 (削れる→閾値が下がる→さらに削れる) を止めるためのもの。 新方式は
            //   絶対量の累計に比例するだけで自己加速しないので、 上限は意味を持たない。
            {
                float lwRatio = MetaProgression.MetaDebuffApplicator.GetLingeringWoundRatio();
                var lwRun = GameLoop.GameManager.Instance?.Run;
                if (lwRatio > 0f && playerHP > 0 && lwRun != null)
                {
                    int target = Mathf.FloorToInt(lwRun.lingeringWoundDamageAccum * lwRatio);
                    int delta = target - lwRun.lingeringWoundLost;
                    if (delta > 0)
                    {
                        lwRun.lingeringWoundLost = target;
                        lwRun.lingeringWoundTriggers++;
                        lwRun.playerMaxHP = Math.Max(1, lwRun.playerMaxHP - delta);
                        lwRun.playerHP = Math.Min(lwRun.playerHP, lwRun.playerMaxHP);
                        Debug.Log($"[長引く負傷] 累計被ダメ {lwRun.lingeringWoundDamageAccum} × {lwRatio:P0}"
                                + $" → 最大HP -{delta} (累計 -{lwRun.lingeringWoundLost}) → {lwRun.playerMaxHP}");
                    }
                }
            }

            // ===== LED演出リセット =====
            if (ledManager != null)
            {
                // 全LEDを消灯（ローリングアニメーション停止も含む）
                ledManager.TurnOffAll();
                
                Debug.Log("[CombatManager] LED animations reset");
            }

            var result = GetLastCombatResult();
            // **EndCombat() は context を null にする。** 計装の flush はその後なので、
            //   ここで参照を掴んでおく (掴まないと黙って 0 件が積まれる)。
            var ctxAtEnd = PassiveSkillManager.Instance?.Context;
            // [計装] 最終段を締める。 SwapEnemy は「次段へ移るとき」しか呼ばれないので、
            //   戦闘が終わった段 (勝っても負けても) はここでしか記録できない。
            if (ctxAtEnd != null && !string.IsNullOrEmpty(ctxAtEnd.bossId)
                && ctxAtEnd.bossId.StartsWith(GameLoop.BossIds.Layer7Prefix))
            {
                GameLoop.RunChronicle.NotePhaseDuration(
                    ctxAtEnd.bossId.EndsWith("_p4") ? 4 : ctxAtEnd.bossId.EndsWith("_p3") ? 3
                        : ctxAtEnd.bossId.EndsWith("_p2") ? 2 : 1,
                    ctxAtEnd.phaseStartTurn, ctxAtEnd.currentTurn);
            }
            MetaProgression.Achievements.AchievementService.NoteCombatEnded(
                GameLoop.GameManager.Instance?.Run, result, ctxAtEnd);
            PassiveSkillManager.Instance.EndCombat();

            // [廃止] 旅団契約の戦闘フック一式 (2026-08-11 にシステムごと削除)。
            //   戦闘開始/ターン終了/戦闘終了 hook・影武者の致死復活・HP20% 解除判定。
            {
                var runRef = GameLoop.GameManager.Instance?.Run;
                if (runRef != null) playerHP = runRef.playerHP;
            }

            // 行動台帳 (RunChronicle): 戦闘 1 件を 1 行積む。
            //   **ここが唯一の記録点にしてある** ── 実プレイも BOT も FinishCombat を通るので、
            //   同じ定義のコード列が両方から出る (＝ BOT のバッチがそのまま計装になる)。
            //   分類は RunChronicle.ClassifyCombat 側に置く。 閾値を 2 箇所に分けると
            //   サマリの緊張感曲線 (knife ≤20% / blowout >80%) と定義が食い違う。
            {
                var chRun = GameLoop.GameManager.Instance?.Run;
                if (chRun != null && currentEnemy != null)
                {
                    // **`playerHP` を読んではいけない。** 直前のブロックで `run.playerHP` から
                    //   上書きされており、 戦闘終了時点の値ではなくなっている
                    //   （敗北なのに残 HP 56% / 被ダメ 107% という矛盾した記録が出た）。
                    //   `result` は上書き前の GetLastCombatResult() で確定しているのでそちらを使う。
                    int endHp = Math.Max(0, result.playerHPRemaining);
                    int endPct = this.playerMaxHP > 0
                               ? Mathf.RoundToInt(100f * endHp / this.playerMaxHP)
                               : 0;
                    // **この戦闘で受けた量**: 開始HP − 終了HP + 戦闘中に入れた回復。
                    //   回復を足さないと、 削られてから回復した戦闘が「無傷」に化ける。
                    int taken = Math.Max(0, _hpAtCombatStart - endHp + result.healApplied);
                    int takenPct = this.playerMaxHP > 0
                                 ? Mathf.RoundToInt(100f * taken / this.playerMaxHP)
                                 : 0;
                    // 7層の内訳を締める。 **[18] は最大値なので差分ではなく max を採る。**
                    if (_bd7F)
                    {
                        for (int i = 0; i < DamageBreakdown.Length; i++)
                            DamageBreakdown7F[i] += DamageBreakdown[i] - _bdAtCombatStart[i];
                        DamageBreakdown7F[18] = Math.Max(DamageBreakdown7F[18], DamageBreakdown[18]);
                        _bd7F = false;
                    }
                    YachtRoleEffects.FlushByDice(chRun.equippedDiceId);
                    GameLoop.RunChronicle.HealInCombat += Math.Max(0, result.healApplied);
                    GameLoop.RunChronicle.ShieldGained += Math.Max(0, result.shieldGained);
                    // [計装] 盾がチップと通常攻撃のどちらに使われ、 何点余ったか。
                    //   ctx は戦闘毎に作り直されるので、 ここが唯一の flush 点。
                    // 防御 r10 (極点): 残ったシールドの半分を次戦へ持ち越す。 **敗北時は持ち越さない**
                    //   (ランが終わるので意味が無い)。 ctx は戦闘毎に作り直されるのでここが唯一の保存点。
                    if (ctxAtEnd != null && result.playerWon
                        && MetaProgression.MetaBuffApplicator.IsShieldCarryUnlocked())
                    {
                        // **2026-09-13: 持ち越すのは残量の 30% (切り上げ)。** 全量 → 50% → 30%。
                        //   50% でも 防御 r10 が Balanced +6.31pt で基準帯を超えていた。
                        chRun.carriedShield = Mathf.CeilToInt(Math.Max(0, ctxAtEnd.consShield) * 0.30f);
                        if (chRun.carriedShield > 0)
                            Debug.Log($"[MetaBuff] シールド持ち越し保存 {chRun.carriedShield} (防御r10)");
                    }
                    if (ctxAtEnd != null)
                        GameLoop.RunChronicle.NoteShieldSplit(
                            currentEnemy.id == GameLoop.BossIds.Layer5Judgment,
                            ctxAtEnd.dbgShieldToChip, ctxAtEnd.dbgShieldToNormal,
                            Math.Max(0, ctxAtEnd.consShield),
                            ctxAtEnd.dbgChipRaw, ctxAtEnd.dbgChipToHp,
                            !result.playerWon, ctxAtEnd.playerDamageThisTurn,
                            Math.Max(0, ctxAtEnd.consShield));
                    GameLoop.RunChronicle.Combat(chRun, currentEnemy.id,
                        GameLoop.BossIds.IsBoss(currentEnemy.id),
                        result.playerWon, result.totalTurns, _hpPctAtCombatStart, endPct, takenPct);
                }
            }

            // **終了通知が誰にも届かない状態を黙って通さない。**
            //   購読者が居ないと戦闘だけ終わってフェーズが Combat のまま残り、
            //   呼び出し側は「戦闘中なのに戦闘が動かない」無限ループに落ちる。
            //   ストール検出は「phase=Combat」としか言わないので、 ここで名指ししないと追えない。
            if (OnCombatEnd == null)
                Debug.LogError("[CombatManager] 戦闘終了イベントの購読者が居ない "
                    + "── フェーズが進まず停止する (GameManager.Start の購読が外れている)");
            OnCombatEnd?.Invoke(result);

            // 通知した結果フェーズが動いたか。 動いていなければ購読者側で止まっている。
            {
                var gmPhase = GameLoop.GameManager.Instance?.CurrentPhase;
                if (gmPhase == GameLoop.GameManager.GamePhase.Combat)
                    Debug.LogError("[CombatManager] 戦闘終了を通知したのにフェーズが Combat のまま "
                        + $"(敵={currentEnemy?.id} 勝敗={(result.playerWon ? "勝" : "敗")} "
                        + $"{result.totalTurns}T)");
            }

            Debug.Log($"[CombatManager] ===== COMBAT END =====");
            Debug.Log($"  Result: {(result.playerWon ? "PLAYER WIN" : "PLAYER LOSE")}");
            Debug.Log($"  Player HP: {playerHP}/{playerMaxHP}");
            Debug.Log($"  Enemy  HP: {enemyHP}/{currentEnemy.maxHP}");
            Debug.Log($"  Total Turns: {result.totalTurns}");
        }

        private CombatResult GetLastCombatResult()
        {
            var ctx = PassiveSkillManager.Instance?.Context;
            return new CombatResult
            {
                enemyId = currentEnemy.id,
                enemyDisplayName = currentEnemy.displayName,
                playerWon = playerHP > 0 && enemyHP <= 0,
                totalTurns = turnLog.Count,
                playerHPRemaining = playerHP,
                enemyHPRemaining = enemyHP,
                turnLog = new List<TurnResult>(turnLog),
                healApplied = ctx?.healAppliedTotal ?? 0,
                shieldGained = ctx?.shieldGainedTotal ?? 0,
                damageDealt = Math.Max(0, (currentEnemy != null ? currentEnemy.maxHP : 0) - Math.Max(0, enemyHP)),
                damageTaken = Math.Max(0, playerMaxHP - Math.Max(0, playerHP)) + (ctx?.healAppliedTotal ?? 0),
                enemyMaxHP = currentEnemy != null ? currentEnemy.maxHP : 0,
                // プレイヤー敗北時のみ死因を記録 (勝利時は Normal)
                deathCause = (playerHP <= 0 && ctx != null) ? ctx.lastDamageCause
                           : InventorySystem.PassiveSkills.DeathCause.Normal,
                playerRollSum = _fightPlayerRollSum,
                playerRollCount = _fightPlayerRollCount,
                playerDamageBySource = BuildDamageBreakdown(ctx),
                strongRollTurns = _fightStrongTurns,
                strongRollBossWins = _fightStrongBossWins,
                weakRollTurns = _fightWeakTurns,
                weakRollBossWins = _fightWeakBossWins,
            };
        }

        /// <summary>被ダメ ソース別内訳のスナップショット (明示帰属のみ)。 メイン被ダメ(ロール敗北/断罪増幅)と
        /// 特殊スキル(審判の炎/毒/反射/王の業炎)が各自計上済み。 heal 込みのグロスで残差を取ると heal 分が
        /// Normal を過大計上するため、 残差寄せはしない。 ※診断対象の1〜6層は実質これで全被ダメを網羅。</summary>
        private Dictionary<InventorySystem.PassiveSkills.DeathCause, int> BuildDamageBreakdown(CombatContext ctx)
        {
            var map = new Dictionary<InventorySystem.PassiveSkills.DeathCause, int>();
            if (ctx == null) return map;
            foreach (var kv in ctx.playerDamageBySource) map[kv.Key] = kv.Value;
            return map;
        }

        // ===========================================================
        //  ユーティリティ
        // ===========================================================

        /// <summary>敵の攻撃ロール。 2026-07-28 以降の敵は **ダイスを振らず**、
        /// enemies.json の `attackRollMin`〜`attackRollMax` から一様乱数を 1 個引く (§6 の「波」)。
        /// 旧データ (attackRollMax 未設定) はダイス方式へ自動フォールバックする。
        ///
        /// - `extraDraws`: 旧「敵ダイス数+N」効果の受け皿。 同じ範囲から追加で引いて足す。
        /// - 弱ロール (ADR-0005): **上限だけ**を比率で縮める。 下限は据え置きなので
        ///   「弱いターンは上振れが消える」という意味になり、 面を一律縮小していた旧挙動と等価。
        /// 返り値が配列なのは LED 表示と enemyDice を読む既存パッシブの互換のため。</summary>
        /// <summary>〈貫きの錐〉: 敵の攻撃値 1 あたりに削るブロック量。</summary>
        public const int PierceAwlPerAttack = 2;

        /// <summary>**ラン内の**戦闘通し番号。 決定論的乱数のキーを戦闘ごとに分けるために使う。
        ///
        /// **ラン開始時に必ず 0 へ戻すこと** (<see cref="ResetRunCombatSequence"/>)。
        /// 2026-08-10 まではリセットが無く、 シングルトンなのでバッチ全体で増え続けていた。
        /// その結果 <see cref="RngIdx"/> が「そのランがバッチの何番目か」に依存し、
        /// **同じシード・同じ runIdx でも戦闘の乱数列が変わっていた**。
        /// 実測: 設定が同一の 2 アームで 94% のランが別結果、 7層クリアが 4.0pt 振れた。
        /// マップ/エンカウント/報酬は別キーなので一致し、 **戦闘に入った瞬間だけ分岐**する
        /// ── 症状が戦闘に限局するので、 集計値だけを見ていると気づけない。</summary>
        private int _combatSeq;

        /// <summary>ラン開始時に呼ぶ。 戦闘通し番号を 0 に戻す。
        /// これを飛ばすと同一シードのペア比較が成立しない (上記)。</summary>
        public void ResetRunCombatSequence() => _combatSeq = 0;

        /// <summary>決定論的乱数の連番。 (戦闘番号, ターン, スロット) で一意になる。
        /// slot は同一ターン内で複数回引く場合の識別子 (ダイスの本数など)。</summary>
        private int RngIdx(int slot)
            => (_combatSeq * 1000 + Math.Max(0, CurrentCombatTurn)) * 100 + slot;

        /// <summary>7層ヴェスカ 4 段連戦の段移行。 現在の敵 id から次段を引き、 あれば SwapEnemy する。
        /// 〈連続実験〉パッシブ (OnTurnEnd) の代替ではなく **最終防衛線**。
        /// 最終段 (p4) には次が無いので何もしない ＝ そこで初めて 7 層クリアになる。</summary>
        private bool TryAdvanceVescaChain()
        {
            string id = currentEnemy?.id;
            if (string.IsNullOrEmpty(id)) return false;
            string next = null, label = null;
            switch (id)
            {
                case "boss_layer7":    next = "boss_layer7_p2"; label = "第二段：遺物学者"; break;
                case "boss_layer7_p2": next = "boss_layer7_p3"; label = "第三段：神話";     break;
                case "boss_layer7_p3": next = "boss_layer7_p4"; label = "第四段：天与";     break;
                default: return false;   // p4 または非ヴェスカ
            }
            Debug.Log($"[覚者連戦] {id} 撃破 → {label} へ移行 (戦闘終了前に確定)");
            var ctx = PassiveSkillManager.Instance?.Context;
            // 計測は SwapEnemy 側で一元化 (ここで重ねると二重計上になる)
            SwapEnemy(next, label);
            if (ctx != null) SyncHPFromContext(ctx);
            return true;
        }

        private int[] RollEnemyAttack(EnemyData e, int extraDraws, bool weakRoll, float weakRatio)
        {
            if (e == null) return new int[0];
            if (e.UsesAttackRoll)
            {
                int hi = weakRoll ? EnemyStance.WeakRollMax(e.attackRollMax, weakRatio) : e.attackRollMax;
                return e.RollAttack(extraDraws, hi);
            }
            // ---- 旧ダイス方式 (互換パス) ----
            int[] faces = e.diceFaces;
            if (faces != null && faces.Length > 0)
                return RollDice(Math.Max(0, e.diceCount + extraDraws), 0,
                                weakRoll ? EnemyStance.WeakRollFaces(faces, weakRatio) : faces);
            int mx = weakRoll ? EnemyStance.WeakRollMax(e.diceMaxValue, weakRatio) : e.diceMaxValue;
            return RollDice(Math.Max(0, e.diceCount + extraDraws), mx);
        }

        /// <summary>[計装] **初回ロールの素の 5個同値率**。 リロール 0 回のターンで極が 4.8% 出ており、
        /// 理論値 0.077% (5個の d6) の 62 倍。 振り直していない以上ここで決まるはずなので、
        /// 本数・面配列の実体・出目分布のどれが効いているかを直に採る。</summary>
        public static long FirstRollCount, FirstRollYacht, FirstRollDiceCountSum, FirstRollFaceLenSum, FirstRollNoFaces;
        public static readonly long[] FirstRollByDiceCount = new long[16];
        public static readonly long[] FirstRollYachtByDiceCount = new long[16];
        public static readonly long[] FirstRollFaceHist = new long[32];

        public static void ResetFirstRollStats()
        {
            FirstRollCount = FirstRollYacht = FirstRollDiceCountSum = FirstRollFaceLenSum = FirstRollNoFaces = 0;
            System.Array.Clear(FirstRollByDiceCount, 0, FirstRollByDiceCount.Length);
            System.Array.Clear(FirstRollYachtByDiceCount, 0, FirstRollYachtByDiceCount.Length);
            System.Array.Clear(FirstRollFaceHist, 0, FirstRollFaceHist.Length);
        }

        private static void NoteFirstRoll(int count, InventorySystem.PassiveSkills.CombatContext ctx, int[] dice)
        {
            FirstRollCount++;
            FirstRollDiceCountSum += count;
            if (count >= 0 && count < FirstRollByDiceCount.Length) FirstRollByDiceCount[count]++;

            int faceLen = (ctx != null && ctx.equippedDiceFaces != null) ? ctx.equippedDiceFaces.Length : 0;
            FirstRollFaceLenSum += faceLen;
            if (faceLen == 0) FirstRollNoFaces++;

            if (dice != null)
                foreach (var v in dice)
                    if (v >= 0 && v < FirstRollFaceHist.Length) FirstRollFaceHist[v]++;

            if (YachtRoles.MaxSameCount(dice, out _, out _) >= 5)
            {
                FirstRollYacht++;
                if (count >= 0 && count < FirstRollYachtByDiceCount.Length) FirstRollYachtByDiceCount[count]++;
            }
        }

        /// <summary>出目パーツ T1: そのダイスが止まっている面が「振り直せない」面か。
        /// 面添字が無い (パーツ未実装経路・素のロール) なら常に false。</summary>
        private static bool IsLockedDie(CombatContext ctx, int[] faceIdx, int die)
        {
            if (ctx?.equippedFaceTiers == null || faceIdx == null) return false;
            if (die < 0 || die >= faceIdx.Length) return false;
            return GameLoop.DiceFaceParts.IsLocked(ctx.equippedFaceTiers, faceIdx[die]);
        }

        private int[] RollDice(int count, int maxValue, int[] diceFaces = null)
            => RollDice(count, maxValue, diceFaces, out _);

        /// <summary>ロールして、 **どの面に止まったか (添字)** も返す。
        ///
        /// <para>出目パーツ (2026-08-17) は「面の個体」に付くので、 値だけでは効果を引けない
        /// ── 素の <c>1</c> とパーツの <c>1</c> は別物。 面配列を使わない素のロールでは
        /// <paramref name="faceIdx"/> は <c>-1</c>。</para>
        ///
        /// <para><b>乱数の呼び方は変えていない。</b> 既に <c>Range</c> の戻り値で添字を引いていたので、
        /// それを捨てずに控えるだけ。 決定論シードの再現性は保たれる。</para></summary>
        private int[] RollDice(int count, int maxValue, int[] diceFaces, out int[] faceIdx)
        {
            var results = new int[count];
            faceIdx = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (diceFaces != null && diceFaces.Length > 0)
                {
                    int idx = GameLoop.GameRng.Range(0, diceFaces.Length, "combat.dieFace", RngIdx(30 + i));
                    faceIdx[i] = idx;
                    results[i] = diceFaces[idx];
                }
                else
                {
                    faceIdx[i] = -1;
                    results[i] = GameLoop.GameRng.Range(1, maxValue + 1, "combat.die", RngIdx(30 + i));
                }
            }
            return results;
        }

        /// <summary>ADR-0010: リロール段階。 **充電を払ってダイスを振り直す**。
        ///
        /// コストは `振り直す個数 × そのターンで何回目か`。 回数で逓増するので
        /// 「全部振り直しを 2 回」は上限 30 では届かず、 **狙った少数を刻む方が効率的**になる。
        /// これがヨットの「何を残すか」をコスト面からも支える。
        ///
        /// 振り直す対象は <see cref="RerollPolicy"/> が決める (BOT / UI が差す)。
        /// null なら振り直さない ── 旧実装は希望消費の自動ポリシーだったが、
        /// リロールのたびに発狂へ近づく形だとヨットの中核が経済ペナルティに潰される (§design-yacht)。
        /// </summary>
        private void RerollPhase(CombatContext ctx, int[] playerDice, int[] enemyDice, int playerDiceMax,
                                 int[] playerFaceIdx = null)
        {
            if (ctx == null || playerDice == null || playerDice.Length == 0) return;
            if (RerollPolicy == null) return;
            // 強制ロール中は対象外 (出た目が仕様で固定されているため)
            if (ctx.GetAccumulated("player_contre") > 0) return;

            // 予告を仮組みする。 敵ダイスはこの後変わらないので素の合計で作れる。
            int rawEnemyTotal = 0;
            if (enemyDice != null) for (int i = 0; i < enemyDice.Length; i++) rawEnemyTotal += enemyDice[i];
            var tele = new MutualTurnTelegraph
            {
                turn = ctx.currentTurn,
                enemyAttackValue = Escalation.EnemyAttackValue(currentEnemy, EscalationTurn(ctx), rawEnemyTotal, ctx.currentTurn),
                escalationStage = Escalation.StageOf(EscalationTurn(ctx)),
                nextThresholdTurn = Escalation.NextThresholdTurn(EscalationTurn(ctx)),
                turnsToHeavy = Escalation.TurnsToHeavy(currentEnemy, ctx.currentTurn),
                heavyMul = currentEnemy?.heavyMul ?? 0f,
                enemyDice = enemyDice,
                enemyDiceTotal = rawEnemyTotal,
            };

            // 〈中階〉(前ターン成立): 1 回目のリロールを無料にする。 **個数に上限を付けない** ──
            // 「何個振り直すか」の判断はそのままコストで効かせたいので、 免除するのは 1 回目だけ。
            bool freeReroll =
                ctx.currentBuffs.TryGetValue(YachtRoleEffects.FreeRerollKey, out float freeFlag) && freeFlag > 0f;

            int attempt = 1;
            int guard = 16;   // 方策が空でない配列を返し続けても止まるようにする
            while (guard-- > 0)
            {
                var pick = RerollPolicy(playerDice, tele, attempt);
                if (pick == null || pick.Length == 0) break;

                int cost = pick.Length * attempt;
                if (freeReroll) { cost = 0; freeReroll = false; }
                if (!ctx.ConsumeCharge(cost)) break;   // 払えなければそこで打ち切り

                for (int k = 0; k < pick.Length; k++)
                {
                    int i = pick[k];
                    if (i < 0 || i >= playerDice.Length) continue;
                    // 出目パーツ T1〈振り直せない〉: 方策が誤って指してきても、 ここで最終的に弾く。
                    //   方策側 (BOT / UI) にも同じ判定を置くが、 **規則の強制点はこの 1 箇所**。
                    if (IsLockedDie(ctx, playerFaceIdx, i)) continue;
                    if (ctx.equippedDiceFaces != null && ctx.equippedDiceFaces.Length > 0)
                    {
                        int idx = GameLoop.GameRng.Range(
                            0, ctx.equippedDiceFaces.Length, "combat.rerollFace", RngIdx(20 + i + attempt * 8));
                        playerDice[i] = ctx.equippedDiceFaces[idx];
                        if (playerFaceIdx != null && i < playerFaceIdx.Length) playerFaceIdx[i] = idx;
                    }
                    else
                    {
                        playerDice[i] = GameLoop.GameRng.Range(1, playerDiceMax + 1, "combat.reroll", RngIdx(20 + i + attempt * 8));
                        if (playerFaceIdx != null && i < playerFaceIdx.Length) playerFaceIdx[i] = -1;
                    }
                }
                RerollStats[0]++; RerollStats[1] += pick.Length; RerollStats[2] += cost;
                MetaProgression.Achievements.AchievementService.NoteReroll();
                Debug.Log($"[リロール] {attempt}回目 {pick.Length}個 / 充電-{cost} → 残{ctx.GetCharge()}");
                attempt++;
            }

            // [計装] このターンで何回刻んだか。 attempt は「次に何回目か」なので −1 が実回数。
            int did = Math.Max(0, attempt - 1);
            RerollPerTurn[Math.Min(8, did)]++;
            // 〈極〉が成立している手なら、 極側の分布にも積む
            // （振り直しが極の追跡に使われているかを、 全体分布との形の差で見る）。
            if (YachtRoles.MaxSameCount(playerDice, out _, out _) >= 5)
                RerollPerTurnYacht[Math.Min(8, did)]++;
        }

        /// <summary>ラストスタンド (メタ〈外殻〉r10): HP 0 なら<b>その場で蘇生して戦闘を続ける</b>。
        ///
        /// <para>2026-09-13 に戦闘終了後の救済チェーン
        /// (<see cref="GameLoop.LastStand.TryConsumeRevival"/>) からここへ移した。
        /// あちらは「戦闘を畳んでマップへ戻る」経路で、 <b>ボスノードは出力接続が無い</b>ため
        /// ボス戦で発動したランが移動先なしで詰んでいた (300 ラン中 61 件 = 20.3%)。
        /// 灯火とフルーレがボス戦で発動しないのは元々この理由で、
        /// ラストスタンドだけ解禁したのに退却経路は共通のままだった。</para>
        ///
        /// <para><b>呼ぶ位置。</b> 致死が確定するより前。 戦闘を閉じる判定の手前であれば、
        /// ターン終了の被ダメ適用後でもターン開始の刻限清算後でもよい ──
        /// <b>死因によって蘇生したりしなかったりする形にしないこと。</b></para></summary>
        private void TryLastStandRevive(CombatContext ctx)
        {
            if (playerHP > 0) return;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            bool isBoss = MapSystem.MapManager.Instance?.CurrentNode != null
                && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Boss;
            if (!GameLoop.LastStand.TryConsumeLastStandInCombat(run, isBoss)) return;

            playerMaxHP = run.playerMaxHP;
            playerHP = run.playerHP;
            if (ctx != null)
            {
                ctx.playerMaxHP = playerMaxHP;
                ctx.playerCurrentHP = playerHP;
            }
        }

        /// <summary>T4-A〈破綻〉: HP が閾値を下回っていたら**その時点の最大HP を半分にする**。
        ///
        /// 発動後は HP が新しい最大HP の 38% 相当になるので、 **その場で連鎖しない**。
        /// 次の発動は新しい最大HP の 20% を割ったときで、 踏むたびに難しくなる。
        /// 現在HP は下げない ── 半減で最大HP を下回ったぶんだけ切り詰める。</summary>
        private void ApplyBreakdownIfNeeded()
        {
            float th = MetaProgression.MetaDebuffApplicator.GetBreakdownThreshold();
            if (th <= 0f || playerHP <= 0) return;
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null || run.playerMaxHP <= 1) return;
            // **回数制限なし** (2026-08-11 差し戻し)。 1 回限りにしたところ 1000 ラン で
            //   590 回発動してなお T4 コスト −2.2pt しかなかった ── HP 20% を割る局面は
            //   既に負けが決まっており、 そこで 1 度だけ最大HP を半分にしても結果が動かない。
            //   複数回に戻すと「踏むたびに次の閾値が下がる」自己減速はそのままに、
            //   立て直しの余地だけが削られる。
            if (playerHP >= Mathf.CeilToInt(run.playerMaxHP * th)) return;

            int before = run.playerMaxHP;
            run.playerMaxHP = Math.Max(1, run.playerMaxHP / 2);
            run.breakdownCount++;
            MetaProgression.MetaDebuffApplicator.BreakdownTriggers++;
            run.playerHP = Math.Min(playerHP, run.playerMaxHP);
            playerMaxHP = run.playerMaxHP;
            playerHP = run.playerHP;
            Debug.Log($"[破綻] HP {playerHP} が閾値 {th:P0} を割った → 最大HP {before} → {run.playerMaxHP}"
                    + $" (通算 {run.breakdownCount} 回目)");
        }

        /// <summary>T4-E〈最後の審判〉のラン累計戦闘ターンを進める。
        /// 刻限後の罰は各被ダメージの最終適用時に乗せる。</summary>
        private void ApplyFinalJudgment()
        {
            var run = GameLoop.GameManager.Instance?.Run;
            if (run == null) return;
            run.totalCombatTurns++;
            MetaProgression.MetaDebuffApplicator.JudgmentTurnsTotal++;
            int th = MetaProgression.MetaDebuffApplicator.GetJudgmentTurnThreshold();
            // 刻限の分布は**デバフの有無に関わらず**採る (閾値を決める材料なので)。
            if (th <= 0)
            {
                const int ProbeThreshold = 120;
                if (run.totalCombatTurns == ProbeThreshold)
                    MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold++;
                return;
            }
            if (run.totalCombatTurns == th)
                MetaProgression.MetaDebuffApplicator.JudgmentRunsOverThreshold++;
            if (run.totalCombatTurns == th)
                Debug.Log($"[最後の審判] 刻限到来 (累計{run.totalCombatTurns}T >= {th}) → 以後の被ダメージ×1.30");
        }

        /// <summary>[計装] リロールの実測。 0=回数 / 1=振り直したダイス総数 / 2=消費充電。
        /// ADR-0010 Verification で「リロール経済が回っているか」を見るため。</summary>
        public static readonly long[] RerollStats = new long[3];

        /// <summary>[計装] 装備維持費。 Due=請求額 / Paid=実際に払えた額。
        /// 差が大きいほど「充電が足りずに踏み倒している」＝維持費が重すぎる目安。</summary>
        public static long PassiveUpkeepDue, PassiveUpkeepPaid;

        /// <summary>[計装] 1 ターンあたりのリロール回数の分布 (添字 = 回数、 8 以上は 8 に丸め)。
        /// **平均では分布が分からない** ── 全体平均 0.67 回/ターンでも、
        /// 極が成立する少数のターンだけ 3〜5 回刻んでいる可能性がある。
        /// 「2 回制限」が拘束力を持つかは、 3 回以上の帯の厚みで決まる。</summary>
        public static readonly long[] RerollPerTurn = new long[9];
        /// <summary>そのターンで〈極〉が成立したときのリロール回数分布 (同上)。
        /// 上の分布と比べて右に寄っていれば、 **振り直しが極の追跡に使われている**。</summary>
        public static readonly long[] RerollPerTurnYacht = new long[9];

        // 役の判定で使う一時バッファ。 毎ターン new すると GC を踏むので使い回す。
        private readonly List<RoleKind> _formedRoles = new List<RoleKind>(16);
        private readonly List<int> _groupBuf = new List<int>(8);

        /// <summary>ADR-0010: 役を判定し、 方策が切ると決めたものを適用する。
        ///
        /// 手順:
        ///   1. 手札役 (5個全体) / 端子役 (攻撃・ブロックの各組) / 配線役 (盤面) を集める
        ///   2. **既に使った役を除く** (1 戦闘 1 回)
        ///   3. <see cref="RolePolicy"/> に「どれを切るか」を決めさせる (**任意・見送り自由**)
        ///   4. 切った役の効果を適用し、 usedRoles へ記録する
        ///
        /// 同じ役が 2 端子で同時成立したら **1 回消費で両方発動**する (択一にしない)。
        /// 分割で二重取りするのは配線という行為そのものへの報酬なので、 罰する理由がない。</summary>
        private RoleOutcome ResolveRoles(CombatContext ctx, int[] playerDice, DiceTerminal[] wiring,
                                         MutualTurnTelegraph tele,
                                         ref int attackSum, ref int blockSum, int enemyAttackValue)
        {
            var outcome = new RoleOutcome();
            if (ctx == null || playerDice == null || playerDice.Length == 0) return outcome;

            YachtRoleEffects.TurnsEvaluated++;
            YachtRoleEffects.NoteTurn(tele.turn, enemyAttackValue);

            // 端子ごとの組を作る
            var attackGroup = CollectGroup(playerDice, wiring, DiceTerminal.Attack);
            var blockGroup  = CollectGroup(playerDice, wiring, DiceTerminal.Block);

            _formedRoles.Clear();
            YachtRoles.EvaluateHand(playerDice, _formedRoles);
            // 〈無銘の賽〉は端子役を成立させない
            if (!ctx.suppressTerminalRoles)
            {
                YachtRoles.EvaluateTerminal(attackGroup, _formedRoles);
                YachtRoles.EvaluateTerminal(blockGroup, _formedRoles);
            }
            YachtRoles.EvaluateWiring(attackSum, blockSum, enemyAttackValue,
                                      ctx.enemyCurrentHP, _formedRoles);

            // 重複を潰しつつ、 使用済みと**封印された役**を除く
            var candidates = new List<RoleKind>(_formedRoles.Count);
            for (int i = 0; i < _formedRoles.Count; i++)
            {
                var k = _formedRoles[i];
                if (candidates.Contains(k)) continue;
                YachtRoleEffects.FormedCount[(int)k]++;
                // 発動後の再成立を数える (温存の余地の直接測定)。 使用済みでも呼ぶ。
                YachtRoleEffects.NoteFormed(k, tele.turn, enemyAttackValue);
                if (ctx.usedRoles.Contains(k)) continue;
                // T4-C〈凶運〉: このランで封印されている役は成立しても切れない。
                if ((ctx.sealedRoleMask & (1 << (int)k)) != 0) continue;
                candidates.Add(k);
            }
            if (candidates.Count == 0) return outcome;

            var fire = RolePolicy?.Invoke(candidates, tele, ctx.usedRoles);
            if (fire == null || fire.Count == 0) return outcome;

            for (int i = 0; i < fire.Count; i++)
            {
                var k = fire[i];
                if (!candidates.Contains(k)) continue;   // 成立していない / 使用済みは弾く
                if (ctx.usedRoles.Contains(k)) continue;

                var scope = YachtRoles.ScopeOf(k);
                if (scope == RoleScope.Terminal)
                {
                    // **2 端子で成立していれば両方発動** (消費は 1 回)
                    if (TerminalHas(attackGroup, k))
                        YachtRoleEffects.Apply(k, DiceTerminal.Attack, ctx, attackGroup,
                                               attackSum, blockSum, enemyAttackValue, ref outcome);
                    if (TerminalHas(blockGroup, k))
                        YachtRoleEffects.Apply(k, DiceTerminal.Block, ctx, blockGroup,
                                               attackSum, blockSum, enemyAttackValue, ref outcome);
                }
                else
                {
                    YachtRoleEffects.Apply(k, DiceTerminal.Attack, ctx, playerDice,
                                           attackSum, blockSum, enemyAttackValue, ref outcome);
                }

                ctx.usedRoles.Add(k);
                ctx.firedRolesThisTurn.Add(k);
                YachtRoleEffects.FiredCount[(int)k]++;
                // [計装 2026-08-15] 〈極〉のダイス別発動は**ここで採らない**。
                //   役の発動はホットパスで、 ここから GameManager.Instance を引くと
                //   1 ラン 8.7 回 × ラン数だけシングルトン解決が走る。
                //   装備ダイスは戦闘中に変わらないので、 FinishCombat で戦闘単位に数える
                //   （呼び出しが 8.7 万 → 3.3 万 に減り、 既存の run 参照を使い回せる）。
                YachtRoleEffects.FiredThisCombat[(int)k]++;
                YachtRoleEffects.NoteFireTiming(k, tele.turn, enemyAttackValue);
                MetaProgression.Achievements.AchievementService.NoteRoleFired(k);
            }

            attackSum = Math.Max(0, attackSum + outcome.attackSumDelta);
            blockSum  = Math.Max(0, blockSum  + outcome.blockSumDelta);

            if (ctx.firedRolesThisTurn.Count > 0)
            {
                var names = new string[ctx.firedRolesThisTurn.Count];
                for (int i = 0; i < names.Length; i++) names[i] = YachtRoles.NameOf(ctx.firedRolesThisTurn[i]);
                Debug.Log($"[役] T{ctx.currentTurn} 発動: {string.Join("/", names)}"
                        + $"  攻{attackSum} 防{blockSum}");
            }
            return outcome;
        }

        /// <summary>配線の後始末。 **方策や UI が返した配線を信用しない**。
        ///
        ///   ① 特殊端子 (§6-5) の接続制限 N を超えた分を攻撃へ落とす。 未装着なら N=0 ＝ 全部落とす
        ///   ② 挑戦デバフ〈不器用〉の「同時に配線できる端子の上限」を強制する
        ///
        /// ② は **使う端子を減らす**ので、 どれを捨てるかで結果が変わる。
        /// 上限に収まらなかった端子のうち **本数の少ない方から**畳んで攻撃へ寄せる ──
        /// 厚く挿した端子ほどプレイヤーの意図が強いとみなす。</summary>
        /// <summary>ゴースト接続の検算。 **方策/UI を信用しない。**
        ///
        /// <para>規則は 2 つだけ。
        /// ① ゴーストを持てるのは <b>T3 / T4 の面に止まったダイス</b>だけ。
        /// ② <b>同一端子への重ねは T4 だけ</b> ── T3 は「異なる 2 端子」なので、
        ///    実体と同じ端子を指してきたらゴーストを剥がす。</para>
        ///
        /// <para>違反は握り潰さずログに出す。 方策のバグが黙って通ると、
        /// 「なぜか合計値が合わない」だけが残って原因に辿り着けない。</para></summary>
        private void SanitizeGhost(CombatContext ctx, int[] ghost, int[] faceIdx, int diceCount)
        {
            if (ghost == null) return;
            var tiers = ctx?.equippedFaceTiers;
            for (int i = 0; i < ghost.Length && i < diceCount; i++)
            {
                if (ghost[i] == WiringPlan.NoGhost) continue;

                int fi = (faceIdx != null && i < faceIdx.Length) ? faceIdx[i] : -1;
                if (!GameLoop.DiceFaceParts.AllowsDualLink(tiers, fi))
                {
                    Debug.LogWarning($"[配線] ダイス{i + 1} は 2 接続できない面なのにゴーストが来た → 剥がす");
                    ghost[i] = WiringPlan.NoGhost;
                    continue;
                }
                bool sameTerminal = i < mutualWiring.Length && (int)mutualWiring[i] == ghost[i];
                if (sameTerminal && !GameLoop.DiceFaceParts.AllowsSameTerminalStack(tiers, fi))
                {
                    Debug.LogWarning($"[配線] ダイス{i + 1} は T3 なので同一端子へ重ねられない → ゴーストを剥がす");
                    ghost[i] = WiringPlan.NoGhost;
                }
            }
        }

        private void SanitizeWiring(DiceTerminal[] wiring, int diceCount)
        {
            if (wiring == null) return;
            int n = Math.Min(diceCount, wiring.Length);

            // ① 特殊端子の接続制限
            int limit = SpecialTerminals.EquippedLimit(GameLoop.GameManager.Instance?.Run);
            int used = 0;
            for (int i = 0; i < n; i++)
            {
                if (wiring[i] != DiceTerminal.Special) continue;
                if (used < limit) { used++; continue; }
                wiring[i] = DiceTerminal.Attack;
            }

            // ② 〈綻び〉: 封印された端子へ挿さっているダイスを追い出す。
            //   **方策/UI を信用しない** ── 封印を破った配線が来たら別の端子へ落とす。
            {
                int kinds = limit > 0 ? 4 : 3;
                int sealed_ = MetaProgression.MetaDebuffApplicator.GetSealedTerminal(
                    PassiveSkillManager.Instance?.Context?.currentTurn ?? 0, kinds);
                if (sealed_ >= 0)
                {
                    // 逃がし先は攻撃。 攻撃が封印されているなら充電へ (最も無害な受け皿)。
                    var fallback = (sealed_ == (int)DiceTerminal.Attack)
                                 ? DiceTerminal.Charge : DiceTerminal.Attack;
                    for (int i = 0; i < n; i++)
                        if ((int)wiring[i] == sealed_) wiring[i] = fallback;
                }
            }
            // (旧〈不器用〉の端子上限は撤去済み。)
            //   2026-08-10 リワーク前は「同時に使える端子の種類数の上限」で、 ここで畳んでいた。
            //   新効果は**厚みへの後払い**なので、 配線を検閲せず ResolveClumsyOverload が
            //   次ターンの合計へ罰を掛ける。 配線の自由は残す。

            // (〈不完全な起動〉の端子ごとの本数上限は 2026-09-14 に撤去。
            //   5 ダイスなら 2+2+1 で置けてしまい全振りとの差が小さく、
            //   1 本上限にすると 3 端子へ 5 本置けず**配線不能**になる。
            //   罰は賽の本数 (GateFlaws.DiceCountAfterIgnition) へ移した。)
        }

        /// <summary>特殊端子 (§6-5) の効果を適用する。 効果表の正本は
        /// <see cref="SpecialTerminals"/> と docs/GAME.md §6-5。
        ///
        /// **特殊端子は端子役 (ADR-0010) を成立させない。** 固有効果と接続制限を既に持っており、
        /// そこへ役まで乗せると同じダイスで二重に報われる。 接続制限が最大 3 本なので
        /// 分布系の役 (3 本以上) がちょうど成立してしまう点も避けたい。</summary>
        private void ApplySpecialTerminal(CombatContext ctx, int[] dice, int sum, int count,
                                          ref int attackSum, ref int blockSum, ref int chargeSum)
        {
            var run = GameLoop.GameManager.Instance?.Run;
            var def = SpecialTerminals.Equipped(run);
            if (def == null || ctx == null || sum <= 0) return;

            switch (def.kind)
            {
                case SpecialTerminalKind.HeavyStrike:
                {
                    int add = Mathf.CeilToInt(sum * SpecialTerminals.HeavyStrikeMultiplier);
                    attackSum += add;
                    Debug.Log($"[特殊端子/重攻撃] 出目{sum} ×{SpecialTerminals.HeavyStrikeMultiplier} → 攻撃 +{add}");
                    break;
                }
                case SpecialTerminalKind.FullGuard:
                {
                    int add = sum * SpecialTerminals.FullGuardMultiplier;
                    blockSum += add;
                    Debug.Log($"[特殊端子/完全防御] 出目{sum} ×{SpecialTerminals.FullGuardMultiplier} → ブロック +{add}");
                    break;
                }
                case SpecialTerminalKind.Bleed:
                {
                    ctx.enemyBleedStacks += sum;
                    // 接続**本数**ぶんだけ即時に発動させる (合計値ではない ── 制限Ⅱなので最大 2 回)
                    for (int k = 0; k < count && ctx.enemyBleedStacks > 0; k++)
                    {
                        int dmg = BattleModifierManager.ApplyBleedModifiers(ctx, ctx.enemyBleedStacks);
                        enemyHP = Math.Max(0, enemyHP - dmg);
                        ctx.enemyCurrentHP = enemyHP;
                    }
                    Debug.Log($"[特殊端子/出血] +{sum} スタック (計 {ctx.enemyBleedStacks}) / 即時 {count} 回");
                    break;
                }
                case SpecialTerminalKind.Heal:
                    HealPlayer(sum);
                    Debug.Log($"[特殊端子/治癒] HP +{sum}");
                    break;

                case SpecialTerminalKind.Deathwish:
                {
                    int missing = Math.Max(0, playerMaxHP - playerHP);
                    int bonus = Mathf.FloorToInt(missing * SpecialTerminals.DeathwishMissingHpPct);
                    // 自傷は **HP を 1 未満にしない** ── 端子を挿しただけで死ぬのは選択にならない
                    int selfDmg = Math.Min(sum, Math.Max(0, playerHP - 1));
                    playerHP -= selfDmg;
                    ctx.playerCurrentHP = playerHP;
                    attackSum += sum + bonus;
                    Debug.Log($"[特殊端子/決死] 自傷{selfDmg} → 攻撃 +{sum}+{bonus}(不足HP{missing}の5%)");
                    break;
                }
                case SpecialTerminalKind.VitalPoint:
                {
                    int min = int.MaxValue;
                    for (int i = 0; i < dice.Length; i++) if (dice[i] < min) min = dice[i];
                    // 接続したダイスが **すべてそのロールの最小値** のときだけ発動
                    bool allMin = true;
                    for (int i = 0; i < dice.Length && allMin; i++)
                        if (i < mutualWiring.Length && mutualWiring[i] == DiceTerminal.Special
                            && dice[i] != min) allMin = false;
                    if (allMin)
                    {
                        int dmg = sum * SpecialTerminals.VitalPointMultiplier;
                        ctx.fixedDamageToEnemy += dmg;
                        Debug.Log($"[特殊端子/急所] 最小値{min} を接続 → 軽減不可 {dmg}");
                    }
                    break;
                }
                case SpecialTerminalKind.Aim:
                {
                    int pts = Mathf.CeilToInt(sum / 2f);
                    ctx.critRatePctAdd += pts / 100f;
                    DmgSourceDiag.Note(DmgSourceDiag.AimKey, DmgSourceDiag.CritRate, pts / 100f);
                    Debug.Log($"[特殊端子/照準] 会心率 +{pts}pt");
                    break;
                }
                case SpecialTerminalKind.Battery:
                    chargeSum += sum + count * SpecialTerminals.BatteryPerDice;
                    Debug.Log($"[特殊端子/蓄電池] 充電 +{sum} +{count * SpecialTerminals.BatteryPerDice}");
                    break;

                case SpecialTerminalKind.Coolant:
                {
                    float acc = ctx.GetAccumulated(CoolantAccKey) + sum;
                    int gained = 0;
                    while (acc >= SpecialTerminals.CoolantThreshold)
                    { acc -= SpecialTerminals.CoolantThreshold; gained++; }
                    ctx.accumulatedValues[CoolantAccKey] = acc;
                    if (gained > 0)
                    {
                        ctx.accumulatedValues[CoolantDelayKey] =
                            ctx.GetAccumulated(CoolantDelayKey) + gained;
                        Debug.Log($"[特殊端子/冷却材] エスカレーションを {gained}T 遅延 "
                                + $"(累計 {(int)ctx.GetAccumulated(CoolantDelayKey)}T)");
                    }
                    break;
                }
                case SpecialTerminalKind.Foundry:
                {
                    float acc = ctx.GetAccumulated(FoundryAccKey) + sum;
                    int gained = 0;
                    while (acc >= SpecialTerminals.FoundryThreshold)
                    { acc -= SpecialTerminals.FoundryThreshold; gained++; }
                    ctx.accumulatedValues[FoundryAccKey] = acc;
                    if (gained > 0)
                    {
                        ctx.accumulatedValues[FoundryBonusKey] =
                            ctx.GetAccumulated(FoundryBonusKey) + gained;
                        Debug.Log($"[特殊端子/鋳造口] この戦闘中の出目 +{gained} "
                                + $"(累計 +{(int)ctx.GetAccumulated(FoundryBonusKey)})");
                    }
                    break;
                }
                case SpecialTerminalKind.Riposte:
                    // 反射量の上限を積むだけ。 実際の反射は被ダメ確定後に行う
                    ctx.accumulatedValues[RiposteKey] = sum;
                    break;
            }
        }

        /// <summary>特殊端子が accumulatedValues に置くキー。 **フィールドを増やさない**
        /// (CombatContext の 3 区分規約: set/read が 1〜2 箇所なら辞書へ)。</summary>
        private const string CoolantAccKey   = "termCoolantAcc";
        private const string CoolantDelayKey = "termCoolantDelay";
        private const string FoundryAccKey   = "termFoundryAcc";
        private const string FoundryBonusKey = "termFoundryBonus";
        private const string RiposteKey      = "termRiposteCap";

        /// <summary>端子上限の畳み込みで使う本数バッファ。 毎ターン new しないため使い回す。</summary>
        private readonly int[] _termCountBuf = new int[4];

        /// <summary>エスカレーション判定に使うターン。 〈冷却材〉の遅延を差し引く。
        /// **1 を下回らせない** ── 段階 0 より前は存在しない。</summary>
        private int EscalationTurn(CombatContext ctx)
        {
            if (ctx == null) return 1;
            int delay = (int)ctx.GetAccumulated(CoolantDelayKey);
            return Math.Max(1, ctx.currentTurn - delay);
        }

        /// <summary>指定端子へ配線されたダイスの組を返す。</summary>
        private int[] CollectGroup(int[] dice, DiceTerminal[] wiring, DiceTerminal want)
        {
            _groupBuf.Clear();
            for (int i = 0; i < dice.Length; i++)
            {
                var t = (wiring != null && i < wiring.Length) ? wiring[i] : DiceTerminal.Attack;
                if (t == want) _groupBuf.Add(dice[i]);
            }
            return _groupBuf.ToArray();
        }

        /// <summary>その組で当該の端子役が成立しているか。</summary>
        private static bool TerminalHas(int[] group, RoleKind k)
        {
            if (group == null || group.Length == 0) return false;
            var tmp = new List<RoleKind>(8);
            YachtRoles.EvaluateTerminal(group, tmp);
            return tmp.Contains(k);
        }

        /// <summary>ロール前の推定勝率（正規近似・ADR-0006）。P(自合計 > 敵合計) を、両者のダイス期待値・分散から
        /// 正規近似＋ロジスティックCDFで概算する。Might等のロール後フラット加算は無視（学習閾値が平均バイアスを吸収）。
        /// 敵の弱ロール（面縮小）は eMax に反映済みで渡る。</summary>
        private float EstimateWinProbability(int pCount, int pMax, int[] pFaces, int eCount, int eMax)
        {
            DiceMoments(pCount, pMax, pFaces, out double muP, out double varP);
            DiceMoments(eCount, eMax, null,   out double muE, out double varE);
            double diffMu = muP - muE;
            double sigma = System.Math.Sqrt(varP + varE);
            if (sigma < 1e-6) return diffMu > 0 ? 1f : (diffMu < 0 ? 0f : 0.5f);
            double z = diffMu / sigma;
            double p = 1.0 / (1.0 + System.Math.Exp(-1.702 * z)); // 標準正規CDFのロジスティック近似
            return (float)System.Math.Max(0.0, System.Math.Min(1.0, p));
        }

        /// <summary>ダイス合計の平均・分散。faces 指定時はその面集合、無ければ一様 1..maxValue。</summary>
        private void DiceMoments(int count, int maxValue, int[] faces, out double mean, out double variance)
        {
            double m, v;
            if (faces != null && faces.Length > 0)
            {
                double s = 0, s2 = 0;
                for (int i = 0; i < faces.Length; i++) { s += faces[i]; s2 += (double)faces[i] * faces[i]; }
                m = s / faces.Length;
                v = s2 / faces.Length - m * m;
            }
            else
            {
                if (maxValue < 1) maxValue = 1;
                m = (maxValue + 1) / 2.0;
                v = (maxValue * (double)maxValue - 1.0) / 12.0; // 一様1..M の分散
            }
            mean = count * m;
            variance = count * v;
        }

        private void SyncHPFromContext(CombatContext ctx)
        {
            if (ctx == null) return;
            playerHP = ctx.playerCurrentHP;
            playerMaxHP = ctx.playerMaxHP;
            enemyHP = ctx.enemyCurrentHP;
        }

        private void LogTurnResult(TurnResult r)
        {
            string playerDiceStr = r.playerDice != null ? string.Join("+", r.playerDice) : "?";
            string enemyDiceStr = r.enemyDice != null ? string.Join("+", r.enemyDice) : "?";
            string critStr = r.isCritical ? " ★CRITICAL★" : "";
            string winStr = r.isDraw ? "DRAW" : (r.playerWon ? "Player WIN" : "Enemy WIN");

            Debug.Log($"[Turn {r.turnNumber}] {winStr}{critStr}");
            Debug.Log($"  Player Dice: [{playerDiceStr}] = {r.playerDiceTotal}");
            Debug.Log($"  Enemy  Dice: [{enemyDiceStr}] = {r.enemyDiceTotal}");
            if (!r.isDraw)
            {
                Debug.Log($"  Main: {r.mainDamage}, Pursuit: {r.pursuitDamage}, " +
                          $"Total: {r.totalDamage}, Fixed: {r.fixedDamage}, Scratch: {r.scratchDamage}");
            }
            Debug.Log($"  Player HP: {r.playerHPAfter}, Enemy HP: {r.enemyHPAfter}");
        }

        void OnDestroy()
        {
            if (instance == this) 
            {
                instance = null;
                // イベント購読者のクリーンアップ
                OnCombatStart = null;
                OnTurnEnd = null;
                OnCombatEnd = null;
            }
        }
        
        void OnApplicationQuit()
        {
            isApplicationQuitting = true;
            if (instance == this) instance = null;
        }

    }
}
