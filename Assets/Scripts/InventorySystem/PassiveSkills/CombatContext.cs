using System.Collections.Generic;

namespace InventorySystem.PassiveSkills
{
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
        public int diceDifference;      // ダイス差（player - enemy）
        
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
        /// <summary>亡者の招待: 被ダメ+30% (0.3 = +30%)</summary>
        public float receivedDamageBonus;
        /// <summary>激情の刃 等の与ダメ倍率（1.0 = 変化なし。BeginNewTurn でリセット）</summary>
        public float outgoingDamageMultiplier = 1f;

        /// <summary>苦難の刻印・不屈の鎧 等の被ダメ固定減算（複数アイテム合算）。CombatManager 敗北分岐で適用。BeginNewTurn でリセット。</summary>
        public int playerFlatDamageReduction;

        /// <summary>残り敵パッシブ無効化ターン数。> 0 のとき FireEnemyTrigger をスキップ。各ターン末に減算。</summary>
        public int enemyPassivesDisabledTurns;

        /// <summary>星火燎原 等: 敵(ボス)ダイス合計への加算ボーナス（累積。BeginNewTurn でリセットしない）。
        /// ProcessPostRoll の勝敗判定前に enemyDiceTotal へ加算される。</summary>
        public int enemyDiceTotalBonus;

        /// <summary>当ターン中にプレイヤーが消費品/レイピアを使用したか。
        /// BeginNewTurn でリセット。覚者の「悟達の試練」が観想中断判定に使う。</summary>
        public bool consumablesUsedThisTurn;

        /// <summary>〈灰燼の烙印〉: 6層ボスがHP1で踏みとどまった後の決着ターン。
        /// true の間は両ダイスを 1d6 に強制し、ロール勝者の与ダメに +999（相打ち上等のサドンデス）。
        /// 決着がつくまで持続するため BeginNewTurn ではリセットしない。</summary>
        public bool ashenSuddenDeath;

        /// <summary>〈妙覚〉サドンデス: 妙覚T2+でロール敗北かつ生存中。
        /// 次ターン以降、両ダイスを 1d2 に強制し決着まで継続。
        /// プレイヤー勝利時 gedatsuPending=true をセットし【解脱】特殊勝利。</summary>
        public bool myokakuSuddenDeath;

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

            // 狂暴化系も毎ターンリセット（狂暴化パッシブが OnTurnStart で再適用）
            healBlocked = false;
            enemyDamageTakenMultiplier = 1f;

            // 被ダメ固定減算もリセット（毎ターン再評価）
            playerFlatDamageReduction = 0;

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
