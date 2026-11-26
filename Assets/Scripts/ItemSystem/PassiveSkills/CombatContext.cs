using System.Collections.Generic;

namespace ItemSystem.PassiveSkills
{
    /// <summary>プレイヤーに止めを刺した致死メカニズムの分類 (死因の集計用)。</summary>
    public enum DeathCause
    {
        Normal,       // 通常ロール敗北の被ダメ
        Judgment,     // 灰燼: 業火の断罪
        Reflect,      // 鏡の双子: 鏡映の応答 (旧: 覚者・無相)
        Burst,        // 業火・残響: 爆ぜ火 (敗北時固定ダメ)
        Chip,         // 審判官: 審判の炎 (継続ダメ)
        SuddenDeath,  // 欠番 (旧: サドンデス・灰燼の烙印)。 書く所は 0 件だが、 int の並びを保つために値は残す。
        Other,
        // **末尾に足すこと。** runs.jsonl へ int で載るので、 既存の並びを動かすと
        //   過去バッチと突き合わせたときに黙って別の死因へすり替わる。
        MassiveBleed, // ヴェスカ・天与: 大出血 (スタック累積・解除不可・4連戦を跨ぐ)
        Rift,         // ヴェスカ: 裂け目 (双方の最大HP 2%/T)
        Erosion,      // 挑戦カード〈蝕み〉 (ターンの始めの軽減できないダメージ)
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
        /// <summary>[persistent] (CombatManager が HP を同期する)</summary>
        public int playerCurrentHP;
        /// <summary>[persistent]</summary>
        public int playerMaxHP;

        // ===== 敵状態 =====
        /// <summary>[persistent] (CombatManager が HP を同期する)</summary>
        public int enemyCurrentHP;
        /// <summary>[persistent]</summary>
        public int enemyMaxHP;

        // ===== ダイスロール結果 =====
        public int[] playerDice;        // [persistent] (BeginNewTurn では戻さない。 毎ターンの振り (PassiveSkillManager) が上書きする) プレイヤーが振った各ダイスの値
        public int[] enemyDice;         // [persistent] (BeginNewTurn では戻さない。 毎ターンの振り (PassiveSkillManager) が上書きする) 敵が振った各ダイスの値
        public int playerDiceTotal;     // [persistent] (BeginNewTurn では戻さない。 毎ターンの振り (PassiveSkillManager) が上書きする) プレイヤーのダイス合計値
        public int enemyDiceTotal;      // [persistent] (BeginNewTurn では戻さない。 毎ターンの振り (PassiveSkillManager) が上書きする) 敵のダイス合計値
        
        // ===== ダイス設定値 =====  
        public int playerDiceMax;       // [persistent] (戦闘開始で設定・CombatManager.Turn.cs が毎ターン入れ直す) プレイヤーのダイス最大出目（装備武器由来）
        public int enemyDiceMax;        // [persistent] (戦闘開始・段の差し替えで設定) 敵のダイス最大出目

        // ===== ダイスカスタマイズ（装備ダイス） =====
        /// <summary>
        /// [persistent] (戦闘開始で設定) 装備中ダイスの面配列。null時は通常ロール(1～diceMax)。
        /// 全ダイス共通でこの面からランダム抽選。
        /// </summary>
        public int[] equippedDiceFaces;

        /// <summary>[persistent] <see cref="equippedDiceFaces"/> と**添字が 1 対 1 で対応する** Tier 配列。
        /// 素の面 {2,3,4,5} は T1 (無能力)、 出目パーツで得た面はその Tier (T2 = 2 個そろい、 T3 = ゾロ目、 T4 = 固有効果)。
        /// <see cref="GameLoop.EquippedDie.Build"/> が faces と対で組む ── 別々に作らないこと。
        /// null なら役は何も成立しない。</summary>
        public GameLoop.DiceFaceParts.Tier[] equippedFaceTiers;

        /// <summary>[perTurn] このターン、 各ダイスが**どの面に止まったか** (equippedDiceFaces の添字)。
        ///
        /// <para><b>値ではなく個体を追う。</b> 素の <c>1</c> とパーツの <c>1</c> は別物なので、
        /// 効果の判定には値ではなくこの添字が要る。 <c>-1</c> は「面配列を使わない素のロール」。
        /// <see cref="playerDice"/> と同じ長さ・同じ並びで持つ。</para>
        ///
        /// <para>リロール・レイピアの追加ダイス・パッシブの再ロールでも必ず更新すること。
        /// 更新し忘れると**前ターンの面の効果が今ターンに乗る**。</para></summary>
        public int[] playerDiceFaceIdx;

        /// <summary>[persistent] ADR-0010〈無銘の賽〉: 装備ダイスが**端子役を成立させない**か。
        /// 手札役・配線役は通常どおり成立する。 戦闘開始時に 1 度だけ set し、
        /// 4 連戦を通じて変わらない (装備は戦闘中に変えられないため)。</summary>
        public bool suppressTerminalRoles;

        /// <summary>[persistent] T4-C〈凶運〉でこのランずっと封印されている役のビットマスク。
        /// **ラン単位の設定なので戦闘中に変わらない** (BeginNewTurn でのリセット対象外)。
        /// RunState.sealedRoleMask を BeginCombat で写す。</summary>
        public int sealedRoleMask;

        /// <summary>[perTurn] ADR-0011: このターン効いた役 (ログ・計装用。 使用済みの管理は持たない)。
        /// BeginNewTurn Phase B でクリアする。</summary>
        public readonly List<CombatSystem.RoleKind> firedRolesThisTurn = new List<CombatSystem.RoleKind>();

        // ===== ダメージ計算 =====
        public int baseDamage;          // [persistent] (ProcessDamage が計算のたびに上書きする作業値) 基本ダメージ
        public int finalDamage;         // [persistent] (ProcessDamage が計算のたびに上書きする作業値) 最終ダメージ（スキル補正後）
        public int pursuitDamage;       // [perTurn] 追撃ダメージ（パッシブ由来の固定追撃）

        /// <summary>[perTurn] このターン、 ブロック端子へ配線した**出目の合計**。 本数ではない。
        /// 剣〈切り返し〉と盾〈城壁〉が読む (2026-10-28・旧読み手は盾〈反攻〉)。
        /// CombatManager の配線集計が毎ターン set し、 BeginNewTurn Phase B で 0 に戻す。
        /// 配線 (§9.2 step5) は解決 (step6) より前なので OnWired / OnPreDealDamage / OnTurnEnd から読める。
        /// **OnPostRoll は配線より前なので読めない** (前ターンの値ではなく 0 が入っている)。</summary>
        public int blockSumThisTurn;

        /// <summary>[persistent] この戦闘が 2 体戦か (EncounterPresets のペア)。
        /// 剣家系〈一対一〉が「単体戦でしか効かない」を判定するために読む。
        /// CombatManager が戦闘開始時に 1 度だけ set する ── 戦闘中は変わらない。</summary>
        public bool isPairEncounter;

        /// <summary>[perTurn] 会心率への加算 (小数・0.05 = +5%)。 パッシブ・消耗品・戦闘中バフの
        /// <b>会心率はすべてここへ足す</b> (2026-09-19 に旧「分子」を撤去して % へ統一)。
        /// 武器 + 層補正の素の会心率は ProcessDamage の引数 critBaseRate で別に渡る。 BeginNewTurn で 0 リセット。</summary>
        public float critRatePctAdd;
        public bool isCritical;         // [perTurn] 会心判定結果
        /// <summary>[perTurn] 会心強制フラグ。true なら乱数判定を無視して会心確定（末那識など）。
        /// 会心率cap(注意散漫)下でも真に確定する。BeginNewTurn でリセット、毎ターン効果側が再set。</summary>
        public bool forceCritical;
        /// <summary>[perTurn] 会心封印フラグ。true なら会心率を 0 にする（無心の刃など）。
        /// forceCritical より優先度が低い（強制会心が立っていれば会心する）。BeginNewTurn でリセット。</summary>
        public bool critSuppressed;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この 1 撃だけ会心を確定させる (〈抜き打ちの作法〉〈シュヴァリエのレイピア〉の解除など・OnCriticalCheck で立てる)。
        /// <see cref="forceCritical"/> はそのターンの全部の撃に効くので、 1 撃だけの確定はこちら。</summary>
        public bool forceCritThisHit;
        /// <summary>[perTurn] (撃ごとに CombatManager が書く) いま解決している主攻撃の撃の番号 (0 = そのターンの最初の 1 撃・ADR-0016)。 BeginNewTurn で 0。</summary>
        public int hitIndex;
        /// <summary>[perTurn] (撃の前に CombatManager が書く) このターンの主攻撃の撃の数 (攻撃しないターンは 0)。 BeginNewTurn で 0。
        /// 攻撃しないターンも固定ダメの枠を取り出すために 1 回 ProcessDamage を通るので、 「撃ごと」の品は <c>hitIndex &lt; hitCount</c> で本物の撃か見る。</summary>
        public int hitCount;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この 1 撃だけの与ダメ % の池への上乗せ (%)。 〈加重の拳套〉が OnPreDealDamage で書く。
        /// 与ダメ % の池 (<see cref="outgoingDamageMultiplier"/>) にその撃だけ足して掛ける (CombatManager.Damage.cs・池そのものは書き換えない)。</summary>
        public int hitOutgoingPct;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この 1 撃だけの最終与ダメージ倍率の上乗せ (%)。 〈血桜〉が OnPreDealDamage で書く。
        /// 主攻撃の最終与ダメージ倍率 (<see cref="finalDamagePct"/>) に足して掛ける (CombatManager.Damage.cs)。</summary>
        public int hitFinalDamagePct;
        /// <summary>[perTurn] (撃ごとに CombatManager が false に戻す) この 1 撃は敵の防御を無視する (〈破城斧〉が OnPreDealDamage で立てる・CombatManager.Damage の防御の計算)。</summary>
        public bool hitIgnoreDefense;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この 1 撃だけ敵の防御を N 無視する (貫通に足す・剣〈切り返し〉の最初の 1 撃が OnPreDealDamage で書く・CombatManager.Damage の防御の計算)。</summary>
        public int hitArmorPen;
        /// <summary>[perTurn] (撃ごとに CombatManager が書く) この撃 (攻撃の端子 1 枚) に挿した目の一番大きい値 (CombatSystem.CritCore.HitFaceMax・0 = 目なし)。 〈狙い目の刻み〉が読む。 BeginNewTurn で 0。</summary>
        public int hitFaceMax;
        /// <summary>[perTurn] (撃ごとに CombatManager が false に戻す) この撃は会心の乱数を引かない (〈必殺の照準器〉がもう 1 回発火した撃・会心は forceCritThisHit で決まる)。
        /// 乱数のキーの並びを崩さないため (発火した撃の分だけ引く数が増えないように)。</summary>
        public bool critSkipRoll;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この 1 撃の会心率への乗算の上乗せ (小数・0.25 = 合算した会心率 ×1.25・〈測量師の片眼鏡〉が OnCriticalCheck で書く・2026-11-23)。
        /// PassiveSkillManager.ProcessDamage が加算の合計に (1 + これ) を掛ける。 BeginNewTurn で 0。</summary>
        public float hitCritRateMulAdd;
        /// <summary>[perTurn] (撃ごとに CombatManager が false に戻す) この撃は【集中】の撃 (CritCore.FocusedRate・PassiveSkillManager.ProcessDamage が書く・2026-11-23)。 BeginNewTurn で false。</summary>
        public bool hitIsFocused;
        /// <summary>[perTurn] (撃ごとに CombatManager が 0 に戻す) この撃の集中会心で使った【練気】(集中会心でなければ 0・PassiveSkillManager.ProcessDamage が書く)。 BeginNewTurn で 0。</summary>
        public int hitFocusSpentRenki;
        /// <summary>[perTurn] (撃ごとに CombatManager が書く) この撃は〈必殺の照準器〉のもう 1 回の撃 (もう一度集中にならない・【練気】を使わない)。 BeginNewTurn で false。</summary>
        public bool isRefireHit;
        /// <summary>[perTurn] (撃ごとに CombatManager が書く) この撃は【再起動】で発動した撃 (〈灼線鞭〉〈溜め撃ちの誘導コイル〉が読む)。</summary>
        public bool isRebootHit;
        /// <summary>この撃が本物の主攻撃の撃か (攻撃しないターンの空の 1 回ではない)。</summary>
        public bool IsRealHit => playerWonRoll && hitIndex < hitCount;
        public float criticalMultiplier; // [perTurn] (Phase B で基礎値 + メタ・遺物へ戻す) 会心倍率（既定 2.0。2026-07-26 に 3.0 へ上げ、2026-08-03 に 2.0 へ差し戻し）

        // ===== Threat/Scratchシステム =====
        /// <summary>[persistent] (戦闘開始・段の差し替えで設定) 敵の脅威値。毎ターン宣言される削りダメージの基準</summary>
        public int enemyThreat;
        /// <summary>[perTurn] 今ターンのscratchダメージ（勝利時: max(0, threat-diff)）</summary>
        public int scratchDamage;

        // ===== 戦場修飾子 =====
        /// <summary>[persistent] この戦闘のアクティブな修飾子一覧</summary>
        public List<BattleModifier> activeModifiers = new List<BattleModifier>();

        // ===== ターン管理 =====
        public int currentTurn;         // [decay] (Phase A で +1 する通算のターン数 (0 へ向かう値ではない)) 現在のターン数（1始まり）
        public bool isFirstRoll;        // [perTurn] (Phase A で currentTurn から決め直す) 戦闘開始後の初回ロールか

        // ===== 戦闘相手の種別（暗殺教団契約等で参照） =====
        /// <summary>[persistent] 戦闘相手の種別 (Normal/Elite/Boss)。 マップタイル種別から StartCombat 時に設定。
        /// 暗殺教団 (通常戦闘マスのみ発動) / 暗殺教団+戦術家協力 (エリートに拡張) で参照する。</summary>
        public CombatSystem.EnemyKind currentEnemyKind = CombatSystem.EnemyKind.Normal;

        // ===== 脆弱状態異常（狩猟旅団契約で敵に付与）=====
        /// <summary>[persistent] 脆弱の倍率 (狩猟旅団 L1=0.15 / L2=0.30 / L3=0.45)。 0 なら契約なし。</summary>
        public float enemyVulnerabilityMultiplier = 0f;
        /// <summary>[persistent] (VulnerabilityStatus が立てる・倒す) 脆弱が armed (発動可能) 状態か。 会心ダメで消費、 ロール勝利時の非会心ダメで再点火。</summary>
        public bool enemyVulnerabilityArmed = false;

        // ===== 蓄積/持続値（スキルが読み書き） =====
        /// <summary>[persistent] (辞書は戦闘を通して保持。 一部のキー (ChargeSpentThisTurnKey 等) は Phase B で戻す) スキルごとの蓄積データ（キー = スキルID）</summary>
        public Dictionary<string, float> accumulatedValues = new Dictionary<string, float>();

        /// <summary>[decay] (Phase A で currentBuffs へ移して空にする) 次ターンへのバフ/デバフ転送用（キー = バフ名）</summary>
        public Dictionary<string, float> nextTurnBuffs = new Dictionary<string, float>();
        
        /// <summary>[perTurn] (Phase A で空にして nextTurnBuffs から入れ替える) 現在ターンのバフ/デバフ</summary>
        public Dictionary<string, float> currentBuffs = new Dictionary<string, float>();

        // ===== 敵のダイス制約（処刑用） =====
        /// <summary>[perTurn]</summary>
        public Dictionary<int, int> enemyDiceOverrides = new Dictionary<int, int>();
        /// <summary>[persistent] (積んだ要求は次の振りの適用 (PassiveSkillManager) で空になる)</summary>
        public List<DiceOverrideRequest> pendingDiceOverrides = new List<DiceOverrideRequest>();

        // ===== 出血・状態異常 =====
        public int enemyBleedStacks;    // [decay] 敵の出血スタック数（※統一フレーム未移行・現状フィールドのまま）
        /// <summary>[persistent] 止血阻害 (AntiClotting) が立てるフラグ。BeginNewTurn の出血自然減衰を無効化。</summary>
        public bool bleedDecayDisabled;
        // burnDecayHalved は 2026-07-18 削除 (Burn 全廃・EverBurning deregister に伴う死コード掃除)

        /// <summary>[decay] (減衰は BeginNewTurn の直後に PassiveSkillManager が呼ぶ TickStatuses) 汎用ステータス・スタック（#3 統一フレーム。id→stacks）。Player/Enemy は絶対視点（スワップしない）。
        /// 炎上(burn)はここへ移行済み。DOT/減衰は <see cref="TickStatuses"/> が一括処理する。</summary>
        public Dictionary<string, int> playerStatusStacks = new Dictionary<string, int>();
        /// <summary>[decay] (減衰は BeginNewTurn の直後に PassiveSkillManager が呼ぶ TickStatuses)</summary>
        public Dictionary<string, int> enemyStatusStacks = new Dictionary<string, int>();


        // ===== 勝敗フラグ =====
        // 名前は旧ロール勝負の名残。 相互攻撃モデル (ADR-0009) では場面ごとに意味が変わる (2026-11-12 棚卸し・GAME.md §23-1):
        //   - ProcessDamage の中: 「いま攻撃している側」(プレイヤーの攻撃 = true / 敵の攻撃 = false。 CombatManager.Turn.cs が計算の前に立てる)
        //   - 収支の確定後 (収支トリガー・OnTurnEnd): 収支 (与ダメ − 被ダメ) の符号
        //   - ProcessPostRoll は立てない (出目の合計の比較は 2026-11-12 #112 で削除)
        /// <summary>[persistent] (BeginNewTurn では戻さない。 CombatManager.Turn.cs が攻撃の前後で切り替える)</summary>
        public bool playerWonRoll;
        /// <summary>[persistent] (BeginNewTurn では戻さない。 CombatManager.Turn.cs が攻撃の前後で切り替える)</summary>
        public bool playerLostRoll;

        // ===== ダメージ無効化フラグ =====
        /// <summary>[perTurn]</summary>
        public bool nullifyPursuitDamage;

        // ===== 固定ダメージ =====
        public int fixedDamageToEnemy;  // [perTurn] プレイヤー→敵への軽減不可固定ダメージ
        public int fixedDamageToPlayer; // [perTurn] 敵→プレイヤーへの軽減不可固定ダメージ

        // ===== イベント由来時限バフ =====
        /// <summary>[persistent] (戦闘開始で立て、 1 回使ったら false) 共助: 1ターン目の敵の攻撃ダメージを半減（適用後にfalse）</summary>
        public bool halveFirstEnemyAttack;
        /// <summary>[persistent] (残り回数・被弾で減る) 獣の絆: 被弾を回数分無効化（>0時、被弾時にデクリメント）</summary>
        public int playerDamageNegateCharges;
        /// <summary>[persistent] 呪いの渇き: HP回復効果半減</summary>
        public bool healHalved;
        /// <summary>[perTurn] 狂暴化(ボス50T後): プレイヤーの回復を完全に封じる。毎ターン狂暴化パッシブが再set。BeginNewTurn でリセット。</summary>
        public bool healBlocked;
        /// <summary>[perTurn] 狂暴化(ボス50T後): エネミーが受けるダメージ倍率（1.0=等倍, 狂暴化中3.0）。BeginNewTurn でリセット。</summary>
        public float enemyDamageTakenMultiplier = 1f;
        /// <summary>[perTurn] 狂暴化(51T〜・2026-11-21): 敵の主攻撃はプレイヤーの防御・ブロック・シールド・被ダメの軽減をすべて通さず HP に入る
        /// (CombatManager の敵の攻撃の解決・予告の <c>MutualTurnTelegraph.enemyAttackIgnoresGuard</c>)。 毎ターン狂暴化パッシブが再set。BeginNewTurn でリセット。</summary>
        public bool enemyAttackIgnoresGuard;
        // (回復・シールドの減衰 healShieldReduction は、 書き手の〈天衣無縫〉が 2026-10-17 に消えたので 2026-11-12 に削除。 #128)

        // ===== 7層 ヴェスカ（遺物学者）。 正本: docs/GAME.md §13-4 =====
        // 4 段連戦を通じて CombatContext は使い回されるため、 [persistent] 指定のものは
        // 段が変わっても引き継がれる。

        /// <summary>[persistent] ヴェスカのシールド。 §6-3 段0 で **半減(切り捨て) → 抽選付与** の順に処理する。
        /// 定常上限は「毎ターン収入 × 2」で自動的に決まるため別途キャップ不要。
        /// 挑戦カード〈敵のシールド〉もこの池。 <b>増えた分には〈血の楔〉(<see cref="WedgeEnemyGain"/>) が掛かる</b> (2026-11-20)。</summary>
        public int vescaShield
        {
            get => _vescaShield;
            set { if (value > _vescaShield) value = _vescaShield + WedgeEnemyGain(value - _vescaShield); _vescaShield = value; }
        }
        private int _vescaShield;
        /// <summary>[persistent] このターン、 ヴェスカのシールドが吸収したダメージ量。
        /// 返しの盾 / 不落の城盾 の反射量算出に使う。 遺物学者の OnTurnStart で 0 に戻す。</summary>
        public int vescaShieldAbsorbedThisTurn;
        /// <summary>[persistent] 反射率（0=無し / 1.0=返しの盾 / 2.0=不落の城盾）。 遺物学者の OnTurnStart でリセット。</summary>
        public float vescaShieldReflectRate;

        /// <summary>[persistent] 大出血スタック。 **解除不可・ターン経過で減衰しない・4 連戦を跨いで持続**。
        /// 毎ターン開始時にこのスタック数分の軽減不可ダメージをプレイヤー HP へ与える。
        /// 既存の enemyBleedStacks（自然減衰あり・敵に付与）とは別物。</summary>
        public int massiveBleedStacks;

        /// <summary>[persistent] 〈解析演算〉の回避クールダウン。 回避に成功した次のターンは回避できない。
        /// 成功時に 2 を立て、 遺物学者の OnTurnStart で 1 ずつ減らす
        /// （立てたターン分を含めて数えるので、 実際に塞がるのは「次の 1 ターン」だけ）。</summary>
        public int enemyDodgeCooldown;

        /// <summary>[persistent] 〈天与の才〉: 次のターンだけ遺物を 2 枚引くか。
        /// ヴェスカの HP が 50% / 25% / 10% を **初めて割った次のターン**に立つ。
        /// 常時 2 枚だった旧仕様は上振れの天井が高すぎ (天与の剣+殺戮 = 攻撃+40 が同時に乗る)、
        /// 予告値の振れ幅が読めなかった。 追い詰められた瞬間だけ牙を剥く形へ。</summary>
        public bool vescaDoubleDrawNextTurn;
        /// <summary>[persistent] 既に通過した HP 閾値の数 (0..3)。 同じ閾値で二度発火させないための記録。</summary>
        public int vescaThresholdsPassed;

        /// <summary>[persistent] 解析演算による敵の回避率（0〜0.40）。 プレイヤーが収支プラスで終えるたび +0.02。
        /// **通常・固定・軽減不可を問わず全ダメージを回避する**（既存のメタ俊敏回避と同じ挙動）。</summary>
        public float enemyDodgeChance;

        // 旧 stagnationStacks (停滞する時間のスタック) は 2026-11-12 #103 で削除。 今の〈停滞する時間〉は端子を止める効果で、
        //   止まった場所は CombatSystem.TerminalBattleState.stopped (山札の状態) に持つ。

        /// <summary>[perTurn] 〈天与の指輪〉: このターン、 3 以上の出目をすべて 1〜2 へ再抽選する。
        /// 旧「全出目 1」は本数ぶんの手が完全に死に、 その前の「行動不可」と実質同じ重さだった。
        /// 再抽選なら低い目のまま **本数と幅は残る**ので、 配線の判断が成立する。</summary>
        public bool playerHighDiceCrushed;

        /// <summary>[perTurn] 〈天与の盾〉: このターン、 ヴェスカへの与ダメージが半分になる。
        /// 完全無効は「殴っても無駄」でターンが消えるのと変わらないため、 半減に留める。</summary>
        public bool vescaHalvesDamageThisTurn;

        /// <summary>[perTurn] このターンのプレイヤー攻撃力減算。 実効攻撃力は 0 で床を引く。
        /// 書き手: 7層ヴェスカ〈麻痺毒の小瓶〉/ 敵〈罠師〉〈呪縛〉(前のターンに収支で勝った分・OnTurnStart・2026-11-11 #104)。</summary>
        public int playerAttackPowerPenalty;

        /// <summary>[perTurn] 〈貫きの錐〉: このターン、 プレイヤーのブロック端子配線を無視する
        /// (blockSum を 0 として被ダメを計算)。 端子調律メタの本数ボーナスも巻き込んで無効化する。
        /// 予告 (§6-3 段2) に出るので、 「ブロックに振っても無駄」と配置前に読める。</summary>
        public bool playerBlockIgnored;
        /// <summary>[perTurn] 落雷の避雷針: プレイヤーダイスのうち 1 本（ランダム）を
        /// **そのダイスの最小面**に固定する予約 (2026-08-16: 固定値 1 から変更)。</summary>
        public bool lightningRodArmed;
        /// <summary>[decay] 腐敗の塗り薬: 残りターン数 (付与時 2・2026-08-16 に 3→2)。
        /// >0 の間プレイヤーの回復量を 50% 減少。
        /// 遺物学者の OnTurnStart でデクリメントする。</summary>
        public int playerHealHalvedTurns;
        /// <summary>[perTurn] 予告（§6-3 段2）表示用。 このターン ヴェスカが引いた遺物名。
        /// パッシブ欄に `名前[遺物学者]` 形式で出す。 遺物学者の OnTurnStart で作り直す。</summary>
        public List<string> vescaDrawnRelics = new List<string>();

        /// <summary>[perTurn] ヴェスカの攻撃が命中したとき、 プレイヤーに与える大出血スタック数
        /// （裂傷の刃心=1 / 殺戮=3）。</summary>
        public int vescaBleedOnHit;
        /// <summary>[perTurn] 天与の剣: ヴェスカの攻撃時にプレイヤーの最大 HP をこの数値だけ削る。</summary>
        public int vescaMaxHpBiteOnHit;
        /// <summary>[perTurn] 処刑人の烙印: 攻撃終了時、 プレイヤー HP が最大の 20% 以下なら
        /// 9999 の軽減不可ダメージ。</summary>
        public bool vescaExecuteArmed;

        /// <summary>[persistent] (戦闘の累計) 検証用計測: この戦闘でプレイヤーが実際に獲得した累計回復量／シールド量。
        /// AutoRunner が6/7層ボス戦で集計しサマリに記載する。戦闘ごとに新規生成でリセット。</summary>
        public int healAppliedTotal;
        /// <summary>[persistent] (戦闘の累計)</summary>
        public int shieldGainedTotal;

        /// <summary>[perTurn] このターンにプレイヤーが実際に受けたダメージ量（メイン＋固定）。
        /// 焦土〈最大HP -被ダメ10%〉が参照する。BeginNewTurn でリセット。</summary>
        public int playerDamageThisTurn;
        /// <summary>[persistent] 亡者の招待: 被ダメ+30% (0.3 = +30%)</summary>
        public float receivedDamageBonus;
        /// <summary>[perTurn] 激情の刃 等の与ダメ倍率（1.0 = 変化なし。BeginNewTurn でリセット）</summary>
        public float outgoingDamageMultiplier = 1f;

        /// <summary>[perTurn] 貪欲のダイス: 与ダメージのこの割合をプレイヤーが回復（0=無し。BeginNewTurn でリセット、パッシブが毎ターン再適用）</summary>
        public float lifestealPct;

        /// <summary>[perTurn] 苦難の刻印・不屈の鎧 等の被ダメ固定減算（複数アイテム合算）。CombatManager 敗北分岐で適用。BeginNewTurn でリセット。</summary>
        public int playerFlatDamageReduction;

        /// <summary>[decay] 残り敵パッシブ無効化ターン数。> 0 のとき FireEnemyTrigger をスキップ。各ターン末に減算。</summary>
        public int enemyPassivesDisabledTurns;

        /// <summary>[persistent] (段の差し替えで 0) 星火燎原 等: 敵(ボス)ダイス合計への加算ボーナス（累積。BeginNewTurn でリセットしない）。
        /// ProcessPostRoll の勝敗判定前に enemyDiceTotal へ加算される。</summary>
        public int enemyDiceTotalBonus;

        /// <summary>[perTurn] 沈黙の剣帯 等: 敵ダイス合計への「床なし」減算ペナルティ。
        /// enemyDiceDebuff は Math.Max(0) で0床処理されるが、こちらは床処理せず
        /// enemyDiceTotal を負値まで押し下げられる（=ロール大差勝ち＝基礎ダメ激増）。BeginNewTurn でリセット。</summary>
        public int enemyDiceTotalPenalty;

        /// <summary>[persistent] (段の差し替えで 0) ボス強者バフ: ボス(boss_layer*)のダイス合計への固定加算。フロアに応じて戦闘開始/形態swap時に設定。
        /// enemyDiceTotalBonus とは別枠（星火燎原等の上書きと競合させないため）。勝敗判定前に enemyDiceTotal へ加算。</summary>
        public int bossDiceBonus;
        /// <summary>[persistent] [計装] 加算前の素の敵ダイス合計。 BossCombatTrace が内訳を出すため。
        /// <b>persistent 区分</b> ── RecomputeDiceTotals が毎ロール上書きする。</summary>
        public int rawEnemyDiceTotal;

        /// <summary>[persistent] 〈回帰性真理〉(7層 p4 ヴェスカ・天与) が有効か。
        ///
        /// <para><b>2026-08-16 に「免疫」から「耐性」へ作り替えた。</b> 旧実装はデバフの付与を
        /// 丸ごと拒否していたため、 <b>毒ビルド・出血ビルドが最終ボスに一切参加できない</b>という
        /// 状態になっていた。 HP を「時計」(〈裂け目〉の自壊) として扱っていた頃は、
        /// 累積デバフが巨大 HP への近道になるので塞ぐ理由があったが、
        /// 自壊を撤去して HP が実際の壁になった以上、 その理由は消えている。</para>
        ///
        /// 立っている間の効果:
        ///   1. **毒・出血の蓄積を毎ターン半減** (付与は通る。 累積だけを抑える)
        ///   2. **受ける会心ダメージ −50%** (<see cref="enemyCritDamageMult"/>)
        ///   3. **最大 HP の変更**を拒否
        ///   4. **処刑** (HP 閾値による即死) を発動させない
        /// 3・4 は「一撃で決着する近道」を塞ぐためのもので、 デバフ排除とは別の話なので残す。
        /// 戦闘開始時にパッシブが立て、 戦闘終了まで持続する (p4 は最終段なので段 swap は起きない)。</summary>
        public bool enemyRecurrentTruth;

        /// <summary>[persistent] (計装・段の差し替えで更新) いまの形態が始まったターン番号 (1 始まり)。
        /// 4 連戦は <c>currentTurn</c> をリセットしないので、 形態ごとの実長を出すには
        /// 開始点を控えるしかない。 <c>SwapEnemy</c> が次段の開始ターンへ更新し、
        /// 戦闘終了時に最終段ぶんを締める。 単段の戦闘では 1 のまま使われない。</summary>
        public int phaseStartTurn = 1;

        /// <summary>[persistent] 敵が受ける会心の**倍率からの減算量** (既定 0 = 減算なし)。
        /// 〈回帰性真理〉が 0.5 を入れる ── 会心倍率 2.0 なら 1.5 になる。
        /// 適用後も <b>1.0 を下回らせない</b> (会心が通常攻撃より弱くなるのは倒錯するため)。
        /// <b>BeginNewTurn でリセットしない</b> ── 戦闘開始時に一度立てて終戦まで持つので persistent。
        ///
        /// <para><b>乗算 (×0.5) ではなく減算にしている理由。</b> 乗算だと会心倍率 2.0 が 1.0 になり
        /// 会心が完全に無意味になるうえ、 遺物で倍率を積むほど削られる量も比例して増える
        /// ＝ <b>投資するほど損</b>という反転が起きる。 減算なら 4.7 → 4.2 のように
        /// 積んだぶんは残るので、 会心ビルドの投資判断が壊れない。</para>
        ///
        /// <para><c>criticalMultiplier</c> を直接下げないのは、 あちらが BeginNewTurn で
        /// メタ値+遺物から**毎ターン再構築される**ため。 パッシブ側で書き換えても、
        /// その後に走る加算 (メタ精密 r10 の還元・物資系) に押し流されて効かない。
        /// 会心倍率が実際に乗る 1 箇所 (PassiveSkillManager の isCritical 分岐) で引く。</para></summary>
        public float enemyCritMultReduction = 0f;

        /// <summary>現在実効の会心倍率減算量。ダウン中は〈回帰性真理〉が外れるため 0。</summary>
        public float EffectiveEnemyCritMultReduction => CombatSystem.BossDown.ActiveNow ? 0f : enemyCritMultReduction;


        /// <summary>敵の処刑 (HP 閾値による即死) が通るか。 〈回帰性真理〉持ちには通らない（ダウン中は外れる）。</summary>
        public bool CanExecuteEnemy()
        {
            if (!enemyRecurrentTruth || CombatSystem.BossDown.ActiveNow) return true;
            UnityEngine.Debug.Log("[回帰性真理] 処刑を無効化");
            return false;
        }

        /// <summary>敵の最大 HP を変更する。 〈回帰性真理〉持ちは増減とも拒否する（ダウン中は外れる）。</summary>
        public void SetEnemyMaxHP(int value)
        {
            if (enemyRecurrentTruth && !CombatSystem.BossDown.ActiveNow)
            {
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[回帰性真理] 最大HP変更 ({enemyMaxHP}→{value}) を拒否");
                return;
            }
            enemyMaxHP = value;
        }

        /// <summary>[persistent] (戦闘開始・段の差し替えで設定) 現在の敵ボスid (boss_layer*)。 非ボス戦は空。 各ボススキルが BossParams.Param(bossId, ...) を引くのに使う。
        /// 戦闘開始/形態swap時に CombatManager がセット。</summary>
        public string bossId = "";

        /// <summary>[perTurn] 直近にプレイヤーへダメージを与えた致死メカニズムの分類。
        /// 各致死スキルが発動時にセット、 通常被ダメ経路は Normal。 プレイヤー死亡時の死因記録に使う。</summary>
        public DeathCause lastDamageCause = DeathCause.Normal;

        /// <summary>この戦闘でプレイヤーが受けた総被ダメの **ソース別内訳** (診断用)。
        /// キル時の一撃ではなく「戦闘を通じてどのメカニズムが何割を占めたか」で診断するための集計。
        /// 特殊スキル(審判の炎/毒/反射/断罪/爆ぜ火 等)が自分の与ダメを加算。 通常被ダメ(ロール敗北等)は
        /// 戦闘終了時に総被ダメからの残差として Normal に計上する。 戦闘開始時にクリア。</summary>
        public readonly System.Collections.Generic.Dictionary<DeathCause, int> playerDamageBySource
            = new System.Collections.Generic.Dictionary<DeathCause, int>();

        /// <summary>プレイヤー被ダメをソース別に加算 (特殊スキルが自分の与ダメ分を計上)。</summary>
        public void AddPlayerDamageSource(DeathCause cause, int amount)
        {
            if (amount <= 0) return;
            playerDamageBySource.TryGetValue(cause, out int v);
            playerDamageBySource[cause] = v + amount;
        }

        /// <summary>**敵パッシブ専用**: 軽減を無視するダメージをプレイヤーへ与える。
        ///
        /// <para><b>「軽減無視」はシールドを貫かない。</b> 軽減 (%カット・定数カット・基礎防御) を
        /// 無視するだけで、 <b>シールドは肩代わりなので機能する</b> ── これが仕様
        /// (2026-08-15 決定)。 旧実装は各スキルが <c>enemyCurrentHP</c> を直接引いており、
        /// <see cref="CombatManager.ApplyLossDamageModifiers"/> 内にあるシールド吸収を
        /// 経路ごと飛ばしていたため、 盾を厚く積む意味が「通常攻撃にだけある」状態になっていた。</para>
        ///
        /// <para><b>敵視点でのみ呼ぶこと。</b> <c>PassiveSkillManager.SwapPerspective</c> は HP/ダイス/勝敗を
        /// 入れ替えるが <see cref="consShield"/> は入れ替えない。 したがって敵スキルの中では
        /// <c>enemyCurrentHP</c> = プレイヤーHP、 <c>consShield</c> = プレイヤーのシールドになり、
        /// この組み合わせだけが正しい。 プレイヤー側スキルから呼ぶと自分の盾を削る。</para>
        ///
        /// <param name="cause">被ダメ内訳と死因タグへ計上する分類。 <c>null</c> なら計上しない
        ///   (従来から計上していなかったスキルの挙動を保つため)。</param>
        /// <returns>シールドで吸収されず実際に HP を削った量。</returns>
        /// <summary>[persistent・計装] この戦闘でシールドが肩代わりした量の内訳と、 軽減無視ダメージの総量。
        /// 戦闘ごとに ctx が作り直されるので BeginNewTurn でのリセットは不要。
        ///
        /// <para><b>何を切り分けるための計装か。</b> 軽減無視をシールドで受けられるようにしたところ
        /// (2026-08-15)、 全体の被ダメ総量は −3.7% になったのに <b>5層ボス戦だけ被ダメが
        /// 49.6 → 54.0 と増え</b>、 6F 到達がペア比較で有意に悪化した (改善20/悪化45, p=0.003)。
        /// シールドは有限の池なので、 <b>毎ターン確実に飛ぶチップが先に食い、
        /// 致命傷になる大きい一撃の時に残っていない</b>という疑いがある。
        /// それを見るには「盾がチップに何点使われ、通常攻撃に何点使われ、何点余ったか」が要る。</para></summary>
        public int dbgShieldToChip, dbgShieldToNormal, dbgChipRaw, dbgChipToHp;

        /// <summary>軽減無視ダメージをシールドが肩代わりするか。 **既定 true (2026-08-15 の仕様)**。
        ///
        /// <para>false は<b>対照群を採るためだけ</b>の退避経路で、 製品挙動ではない。
        /// この仕様を入れたところ 5層ボス戦が 14.7 → 15.6 ターンに伸び、 6F 到達が
        /// ペア比較で有意に悪化した (改善20/悪化45, p=0.003)。 〈シールドバッシュ〉が
        /// **残存シールドの 20〜80% を攻撃力に加算**する常時効果で、 しかも学習 Tier で S 判定
        /// (累積 233,500 ラン) のため広く所持されている ── チップが毎ターン盾を削ると
        /// この火力が消える、 という筋が疑わしい。 空振り率の変更前の値を採るために要る。</para></summary>
        public static bool ShieldAbsorbsUnmitigable = true;

        public int EnemyDealUnmitigable(int dmg, DeathCause? cause)
        {
            if (dmg <= 0) return 0;
            dbgChipRaw += dmg;

            if (ShieldAbsorbsUnmitigable && consShield > 0)
            {
                int absorbed = Keywords.Kw.AbsorbShield(this, dmg, isAttack: false);
                dmg -= absorbed;
                dbgShieldToChip += absorbed;
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[軽減無視] シールドが {absorbed} 肩代わり (残シールド {consShield})");
                if (dmg <= 0) return 0;
            }

            dbgChipToHp += dmg;
            enemyCurrentHP = System.Math.Max(0, enemyCurrentHP - dmg);
            if (cause.HasValue)
            {
                AddPlayerDamageSource(cause.Value, dmg);
                if (enemyCurrentHP <= 0) lastDamageCause = cause.Value;
            }
            return dmg;
        }

        // ===== 防御値と貫通 (ADR-0013・2026-10-28) =====
        //   軽減の式は CombatSystem.DefenseFormula。 両陣営で同じ式・同じ目盛り。
        //   貫通は相手の防御値を固定値で引く。 余った貫通は何にもならない。

        /// <summary>[persistent] 敵の防御値。 <c>EnemyData.defense</c> を戦闘開始 / 形態 swap 時に設定し、
        /// 〈精鋭〉+2・〈硬鱗〉+4・〈重甲〉+4 が OnBattleStart で足す。 視点スワップでは入れ替わらない。
        /// 勝利分岐で敵の軽減パッシブの後に <c>DefenseFormula.Apply(total, Effective(enemyDefense, playerArmorPen))</c>。</summary>
        public int enemyDefense;

        /// <summary>[persistent] 敵がプレイヤーの防御を固定値で削る量。 <c>EnemyData.armorPen</c> を戦闘開始 / swap 時に設定。</summary>
        public int enemyArmorPen;

        /// <summary>[persistent] プレイヤーの防御の土台 = 初期値 + 武器。 戦闘開始時に CombatManager が設定する。</summary>
        public int playerDefenseBase;

        /// <summary>[perTurn] プレイヤーの防御値。 BeginNewTurn で <see cref="playerDefenseBase"/> に戻り、
        /// アイテムのステータス <c>def</c> が OnPostRoll で足す。 予告の前に敵の攻撃値へ掛かる (ブロックの前)。</summary>
        public int playerDefense;

        /// <summary>[persistent] プレイヤーの貫通の土台 = 武器。 戦闘開始時に CombatManager が設定する。</summary>
        public int playerArmorPenBase;

        /// <summary>[perTurn] プレイヤーの貫通。 BeginNewTurn で <see cref="playerArmorPenBase"/> + 遺物に戻り、
        /// アイテムのステータス <c>pen</c> が OnPostRoll で、 役〈攻め〉T4 が解決時に足す。</summary>
        public int playerArmorPen;

        /// <summary>[perTurn] 敵の固定削り (<see cref="enemyArmorPen"/>) を打ち消す量。 剣〈間合〉が OnPostRoll で足し、
        /// BeginNewTurn で 0 に戻る。 予告の前にプレイヤーの防御を掛けるとき、 削りから先に引く (下限 0)。</summary>
        public int enemyArmorPenNegate;

        /// <summary>[perTurn] 勝利時の与ダメ最低保証（基本1、利刃Lvで1/2/3/4）。
        /// パッシブが OnPostRoll で再set。BeginNewTurn で 1 リセット。</summary>
        public int winMinDamage;

        /// <summary>[persistent] 敵の回復量を減衰させる割合(0～1)。治癒阻害(0.5)/治癒遮断(1.0)が OnBattleStart で設定。
        /// 敵パッシブの回復は ReduceEnemyHeal() を通すことで適用される。BeginNewTurn でリセットしない。</summary>
        public float enemyHealReductionPct;

        /// <summary>[persistent] 軽減無視ダメージ(fixedDamageToEnemy)の倍率。蒼白の槍騎士が設定(既定1.0)。
        /// 血令/反撃/業火/停戦 等の固定ダメに乗る。BeginNewTurn でリセットしない。</summary>
        public float fixedDamageMultiplier = 1f;

        /// <summary>[perTurn] シールドバッシュ: ロール勝利時、与ダメのこの割合をシールド化(5/10/15/20%)。
        /// パッシブが OnTurnStart で再set。BeginNewTurn で 0 リセット。</summary>
        public float shieldOnWinPct;

        // ==== 臨界メーター (Rinkai) 2026-07-16 追加 ====
        /// <summary>[persistent] 臨界メーター: 攻撃の端子に挿した出目の合計の半分 (HeatCore.EndGain) を毎ターン配線のとき (攻撃の前・2026-11-25〜) に足し、 品・端子の分はその時点で足す (Keywords.Kw.AddHeat)。
        /// 閾値に届くとその場で爆発する (式は CombatSystem.HeatCore)。</summary>
        public int rinkaiMeter;
        /// <summary>[persistent] 直近ターンにプレイヤーが与えた総ダメージ。
        /// オーバーロードの発動判断 (「倍にすれば倒し切れるか」) にだけ使う。
        /// ctx は戦闘ごとに作り直されるので戦闘跨ぎでは残らない。</summary>
        public int lastPlayerDamage;
        /// <summary>[perTurn] このターンの与ダメージを 2 倍にする (オーバーロード発動中)。
        /// BeginNewTurn Phase B でリセットする。</summary>
        // [撤去 2026-09-13] overloadThisTurn ── 出力 r10 の旧極点〈オーバーロード〉。
        //   〈戦意〉(累積成長) へ差し替え。 経緯は MetaPanel.BattleSpiritUnlocked。
        /// <summary>[persistent] 臨界の閾値。 既定 30 (〈早沸かし釜〉−12・〈暴れ窯の火床〉で下がる・下限 12・CombatSystem.HeatCore)。 0 = 臨界が動いていない。</summary>
        public int rinkaiThreshold;

        // ---- 規則の品 (2026-10-31 アイテム再編)。 どれも戦闘開始時に品が立てる [persistent] ----
        /// <summary>[persistent] 〈雷壷〉: 充電の上限が無くなる (<see cref="AddCharge"/>)。 ターン終了時の目減りは品のクラス (ChargeEffects.ThunderJar)。</summary>
        public bool chargeCapRemoved;
        /// <summary>[persistent] 〈廃庭の霧吹き〉: 敵が攻撃した直後にも、 毒のダメージ (威力 50%・毒は減らない) が 1 回入る (1 ターン 1 回・CombatManager の敵の攻撃の解決)。</summary>
        public bool poisonTickOnEnemyAttack;
        /// <summary>[persistent] 〈毒溜まりの主〉: 敵の毒が <see cref="CombatSystem.PoisonCore.PoolNeed"/> 以上なら、 ターン開始の毒のダメージが 2 回入る (2 回目では毒が減らない・<see cref="TickStatuses"/>)。</summary>
        public bool poisonDoubleTick;
        /// <summary>[persistent] 〈骨溶かしの黒油〉: ダウン中の敵への毒のダメージ ×2 (<see cref="TickStatuses"/>・起爆は PoisonCore.Damage)。</summary>
        public bool poisonDownDouble;

        // ---- 軸の締めの LEGENDARY (2026-11-20・GAME.md §23-32) ----
        /// <summary>[persistent・戦闘開始で 0] 最終与ダメージ倍率の上乗せ (%)。 すべての倍率 (会心・与ダメ % の池・端子の別枠) の後、
        /// 敵の軽減パッシブと防御の前に ×(100 + これ)/100 で掛ける (<see cref="Keywords.Kw.ApplyFinalDamage"/>・大罪:怠惰と同じ位置)。 〈防人の軍標〉が積む。</summary>
        public int finalDamagePct;
        /// <summary>[persistent] 〈腐蝕の王冠〉: 敵の毒の上限 (99) が無くなり、 敵へ与える毒がすべて 1.5 倍 (切り上げ・<see cref="AddStatus"/>)。 段の交代で毒の半分が次の段へ残る (CombatManager.SwapEnemy)。</summary>
        public bool poisonCrown;
        /// <summary>[persistent] 〈蓋付きの毒指輪〉: 戦闘開始から 5 ターン目まで敵へ与える毒 +2 (<see cref="AddStatus"/>)。</summary>
        public bool poisonRingEarly;
        /// <summary>[persistent] 〈血の楔〉: 敵が得るシールドと回復の量、 敵の被ダメ軽減のパッシブで減る量を 出血 ÷ (出血 + 30) だけ弱める (<see cref="WedgeEnemyGain"/>)。</summary>
        public bool bleedWedge;
        /// <summary>[persistent] 〈涸れぬ血晶〉: 敵の出血が既に <see cref="CombatSystem.BleedCore.CrystalNeed"/> 以上ある時、 与える出血が 2 倍 (<see cref="AddEnemyBleed"/>)。</summary>
        public bool bleedDouble;
        /// <summary>〈血の楔〉の割る数の足し分 (出血 ÷ (出血 + これ))。</summary>
        public const int BleedWedgeK = 30;
        /// <summary>〈血の楔〉: 敵の得る量 n を 30 ÷ (出血 + 30) 倍にする (切り捨て)。 品が無い・出血 0 なら素通し。</summary>
        public int WedgeEnemyGain(int n)
            => bleedWedge && n > 0 && enemyBleedStacks > 0 ? (int)((long)n * BleedWedgeK / (enemyBleedStacks + BleedWedgeK)) : n;

        // ---- 軸の ① 数を参照・②【規則】の品が立てる値 (2026-11-07)。 どれも戦闘開始時に品が書く [persistent] ----
        /// <summary>[persistent] 毒がダメージを出したあとに残る割合 (%)。 基準 50 (半減)。 〈瘴気〉の②で上がる。</summary>
        public int poisonKeepPct = 50;
        /// <summary>毒が残る割合。 上限 90% (2026-11-10: 〈腐れ沼の苔玉〉の 70% と重ねられるように・遺物の上乗せは 2026-11-23 に撤去)。</summary>
        public int EffectivePoisonKeepPct => System.Math.Min(PoisonKeepCap, poisonKeepPct);
        /// <summary>毒が残る割合の上限 (%)。</summary>
        public const int PoisonKeepCap = 90;
        /// <summary>[persistent] 出血の比例減衰の割る数への上乗せ (AxisRules.BleedDecay)。 〈血晶〉の②。</summary>
        public int bleedDecayDivisorBonus;
        /// <summary>[persistent] 爆発の基礎への上乗せ (〈二度詰めの雷管〉が爆発のたびに +6・上限なし・減らない・CombatSystem.HeatCore)。</summary>
        public int burstStackBonus;
        /// <summary>[persistent] ターン開始の減衰で残るシールドへの上乗せ (% ポイント)。 〈油染みの盾革〉が +20 (50% → 70%)。 残る割合は最大 80%。</summary>
        public int shieldKeepBonusPct;
        /// <summary>[persistent] 会心した攻撃だけに足す貫通 (〈刃こぼれの見立て〉・CombatManager.Damage の防御の計算)。</summary>
        public int critArmorPen;
        /// <summary>[persistent] 充電の上限への上乗せ (〈雷鳴の太鼓〉・端子〈大導電＋〉・<see cref="ChargeCap"/>)。</summary>
        public int chargeCapBonus;
        /// <summary>[perTurn] 敵の攻撃を割合で減らす (0.2 = −20%)。 予告の前に掛ける (CombatManager.Turn の予告)。 BeginNewTurn の Phase B で 0。</summary>
        public float enemyAtkCutPct;

        /// <summary>[persistent] 種火・毒 r2 の上乗せを最後に使ったターン (1 ターンに 1 回)。</summary>
        private int _poisonBonusTurn = -1;

        /// <summary>[persistent] 軸の品の数の控え (戦闘ごとに CombatContext を作り直すので、 戦闘の間は所持品が変わらない前提で 1 度だけ数える)。</summary>
        private readonly System.Collections.Generic.Dictionary<string, int> _axisDepth = new System.Collections.Generic.Dictionary<string, int>();

        /// <summary>軸の品の数 (心眼 / 血晶 / 瘴気 / 炉温 / 電流 / 堅牢・<see cref="AxisRules"/>)。</summary>
        public int AxisDepth(string axis)
        {
            if (_axisDepth.TryGetValue(axis, out int d)) return d;
            d = AxisRules.Count(GameLoop.GameManager.Instance?.Run, axis);
            _axisDepth[axis] = d;
            return d;
        }
        /// <summary>偏重の順位: この軸より品の数が多い軸の数 (0 = 最も大きい偏重・同数は上に数えない)。
        /// 品を 1 つも持たない軸は順位に入らない (99)。</summary>
        public int AxisRank(string axis)
        {
            int d = AxisDepth(axis);
            if (d <= 0) return 99;
            int above = 0;
            foreach (var a in AxisRules.Ranked) if (a != axis && AxisDepth(a) > d) above++;
            return above;
        }
        /// <summary>[persistent] 〈寸鉄〉: 超過会心率の会心倍率への変換が 2 倍 (CombatSystem.CritCore.ExcessMul・PassiveSkillManager.ProcessDamage)。</summary>
        public bool critExcessDouble;
        /// <summary>[persistent] 〈銑鉄棍〉: 会心しなかった撃に、 会心率 1% につき与ダメージ +1% (100% を上限に数える・与ダメ % の池へ足す・PassiveSkillManager.ProcessDamage)。 会心は普通に出る。</summary>
        public bool critToDamage;
        /// <summary>[persistent] 〈天工開物〉: 爆発の基礎への上乗せ (武器を強化した回数 × 8・CombatSystem.HeatCore.Damage)。</summary>
        public int burstFlatBonus;
        /// <summary>[persistent] 〈重装兵の隠し刃〉: ボスのダウン中のターンは、 ターン開始時にシールドが半減しない (BeginNewTurn)。</summary>
        public bool shieldKeepOnBossDown;
        /// <summary>[persistent] 〈脈打たぬ鋼の心臓〉: ターン開始時のシールドがこれ以上なら減衰しない (0 = なし・BeginNewTurn)。</summary>
        public int shieldNoDecayAt;
        /// <summary>[persistent] 〈二拍目の心臓〉: この戦闘のシールドの獲得量 +N% (増えた分に掛けて切り捨て・<see cref="consShield"/> の set)。</summary>
        public int shieldGainPct;
        /// <summary>[persistent] 〈過負荷チューナー〉: 過充電の間、 充電を払う行動 (振り直し・再起動・端子・秘宝) の払う量が 2 減る (最低 1・<see cref="ChargeCostNow"/>)。</summary>
        public bool overloadChargeCut;
        /// <summary>充電を払う量から今引く量 (〈過負荷チューナー〉の過充電の間 <see cref="CombatSystem.ChargeCore.TunerCut"/>・それ以外 0)。 端子の解決には TerminalEnv.chargeCostCut で渡す。</summary>
        public int ChargeCostCut => overloadChargeCut && IsOvercharged() ? CombatSystem.ChargeCore.TunerCut : 0;
        /// <summary>充電を n 払う時に実際に払う量 (<see cref="CombatSystem.ChargeCore.CostNow"/>・最低 1)。 振り直しの支払いが通す。</summary>
        public int ChargeCostNow(int n) => CombatSystem.ChargeCore.CostNow(n, ChargeCostCut);
        /// <summary>[perTurn] 品のブロック +N (このターンのブロックに足す・CombatManager.Turn の配線の集計)。 BeginNewTurn の Phase B で 0。</summary>
        public int itemBlockBonus;
        /// <summary>[perTurn] このターンの最初の 1 撃の攻撃 +N (〈引き直しの撃鉄〉・攻撃しないターンは消える)。 BeginNewTurn の Phase B で 0。</summary>
        public int firstHitAttackBonus;
        /// <summary>[perTurn] 直近のプレイヤーの攻撃で合算した会心率 (%・100 超えもそのまま)。 超過会心率の変換 (CritCore)・〈銑鉄棍〉が読む。
        /// ProcessDamage が書き、 BeginNewTurn の Phase B で 0。</summary>
        public float lastCritAddPct;


        /// <summary>敵(自身)の回復量に enemyHealReductionPct と〈血の楔〉を適用して返す（治癒阻害用）。</summary>
        public int ReduceEnemyHeal(int heal)
        {
            if (heal <= 0) return heal;
            if (enemyHealReductionPct > 0f) heal = (int)(heal * (1f - enemyHealReductionPct));
            return WedgeEnemyGain(heal);   // 〈血の楔〉(2026-11-20)
        }


        // === 2026-06-28: 職業スターター消耗品の状態フラグ ===
        /// <summary>[persistent] (1 撃使ったら CombatManager.Damage.cs が false) 瞬間研磨剤 (剣士): 次の 1 撃だけ 与ダメージ+150%。
        /// ApplyWinDamageModifiers で outgoingDamageMultiplier += 1.5 して disarm (2026-07-27 リワーク)。</summary>
        public bool polishArmed;
        /// <summary>[persistent] 不抜の聖紋 (騎士): 戦闘中 HP 80% 以上を保っている間 被ダメ -40%。
        /// 一度でも 80% を割ったら oathBroken=true で以後無効。 戦闘終了で破棄。</summary>
        public bool oathArmed;
        /// <summary>[persistent]</summary>
        public bool oathBroken;
        /// <summary>[perTurn] (Phase A で判定してから false (Phase B ではない)) 痛覚遮断剤 (狂戦士): 使用ターン 致命ダメで HP 1 踏みとどまり、
        /// 次ターン開始時 HP=1 なら攻撃+10・そのターンの攻撃の最終与ダメージ+50% (Consumables.PainkillerFinalPct)、 攻撃終了時に死亡。
        /// armedThisTurn=使用ターン中、 procced=次ターンに踏みとどまり判定済+強化バフ発動中。</summary>
        public bool painkillerArmedThisTurn;
        /// <summary>[decay] (Phase A で立ち、 そのターンの攻撃の後に CombatManager.Turn.cs が倒す)</summary>
        public bool painkillerProcced;
        /// <summary>[perTurn] (戻すのは BeginNewTurn ではなく、 そのターンの敵の攻撃の解決 (CombatManager.Turn.cs)) 仕込み刃 (暗殺者): 使ったターンの被ダメを 0 にし、 その
        /// <see cref="CombatSystem.CombatManager.DaggerReflectMultiplier"/> 倍を軽減不可で敵に与える。
        /// <b>このターン限り</b>: 被弾の有無にかかわらず敵の攻撃処理 (行動不能を含む) で破棄。 persistent 区分。</summary>
        public bool daggerArmed;

        /// <summary>[persistent] (予約。 SwapEnemy で null) ボス連戦 (ヴェスカ 4段): 次段の enemy id。敵パッシブ OnTurnEnd で設定され、
        /// CombatManager が perspective 復帰後に SwapEnemy で消費 → null クリア。</summary>
        public string pendingEnemySwapId;
        /// <summary>[persistent] (予約。 SwapEnemy で null) ヴェスカ連戦: 遷移ログラベル</summary>
        public string pendingEnemySwapLabel;

        // ===== 消費アイテム由来（この1戦闘のみ。ctxは戦闘毎に生成→破棄で自動消去） =====
        public int consDiceRoll;          // [persistent] 攻撃+X（CombatManager.Turn.cs が攻撃値へ足す）。 書くのは〈痛覚遮断剤〉の発動 (BeginNewTurn Phase A) だけ
        /// <summary>[persistent] この戦闘で払ったリロールの回数（2026-10-17〜）。 費用 = 振り直す個数 × (この値 + 1)。
        /// <b>ターンでは戻らない</b> ── 同じ戦闘で振り直すほど高くつく（続けるとジリ貧）。 ctx は戦闘ごとに作り直すので、
        /// 戦闘の頭で 0。 〈中階〉の無料リロールは数えない。</summary>
        public int rerollsThisCombat;
        /// <summary>[perTurn] このターンに振り直した回数（無料を含む）。 BOT の 1 ターンあたりの上限判定に使う。
        /// BeginNewTurn の Phase B で 0。</summary>
        public int rerollsThisTurn;
        /// <summary>[decay] 残シールド吸収量。 毎ターン頭 (T2 以降) に半減 (BeginNewTurn の Phase C)。
        /// <b>増えたときに〈シールドが増えた〉出来事を配る</b> (2026-10-31)。 減らすのは吸収 (Kw.AbsorbShield) と半減と品の消費。</summary>
        public int consShield
        {
            get => _consShield;
            set
            {
                if (value > _consShield)
                {
                    int asked = value;
                    if (shieldGainPct > 0) value += (value - _consShield) * shieldGainPct / 100;   // 〈二拍目の心臓〉: この戦闘のシールドの獲得量 ×1.5
                    // カイロスの傲慢 (挑戦カード IV): シールドの獲得 −N% (四捨五入・デバフの丸め)
                    float prideCut = MetaProgression.MetaDebuffApplicator.GetSinShieldCutPct();
                    if (prideCut > 0f)
                        value = _consShield + UnityEngine.Mathf.RoundToInt((value - _consShield) * (1f - prideCut / 100f));
                    // [計装 2026-11-12・#95] 呼び出し側は頼んだ量だけを shieldGainedTotal へ足すので、 遺物の上乗せと傲慢の削りの差をここで足す
                    //   (遺物なし・傲慢なしでは 0 ── 学習の条件では記録が変わらない)。
                    shieldGainedTotal += value - asked;
                }
                int gained = value - _consShield;
                _consShield = value;
                if (gained > 0) Keywords.Kw.Raise(this, PassiveSkillTrigger.OnShieldGain, gained);
            }
        }
        private int _consShield;
        /// <summary>[perTurn] (BeginNewTurn の Phase B で 0) このターンに<b>敵の主攻撃</b>をシールドが肩代わりした量 (2026-11-14)。
        /// 書くのは CombatManager.ApplyLossDamageModifiers のシールド吸収 (1 回目・〈連撃〉/2 体戦の 2 回目の両方) だけ。
        /// 軽減無視 (〈業火〉の烈炎の分・審判の炎・〈蝕み〉など EnemyDealUnmitigable) の肩代わりは数えない。
        /// 灰燼の王の〈烈炎〉がターン終了時に読む (<see cref="CombatSystem.HeavyCycle.BlazeGain"/>・N ごとに +1)。 戦闘の通算は <see cref="dbgShieldToNormal"/>。</summary>
        public int shieldAbsorbedAttackThisTurn;
        public int consShieldExpireTurn;  // [decay] (減算は CombatManager.Turn.cs (BeginNewTurn の外)) この番号を超えるターンで失効。-1=無制限/0=無
        public int consDmgMultPct;        // [decay] (残ターンが尽きると CombatManager.Turn.cs が 0 (BeginNewTurn の外)) 与ダメ +X%
        /// <summary>[decay] cons_dmg 効果の残ターン数。0=無効, -1=戦闘中永続, N=残 N ターン。
        /// EndTurn で decrement し 0 になった瞬間 consDmgMultPct を 0 化する。2026-07-21 追加。</summary>
        public int consDmgMultTurnsLeft;

        // ===== エリート: 精鋭ハーピィ「死翔」 =====
        public bool consumablesLocked;    // [persistent] この戦闘中、消費アイテム使用不可（戦闘開始時に付与・戦闘ごとに新規生成でリセット）

        /// <summary>
        /// コンテキストを初期化
        /// </summary>
        public CombatContext(int playerMaxHP, int enemyMaxHP = 0, int enemyThreat = 0)
        {
            this.playerMaxHP = playerMaxHP;
            playerCurrentHP = playerMaxHP;
            this.enemyMaxHP = enemyMaxHP;
            enemyCurrentHP = enemyMaxHP;
            this.enemyThreat = enemyThreat;
            currentTurn = 0;
            isFirstRoll = true;
            enemyDefense = 0;
            enemyArmorPen = 0;
            playerDefenseBase = 0;
            playerDefense = 0;
            playerArmorPenBase = 0;
            playerArmorPen = 0;
            enemyArmorPenNegate = 0;
            winMinDamage = 1;
            enemyHealReductionPct = 0f;
            fixedDamageMultiplier = 1f;
            enemyVulnerabilityMultiplier = 0f;
            enemyVulnerabilityArmed = false;
            currentEnemyKind = CombatSystem.EnemyKind.Normal;
            shieldOnWinPct = 0f;
            // 会心倍率: 基礎 2.0 + メタバフ Lv58 (会心ダメージ +X%)。 他バフ (ProvisionSystem苦悩・パッシブ) と加算合成。
            criticalMultiplier = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier()
                                 + MetaProgression.MetaBuffApplicator.GetCritDamageBonus();
            accumulatedValues = new Dictionary<string, float>();
            nextTurnBuffs = new Dictionary<string, float>();
            currentBuffs = new Dictionary<string, float>();
            enemyDiceOverrides = new Dictionary<int, int>();
            pendingDiceOverrides = new List<DiceOverrideRequest>();
            activeModifiers = new List<BattleModifier>();
            playerStatusStacks = new Dictionary<string, int>();
            enemyStatusStacks = new Dictionary<string, int>();
        }

        // ===== 会心率カーブ（2026-07-26 リバランス） =====

        /// <summary>戦闘中バフ (currentBuffs / nextTurnBuffs) の会心率キー。 値は小数 (0.165 = +16.5%)。</summary>
        public const string CritRateBuffKey = "critRatePct";

        /// <summary>会心率の加算合計を実効会心率へ変換する。 <b>2026-09-15 に逓減を撤去</b>したので
        /// 現在は 0〜1 へのクランプだけ ── <b>加算した分がそのまま乗る</b>。
        ///
        /// <para>旧実装は knee 20% からのハイパボリック逓減 (<c>knee + over/(1+over×1.25)</c>) で、
        /// 100% へは漸近するだけだった。 これが会心を軸にしたビルドを構造的に潰していた:
        /// 実効会心率の分布が P90/P10 = 1.46 倍まで潰れ (§15-5)、 会心率を配るアイテムも遺物軸も
        /// 「積むほど効かない」ため差がつかなかった。 精密トラックが 2026-09-10 に撤去された
        /// 直接の原因も二重減衰 (名目 +15% が実効 +9.2pt) で、 逓減はそちら側からも否定されている。</para>
        ///
        /// <para><b>関数そのものは残す。</b> 表示 (<see cref="ItemDataV2.CriticalRateLabel"/>) と
        /// 判定 (<see cref="PassiveSkillManager"/>) が同じ 1 箇所を通ることが乖離を防いでいる。
        /// カーブを入れ直すならここだけを書き換えれば全経路に効く。</para></summary>
        public static float ResolveCritRate(float addTotal)
        {
            if (addTotal <= 0f) return 0f;
            return addTotal < 1f ? addTotal : 1f;
        }

        // ===== ステータス統一フレーム（#3） =====

        /// <summary>ステータスをスタック付与（maxStacks でクランプ）。target は絶対視点。
        /// 2026-07-25 v6: 毒スタック付与 (target=Enemy, id="poison", stacks>0) の場合、
        /// メタ「毒 r2 濃縮」で追加 +N (通常 +1)。 減衰・削減 (stacks<0) には作用しない。</summary>
        public void AddStatus(StatusTarget target, string id, int stacks)
        {
            if (stacks == 0 || string.IsNullOrEmpty(id)) return;
            // 2026-08-16: 〈回帰性真理〉のデバフ**付与拒否**は撤去した。
            //   毒/出血ビルドが最終ボスに参加できない状態になっていたため。
            //   累積の抑制は RecurrentTruth 側の「毎ターン半減」が担う。
            if (stacks > 0 && target == StatusTarget.Enemy && id == "poison")
            {
                if (_poisonBonusTurn != currentTurn)
                {
                    // 種火・毒 r2 (2026-11-07): 「毒を与えるたび +1」→「1 ターンに最初の 1 回だけ +2」。
                    //   毒の入り口を品ごとに増やしたので、 与えるたびだと入り口の +1 がすべて 2 倍になった。
                    int metaBonus = MetaProgression.MetaBuffApplicator.GetPoisonApplyBonus();
                    if (metaBonus > 0) { stacks += metaBonus; _poisonBonusTurn = currentTurn; }
                }
                if ((poisonRingEarly || (currentTurn <= 1 && AxisRules.Owns(GameLoop.GameManager.Instance?.Run, GameLoop.ItemIds.PoisonRing)))
                    && currentTurn <= CombatSystem.PoisonCore.RingAssassinTurns)
                {
                    stacks += CombatSystem.PoisonCore.RingBonusPoison;
                }
            }
            if (poisonCrown && stacks > 0 && target == StatusTarget.Enemy && id == "poison") stacks = (stacks * 3 + 1) / 2;   // 〈腐蝕の王冠〉: 1.5 倍 (切り上げ)
            var dict = target == StatusTarget.Enemy ? enemyStatusStacks : playerStatusStacks;
            int cur = dict.TryGetValue(id, out var v) ? v : 0;
            int max = StatusRegistry.Defs.TryGetValue(id, out var def) ? def.maxStacks : int.MaxValue;
            if (poisonCrown && target == StatusTarget.Enemy && id == "poison") max = int.MaxValue;   // 〈腐蝕の王冠〉
            int next = System.Math.Max(0, System.Math.Min(max, cur + stacks));
            dict[id] = next;
            if (target == StatusTarget.Enemy && id == "poison" && stacks > 0)   // [計装 2026-11-05] 毒の付与量を出どころ別に
            {
                string hlSrc = string.IsNullOrEmpty(CurrentSkillId) ? "(戦闘開始・品以外)" : CurrentSkillId;
                CombatSystem.HpLossDiag.Add("毒付与:" + hlSrc, stacks);
                int hlOver = cur + stacks - max;
                if (hlOver > 0) CombatSystem.HpLossDiag.Add("毒溢れ:" + hlSrc, hlOver);
            }
            // 敵の毒の増加を配る (2026-10-31 アイテム再編)
            if (target == StatusTarget.Enemy && id == "poison" && next > cur)
            {
                Keywords.Kw.Raise(this, PassiveSkillTrigger.OnPoisonGain, next - cur);
            }
        }

        /// <summary>敵の毒の量 (読み取り用)。</summary>
        public int EnemyPoison => GetStatus(StatusTarget.Enemy, "poison");

        /// <summary>毒のダメージ 1 回分 (ターン開始の分の <paramref name="powerPct"/>%・下限 1) を数え、 出来事を配って返す。 毒は減らさない。
        /// 〈廃庭の霧吹き〉で敵の攻撃の直後に CombatManager が呼び (1 ターン 1 回・威力 50%)、 返った量を敵の HP から直接引く。</summary>
        public int TickPoisonOnce(int powerPct)
        {
            if (!StatusRegistry.Defs.TryGetValue("poison", out var def)) return 0;
            int stacks = EnemyPoison;
            if (stacks <= 0) return 0;
            int dmg = System.Math.Max(1, DotDamage(def, stacks) * powerPct / 100);
            CombatSystem.CombatStats.DamageBreakdown[20] += dmg;   // [計装] 毒のダメージ
            Keywords.Kw.Raise(this, PassiveSkillTrigger.OnPoisonTick, dmg);
            CombatSystem.CombatManager.RaiseDotDamage(CombatSystem.CombatManager.DotKind.Poison, dmg);
            return dmg;
        }

        /// <summary>敵へ出血スタックを付与する。
        /// enemyBleedStacks は統一フレーム未移行のフィールドなので、 **付与は必ずここを通すこと**。
        /// 減衰 (BeginNewTurn の自然減) と 0 クリアは直接触ってよい。
        /// 2026-08-16: 〈回帰性真理〉の付与拒否を撤去（累積抑制は毎ターン半減へ移行）。</summary>
        public void AddEnemyBleed(int stacks)
        {
            if (stacks <= 0) return;
            stacks = CombatSystem.BleedCore.CrystalGain(stacks, enemyBleedStacks, bleedDouble);   // 〈涸れぬ血晶〉
            enemyBleedStacks += stacks;
            if (enemyBleedStacks > GetAccumulated(Keywords.Kw.BleedMaxKey))
                accumulatedValues[Keywords.Kw.BleedMaxKey] = enemyBleedStacks;   // 実績が読む
        }

        /// <summary>〈回帰性真理〉が毎ターン残す毒の % (切り捨て・2026-11-22 neon 収束: 50 → 75)。</summary>
        public const int RecurrentTruthPoisonKeepPct = 75;
        /// <summary>〈回帰性真理〉: 敵に乗っている出血の蓄積を**半減**、 毒の蓄積を **3/4** にする (どちらも切り捨て・2026-11-22 neon 収束: 毒も半減 → 3/4。
        /// 毒はターン開始の毒のダメージでも減るので、 半減を重ねると二重に減っていた)。
        /// ターン開始の DOT 判定が済んだ後に呼ぶ ── ダメージは通し、 累積だけを抑える。
        ///
        /// <para>切り捨てなので、 毎ターン +N を撃ち続けるビルドは概ね N 前後で平衡する
        /// (x → (x+N)/2 の不動点が N)。 「効かない」ではなく「伸びない」に落ちる。</para></summary>
        public void HalveEnemyDots()
        {
            int bleed = enemyBleedStacks;
            if (bleed > 0) enemyBleedStacks = bleed / 2;
            int poison = GetStatus(StatusTarget.Enemy, "poison");
            int poisonAfter = poison * RecurrentTruthPoisonKeepPct / 100;
            if (poison > 0) enemyStatusStacks["poison"] = poisonAfter;
            if (bleed > 0 || poison > 0)
                if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log($"[回帰性真理] 出血を半減・毒を 3/4 (毒 {poison}→{poisonAfter} / 出血 {bleed}→{bleed / 2})");
        }

        /// <summary>現在スタック数（未付与は0）。</summary>
        public int GetStatus(StatusTarget target, string id)
        {
            var dict = target == StatusTarget.Enemy ? enemyStatusStacks : playerStatusStacks;
            return dict.TryGetValue(id, out var v) ? v : 0;
        }

        /// <summary>DoT 1 回分のダメージ。 最大 HP の割合で効く定義 (<see cref="StatusDef.dotMaxHpPer10kPerStack"/>) は
        /// 対象 (絶対視点) の最大 HP から整数で数える。 ターン開始の分と〈廃庭の霧吹き〉の分はここを共有する。</summary>
        private int DotDamage(StatusDef def, int stacks)
        {
            if (def.dotMaxHpPer10kPerStack > 0)
            {
                int maxHp = def.dotUsesCurrentHp
                    ? (def.target == StatusTarget.Enemy ? enemyCurrentHP : playerCurrentHP)
                    : (def.target == StatusTarget.Enemy ? enemyMaxHP : playerMaxHP);
                int n = def.dotScalesWithStacks ? stacks : 1;
                return System.Math.Max(1, (int)((long)n * maxHp * def.dotMaxHpPer10kPerStack / 10000));
            }
            return def.dotScalesWithStacks ? stacks * def.dotPerStack : def.dotPerStack;
        }

        /// <summary>指定タイミングの全ステータスを一括処理：DOTを fixedDamage へ加算 → decay。
        /// ターン開始時に1回呼ぶ（PassiveSkillManager.BeginTurn）。fixedDamage は BeginNewTurn で0化済み前提。</summary>
        public void TickStatuses(StatusTick timing)
        {
            foreach (var def in StatusRegistry.Defs.Values)
            {
                if (def.tickTiming != timing) continue;
                var dict = def.target == StatusTarget.Enemy ? enemyStatusStacks : playerStatusStacks;
                if (!dict.TryGetValue(def.id, out int stacks) || stacks <= 0) continue;
                int dmg = DotDamage(def, stacks);
                bool isPoison = def.target == StatusTarget.Enemy && def.id == "poison";
                if (isPoison)     // [計装 2026-11-05] ターン開始時の毒スタック
                {
                    CombatSystem.HpLossDiag.Add("毒段:T" + System.Math.Min(currentTurn, 8), stacks);
                    CombatSystem.HpLossDiag.Add("毒段n:T" + System.Math.Min(currentTurn, 8), 1);
                    // 〈骨溶かしの黒油〉: このターンがダウン (待っているダウンがある = ターンの頭でダウンになる) なら ×2
                    if (poisonDownDouble && (CombatSystem.CombatManager.Instance?.PendingDownLandsAt(currentTurn) ?? false)) dmg *= CombatSystem.PoisonCore.OilDownMul;   // 溜めで無効になるダウンは数えない (2026-11-26)
                }
                //   〈毒溜まりの主〉: 毒 20 以上なら 2 回 (2 回目の後には減らない ── 減るのは下の 1 回だけ)。 出来事は 1 回目だけ「減る前の毒のダメージ」(〈瘴気溜まりの壺〉)
                int ticks = isPoison && poisonDoubleTick && stacks >= CombatSystem.PoisonCore.PoolNeed ? 2 : 1;
                if (dmg > 0)
                for (int tk = 0; tk < ticks; tk++)
                {
                    if (def.target == StatusTarget.Enemy)
                    {
                        // [測定用の物差し 2026-11-10] 毒の tick が敵の HP に入る量だけ (出来事 OnPoisonTick へ渡す量・半減は素の量のまま)
                        int landed = def.id == "poison" ? CombatSystem.EnemyHpLedger.ProbePassive(dmg) : dmg;
                        fixedDamageToEnemy += landed; CombatSystem.HpLossDiag.NoteSource("状態:" + def.id, landed, 0);
                        if (def.id == "poison") CombatSystem.EnemyHpLedger.NotePoisonIntoFixed(landed);   // [計装 2026-11-10] 固定ダメ枠の毒
                    }
                    else fixedDamageToPlayer += dmg;
                    if (isPoison)
                    {
                        CombatSystem.CombatStats.DamageBreakdown[20] += dmg;   // [計装] 毒のダメージ
                        accumulatedValues[CombatSystem.PoisonCore.DecayTickKey] = tk == 0 ? 1f : 0f;
                        Keywords.Kw.Raise(this, PassiveSkillTrigger.OnPoisonTick, dmg);
                        accumulatedValues[CombatSystem.PoisonCore.DecayTickKey] = 0f;
                        CombatSystem.CombatManager.RaiseDotDamage(CombatSystem.CombatManager.DotKind.Poison, dmg);
                    }
                }
                int decay = def.decayPerTurn;
                // 半減型 (毒): ダメージを出した後に、 いま残っているスタックを半分にする (切り捨て)。 tick 中に増えた分も半分になる
                if (def.halveAfterTick)
                {
                    int now = dict.TryGetValue(def.id, out int nowStacks) ? nowStacks : stacks;
                    int keep = def.target == StatusTarget.Enemy ? EffectivePoisonKeepPct : 50;   // 〈瘴気〉の②は敵の毒だけ
                    dict[def.id] = now * keep / 100;
                }
                else dict[def.id] = System.Math.Max(0, stacks - decay);
            }
        }

        // ============================================================
        //  フィールドリセット規約 (2026-06-28 整理)
        //
        //  新フィールドを追加するときは、以下 3 区分のどれかを選び、該当する場所に書く:
        //    [perTurn]    毎ターン初期値に戻す      → BeginNewTurn の Phase B に追加
        //    [decay]      毎ターン減衰/遷移する      → BeginNewTurn の Phase C に追加
        //    [persistent] 戦闘を通して保持 (EndCombat で ctx ごと破棄)
        //                                            → BeginNewTurn には書かない
        //  区分をフィールドの XML コメントに明記すること。リセット漏れ = 戦闘跨ぎ状態リークの
        //  最頻バグ類型なので、区分不明のフィールドを増やさない。
        //  1〜2 箇所からしか触らない一時値は、フィールド追加より accumulatedValues /
        //  nextTurnBuffs 辞書に載せることを優先する。
        // ============================================================

        /// <summary>
        /// ターン開始時の状態リセット。 Phase A(特殊遷移) → B(perTurn 一括リセット) → C(減衰) の順。
        /// </summary>
        public void BeginNewTurn()
        {
            CombatSystem.DmgSourceDiag.BeginTurn();   // [計装] 与ダメ出どころ別のターン差分を捨てる
            // ---------- Phase A: ターン跨ぎの特殊遷移 (currentTurn++ より前に判定するもの) ----------

            // 痛覚遮断剤 (狂戦士スターター): 前ターン使用 + HP=1 維持なら強化バフ発動、攻撃後死亡
            if (painkillerArmedThisTurn)
            {
                painkillerArmedThisTurn = false; // 単一ターン only
                if (playerCurrentHP == 1)
                {
                    painkillerProcced = true;
                    // 攻撃+10 (consDiceRoll: 攻撃値へ加算)。 最終与ダメージ +50% は painkillerProcced の間 CombatManager.Damage が撃ごとに hitFinalDamagePct へ足す (2026-11-25・旧 +10 固定)
                    consDiceRoll += 10;
                    if (UnityEngine.Debug.unityLogger.logEnabled) UnityEngine.Debug.Log("[痛覚遮断剤] HP=1 維持確認 → 攻撃+10/最終与ダメージ+50%。 攻撃終了時に死亡。");
                }
            }

            currentTurn++;
            isFirstRoll = (currentTurn == 1);

            // 次ターンバフを現在バフへ移行
            currentBuffs.Clear();
            foreach (var kvp in nextTurnBuffs)
            {
                currentBuffs[kvp.Key] = kvp.Value;
            }
            nextTurnBuffs.Clear();

            // ---------- Phase B: perTurn フィールドの一括リセット (単純に初期値へ戻す) ----------


            // ダメージ処理の単ターンフラグ
            shieldAbsorbedAttackThisTurn = 0;   // 〈烈炎〉が読む: このターンに主攻撃をシールドが肩代わりした量 (2026-11-14)
            nullifyPursuitDamage = false;
            fixedDamageToEnemy = 0;
            CombatSystem.EnemyHpLedger.BeginTurn();   // [計装 2026-11-10] 固定ダメ枠と一緒に予約を捨てる
            fixedDamageToPlayer = 0;
            scratchDamage = 0;
            pursuitDamage = 0;
            playerDamageThisTurn = 0;   // 焦土用の被ダメ計測
            rerollsThisTurn = 0;        // リロール: このターンの回数 (戦闘の回数 rerollsThisCombat は戻さない)
            lastDamageCause = DeathCause.Normal; // 断罪等の増幅タグが翌ターンへ残留しないように

            // 7層ヴェスカ (§13-4) の perTurn 分。 persistent 側 (vescaShield / massiveBleedStacks /
            // enemyDodgeChance) は 4 連戦を通じて持続するのでここでは触らない。
            playerHighDiceCrushed = false;
            vescaHalvesDamageThisTurn = false;
            playerAttackPowerPenalty = 0;
            playerBlockIgnored = false;
            lightningRodArmed = false;
            vescaBleedOnHit = 0;
            vescaMaxHpBiteOnHit = 0;
            vescaExecuteArmed = false;

            // 会心系
            isCritical = false;
            forceCritical = false;
            forceCritThisHit = false;
            hitIndex = 0; hitCount = 0; hitFinalDamagePct = 0; hitOutgoingPct = 0; hitIgnoreDefense = false; hitArmorPen = 0; hitFaceMax = 0; critSkipRoll = false; hitCritRateMulAdd = 0f; hitIsFocused = false; hitFocusSpentRenki = 0; isRefireHit = false;
            critSuppressed = false;
            lastCritAddPct = 0f;
            critRatePctAdd = 0f;
            // 会心倍率: 基礎 3.0 (2026-07-26 リバランス) + メタバフ。 他バフ (ProvisionSystem苦悩・パッシブ) と加算合成。
            criticalMultiplier = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier()
                                 + MetaProgression.MetaBuffApplicator.GetCritDamageBonus();

            // パッシブが毎ターン再 set する値 (OnTurnStart / OnPostRoll で再評価される前提)
            outgoingDamageMultiplier = 1f;
            lifestealPct = 0f;
            shieldOnWinPct = 0f;
            // 防御値と貫通 (ADR-0013): 土台へ戻す。 敵側 (enemyDefense / enemyArmorPen) は戦闘通して保持＝ここでは触らない
            playerDefense = playerDefenseBase;
            // (旧 整備パネル〈装甲〉r10〈不動〉の「失った HP 10% ごとに防御 +1」は 2026-11-10 に一撃の上限へ差し替え済み。 常に 0 だった加算を 2026-11-12 に外した)
            playerArmorPen = playerArmorPenBase;
            enemyArmorPenNegate = 0;

            winMinDamage = 1;
            healBlocked = false;            // 狂暴化系
            enemyDamageTakenMultiplier = 1f;
            enemyAttackIgnoresGuard = false;
            playerFlatDamageReduction = 0;  // 被ダメ固定減算
            enemyAtkCutPct = 0f;            // 敵の攻撃の割合減 (軸の品・2026-11-07)

            // ダイス制約・補正
            enemyDiceOverrides.Clear();
            enemyDiceTotalPenalty = 0;      // 沈黙の剣帯の1T目-99 等

            // 配線は毎ターン確定するので perTurn。
            blockSumThisTurn = 0;
            accumulatedValues[ChargeSpentThisTurnKey] = 0f;   // 充電の品が読む [perTurn] 値
            accumulatedValues[ChargeSpendsThisTurnKey] = 0f;
            // 前のターンに役の軸の品が積んだ「次のターンのブロック +N」(Phase A で currentBuffs へ移った分)
            itemBlockBonus = currentBuffs.TryGetValue(CombatSystem.YachtRoleEffects.BlockNextKey, out float roleBlockNext) ? (int)roleBlockNext : 0;
            firstHitAttackBonus = 0;

            // ADR-0010: このターン切った役の記録。 usedRoles は [persistent] なのでここでは触らない。
            firedRolesThisTurn.Clear();

            // ADR-0009 相互攻撃モデル用の一時ボーナス
            mutualAttackBonus = 0;
            attackDicePct = 0f;
            diceDerivedAttack = 0;
            mutualEnemyAttackReduction = 0;
            nonCritOutgoingMultiplier = 0f;

            // ---------- Phase C: 減衰・カウントダウン (0 に向かって進む値) ----------

            if (enemyBleedStacks > 0 && !bleedDecayDisabled) // 出血スタック (止血阻害で減衰無効化)
            {
                // 比例減衰 (2026-11-07): 1 + 出血 / (10 + 〈血晶〉の②)。 上限ではなく、 付与に応じた高さで落ち着く
                int bleedDec = AxisRules.BleedDecay(enemyBleedStacks, bleedDecayDivisorBonus);
                enemyBleedStacks -= bleedDec;
                Keywords.Kw.Raise(this, PassiveSkillTrigger.OnBleedDecay, bleedDec);
                if (enemyBleedStacks == 0) Keywords.Kw.Raise(this, PassiveSkillTrigger.OnBleedZero, 1);
            }
            if (enemyPassivesDisabledTurns > 0) enemyPassivesDisabledTurns--; // 敵パッシブ無効化 残ターン
            // プレイヤーのシールドは毎ターン半減 (切り捨て。 1 → 0 で確実に消える)。 ヴェスカのシールド規約と同じ (GAME.md §13-4)。
            // T1 は戦闘開始時の付与 (開幕シールド等) をそのまま使うので半減しない。 そのターンの付与は OnTurnStart 以降なので「半減 → 付与」の順になる。
            // 失った分を〈半減で失った〉出来事として配る (2026-10-31)。
            // 〈重装兵の隠し刃〉(2026-11-20): このターンがボスのダウンなら半減しない (ダウンはこの後 CombatManager.BeginBossDownTurn が待ちから 1 つ使って立てる)
            bool keepForDown = shieldKeepOnBossDown && (CombatSystem.CombatManager.Instance?.Down?.pending ?? 0) > 0;
            bool keepForHeart = shieldNoDecayAt > 0 && _consShield >= shieldNoDecayAt;   // 〈脈打たぬ鋼の心臓〉(2026-11-21): シールド 25 以上なら減衰しない
            if (currentTurn > 1 && _consShield > 0 && !keepForDown && !keepForHeart)
            {
                // 残る割合 50% + 上乗せ (〈油染みの盾革〉+20・最大 80%)
                int keepPct = System.Math.Min(80, 50 + shieldKeepBonusPct + MetaProgression.MetaBuffApplicator.GetShieldKeepBonusPct());
                int lost = _consShield - _consShield * keepPct / 100;
                _consShield -= lost;
                Keywords.Kw.Raise(this, PassiveSkillTrigger.OnShieldDecay, lost);
            }
        }

        // ============================================================
        //  ADR-0009 相互攻撃モデル (perTurn)
        //  ダイス合計はロール比較で使われない (収支トリガーは解決後の与被ダメで判定)。
        //  「攻撃+N」は配線後の自攻撃値へ加算 = mutualAttackBonus (AddPlayerAttackOrDiceBonus 経由)。
        // ============================================================

        /// <summary>ADR-0009 [perTurn]: 配線後の自攻撃値に加算されるボーナス。
        /// ExecuteTurnMutual が atkBase に加える。</summary>
        public int mutualAttackBonus;

        /// <summary>[perTurn] <b>攻撃に挿した出目への上乗せ率</b> (0.5 = +50%・2026-11-06)。 BeginNewTurn の Phase B で 0。
        /// 効果は配線より前 (OnPostRoll まで) に <see cref="AddAttackDicePct"/> で積み、 CombatManager が配線の確定後に
        /// 「攻撃に挿した出目の合計 × この率」(切り上げ) を攻撃に 1 回だけ足す。 BOT の配線評価もこの値を読む。
        /// <para>攻撃+N の上乗せを出目に掛ける形へ置き換えるための口。 上乗せは出目の比重を薄めるが、 これは出目に比例するので
        /// 何を積んでも攻撃の主成分が出目のまま残り、 「1 個を攻撃へ回すか守りへ回すか」の重みが保たれる。</para></summary>
        public float attackDicePct;

        /// <summary>[perTurn] [計装] このターンの攻撃+N のうち、 出目から来た分 (上乗せ率の分・剣〈切り返し〉)。 BeginNewTurn の Phase B で 0。</summary>
        public int diceDerivedAttack;

        /// <summary>攻撃に挿した出目への上乗せ率を積む (<see cref="attackDicePct"/>)。 配線より前 (OnPostRoll まで) に呼ぶ。</summary>
        public void AddAttackDicePct(float pct)
        {
            if (pct > 0f) attackDicePct += pct;
        }

        /// <summary>与ダメージ +pct (<see cref="outgoingDamageMultiplier"/> の加算プール・未初期化の 0 は 1.0 に直す)。
        /// 2026-11-10: 出目 +% を配っていた主攻撃の品 13 品をこちらへ移した ── 出目 +% は掛け先 (攻撃に挿した出目・
        /// 5 層ボス戦で 1 ターン約 3) が小さく、 ITT で軒並み下位だった。 固定ダメ枠 (毒・爆発) には掛からない。</summary>
        public void AddOutgoingPct(float pct)
        {
            if (pct <= 0f) return;
            if (outgoingDamageMultiplier <= 0f) outgoingDamageMultiplier = 1f;
            outgoingDamageMultiplier += pct;
        }

        /// <summary>ADR-0009 [perTurn]: 敵攻撃値からの追加減算 (呪縛 等の敵ダイス弱化を新モデルで実装)。
        /// ExecuteTurnMutual が Escalation 計算後の enemyAtkValue から差し引く (下限0)。</summary>
        public int mutualEnemyAttackReduction;

        /// <summary>[perTurn] 非会心攻撃時の追加倍率 (0.20 = +20%)。isCritical=false 時のみ ProcessDamage で適用。
        /// 鈍器 (Bludgeon) キーワードの主フィールド。会心を犠牲に非会心火力を高めるビルドの経路。</summary>
        public float nonCritOutgoingMultiplier;

        /// <summary>[計装 2026-09-14] atkBase の <c>パッシブ加算</c> を<b>誰が積んだか</b>で分解する。
        /// 実測で atkBase 61 のうち 36.9 がここ由来 (攻撃端子出目は 19.4) で、
        /// <b>配線より 2 倍大きい</b>。 内訳を逆算で当てにいって 3 回外したので直接記録する
        /// ── 武器家系のラダーは置換式 (WeaponProgression.Compute)、 items.json に
        /// 筋力は I しか無い、 上位ビルドは盾系で攻撃ラダーが薄い ── どれも合わなかった。</summary>
        public static readonly System.Collections.Generic.Dictionary<string, long> AttackBonusBySkill
            = new System.Collections.Generic.Dictionary<string, long>();
        public static readonly System.Collections.Generic.Dictionary<string, long> AttackBonusCalls
            = new System.Collections.Generic.Dictionary<string, long>();

        /// <summary>[計装] <b>最終戦 (8 層ヴェスカ) だけ</b>の内訳。
        ///
        /// <para><b>全戦闘の平均と混ぜてはいけない。</b> 実測で全戦闘の平均は 8.05/攻撃 なのに
        /// 最終戦だけ 36.88 と 4.6 倍あり、 この 2 つを取り違えて
        /// 「フラットが atkBase の 6 割」と誤読した (実際は全体で 26%)。
        /// 道中は武器が T2/T3 で薄く、 終盤に T4+ のラダーとアイテムが積み上がる。</para></summary>
        public static readonly System.Collections.Generic.Dictionary<string, long> AttackBonusBySkill7F
            = new System.Collections.Generic.Dictionary<string, long>();
        public static readonly System.Collections.Generic.Dictionary<string, long> AttackBonusCalls7F
            = new System.Collections.Generic.Dictionary<string, long>();
        /// <summary>いま Execute 中のスキル。 PassiveSkillManager が出入りで設定する。</summary>
        public static string CurrentSkillId = "";
        public static void ResetAttackBonusStats()
        {
            AttackBonusBySkill.Clear(); AttackBonusCalls.Clear();
            AttackBonusBySkill7F.Clear(); AttackBonusCalls7F.Clear();
        }

        /// <summary>[較正専用] フラット攻撃加算に掛ける倍率。 1.0 = 素のまま。
        ///
        /// <para><b>ADR-0009 は配線を判断の主役に置いている</b>のに、 実測で atkBase 61 のうち
        /// 攻撃端子出目は 19.4、 フラット加算が 36.9 ── <b>素火力を除いた比が 34 : 66</b> で、
        /// 配線が脇役になっていた。 目標は <b>配線 : フラット = 5:5 〜 6:4</b>。</para>
        ///
        /// <para>個別のラダー値を決め打ちすると着地点が読めない (フラットを削ると BOT の配線が
        /// 変わりうるので、 配線側が 19.4 のままとは限らない)。 <b>一括倍率で掃いてから
        /// 整数のラダー値へ畳み込む</b> ── 倍率は製品に残さない (§13-3 の隠し倍率禁止と同じ扱い)。</para>
        ///
        /// <para>内訳 (実測・6,000 ラン): 剣の間合 35.8% / 剣の一対一 19.2% / 短剣の疾手 13.8% /
        /// 斧の猛り 12.8% / Riposte 6.0% / 筋力 5.3% / Frenzy 5.2%。
        /// <b>92.8% が武器家系ラダー由来</b>で、 アイテム由来は 7.2% しかない。</para></summary>
        public static float FlatAttackMul = 1f;

        public void AddPlayerAttackOrDiceBonus(int n)
        {
            if (FlatAttackMul < 0.999f && n > 0)
                n = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(n * FlatAttackMul));
            if (n != 0)
            {
                string key = string.IsNullOrEmpty(CurrentSkillId) ? "(不明)" : CurrentSkillId;
                AttackBonusBySkill.TryGetValue(key, out long acc);
                AttackBonusBySkill[key] = acc + n;
                AttackBonusCalls.TryGetValue(key, out long c);
                AttackBonusCalls[key] = c + 1;
                if (!string.IsNullOrEmpty(bossId)
                    && bossId.StartsWith(GameLoop.BossIds.Layer7Prefix, System.StringComparison.Ordinal))
                {
                    AttackBonusBySkill7F.TryGetValue(key, out long a7);
                    AttackBonusBySkill7F[key] = a7 + n;
                    AttackBonusCalls7F.TryGetValue(key, out long c7);
                    AttackBonusCalls7F[key] = c7 + 1;
                }
            }
            mutualAttackBonus += n;
        }

        // ============================================================
        //  ADR-0009 柱5: 充電経済のパッシブ helper
        //  内部キー "mutualCharge" (float だが整数運用)。ExecuteTurnMutual が端子経由で加算。
        //  パッシブは以下のヘルパー経由で読み書きすること (キー直参照は避ける)。
        // ============================================================

        /// <summary>現在の充電量 (0〜ChargeMax)。</summary>
        public int GetCharge() => (int)GetAccumulated("mutualCharge");

        /// <summary>充電を n 加算 (ChargeMax + メタ拡張バッテリー加算 で上限クランプ)。
        /// 2026-07-25 v6: 種火・充電 r2 で ChargeMax +5 (10→15)。</summary>
        /// <para><paramref name="raiseOverflow"/> = false なら上限で溢れた分は捨てるだけで〈過充電〉の出来事を配らない
        /// (〈電流〉の① の毎ターンの分。 配ると〈十五年目の計測器〉が溢れを攻撃に換え、 余った資源で強さを買える穴になる)。</para>
        public void AddCharge(int n, bool raiseOverflow = true)
        {
            if (n <= 0) return;
            int cur = GetCharge();
            // 〈雷壷〉は上限を外す (超えた分の目減りは品のクラスがターン終了時に)。
            int cap = chargeCapRemoved ? int.MaxValue : ChargeCap;
            int next = (int)System.Math.Min((long)cap, (long)cur + n);
            // [計装] 充電経済。 **上限で捨てられた分**を分けて数える ── 収入が支出を上回って
            //   常に満タンなら、 リロールが「安い」のではなく**財布が溢れている**ことになる。
            ChargeGained += next - cur;
            ChargeWasted += n - (next - cur);
            accumulatedValues["mutualCharge"] = next;
            // 増えた分と、 上限で捨てた分 (過充電) を配る (2026-10-31 アイテム再編)
            Keywords.Kw.Raise(this, PassiveSkillTrigger.OnChargeGain, next - cur);
            if (raiseOverflow) Keywords.Kw.Raise(this, PassiveSkillTrigger.OnOvercharge, n - (next - cur));
        }

        /// <summary>充電の上限 (ChargeMax + メタの拡張)。 〈雷壷〉でも「超えた分」の基準はこれ。</summary>
        public int ChargeCap => CombatSystem.CombatManager.ChargeMax + MetaProgression.MetaBuffApplicator.GetChargeCapBonus() + chargeCapBonus;

        /// <summary>[計装] 充電の収支。 Gained=実際に入った / Wasted=上限で捨てた /
        /// SpentTotal=ConsumeCharge 経由の消費 (リロール分は CombatStats.RerollStats[2] と重複)。</summary>
        public static long ChargeGained, ChargeWasted, ChargeSpentTotal, ChargeSpendCalls;
        public static void ResetChargeStats()
            => ChargeGained = ChargeWasted = ChargeSpentTotal = ChargeSpendCalls = 0;

        /// <summary>直近の <see cref="ConsumeCharge"/> 支払い前の充電 (〈過負荷チューナー〉が過充電中だったかを判定する)。</summary>
        public int LastChargeBeforeSpend { get; private set; }

        /// <summary>充電を n 消費 (足りなければ false)。</summary>
        public bool ConsumeCharge(int n)
        {
            if (n <= 0) return true;
            int cur = GetCharge();
            if (cur < n) return false;
            LastChargeBeforeSpend = cur;
            accumulatedValues["mutualCharge"] = cur - n;
            ChargeSpentTotal += n;
            ChargeSpendCalls++;
            NoteChargeSpent(n);
            Keywords.Kw.Raise(this, PassiveSkillTrigger.OnChargeSpend, n);   // リロールもここを通る
            return true;
        }

        /// <summary>[perTurn] このターンに消費した充電の合計を積む。 〈短絡〉が読む。
        ///
        /// <para>充電の消費 (リロール = ADR-0010) は<b>配線より前</b>のフェーズで起きるので、
        /// OnPostRoll の時点で「今ターン何を払ったか」が確定している ──
        /// だから攻撃値への還元 (AddPlayerAttackOrDiceBonus) が間に合う。</para>
        ///
        /// <para>リセットは BeginNewTurn の Phase B。 <c>accumulatedValues</c> は
        /// 毎ターン自動でクリアされない (frenzyDiceBonus のような戦闘持続値が同居する) ので、
        /// perTurn 扱いのキーは明示的に 0 へ戻すこと。</para></summary>
        public const string ChargeSpentThisTurnKey = "chargeSpentThisTurn";
        /// <summary>[perTurn] このターンに充電を使った回数 (支払い 1 回 = 1・振り直しも)。 〈灼線鞭〉の「2 回以上」が読む (TerminalEnv.chargeSpendsTurn)。 BeginNewTurn の Phase B で 0。</summary>
        public const string ChargeSpendsThisTurnKey = "chargeSpendsThisTurn";
        private void NoteChargeSpent(int n)
        {
            if (n <= 0) return;
            accumulatedValues[ChargeSpentThisTurnKey] = GetAccumulated(ChargeSpentThisTurnKey) + n;
            accumulatedValues[ChargeSpendsThisTurnKey] = GetAccumulated(ChargeSpendsThisTurnKey) + 1;
        }

        /// <summary>充電を n 失う (使ったことにならない ── 「充電を使うたび」の品は鳴らない・〈雷壷〉の目減り)。 戻り値 = 失った量。</summary>
        public int LoseCharge(int n)
        {
            int cur = GetCharge(), take = System.Math.Min(System.Math.Max(0, n), cur);
            if (take <= 0) return 0;
            accumulatedValues["mutualCharge"] = cur - take;
            ChargeWasted += take;
            return take;
        }

        /// <summary>充電の**半分**（切り捨て）を没収し、没収した量を返す。
        /// 2026-08-16 追加: ヴェスカの遺物〈刻〉用。 全没収だと「溜めた瞬間に全部消える」ので
        /// 充電ビルドが 1 枚の抽選で成立しなくなる。 半分なら残りで動けるし、
        /// 溜めるほど奪われる量も増えるという駆け引きが残る。</summary>
        public int ConsumeHalfCharge()
        {
            int cur = GetCharge();
            int take = cur / 2;
            if (take > 0) accumulatedValues["mutualCharge"] = cur - take;
            return take;
        }

        /// <summary>過充電 = 充電 10 以上 (2026-11-21・full-item-rework-ledger §①・<see cref="CombatSystem.ChargeCore.OverchargeAt"/>)。</summary>
        public const int OverchargeThreshold = CombatSystem.ChargeCore.OverchargeAt;
        public bool IsOvercharged() => GetCharge() >= OverchargeThreshold;

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

}
