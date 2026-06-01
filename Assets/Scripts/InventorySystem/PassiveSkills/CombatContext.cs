using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
    /// <summary>プレイヤーに止めを刺した致死メカニズムの分類 (ボス難易度オートチューナーの苦戦診断用)。</summary>
    public enum DeathCause
    {
        Normal,       // 通常ロール敗北の被ダメ
        Judgment,     // 灰燼: 業火の断罪
        Reflect,      // 覚者・無相: 鏡映反射
        Burst,        // 業火・残響: 爆ぜ火 (敗北時固定ダメ)
        Chip,         // 業火の審判官: 審判の炎 (継続ダメ)
        SuddenDeath,  // 覚者・妙覚: サドンデス
        Other,
    }

    /// <summary>
    /// 戦闘中の全状態を保持するコンテキスト
    /// パッシブスキルはこのオブジェクトを読み書きしてゲーム状態を変更する
    /// 
    /// 設計意図：
    /// - スキルロジックが直接MonoBehaviourやManagerを参照しない
    /// - テスト時にモック可能
    /// - 1戦闘ごとに生成→破棄でメモリリークなし
    /// </summary>
    public class CombatContext
    {
        // ===== プレイヤー状態 =====
        public int playerCurrentHP;
        public int playerMaxHP;
        public int playerBaseMaxHP;     // 装備補正前の最大HP

        // ===== 敵状態 =====
        public int enemyCurrentHP;
        public int enemyMaxHP;

        // ===== ダイスロール結果 =====
        public int[] playerDice;        // プレイヤーが振った各ダイスの値
        public int[] enemyDice;         // 敵が振った各ダイスの値
        public int playerDiceTotal;     // プレイヤーのダイス合計値
        public int enemyDiceTotal;      // 敵のダイス合計値
        /// <summary>ダイス差（playerDiceTotal − enemyDiceTotal）。両合計から常に算出する読み取り専用プロパティ。
        /// 視点スワップ時も両合計が入れ替わるため符号が自動反転し、明示的な反転代入は不要。
        /// 常に最新値を返すため、OnPostRoll パッシブが合計を改変しても鮮度ズレが起きない。</summary>
        public int diceDifference => playerDiceTotal - enemyDiceTotal;
        
        // ===== ダイス設定値 =====  
        public int playerDiceMax;       // プレイヤーのダイス最大出目（装備武器由来）
        public int enemyDiceMax;        // 敵のダイス最大出目

        // ===== ダイスカスタマイズ（装備ダイス） =====
        /// <summary>
        /// 装備中ダイスの面配列。null時は通常ロール(1～diceMax)。
        /// 全ダイス共通でこの面からランダム抽選。
        /// </summary>
        public int[] equippedDiceFaces;

        // ===== ダメージ計算 =====
        public int baseDamage;          // 基本ダメージ
        public int finalDamage;         // 最終ダメージ（スキル補正後）
        public int pursuitDamage;       // 追撃ダメージ（パッシブ由来の固定追撃）
        public int criticalBonus;       // 会心ボーナスダイス補正値
        public bool isCritical;         // 会心判定結果
        /// <summary>会心強制フラグ。true なら乱数判定を無視して会心確定（末那識など）。
        /// 会心率cap(注意散漫)下でも真に確定する。BeginNewTurn でリセット、毎ターン効果側が再set。</summary>
        public bool forceCritical;
        public float criticalMultiplier; // 会心倍率（デフォルト2.0）
        public bool damageReduced;      // ダメージ軽減が発生したか

        // ===== Threat/Scratchシステム =====
        /// <summary>敵の脅威値。毎ターン宣言される削りダメージの基準</summary>
        public int enemyThreat;
        /// <summary>今ターンのscratchダメージ（勝利時: max(0, threat-diff)）</summary>
        public int scratchDamage;
        /// <summary>scratch無効化フラグ（パリィ等）</summary>
        public bool nullifyScratchDamage;

        // ===== 戦場修飾子 =====
        /// <summary>この戦闘のアクティブな修飾子一覧</summary>
        public List<BattleModifier> activeModifiers = new List<BattleModifier>();

        // ===== ターン管理 =====
        public int currentTurn;         // 現在のターン数（1始まり）
        public bool isFirstRoll;        // 戦闘開始後の初回ロールか

        // ===== 蓄積/持続値（スキルが読み書き） =====
        /// <summary>スキルごとの蓄積データ（キー = スキルID）</summary>
        public Dictionary<string, float> accumulatedValues = new Dictionary<string, float>();

        /// <summary>次ターンへのバフ/デバフ転送用（キー = バフ名）</summary>
        public Dictionary<string, float> nextTurnBuffs = new Dictionary<string, float>();
        
        /// <summary>現在ターンのバフ/デバフ</summary>
        public Dictionary<string, float> currentBuffs = new Dictionary<string, float>();

        // ===== 敵のダイス制約（処刑用） =====
        public Dictionary<int, int> enemyDiceOverrides = new Dictionary<int, int>();
        public List<DiceOverrideRequest> pendingDiceOverrides = new List<DiceOverrideRequest>();

        // ===== 出血・状態異常 =====
        public int enemyBleedStacks;    // 敵の出血スタック数
        /// <summary>炎上残りターン数（業火スキル用）</summary>
        public int enemyBurnTurns;
        /// <summary>炎上の毎ターンダメージ</summary>
        public int enemyBurnDamage;
        
        // ===== 勝敗フラグ =====
        public bool playerWonRoll;
        public bool playerLostRoll;

        // ===== 連続カウンター =====
        public int consecutiveWins;
        public int consecutiveLosses;

        // ===== ダメージ無効化フラグ =====
        public bool nullifyAllDamage;
        public bool nullifyPursuitDamage;

        // ===== オーバーダメージ蓄積 =====
        public int overDamageAccumulated;

        // ===== 固定ダメージ =====
        public int fixedDamageToEnemy;  // プレイヤー→敵への軽減不可固定ダメージ
        public int fixedDamageToPlayer; // 敵→プレイヤーへの軽減不可固定ダメージ

        // ===== イベント由来時限バフ =====
        /// <summary>共助: 1ターン目の敵の攻撃ダメージを半減（適用後にfalse）</summary>
        public bool halveFirstEnemyAttack;
        /// <summary>獣の絆: 被弾を回数分無効化（>0時、被弾時にデクリメント）</summary>
        public int playerDamageNegateCharges;
        /// <summary>獣の恩義: 1ターン目の敵ロールを0扱いに（適用後にfalse）</summary>
        public bool nullifyFirstEnemyRoll;
        /// <summary>呪いの渇き: HP回復効果半減</summary>
        public bool healHalved;
        /// <summary>狂暴化(ボス50T後): プレイヤーの回復を完全に封じる。毎ターン狂暴化パッシブが再set。BeginNewTurn でリセット。</summary>
        public bool healBlocked;
        /// <summary>狂暴化(ボス50T後): エネミーが受けるダメージ倍率（1.0=等倍, 狂暴化中3.0）。BeginNewTurn でリセット。</summary>
        public float enemyDamageTakenMultiplier = 1f;
        /// <summary>覚者〈天衣無縫〉: 覚者がロール勝利するたび+1（上限20）。プレイヤーが獲得する回復量・シールド量を
        /// このスタック分だけ減少させる。戦闘（覚者連戦）を通じて持続するため BeginNewTurn ではリセットしない。</summary>
        public int healShieldReduction;

        /// <summary>検証用計測: この戦闘でプレイヤーが実際に獲得した累計回復量／シールド量。
        /// AutoRunner が6/7層ボス戦で集計しサマリに記載する。戦闘ごとに新規生成でリセット。</summary>
        public int healAppliedTotal;
        public int shieldGainedTotal;
        /// <summary>希望の灯片: この戦闘で1度でもロール敗北したか。
        /// CombatStart 時にリセットし、 PassiveSkillManager の敗北ターン処理で true にする。
        /// CombatEnd で勝利かつ false なら最大HPボーナス発動。</summary>
        public bool rollLossOccurredThisCombat;
        /// <summary>記憶の砂時計: この3ターン区間で蓄積した与ダメ。
        /// 3T毎(ターン3/6/9...)に30%を軽減不可ダメージとして返却し、リセット。</summary>
        public int hourglassPendingDamageWindow;
        /// <summary>L1学習用: 戦闘中にプレイヤーが敵に与えた総ダメージ（メイン+固定+出血+反射 等の合算）。
        /// 戦闘開始時 enemyMaxHP からの最終 enemyCurrentHP 差分で簡易的に算出するため、
        /// 実体は OnBattleEnded で評価する。フィールド自体はオプションのインクリメント用。</summary>
        public int damageDealtTotal;
        /// <summary>L1学習用: 戦闘中にプレイヤーが受けた総ダメージ（ヒール前の純粋な損失）。
        /// 同じく OnBattleEnded 時に最終確定する。</summary>
        public int damageTakenTotal;

        /// <summary>このターンにプレイヤーが実際に受けたダメージ量（メイン＋固定）。
        /// 焦土〈最大HP -被ダメ10%〉が参照する。BeginNewTurn でリセット。</summary>
        public int playerDamageThisTurn;
        /// <summary>亡者の招待: 被ダメ+30% (0.3 = +30%)</summary>
        public float receivedDamageBonus;
        /// <summary>激情の刃 等の与ダメ倍率（1.0 = 変化なし。BeginNewTurn でリセット）</summary>
        public float outgoingDamageMultiplier = 1f;

        /// <summary>貪欲のダイス: 与ダメージのこの割合をプレイヤーが回復（0=無し。BeginNewTurn でリセット、パッシブが毎ターン再適用）</summary>
        public float lifestealPct;

        /// <summary>停戦協定: このターンが「完全引き分け→停戦の一撃」で解決されたか。
        /// true の間は引き分けブランチで出血など他の効果を発動させない。BeginNewTurn でリセット。</summary>
        public bool truceThisTurn;

        /// <summary>苦難の刻印・不屈の鎧 等の被ダメ固定減算（複数アイテム合算）。CombatManager 敗北分岐で適用。BeginNewTurn でリセット。</summary>
        public int playerFlatDamageReduction;

        /// <summary>残り敵パッシブ無効化ターン数。> 0 のとき FireEnemyTrigger をスキップ。各ターン末に減算。</summary>
        public int enemyPassivesDisabledTurns;

        /// <summary>星火燎原 等: 敵(ボス)ダイス合計への加算ボーナス（累積。BeginNewTurn でリセットしない）。
        /// ProcessPostRoll の勝敗判定前に enemyDiceTotal へ加算される。</summary>
        public int enemyDiceTotalBonus;

        /// <summary>ボス強者バフ: ボス(boss_layer*)のダイス合計への固定加算。フロアに応じて戦闘開始/形態swap時に設定。
        /// enemyDiceTotalBonus とは別枠（星火燎原等の上書きと競合させないため）。勝敗判定前に enemyDiceTotal へ加算。</summary>
        public int bossDiceBonus;

        /// <summary>現在の敵ボスid (boss_layer*)。 非ボス戦は空。 各ボススキルが BossTuning.Param(bossId, ...) を引くのに使う。
        /// 戦闘開始/形態swap時に CombatManager がセット。</summary>
        public string bossId = "";

        /// <summary>直近にプレイヤーへダメージを与えた致死メカニズムの分類。
        /// 各致死スキルが発動時にセット、 通常被ダメ経路は Normal。 プレイヤー死亡時の死因記録に使う。</summary>
        public DeathCause lastDamageCause = DeathCause.Normal;

        /// <summary>敵の基礎防御（被ダメ%軽減 0～1）。EnemyData.baseDefenseRate を戦闘開始/形態swap時に設定、
        /// エリート(EliteVigor)が +0.10。利刃で相殺。BeginNewTurn ではリセットしない（戦闘通して保持）。
        /// 勝利分岐で 灰塵の鎧 の後に total ×= (1 - max(0, 軽減率 - armorPenPct))。</summary>
        public float enemyDamageReductionPct;

        /// <summary>利刃: 敵基礎防御の軽減率を剥がす割合(pt)。Lv1-4=0.15/0.20/0.25/0.30。
        /// パッシブが OnPostRoll で毎ターン再set。BeginNewTurn で 0 リセット。</summary>
        public float armorPenPct;

        /// <summary>勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。
        /// パッシブが OnPostRoll で再set。BeginNewTurn で 1 リセット。</summary>
        public int winMinDamage;

        /// <summary>敵の回復量を減衰させる割合(0～1)。治癒阻害(0.5)/治癒遮断(1.0)が OnBattleStart で設定。
        /// 敵パッシブの回復は ReduceEnemyHeal() を通すことで適用される。BeginNewTurn でリセットしない。</summary>
        public float enemyHealReductionPct;

        /// <summary>軽減無視ダメージ(fixedDamageToEnemy)の倍率。蒼白の槍騎士が設定(既定1.0)。
        /// 血令/反撃/業火/停戦 等の固定ダメに乗る。BeginNewTurn でリセットしない。</summary>
        public float fixedDamageMultiplier = 1f;

        /// <summary>リピーター: 会心成立時に与ダメ計算前パッシブ(OnPreDealDamage)を再発火するか。
        /// リピーターが OnBattleStart で設定。BeginNewTurn でリセットしない。</summary>
        public bool retriggerOnCrit;

        // ===== Λ層（時間の狭間）由来の恒久デバフ（戦闘開始時に RunState から設定。戦闘スコープで保持） =====

        /// <summary>重い足取り: 1ターン目のプレイヤーダイス合計デルタ(負値、lv1/2/3=-2/-4/-6)。0=無効。</summary>
        public int lambdaFirstTurnDiceDelta;

        /// <summary>苛立つ強敵: 敵ダイス+1 の発生間隔(5/4/3T)。0=無効。毎ロールで floor(turn/interval) を敵合計へ加算。</summary>
        public int lambdaIrritatingInterval;

        /// <summary>微妙な手応え: 勝利時の与ダメ倍率(lv1/2/3=0.95/0.90/0.85)。1.0=無効。</summary>
        public float lambdaDamageDealtMult;

        /// <summary>注意散漫: 会心分子(X/9)の上限(lv1/2/3=8/6/4)。9=無効。</summary>
        public int lambdaCritNumeratorCap;

        /// <summary>慈悲の処刑: 被弾後 HP がこの割合(lv1/2/3=0.05/0.10/0.15)以下で即死。0=無効。</summary>
        public float lambdaMercifulExecThreshold;

        /// <summary>神経錯乱: このターン未満では消費アイテム使用不可(lv1/2/3=3/5/7)。0=制限なし。</summary>
        public int lambdaConsumableLockUntilTurn;

        /// <summary>シールドバッシュ: ロール勝利時、与ダメのこの割合をシールド化(5/10/15/20%)。
        /// パッシブが OnTurnStart で再set。BeginNewTurn で 0 リセット。</summary>
        public float shieldOnWinPct;

        /// <summary>貸与された時間: 敗北時に被ダメの一部を肩代わりして蓄積した「貸与時間」。
        /// 上限(最大HP×割合)到達で同値の軽減不能ダメージ＋0リセット。ロール勝利で0クリア。
        /// 戦闘を通して持続するため BeginNewTurn ではリセットしない。</summary>
        public int lentTimeStacks;
        /// <summary>貸与された時間 リワーク: 分割返済の残ターン数。 >0 なら毎ターン1/残ターン ずつ清算。
        /// 0なら蓄積中。 戦闘中ロール勝利でゼロリセット (帳消し)。</summary>
        public int lentTimePaybackRemainTurns;
        /// <summary>貸与された時間 リワーク: 清算開始時の総債務 (残ターンで割って毎T払う元本)。</summary>
        public int lentTimePaybackTotal;
        /// <summary>貸与された時間 リワーク: Tier (1-4)。 Tier別の分割ターン数を決める。</summary>
        public int lentTimeTier;
        /// <summary>貸与された時間: このターンで返済支払いが行われたか。 true ならこのターン中の新規借入をブロック。</summary>
        public bool lentTimePaidThisTurn;

        /// <summary>敵(自身)の回復量に enemyHealReductionPct を適用して返す（治癒阻害用）。</summary>
        public int ReduceEnemyHeal(int heal)
        {
            if (enemyHealReductionPct <= 0f || heal <= 0) return heal;
            return (int)(heal * (1f - enemyHealReductionPct));
        }

        /// <summary>当ターン中にプレイヤーが消費品/レイピアを使用したか。
        /// BeginNewTurn でリセット。覚者の「悟達の試練」が観想中断判定に使う。</summary>
        public bool consumablesUsedThisTurn;

        /// <summary>〈灰燼の烙印〉: 6層ボスがHP1で踏みとどまった後の決着ターン。
        /// true の間は両ダイスを 1d6 に強制し、ロール勝者の与ダメに +999（相打ち上等のサドンデス）。
        /// 決着がつくまで持続するため BeginNewTurn ではリセットしない。</summary>
        public bool ashenSuddenDeath;

        /// <summary>〈妙覚〉サドンデス: 妙覚T2+でロール敗北かつ生存中。
        /// 次ターン以降、両ダイスを 1d2 に強制し決着まで継続。
        /// プレイヤー勝利時 gedatsuPending=true をセットし【解脱】特殊勝利。
        /// 2026-06-01 リワーク後は未使用 (素のロール勝負化により強制ロジック撤廃)。</summary>
        public bool myokakuSuddenDeath;

        /// <summary>〈妙覚〉自由攻撃ターン: 妙覚到達後の最初の1ターンだけ true。
        /// CombatManager がボスを 0d0 (ロール合計0) に強制 → プレイヤーが自由に削れる。
        /// 削りきれなければ T2 以降サドンデスへ移行。</summary>
        public bool myokakuFreeHit;

        /// <summary>〈妙覚〉解脱: サドンデスでプレイヤーが勝利。CombatResult.gedatsu に反映される。</summary>
        public bool gedatsuPending;

        /// <summary>覚者連戦: 次フォームの enemy id。敵パッシブ OnTurnEnd で設定され、
        /// CombatManager が perspective 復帰後に SwapEnemy で消費 → null クリア。</summary>
        public string pendingEnemySwapId;
        /// <summary>覚者連戦: 遷移ログラベル</summary>
        public string pendingEnemySwapLabel;

        // ===== 消費アイテム由来（この1戦闘のみ。ctxは戦闘毎に生成→破棄で自動消去） =====
        public int consAtkBurst;          // 次の勝利ターンで与ダメ+X（適用後0）
        public int consDiceRoll;          // 勝敗判定のみ+X（ダメージには非加算）
        public int consShield;            // 残シールド吸収量
        public int consShieldExpireTurn;  // この番号を超えるターンで失効。-1=無制限/0=無
        public int consRegen;             // 毎ターン終了時 +consRegen 回復し consRegen--
        public int consCrit;              // 会心率+X(/9) 永続（毎ターン再適用）
        public int consFlatReduce;        // 被ダメ定数-X 永続（毎ターン再適用）
        public int consDmgMultPct;        // 与ダメ +X%
        public bool consReflect;          // 被メインダメと同量を敵へ反射
        public int consEnemyDiceDebuff;   // 敵ダイス合計 -X（毎ロール）
        public bool gamblerArmed;         // 賭博師のダイス（ロール時に発火・消費）

        // ===== エリート: 精鋭ハーピィ「死翔」 =====
        public bool consumablesLocked;    // この戦闘中、消費アイテム使用不可（戦闘開始時に付与・戦闘ごとに新規生成でリセット）

        // ===== 竜閃 =====
        public bool rollPurity;           // 無我無心: カスタムダイス以外の補正を一切受けない（戦闘中持続）
        public bool garyoProc;            // 画竜点睛: このターン発動したか（毎ターンリセット）
        public int  garyoDieValue;        // 画竜点睛: 発動時の出目

        // ===== 刻印システム =====
        /// <summary>刻印による追加パッシブ効果（戦闘開始時に解決済み）</summary>
        public List<SigilBonus> activeSigilBonuses = new List<SigilBonus>();

        /// <summary>
        /// コンテキストを初期化
        /// </summary>
        public CombatContext(int playerMaxHP, int enemyMaxHP = 0, int enemyThreat = 0)
        {
            this.playerMaxHP = playerMaxHP;
            playerBaseMaxHP = playerMaxHP;
            playerCurrentHP = playerMaxHP;
            this.enemyMaxHP = enemyMaxHP;
            enemyCurrentHP = enemyMaxHP;
            this.enemyThreat = enemyThreat;
            currentTurn = 0;
            isFirstRoll = true;
            enemyDamageReductionPct = 0f;
            armorPenPct = 0f;
            winMinDamage = 1;
            enemyHealReductionPct = 0f;
            fixedDamageMultiplier = 1f;
            retriggerOnCrit = false;
            lambdaFirstTurnDiceDelta = 0;
            lambdaIrritatingInterval = 0;
            lambdaDamageDealtMult = 1f;
            lambdaCritNumeratorCap = 9;
            lambdaMercifulExecThreshold = 0f;
            lambdaConsumableLockUntilTurn = 0;
            shieldOnWinPct = 0f;
            lentTimeStacks = 0;
            lentTimePaybackRemainTurns = 0;
            lentTimePaybackTotal = 0;
            lentTimeTier = 0;
            rollLossOccurredThisCombat = false;
            hourglassPendingDamageWindow = 0;
            criticalMultiplier = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier();
            accumulatedValues = new Dictionary<string, float>();
            nextTurnBuffs = new Dictionary<string, float>();
            currentBuffs = new Dictionary<string, float>();
            enemyDiceOverrides = new Dictionary<int, int>();
            pendingDiceOverrides = new List<DiceOverrideRequest>();
            activeModifiers = new List<BattleModifier>();
            activeSigilBonuses = new List<SigilBonus>();
        }

        /// <summary>
        /// ターン開始時の状態リセット
        /// </summary>
        public void BeginNewTurn()
        {
            currentTurn++;
            isFirstRoll = (currentTurn == 1);
            
            // 次ターンバフを現在バフへ移行
            currentBuffs.Clear();
            foreach (var kvp in nextTurnBuffs)
            {
                currentBuffs[kvp.Key] = kvp.Value;
            }
            nextTurnBuffs.Clear();

            // 単ターン限りのフラグをリセット
            nullifyAllDamage = false;
            nullifyPursuitDamage = false;
            nullifyScratchDamage = false;
            fixedDamageToEnemy = 0;
            fixedDamageToPlayer = 0;
            scratchDamage = 0;
            damageReduced = false;
            isCritical = false;
            forceCritical = false;
            lentTimePaidThisTurn = false;
            criticalBonus = 0;
            criticalMultiplier = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier();
            pursuitDamage = 0;
            consumablesUsedThisTurn = false;

            // 出血スタック減衰（毎ターン-1）
            if (enemyBleedStacks > 0) enemyBleedStacks--;

            // ダイス制約を適用
            enemyDiceOverrides.Clear();

            // 与ダメ倍率は毎ターンリセット（パッシブが毎ターン再評価する）
            outgoingDamageMultiplier = 1f;
            lifestealPct = 0f;
            shieldOnWinPct = 0f;
            truceThisTurn = false;

            // 利刃由来は毎ターンリセット（利刃パッシブが OnPostRoll で再set）。
            // enemyDamageReductionPct は戦闘通して保持するためここではリセットしない。
            armorPenPct = 0f;
            winMinDamage = 1;

            // 狂暴化系も毎ターンリセット（狂暴化パッシブが OnTurnStart で再適用）
            healBlocked = false;
            enemyDamageTakenMultiplier = 1f;

            // 被ダメ固定減算もリセット（毎ターン再評価）
            playerFlatDamageReduction = 0;

            // 焦土用の被ダメ計測は毎ターンリセット
            playerDamageThisTurn = 0;

            // 敵パッシブ無効化のターン残数を減算
            if (enemyPassivesDisabledTurns > 0) enemyPassivesDisabledTurns--;

            // 画竜点睛は毎ターン判定（rollPurity は戦闘中持続なのでリセットしない）
            garyoProc = false;
            garyoDieValue = 0;
        }

        /// <summary>蓄積値を安全に取得（キーが無ければ0）</summary>
        public float GetAccumulated(string key)
        {
            return accumulatedValues.TryGetValue(key, out float val) ? val : 0f;
        }

        /// <summary>蓄積値を加算</summary>
        public void AddAccumulated(string key, float amount)
        {
            if (!accumulatedValues.ContainsKey(key))
                accumulatedValues[key] = 0f;
            accumulatedValues[key] += amount;
        }

        /// <summary>現在ターンのバフ値を取得（なければ0）</summary>
        public float GetBuff(string key)
        {
            return currentBuffs.TryGetValue(key, out float val) ? val : 0f;
        }

        /// <summary>修飾子が有効か確認</summary>
        public bool HasModifier(BattleModifierId id)
        {
            for (int i = 0; i < activeModifiers.Count; i++)
                if (activeModifiers[i].id == id) return true;
            return false;
        }
    }

    /// <summary>ダイス固定リクエスト（次ターンに適用される）</summary>
    public class DiceOverrideRequest
    {
        public enum TargetDice { Lowest, Highest }
        
        public TargetDice target;
        public int fixedValue;
        public string sourceSkill;

        public DiceOverrideRequest(TargetDice target, int fixedValue, string sourceSkill)
        {
            this.target = target;
            this.fixedValue = fixedValue;
            this.sourceSkill = sourceSkill;
        }
    }

    // ===== 戦場修飾子 =====
    public enum BattleModifierId
    {
        Downpour,       // 豪雨: 全ダイス最大値-2
        LunarEclipse,   // 月蝕: 会心率+3
        CursedFog,      // 呪霧: scratchダメージ2倍
        BloodTide,      // 血潮: 出血ダメージ2倍
        IronCurtain,    // 鉄壁: 被ダメ上限5/ターン
        Deathmatch,     // 死闘: 引き分けなし（同値はプレイヤー敗北）
        Fortune,        // 幸運: 報酬2倍
        Adversity,      // 逆境: 敵threat+3
    }

    public class BattleModifier
    {
        public BattleModifierId id;
        public string displayName;
        public string description;

        public BattleModifier(BattleModifierId id, string displayName, string description)
        {
            this.id = id;
            this.displayName = displayName;
            this.description = description;
        }
    }

    // ===== 刻印ボーナス =====
    /// <summary>刻印が武器に隣接して発動するボーナス効果</summary>
    public class SigilBonus
    {
        public string sigilId;          // 刻印の識別名
        public string bonusType;        // ボーナス種別 (pursuit/counter/might/fortitude/insight/vitality/bleed/threatReduce/diceFace)
        public int value;               // ボーナス値

        public SigilBonus(string sigilId, string bonusType, int value)
        {
            this.sigilId = sigilId;
            this.bonusType = bonusType;
            this.value = value;
        }
    }
}
