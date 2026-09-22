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
        SuddenDeath,  // サドンデス (灰燼の烙印)
        Other,
        // **末尾に足すこと。** runs.jsonl へ int で載るので、 既存の並びを動かすと
        //   過去バッチと突き合わせたときに黙って別の死因へすり替わる。
        MassiveBleed, // 覚者・天与: 大出血 (スタック累積・解除不可・4連戦を跨ぐ)
        Rift,         // 覚者: 裂け目 (双方の最大HP 2%/T)
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

        /// <summary>[persistent] <see cref="equippedDiceFaces"/> と**添字が 1 対 1 で対応する** Tier 配列。
        /// 素の面は <c>Tier.None</c>、 出目パーツで増えた面はその Tier。
        /// <see cref="GameLoop.DiceFaceParts.Build"/> が faces と対で組む ── 別々に作らないこと。
        /// null なら「パーツ無し」＝ 全面が素。</summary>
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

        /// <summary>[persistent] ADR-0010: この戦闘で**既に切った役**。 1 役 1 回まで。
        ///
        /// ヨットのスコアカードに相当する。 戦闘単位でリセットする ──
        /// **4 連戦 (7層ヴェスカ) は跨がない**。 跨ぐと後半の形態で手札が空になり、
        /// エスカレーションとの二重の締め付けになるため。</summary>
        public readonly HashSet<CombatSystem.RoleKind> usedRoles = new HashSet<CombatSystem.RoleKind>();

        /// <summary>[perTurn] ADR-0010: このターン切った役 (ログ・計装用)。
        /// BeginNewTurn Phase B でクリアする。</summary>
        public readonly List<CombatSystem.RoleKind> firedRolesThisTurn = new List<CombatSystem.RoleKind>();

        // ===== ダメージ計算 =====
        public int baseDamage;          // 基本ダメージ
        public int finalDamage;         // 最終ダメージ（スキル補正後）
        public int pursuitDamage;       // 追撃ダメージ（パッシブ由来の固定追撃）
        /// <summary>[perTurn] このターン、 ブロック端子に 1 本以上配線したか。
        /// 遺物の刻印〈そのターン、ブロック端子に 1 本も配線していない〉の判定に使う (§15-5)。
        /// CombatManager の配線集計が毎ターン set し、 BeginNewTurn Phase B でリセットする。</summary>
        public bool blockWiredThisTurn;

        /// <summary>[perTurn] このターン、 ブロック端子へ配線した**出目の合計**。 本数ではない。
        /// 盾家系〈反攻〉が「守った量がそのまま反撃になる」を出すために読む。
        /// CombatManager の配線集計が毎ターン set し、 BeginNewTurn Phase B で 0 に戻す。
        /// 配線 (§9.2 step5) は解決 (step6) より前なので、 OnPostRoll / OnPreDealDamage から読める。</summary>
        public int blockSumThisTurn;

        /// <summary>[persistent] この戦闘が 2 体戦か (EncounterPresets のペア)。
        /// 剣家系〈一対一〉〈果たし合い〉が「単体戦でしか効かない」を判定するために読む。
        /// CombatManager が戦闘開始時に 1 度だけ set する ── 戦闘中は変わらない。</summary>
        public bool isPairEncounter;

        /// <summary>[perTurn] 会心率への加算 (小数・0.05 = +5%)。 パッシブ・消耗品・戦闘中バフの
        /// <b>会心率はすべてここへ足す</b> (2026-09-19 に旧「分子」を撤去して % へ統一)。
        /// 武器 + 層補正の素の会心率は ProcessDamage の引数 critBaseRate で別に渡る。 BeginNewTurn で 0 リセット。</summary>
        public float critRatePctAdd;
        public bool isCritical;         // 会心判定結果
        /// <summary>会心強制フラグ。true なら乱数判定を無視して会心確定（末那識など）。
        /// 会心率cap(注意散漫)下でも真に確定する。BeginNewTurn でリセット、毎ターン効果側が再set。</summary>
        public bool forceCritical;
        /// <summary>[perTurn] 会心封印フラグ。true なら会心率を 0 にする（無心の刃など）。
        /// forceCritical より優先度が低い（強制会心が立っていれば会心する）。BeginNewTurn でリセット。</summary>
        public bool critSuppressed;
        public float criticalMultiplier; // 会心倍率（既定 2.0。2026-07-26 に 3.0 へ上げ、2026-08-03 に 2.0 へ差し戻し）
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

        // ===== 戦闘相手の種別（暗殺教団契約等で参照） =====
        /// <summary>戦闘相手の種別 (Normal/Elite/Boss)。 マップタイル種別から StartCombat 時に設定。
        /// 暗殺教団 (通常戦闘マスのみ発動) / 暗殺教団+戦術家協力 (エリートに拡張) で参照する。</summary>
        public CombatSystem.EnemyKind currentEnemyKind = CombatSystem.EnemyKind.Normal;

        // ===== 脆弱状態異常（狩猟旅団契約で敵に付与）=====
        /// <summary>脆弱の倍率 (狩猟旅団 L1=0.15 / L2=0.30 / L3=0.45)。 0 なら契約なし。</summary>
        public float enemyVulnerabilityMultiplier = 0f;
        /// <summary>脆弱が armed (発動可能) 状態か。 会心ダメで消費、 ロール勝利時の非会心ダメで再点火。</summary>
        public bool enemyVulnerabilityArmed = false;

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
        public int enemyBleedStacks;    // 敵の出血スタック数（※統一フレーム未移行・現状フィールドのまま）
        /// <summary>[persistent] 止血阻害 (AntiClotting) が立てるフラグ。BeginNewTurn の出血自然減衰を無効化。</summary>
        public bool bleedDecayDisabled;
        // burnDecayHalved は 2026-07-18 削除 (Burn 全廃・EverBurning deregister に伴う死コード掃除)

        /// <summary>汎用ステータス・スタック（#3 統一フレーム。id→stacks）。Player/Enemy は絶対視点（スワップしない）。
        /// 炎上(burn)はここへ移行済み。DOT/減衰は <see cref="TickStatuses"/> が一括処理する。</summary>
        public Dictionary<string, int> playerStatusStacks = new Dictionary<string, int>();
        public Dictionary<string, int> enemyStatusStacks = new Dictionary<string, int>();

        // ===== 敵スタンス（毎ターン抽選・ロール前テレグラフ。ADR-0005） =====
        /// <summary>敵→プレイヤー被ダメ倍率（高火力>1/低火力<1）。ApplyLossDamageModifiers で乗算。毎ターン1.0リセット。
        /// ロール力は kind を見て CombatManager がロール時に面を縮める（弱ロール=期待値約0.65倍）。</summary>
        public float enemyStanceDamageMult = 1f;
        /// <summary>テレグラフ表示用：0=なし / 1=高ロール・低ダメ / 2=低ロール・高ダメ。毎ターン0リセット。</summary>
        public int enemyStanceKind;
        /// <summary>弱ロールスタンスの面縮小比（ボス別にチューナーが調整・基準0.65）。EnemyStance.Apply が設定、
        /// CombatManager が弱ロール時の WeakRollMax/WeakRollFaces に渡す。毎ターン0.65リセット。</summary>
        public float enemyStanceWeakRollRatio = 0.65f;

        /// <summary>プレイヤー防御スタンス（ADR-0006）。true=防御優先（与ダメ-90%・受け最終-50%）。毎ターンfalseリセット。</summary>
        public bool playerStanceDefense;

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

        // ===== 7層 ヴェスカ（遺物学者）。 正本: docs/GAME.md §13-4 =====
        // 4 段連戦を通じて CombatContext は使い回されるため、 [persistent] 指定のものは
        // 段が変わっても引き継がれる（healShieldReduction と同じ扱い）。

        /// <summary>[persistent] ヴェスカのシールド。 §6-3 段0 で **半減(切り捨て) → 抽選付与** の順に処理する。
        /// 定常上限は「毎ターン収入 × 2」で自動的に決まるため別途キャップ不要。</summary>
        public int vescaShield;
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

        /// <summary>[persistent] 停滞する時間のスタック。 プレイヤーが攻撃端子にダイスを 1 本も置かなかった
        /// ターンごとに +1（上限あり）。 次ターン以降の敵攻撃へ加算される。 L3 チューナーの第一レバー。</summary>
        public int stagnationStacks;

        /// <summary>[perTurn] 〈天与の指輪〉: このターン、 3 以上の出目をすべて 1〜2 へ再抽選する。
        /// 旧「全出目 1」は本数ぶんの手が完全に死に、 その前の「行動不可」と実質同じ重さだった。
        /// 再抽選なら低い目のまま **本数と幅は残る**ので、 配線の判断が成立する。</summary>
        public bool playerHighDiceCrushed;

        /// <summary>[perTurn] 〈天与の盾〉: このターン、 ヴェスカへの与ダメージが半分になる。
        /// 完全無効は「殴っても無駄」で手番が消えるのと変わらないため、 半減に留める。</summary>
        public bool vescaHalvesDamageThisTurn;

        /// <summary>[perTurn] 麻痺毒の小瓶: このターンのプレイヤー攻撃力減算。 実効攻撃力は 0 で床を引く。</summary>
        public int playerAttackPowerPenalty;
        /// <summary>[persistent] 〈虚空〉のスタック。 攻撃端子に 0 本置いたターンごとに +1（戦闘中は戻らない）。
        /// 積むほど被ダメ軽減が薄れ、 敵への刻みが増える ＝「殻が剥がれる」。
        /// 軽減 = max(0, 90 − 15n)% / 刻み = 現在 HP の (5 + 2n)%（上限 n=8）。</summary>
        public int voidStanceStacks;
        /// <summary>[perTurn] 〈虚空〉によるこのターンの被ダメ倍率（1=軽減なし / 0.10=90%軽減）。
        /// **1.0 未満でも 0 にはならない** ── 完全無敵を作らないための下限。 BeginNewTurn で 1 に戻す。</summary>
        public float voidStanceDamageMul = 1f;

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

        /// <summary>沈黙の剣帯 等: 敵ダイス合計への「床なし」減算ペナルティ。
        /// enemyDiceDebuff/consEnemyDiceDebuff は Math.Max(0) で0床処理されるが、こちらは床処理せず
        /// enemyDiceTotal を負値まで押し下げられる（=ロール大差勝ち＝基礎ダメ激増）。BeginNewTurn でリセット。</summary>
        public int enemyDiceTotalPenalty;

        /// <summary>ボス強者バフ: ボス(boss_layer*)のダイス合計への固定加算。フロアに応じて戦闘開始/形態swap時に設定。
        /// enemyDiceTotalBonus とは別枠（星火燎原等の上書きと競合させないため）。勝敗判定前に enemyDiceTotal へ加算。</summary>
        public int bossDiceBonus;
        /// <summary>[計装] 加算前の素の敵ダイス合計。 BossCombatTrace が内訳を出すため。
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
        /// 2026-07-29: HP 割合ダメージの免疫は撤去済み（〈虚空〉が現在 HP 基準になり近道にならないため）。
        /// 戦闘開始時にパッシブが立て、 戦闘終了まで持続する (p4 は最終段なので段 swap は起きない)。</summary>
        public bool enemyRecurrentTruth;

        /// <summary>[persistent・計装] いまの形態が始まったターン番号 (1 始まり)。
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
        /// その後に走る加算 (メタ精密 r10 の還元・希望系) に押し流されて効かない。
        /// 会心倍率が実際に乗る 1 箇所 (PassiveSkillManager の isCritical 分岐) で引く。</para></summary>
        public float enemyCritMultReduction = 0f;

        /// <summary>敵へ与える「最大 HP の割合」ダメージ量。
        /// **割合ダメージを直接計算せず必ずここを通すこと**（新規実装も同様）。
        /// 2026-07-29: 〈回帰性真理〉による 1 丸めは撤去した ── 割合ダメージは p4 にも通る。</summary>
        public int RatioDamageToEnemy(float pct)
            => System.Math.Max(1, UnityEngine.Mathf.CeilToInt(enemyMaxHP * pct));

        /// <summary>敵へ与える「**現在** HP の割合」ダメージ量。 〈虚空〉が使う。
        /// 現在 HP 基準なので指数的に減衰し、 **これ単独では敵を倒し切れない**（漸近するだけ）。
        /// 最後は必ず通常攻撃で仕留める必要がある、 という設計上の制約になる。</summary>
        public int CurrentHpRatioDamageToEnemy(float pct)
            => System.Math.Max(1, UnityEngine.Mathf.CeilToInt(enemyCurrentHP * pct));

        /// <summary>敵の処刑 (HP 閾値による即死) が通るか。 〈回帰性真理〉持ちには通らない。</summary>
        public bool CanExecuteEnemy()
        {
            if (!enemyRecurrentTruth) return true;
            UnityEngine.Debug.Log("[回帰性真理] 処刑を無効化");
            return false;
        }

        /// <summary>敵の最大 HP を変更する。 〈回帰性真理〉持ちは増減とも拒否する。</summary>
        public void SetEnemyMaxHP(int value)
        {
            if (enemyRecurrentTruth)
            {
                UnityEngine.Debug.Log($"[回帰性真理] 最大HP変更 ({enemyMaxHP}→{value}) を拒否");
                return;
            }
            enemyMaxHP = value;
        }

        /// <summary>現在の敵ボスid (boss_layer*)。 非ボス戦は空。 各ボススキルが BossTuning.Param(bossId, ...) を引くのに使う。
        /// 戦闘開始/形態swap時に CombatManager がセット。</summary>
        public string bossId = "";

        /// <summary>直近にプレイヤーへダメージを与えた致死メカニズムの分類。
        /// 各致死スキルが発動時にセット、 通常被ダメ経路は Normal。 プレイヤー死亡時の死因記録に使う。</summary>
        public DeathCause lastDamageCause = DeathCause.Normal;

        /// <summary>この戦闘でプレイヤーが受けた総被ダメの **ソース別内訳** (ボス難易度チューナー用)。
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
                int absorbed = System.Math.Min(consShield, dmg);
                consShield -= absorbed;
                dmg -= absorbed;
                dbgShieldToChip += absorbed;
                UnityEngine.Debug.Log($"[軽減無視] シールドが {absorbed} 肩代わり (残シールド {consShield})");
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

        // ===== Λ層（時間の狭間）由来の恒久デバフ（戦闘開始時に RunState から設定。戦闘スコープで保持） =====

        /// <summary>重い足取り: プレイヤーダイス合計デルタ(負値、lv1/2/3=-2/-4/-8)。0=無効。</summary>
        public int lambdaFirstTurnDiceDelta;
        /// <summary>重い足取りが効く最後のターン (lv1/2=1、lv3=2)。 0 なら効かない。</summary>
        public int lambdaHeavyStepsTurns;
        /// <summary>注意散漫 lv3: 会心の判定を持たない (非会心の枝は通る)。</summary>
        public bool lambdaNoCritBranch;

        /// <summary>苛立つ強敵: 敵ダイス+1 の発生間隔(5/4/2T)。0=無効。毎ロールで floor(turn/interval) を敵合計へ加算。</summary>
        public int lambdaIrritatingInterval;

        /// <summary>微妙な手応え: 勝利時の与ダメ倍率(lv1/2/3=0.95/0.90/0.75)。1.0=無効。</summary>
        public float lambdaDamageDealtMult;

        /// <summary>[persistent] 注意散漫: 実効会心率の上限(lv1/2/3 = 0.30/0.20/0.10)。1f=無効。
        /// ResolveCritRate の**後**に適用され、全ての会心率ソース(レガシー分子/critRatePctAdd/メタ精密)に効く。
        /// forceCritical はこの上限を無視して確定する。BeginNewTurn ではリセットしない。</summary>
        public float lambdaCritRateCap = 1f;

        /// <summary>慈悲の処刑: 被弾後 HP がこの割合(lv1/2/3=0.05/0.10/0.15)以下で即死。0=無効。</summary>
        public float lambdaMercifulExecThreshold;

        /// <summary>神経錯乱: このターン未満では消費アイテム使用不可(lv1/2/3=3/5/7)。0=制限なし。</summary>
        public int lambdaConsumableLockUntilTurn;

        /// <summary>シールドバッシュ: ロール勝利時、与ダメのこの割合をシールド化(5/10/15/20%)。
        /// パッシブが OnTurnStart で再set。BeginNewTurn で 0 リセット。</summary>
        public float shieldOnWinPct;

        // ==== 臨界メーター (Rinkai) 2026-07-16 追加 ====
        /// <summary>[persistent] 臨界メーター: 攻撃配線ダイス合計 (attackSum) を毎T末に加算。
        /// 閾値 (RinkaiThreshold 基準50、LowerThreshold で下がる) 到達で 次T の攻撃に爆発ボーナス +RinkaiBurstDamage を付与し、0リセット。
        /// パッシブ「連鎖爆発」で爆発T会心確定、「不朽の熱」で致命耐え、「余熱」で爆発後20残し、等の派生あり。</summary>
        public int rinkaiMeter;
        /// <summary>[persistent] 臨界爆発が予約されているか (前Tに meter 閾値到達で set)。
        /// ProcessDamage が finalDamage += RinkaiBurstDamage を適用してから false に戻す。
        /// 攻撃機会のない T (自撃破後等) を跨いでも予約は残る = 持続扱い。</summary>
        public bool rinkaiBurstActive;

        /// <summary>[persistent] 直近ターンにプレイヤーが与えた総ダメージ。
        /// オーバーロードの発動判断 (「倍にすれば倒し切れるか」) にだけ使う。
        /// ctx は戦闘ごとに作り直されるので戦闘跨ぎでは残らない。</summary>
        public int lastPlayerDamage;
        /// <summary>[perTurn] このターンの与ダメージを 2 倍にする (オーバーロード発動中)。
        /// BeginNewTurn Phase B でリセットする。</summary>
        // [撤去 2026-09-13] overloadThisTurn ── 出力 r10 の旧極点〈オーバーロード〉。
        //   〈戦意〉(累積成長) へ差し替え。 経緯は MetaPanel.BattleSpiritUnlocked。
        /// <summary>[persistent] 臨界の閾値。 基準 50。 パッシブ「降下閾値」で 35 に下がる (最小適用)。</summary>
        public int rinkaiThreshold;
        /// <summary>[persistent] 臨界爆発時の flat damage。 基準 50。 パッシブ「臨界圧」で 80 に上がる。</summary>
        public int rinkaiBurstDamage;
        /// <summary>[persistent] 爆発発動時に meter を 0 でなく残す量。 パッシブ「余熱」で 20 に。</summary>
        public int rinkaiAfterglow;
        /// <summary>[persistent] 爆発T の攻撃を会心確定にするか。 パッシブ「連鎖爆発」で true。</summary>
        public bool rinkaiCritOnBurst;
        /// <summary>[persistent] 攻撃配線した T の臨界追加加算 (輻射)。</summary>
        public int rinkaiRadiationBonus;
        /// <summary>[persistent] 被ダメを臨界メーターに追加加算するか (熱伝導)。</summary>
        public bool rinkaiConductionEnabled;
        /// <summary>[persistent] 「不朽の熱」の 1戦闘 1回消費フラグ。 true で発動済み。</summary>
        public bool rinkaiUnyieldingUsed;

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

        // === 2026-06-28: 職業スターター消耗品の状態フラグ ===
        /// <summary>[perTurn 相当・消費で armed] 瞬間研磨剤 (剣士): 次の 1 撃だけ 与ダメージ+150%。
        /// ApplyWinDamageModifiers で outgoingDamageMultiplier += 1.5 して disarm (2026-07-27 リワーク)。</summary>
        public bool polishArmed;
        /// <summary>不抜の聖紋 (騎士): 戦闘中 HP 80% 以上を保っている間 被ダメ -40%。
        /// 一度でも 80% を割ったら oathBroken=true で以後無効。 戦闘終了で破棄。</summary>
        public bool oathArmed;
        public bool oathBroken;
        /// <summary>痛覚遮断剤 (狂戦士): 使用ターン 致命ダメで HP 1 踏みとどまり、
        /// 次ターン開始時 HP=1 ならダイス合計+10/基礎攻撃+10、 攻撃終了時に死亡。
        /// armedThisTurn=使用ターン中、 procced=次ターンに踏みとどまり判定済+強化バフ発動中。</summary>
        public bool painkillerArmedThisTurn;
        public bool painkillerProcced;
        /// <summary>仕込み刃 (暗殺者): 次のダイスロール敗北時、 受けるダメージ無効化 +
        /// 同値を軽減不能で敵に与える。 敗北発火 or 戦闘終了で破棄。</summary>
        public bool daggerArmed;

        /// <summary>ボス連戦 (ヴェスカ 4段): 次段の enemy id。敵パッシブ OnTurnEnd で設定され、
        /// CombatManager が perspective 復帰後に SwapEnemy で消費 → null クリア。</summary>
        public string pendingEnemySwapId;
        /// <summary>覚者連戦: 遷移ログラベル</summary>
        public string pendingEnemySwapLabel;

        // ===== 消費アイテム由来（この1戦闘のみ。ctxは戦闘毎に生成→破棄で自動消去） =====
        public int consAtkBurst;          // 次の勝利ターンで与ダメ+X（適用後0）
        public int consDiceRoll;          // 勝敗判定のみ+X（ダメージには非加算）
        /// <summary>[decay] cons_atk 効果の残ターン数。0=無効, -1=戦闘中永続, N=残 N ターン。
        /// EndTurn で decrement し 0 になった瞬間 consDiceRoll を 0 化する。2026-07-21 追加。</summary>
        public int consDiceRollTurnsLeft;
        public int consShield;            // 残シールド吸収量
        public int consShieldExpireTurn;  // この番号を超えるターンで失効。-1=無制限/0=無
        public int consRegen;             // 毎ターン終了時 +consRegen 回復し consRegen--
        public float consCritPct;         // 会心率 (小数) 永続（毎ターン critRatePctAdd へ再適用）
        public int consFlatReduce;        // 被ダメ定数-X 永続（毎ターン再適用）
        public int consDmgMultPct;        // 与ダメ +X%
        /// <summary>[decay] cons_dmg 効果の残ターン数。0=無効, -1=戦闘中永続, N=残 N ターン。
        /// EndTurn で decrement し 0 になった瞬間 consDmgMultPct を 0 化する。2026-07-21 追加。</summary>
        public int consDmgMultTurnsLeft;
        public bool consReflect;          // 被メインダメと同量を敵へ反射
        public int consEnemyDiceDebuff;   // 敵ダイス合計 -X（毎ロール）
        public bool gamblerArmed;         // 賭博師のダイス（ロール時に発火・消費）

        // ===== エリート: 精鋭ハーピィ「死翔」 =====
        public bool consumablesLocked;    // この戦闘中、消費アイテム使用不可（戦闘開始時に付与・戦闘ごとに新規生成でリセット）

        // ===== 竜閃 =====
        public bool rollPurity;           // 無我無心: カスタムダイス以外の補正を一切受けない（戦闘中持続）
        public bool garyoProc;            // 画竜点睛: このターン発動したか（毎ターンリセット）
        public int  garyoDieValue;        // 画竜点睛: 発動時の出目

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
            enemyVulnerabilityMultiplier = 0f;
            enemyVulnerabilityArmed = false;
            currentEnemyKind = CombatSystem.EnemyKind.Normal;
            lambdaFirstTurnDiceDelta = 0;
            lambdaHeavyStepsTurns = 0;
            lambdaNoCritBranch = false;
            lambdaIrritatingInterval = 0;
            lambdaDamageDealtMult = 1f;
            lambdaCritRateCap = 1f;
            lambdaMercifulExecThreshold = 0f;
            lambdaConsumableLockUntilTurn = 0;
            shieldOnWinPct = 0f;
            lentTimeStacks = 0;
            lentTimePaybackRemainTurns = 0;
            lentTimePaybackTotal = 0;
            lentTimeTier = 0;
            rollLossOccurredThisCombat = false;
            hourglassPendingDamageWindow = 0;
            // 会心倍率: 基礎 2.0 + メタバフ Lv58 (会心ダメージ +X%)。 他バフ (HopeSystem苦悩・パッシブ) と加算合成。
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
                stacks += MetaProgression.MetaBuffApplicator.GetPoisonApplyBonus();
            }
            var dict = target == StatusTarget.Enemy ? enemyStatusStacks : playerStatusStacks;
            int cur = dict.TryGetValue(id, out var v) ? v : 0;
            int max = StatusRegistry.Defs.TryGetValue(id, out var def) ? def.maxStacks : int.MaxValue;
            dict[id] = System.Math.Max(0, System.Math.Min(max, cur + stacks));
        }

        /// <summary>敵へ出血スタックを付与する。
        /// enemyBleedStacks は統一フレーム未移行のフィールドなので、 **付与は必ずここを通すこと**。
        /// 減衰 (BeginNewTurn の自然減) と 0 クリアは直接触ってよい。
        /// 2026-08-16: 〈回帰性真理〉の付与拒否を撤去（累積抑制は毎ターン半減へ移行）。</summary>
        public void AddEnemyBleed(int stacks)
        {
            if (stacks <= 0) return;
            enemyBleedStacks += stacks;
        }

        /// <summary>〈回帰性真理〉: 敵に乗っている毒・出血の**蓄積を半減**する (切り捨て)。
        /// ターン開始の DOT 判定が済んだ後に呼ぶ ── ダメージは通し、 累積だけを抑える。
        ///
        /// <para>切り捨てなので、 毎ターン +N を撃ち続けるビルドは概ね N 前後で平衡する
        /// (x → (x+N)/2 の不動点が N)。 「効かない」ではなく「伸びない」に落ちる。</para></summary>
        public void HalveEnemyDots()
        {
            int bleed = enemyBleedStacks;
            if (bleed > 0) enemyBleedStacks = bleed / 2;
            int poison = GetStatus(StatusTarget.Enemy, "poison");
            if (poison > 0) enemyStatusStacks["poison"] = poison / 2;
            if (bleed > 0 || poison > 0)
                UnityEngine.Debug.Log($"[回帰性真理] 毒/出血を半減 (毒 {poison}→{poison / 2} / 出血 {bleed}→{bleed / 2})");
        }

        /// <summary>現在スタック数（未付与は0）。</summary>
        public int GetStatus(StatusTarget target, string id)
        {
            var dict = target == StatusTarget.Enemy ? enemyStatusStacks : playerStatusStacks;
            return dict.TryGetValue(id, out var v) ? v : 0;
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
                int dmg = def.dotScalesWithStacks ? stacks * def.dotPerStack : def.dotPerStack;
                if (dmg > 0)
                {
                    if (def.target == StatusTarget.Enemy) fixedDamageToEnemy += dmg;
                    else fixedDamageToPlayer += dmg;
                }
                int decay = def.decayPerTurn;
                dict[def.id] = System.Math.Max(0, stacks - decay);
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
                    // ダイス合計+10 / 基礎攻撃+10 (consDiceRoll / consAtkBurst を流用)
                    consDiceRoll += 10;
                    consAtkBurst += 10;
                    UnityEngine.Debug.Log("[痛覚遮断剤] HP=1 維持確認 → ダイス+10/攻撃+10。 攻撃終了時に死亡。");
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


            // 敵スタンス（毎ターン抽選し直す。ADR-0005）/ プレイヤースタンス（既定=攻撃。ADR-0006）
            enemyStanceDamageMult = 1f;
            enemyStanceKind = 0;
            enemyStanceWeakRollRatio = 0.65f;
            playerStanceDefense = false;

            // ダメージ処理の単ターンフラグ
            nullifyAllDamage = false;
            nullifyPursuitDamage = false;
            nullifyScratchDamage = false;
            fixedDamageToEnemy = 0;
            fixedDamageToPlayer = 0;
            scratchDamage = 0;
            damageReduced = false;
            pursuitDamage = 0;
            playerDamageThisTurn = 0;   // 焦土用の被ダメ計測
            lastDamageCause = DeathCause.Normal; // 断罪等の増幅タグが翌ターンへ残留しないように

            // 7層ヴェスカ (§13-4) の perTurn 分。 persistent 側 (vescaShield / massiveBleedStacks /
            // enemyDodgeChance / stagnationStacks) は 4 連戦を通じて持続するのでここでは触らない。
            playerHighDiceCrushed = false;
            vescaHalvesDamageThisTurn = false;
            playerAttackPowerPenalty = 0;
            playerBlockIgnored = false;
            voidStanceDamageMul = 1f;
            lightningRodArmed = false;
            vescaBleedOnHit = 0;
            vescaMaxHpBiteOnHit = 0;
            vescaExecuteArmed = false;

            // 会心系
            isCritical = false;
            forceCritical = false;
            critSuppressed = false;
            critRatePctAdd = 0f;
            // 会心倍率: 基礎 3.0 (2026-07-26 リバランス) + メタバフ。 他バフ (HopeSystem苦悩・パッシブ) と加算合成。
            criticalMultiplier = MetaProgression.MetaBuffApplicator.GetCriticalMultiplier()
                                 + MetaProgression.MetaBuffApplicator.GetCritDamageBonus();

            // 遺物 (§15-5): 会心率 / 会心倍率。 リセット直後に載せる。
            // 会心率は加算した分がそのまま乗る (逓減は 2026-09-15 に撤去・ResolveCritRate 参照)。
            {
                var relicRun = GameLoop.GameManager.Instance?.Run;
                float dsRcr = MetaProgression.Relics.RelicApplicator.GetCritRatePct(relicRun, this);
                float dsRcm = MetaProgression.Relics.RelicApplicator.GetCritMultBonus(relicRun, this);
                critRatePctAdd     += dsRcr;
                criticalMultiplier += dsRcm;
                if (dsRcr > 0f) CombatSystem.DmgSourceDiag.Note("遺物〈会心率〉", CombatSystem.DmgSourceDiag.CritRate, dsRcr);
                if (dsRcm > 0f) CombatSystem.DmgSourceDiag.Note("遺物〈会心倍率/Λ共鳴〉", CombatSystem.DmgSourceDiag.CritMul, dsRcm);
            }

            // パッシブが毎ターン再 set する値 (OnTurnStart / OnPostRoll で再評価される前提)
            outgoingDamageMultiplier = 1f;
            lifestealPct = 0f;
            shieldOnWinPct = 0f;
            armorPenPct = 0f;      // 利刃由来。enemyDamageReductionPct は戦闘通して保持＝ここでは触らない

            // 遺物 (§15-5)〈背水〉の吸収。 低HP のときだけ 0 より大きい。 吸命の牙などと同じ枠へ additive。
            lifestealPct += MetaProgression.Relics.RelicApplicator
                            .GetLastBreathLifestealPct(GameLoop.GameManager.Instance?.Run, this);

            // 遺物 (§15-5) 防御貫通+N%。 リセット直後に載せる ── 利刃と同じ枠へ additive。
            // 余剰貫通は ApplyWinDamageModifiers で与ダメ%へ転用されるので、無装甲でも腐らない。
            armorPenPct += MetaProgression.Relics.RelicApplicator
                           .GetArmorPenPct(GameLoop.GameManager.Instance?.Run, this);
            winMinDamage = 1;
            healBlocked = false;            // 狂暴化系
            enemyDamageTakenMultiplier = 1f;
            playerFlatDamageReduction = 0;  // 被ダメ固定減算

            // ダイス制約・補正
            enemyDiceOverrides.Clear();
            enemyDiceTotalPenalty = 0;      // 沈黙の剣帯の1T目-99 等

            // その他 単ターンフラグ
            truceThisTurn = false;
            lentTimePaidThisTurn = false;
            consumablesUsedThisTurn = false;
            garyoProc = false;              // 画竜点睛は毎ターン判定（rollPurity は戦闘中持続）
            garyoDieValue = 0;

            // 遺物の刻印 (§15-5) 判定用。 配線は毎ターン確定するので perTurn。
            blockWiredThisTurn = false;
            blockSumThisTurn = 0;
            accumulatedValues[ChargeSpentThisTurnKey] = 0f;   // 〈短絡〉が読む [perTurn] 値

            // ADR-0010: このターン切った役の記録。 usedRoles は [persistent] なのでここでは触らない。
            firedRolesThisTurn.Clear();

            // ADR-0009 相互攻撃モデル用の一時ボーナス (旧パイプラインでは未使用)
            mutualAttackBonus = 0;
            mutualEnemyAttackReduction = 0;
            nonCritOutgoingMultiplier = 0f;

            // ---------- Phase C: 減衰・カウントダウン (0 に向かって進む値) ----------

            if (enemyBleedStacks > 0 && !bleedDecayDisabled) enemyBleedStacks--; // 出血スタック (止血阻害で減衰無効化)
            if (enemyPassivesDisabledTurns > 0) enemyPassivesDisabledTurns--; // 敵パッシブ無効化 残ターン
            // 軸10〈反響〉: 溜まった反射分は CombatManager がターン頭で適用して 0 に戻す。
            // ここでは触らない (適用前にクリアすると効果が消えるため)。
        }

        // ============================================================
        //  ADR-0009 相互攻撃モデル ブリッジ (perTurn)
        //  旧パイプライン: 「ダイス合計 +N」効果は playerDiceTotal に直加算 (ロール勝負を有利化)
        //  新パイプライン: ダイス合計はロール比較で使われない (収支トリガーは解決後の与被ダメで判定)。
        //                  「+N」は配線後の自攻撃値へ加算 = mutualAttackBonus。
        //  Helper AddPlayerAttackOrDiceBonus() が UseMutualAttackPipeline で自動分岐する。
        // ============================================================

        /// <summary>ADR-0009 [perTurn]: 配線後の自攻撃値に加算されるボーナス。
        /// ExecuteTurnMutual が atkBase に加える。旧パイプラインでは未使用。</summary>
        public int mutualAttackBonus;

        /// <summary>[decay] 挑戦デバフ 軸10〈反響〉: **次のターン開始時に**プレイヤーへ入る反射ダメージ。
        /// 敵から受けたダメージの一定割合をここへ溜め、 翌ターン頭で適用してから 0 に戻す。
        /// 同ターン内に返すと通常の被弾と区別がつかず、 「反響」という遅延の意味が消えるため
        /// 1 ターン遅らせている。 リセットは BeginNewTurn の Phase C (適用後に 0)。</summary>
        public int echoPendingDamage;

        /// <summary>ADR-0009 [perTurn]: 敵攻撃値からの追加減算 (呪縛 等の敵ダイス弱化を新モデルで実装)。
        /// ExecuteTurnMutual が Escalation 計算後の enemyAtkValue から差し引く (下限0)。</summary>
        public int mutualEnemyAttackReduction;

        /// <summary>[perTurn] 非会心攻撃時の追加倍率 (0.20 = +20%)。isCritical=false 時のみ ProcessDamage で適用。
        /// 鈍器 (Bludgeon) キーワードの主フィールド。会心を犠牲に非会心火力を高めるビルドの経路。</summary>
        public float nonCritOutgoingMultiplier;

        /// <summary>「自ダイス合計 +N 相当」のパッシブ効果の書き先を新旧パイプラインで自動分岐する。
        /// 新パイプラインでは playerDiceTotal は配線でしか実効化しないため、
        /// 攻撃力ボーナス (mutualAttackBonus) として積む。</summary>
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
            if (CombatSystem.CombatManager.UseMutualAttackPipeline)
                mutualAttackBonus += n;
            else
                playerDiceTotal += n;
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
        public void AddCharge(int n)
        {
            if (n <= 0) return;
            int cur = GetCharge();
            int cap = CombatSystem.CombatManager.ChargeMax + MetaProgression.MetaBuffApplicator.GetChargeCapBonus();
            int next = System.Math.Min(cap, cur + n);
            // [計装] 充電経済。 **上限で捨てられた分**を分けて数える ── 収入が支出を上回って
            //   常に満タンなら、 リロールが「安い」のではなく**財布が溢れている**ことになる。
            ChargeGained += next - cur;
            ChargeWasted += n - (next - cur);
            accumulatedValues["mutualCharge"] = next;
        }

        /// <summary>[計装] 充電の収支。 Gained=実際に入った / Wasted=上限で捨てた /
        /// SpentTotal=ConsumeCharge 経由の消費 (リロール分は CombatManager.RerollStats[2] と重複)。</summary>
        public static long ChargeGained, ChargeWasted, ChargeSpentTotal, ChargeSpendCalls;
        public static void ResetChargeStats()
            => ChargeGained = ChargeWasted = ChargeSpentTotal = ChargeSpendCalls = 0;

        /// <summary>充電を n 消費 (足りなければ false)。</summary>
        public bool ConsumeCharge(int n)
        {
            if (n <= 0) return true;
            int cur = GetCharge();
            if (cur < n) return false;
            accumulatedValues["mutualCharge"] = cur - n;
            ChargeSpentTotal += n;
            ChargeSpendCalls++;
            NoteChargeSpent(n);
            return true;
        }

        /// <summary>充電を全消費し、消費した量を返す。</summary>
        public int ConsumeAllCharge()
        {
            int cur = GetCharge();
            if (cur > 0) { accumulatedValues["mutualCharge"] = 0; NoteChargeSpent(cur); }
            return cur;
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
        private void NoteChargeSpent(int n)
        {
            if (n <= 0) return;
            accumulatedValues[ChargeSpentThisTurnKey] = GetAccumulated(ChargeSpentThisTurnKey) + n;
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

        /// <summary>過充電判定 (charge が OverchargeThreshold 以上・2026-07-16: 上限10維持のまま閾値を7に下方)。
        /// 目的: Spark/LightningStrike の消費と Overload/Criticality 維持を両立可能にする。</summary>
        public const int OverchargeThreshold = 7;
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
