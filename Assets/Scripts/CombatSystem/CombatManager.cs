using System;
using System.Collections.Generic;
using UnityEngine;
using ItemSystem;
using ItemSystem.PassiveSkills;

namespace CombatSystem
{
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
    public partial class CombatManager : MonoBehaviour
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

        // ── 逃げる (2026-09-28・docs/GAME.md §6-3) ──
        //   プレイヤーの手番 (配線の前) に選べる。 そのターンはこちらの攻撃が発生せず、 敵の予告攻撃は
        //   ブロック有効で受ける (配線したブロック端子で軽減できる)。 生き残ったらそこで戦闘を抜ける。
        //   代償 (物資 −100・ゴールド半減・報酬なし・直前の点へ戻る) は GameManager 側。
        private bool _fleeRequested;
        /// <summary>この戦闘で逃げられるか。 GameManager が戦闘開始時に立てる
        /// (航行マップの層の戦闘・エリート・追跡者／使い魔の戦闘だけ。 層のボス・イベント戦では false)。</summary>
        public bool FleeAllowed { get; set; }
        /// <summary>このターンに逃げると宣言済みか。</summary>
        public bool FleeRequested => _fleeRequested;
        /// <summary>直前の戦闘が逃げて終わったか。 CombatResult.playerFled と同じ値。</summary>
        public bool LastCombatFled { get; private set; }

        /// <summary>逃げると宣言する。 次の <see cref="ExecuteTurn"/> がそのターンを逃げとして解決する。
        /// 層のボスからは逃げられない (<see cref="FleeAllowed"/> が false)。</summary>
        public bool RequestFlee()
        {
            if (!isCombatActive || !FleeAllowed) return false;
            if (currentEnemy != null && GameLoop.BossIds.IsBoss(currentEnemy.id)) return false;
            _fleeRequested = true;
            return true;
        }
        private long _fightPlayerRollSum;    // ボス難易度チューナー: この戦闘のプレイヤーロール合計の累計
        private int _fightPlayerRollCount;   // 同 ロール回数 (平均出目 = sum/count)
        // ボス難易度チューナー: スタンス別の「ボスがロール勝ちしたターン数 / そのスタンスの総ターン数」(強/弱で別レンジ管理)
        private int _fightStrongTurns, _fightStrongBossWins, _fightWeakTurns, _fightWeakBossWins;
        private bool wrathDiceOverrideArmed; // 恒久デバフ「憤怒」: 1T目のダイス最大化トリガー
        private int playerDiceCount;
        private int playerDiceMax;
        private int playerAttackPower = 2;            // #2 案A': 装備武器の素火力（勝利base = attackPower + floor(|差|/3)）
        /// <summary>装備武器の防御・貫通 (ADR-0013)。 戦闘開始時に解決し、 ctx の土台へ写す。</summary>
        private int _weaponDefense, _weaponArmorPen;
        private const int WeaponDiffPerBonus = 3;     // #2 案A': 差ボーナス＝floor(|差|/3)（差3ごとに+1）。会心には非干渉
        /// <summary>武器 + 層補正の会心率 (小数)。 パッシブ・メタ分は判定時に足す。</summary>
        private float playerCritBaseRate;
        private bool isCombatActive;

        // パリィ / オーバーロード (タイミング判定) は ADR-0010 で全廃した。
        //   反射神経を要求する技量帯を、 ダイス 5 個をどう分割して役を作るかという
        //   **判断の技量帯**へ移すのが ADR-0010 の主眼で、 両者を併存させると
        //   「上手さ」の定義が二重になる。 予見メタ軸もこれに連座して消えた (§24)。

        private List<TurnResult> turnLog = new List<TurnResult>();

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
        //   反動を HP でも 物資 でも成立させられず、 〈戦意〉(累積成長) へ差し替えた。
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
            var e = CombatSystem.BossParams.Apply(secondary).Clone();

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
            // ADR-0011 〈富〉(目 9) の報酬加算は戦闘ごと。 持ち越さない。
            { var goldRun = GameLoop.GameManager.Instance?.Run; if (goldRun != null) goldRun.roleGoldBonusPct = 0; }

            // ボス難易度オートチューナー: hpMul を maxHP に適用 (非ボスは素通り)
            enemy = CombatSystem.BossParams.Apply(enemy);

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
            ItemSystem.PassiveSkills.RunPassiveSync.RefreshFromRun(GameLoop.GameManager.Instance?.Run);

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
            if (_bd7F) System.Array.Copy(CombatStats.DamageBreakdown, _bdAtCombatStart, CombatStats.DamageBreakdown.Length);
            enemyHP = enemy.maxHP;
            // [計測] 7層連戦の 1 段目突入。 2 段目以降との比較の基準になる。
            if (enemy != null && enemy.id == "boss_layer7")
                BossDmgDiag.PhaseEntry(enemy.id, this.playerMaxHP > 0 ? (double)playerHP / this.playerMaxHP : 1.0);
            playerDiceCount = pDiceCount;
            playerDiceMax = pDiceMax;
            playerCritBaseRate = pCritRate;

            // #2 案A': 装備武器の attackPower を解決（RunState.equippedWeaponId・既定2）
            playerAttackPower = 2;
            _weaponDefense = 0; _weaponArmorPen = 0;
            {
                ItemSystem.CompleteItemData apW = null;
                string wid = GameLoop.GameManager.Instance?.Run?.equippedWeaponId;
                if (!string.IsNullOrEmpty(wid)) apW = ItemSystem.ItemDatabase.Instance?.GetItem(wid);
                if (apW != null && apW.attackPower > 0) playerAttackPower = apW.attackPower;
                // 武器の防御・貫通 (ADR-0013)。 "+" 段階で 2 要素目を使う。
                if (apW != null)
                {
                    int plus = GameLoop.GameManager.Instance?.Run?.weaponPlus ?? 0;
                    _weaponDefense = apW.DefenseAt(plus);
                    _weaponArmorPen = apW.ArmorPenAt(plus);
                }
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
            _fleeRequested = false;
            LastCombatFled = false;
            FleeAllowed = false;   // 戦闘ごとに GameManager が立て直す (既定は逃げられない)
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
            // 剣家系〈一対一〉が読む。 **戦闘中は変わらない [persistent]**。
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

            // 物資(ADR-0002) 迷妄: 絶望帯(物資≤20)以降、戦闘開始時にプレイヤーパッシブを1-3個ランダム無効化。
            // 佯狂者は PassiveItem 系統のため対象外（activeSkillNames に含まれない）。
            int delusionCount = GameLoop.ProvisionSystem.RollPassiveDisableCount(GameLoop.GameManager.Instance?.Run);
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
            // 剣家系〈一対一〉の判定材料。 **戦闘開始時に 1 度だけ確定する [persistent]**
            //   ── secondaryEnemy は上で解決済みなので、 ここで写せば戦闘中ぶれない。
            if (ctx != null) ctx.isPairEncounter = secondaryEnemy != null;
            // 防御値と貫通 (ADR-0013)。 敵の防御は〈精鋭〉〈硬鱗〉〈重甲〉が OnBattleStart で足す。
            //   プレイヤーの土台 = 初期値 + 武器。 アイテムの def / pen は毎ターン OnPostRoll で乗る。
            if (ctx != null)
            {
                ctx.enemyDefense = Math.Max(0, enemy.defense);
                ctx.enemyArmorPen = Math.Max(0, enemy.armorPen);
                ctx.playerDefenseBase = DefenseFormula.PlayerBaseDefense + _weaponDefense;
                ctx.playerArmorPenBase = _weaponArmorPen;
                ctx.playerDefense = ctx.playerDefenseBase;
                ctx.playerArmorPen = ctx.playerArmorPenBase;
                // ボス難易度オートチューナー: 軸別係数引き用にボスidを記録 (非ボスは空)
                ctx.bossId = CombatSystem.BossParams.IsBoss(enemy.id) ? enemy.id : "";
                ctx.lastDamageCause = ItemSystem.PassiveSkills.DeathCause.Normal;

                // 物資(ADR-0002) 苦悩: 悲観床(45)以下で会心倍率 -0.5。
                ctx.criticalMultiplier += GameLoop.ProvisionSystem.GetCritMultiplierDelta(GameLoop.GameManager.Instance?.Run);
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
                    ctx.AddStatus(ItemSystem.PassiveSkills.StatusTarget.Enemy, "poison", rp);
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
            ItemSystem.PassiveItems.PassiveItemManager.OnCombatStart(
                ctx, GameLoop.GameManager.Instance?.Run, this);

            OnCombatStart?.Invoke(enemy.id);
            MetaProgression.Achievements.AchievementService.NoteCombatStarted();

            
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

        /// <summary>戦闘中にエネミーを差し替える（ヴェスカの 4 連戦で使用）。
        /// プレイヤー状態(HP/ダイス/buffs/currentTurn) は完全保持。
        /// 敵側パッシブと敵固有の accumulatedValues キーは新エネミーのものに置換。</summary>
        /// <param name="newEnemyId">enemies.json の id</param>
        /// <param name="logLabel">遷移ログに使うラベル(例:"ヴェスカ第二形態へ")</param>
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
            // ボス難易度オートチューナー: ヴェスカ連戦の各形態にも hpMul を適用
            newEnemy = CombatSystem.BossParams.Apply(newEnemy);

            string prevName = currentEnemy?.displayName ?? "?";
            currentEnemy = newEnemy;
            enemyHP = newEnemy.maxHP;

            var psm = PassiveSkillManager.Instance;
            if (psm == null) { Debug.LogError("[CombatManager.SwapEnemy] PSM null"); return false; }

            // 敵側パッシブを差し替え（プレイヤー側パッシブは保持）。
            // ボス連戦(ヴェスカ)でも狂暴化を維持する。
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
                ctx.enemyDefense = Math.Max(0, newEnemy.defense);   // 新形態の防御値を反映 (ADR-0013)
                ctx.enemyArmorPen = Math.Max(0, newEnemy.armorPen);
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
                ctx.bossId = CombatSystem.BossParams.IsBoss(newEnemy.id) ? newEnemy.id : "";
                ctx.lastDamageCause = ItemSystem.PassiveSkills.DeathCause.Normal; // 形態swapで死因タグをリセット (前形態の残留防止)
                // (ADR-0011: 役は毎ターン自動で効き、 使用済みの管理を持たないので、 形態交代で戻すものは無い。)
            }

            // 新エネミーの OnBattleStart 発火
            psm.FireEnemyTrigger(PassiveSkillTrigger.OnBattleStart);

            string label = string.IsNullOrEmpty(logLabel) ? newEnemy.displayName : logLabel;
            Debug.Log($"[CombatManager] ━━ エネミー差し替え: {prevName} → {label} (HP:{enemyHP}/{newEnemy.maxHP}, Dice:{newEnemy.DiceNotation}, Threat:{newEnemy.threat})");

            // 計測フック: 新フォームを「新エネミーとの遭遇」として通知
            // (AutoRunner 等がチェーン途中フォームのスタッツを取れるようにする)
            GameLoop.GameManager.Instance?.RaiseEnemyEncountered(newEnemy);
            return true;
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
