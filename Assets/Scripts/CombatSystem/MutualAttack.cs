namespace CombatSystem
{
    /// <summary>
    /// ADR-0009 柱1: ダイス配線先の端子種別。
    /// Special (特殊端子・ショップ販売・接続制限N) は W7 後段で追加する。
    /// </summary>
    public enum DiceTerminal
    {
        Attack = 0,  // 置いた出目分、自攻撃ダメージへ加算 (未配置ダイスの自動合流先)
        Block = 1,   // 置いた出目分、敵攻撃を軽減
        Charge = 2,  // 置いたダイス個数分、充電を獲得 (柱5)
        /// <summary>§6-5 特殊端子。 **ショップで買った 1 種を装着しているときだけ存在する**。
        /// 未装着なら接続制限 0 ＝ 1 本も挿せない。 効果と接続制限は
        /// <see cref="SpecialTerminals"/> が持つ。</summary>
        Special = 3,
    }

    /// <summary>1 ターンぶんの配線。 **実体とゴーストを対で持つ。**
    ///
    /// <para>出目パーツ T3/T4 (2026-08-17) は 1 個のダイスを 2 端子へ接続できる。
    /// 片方が<b>実体</b>、 もう片方が<b>ゴースト</b>で、
    /// <b>合計値には両方が乗るが、端子役の判定に参加するのは実体だけ</b>。
    /// これが無いと 1 個が 2 個ぶんとして数えられ、 同一端子へ重ねた瞬間に
    /// 〈対〉が確定成立する (端子役は 対 / 束 / 小階 / 飛階)。</para>
    ///
    /// <para><b>実体とゴーストは必ずこの型で一緒に受け渡す。</b> 別々のデリゲートで
    /// 取ると、 方策が片方だけ更新したときに不整合が静かに通る。</para></summary>
    public struct WiringPlan
    {
        /// <summary>実体の接続先。 添字 = ダイス番号。 合計値にも役判定にも乗る。</summary>
        public DiceTerminal[] main;

        /// <summary>ゴーストの接続先。 添字 = ダイス番号。 <b>-1 は「ゴースト無し」</b>。
        /// 合計値にだけ乗る。 null なら全ダイスがゴースト無し。</summary>
        public int[] ghost;

        public static WiringPlan Of(DiceTerminal[] main) => new WiringPlan { main = main, ghost = null };

        /// <summary>ゴースト無しを表す番兵。</summary>
        public const int NoGhost = -1;
    }

    /// <summary>
    /// ADR-0009 柱3: 完全情報テレグラフ。敵ロール確定後・配線前に
    /// 配線ポリシー (AutoRunner) / 配線 UI (W5) へ渡す情報一式。
    /// </summary>
    public struct MutualTurnTelegraph
    {
        public int turn;               // 現在ターン (1 始まり)
        public int enemyAttackValue;   // ブロック減算前の敵攻撃実値 (基礎×段階倍率+ダイス寄与)
        public int escalationStage;    // エスカレーション段階 0〜4
        public int nextThresholdTurn;  // 次段階の閾値ターン (-1=最終段階)
        /// <summary>次の大技まで何ターンか (0 = このターンが大技 / −1 = 大技なし)。 EnemyData.heavyPeriod。</summary>
        public int turnsToHeavy;
        /// <summary>大技ターンの攻撃倍率 (予告表示用)。</summary>
        public float heavyMul;
        public int[] enemyDice;        // 敵の出目 (LED 表示と同値)
        public int enemyDiceTotal;     // 敵ダイス合計 (パッシブ補正込み)

        /// <summary>2 体戦 (エンカウントプリセット) の 2 体目の攻撃値。 0 = 単体戦 or 撃破済み。
        ///
        /// <para><b>ブロックはこのパケットからも全額引かれる</b> (倍率なし) ので、
        /// 実被ダメは <c>max(0, enemyAttackValue − block) + max(0, secondaryAttackValue − block)</c>。
        /// 2 本の折れ線の和なので、 合計値ひとつに畳むと形が再現できない ── 方策側は
        /// 必ず 2 項として評価すること。</para>
        ///
        /// <para><b>配線前に確定している。</b> 柱3 (完全情報テレグラフ) の対象で、
        /// 見えていない一撃を作らないために 1 体目と同じタイミングで振る。</para></summary>
        public int secondaryAttackValue;

        // ── 7層ヴェスカ〈遺物学者〉の抽選結果 (§13-4)。
        //    抽選はターン開始時に解決済みで、 §6-2 の完全情報原則により**配線前に開示する**。
        //    enemyAttackValue には既に反映されている (遺物→enemyDiceTotalBonus→β→攻撃値) が、
        //    以下は攻撃値に乗らない性質なので別枠で渡す。 これが無いと配線側が
        //    「殴ると反射で死ぬターン」「殴っても 99% 減衰で無駄なターン」を判断できない。
        public string[] drawnRelics;        // 予告表示用の遺物名 (空なら該当なし)
        public int enemyShield;             // 敵の残シールド (与ダメを先に吸う)
        public float enemyShieldReflectRate;// シールドが吸収した分の反射率 (0/1.0/2.0)
        public float enemyDodgeChance;      // 全ダメージ (通常/固定/軽減不可) の回避率 0〜0.60
        public float enemyDamageTakenMul;   // 敵が受けるダメージ倍率 (旧・未使用)
        public bool enemyHalvesDamageThisTurn; // このターン敵への与ダメが半分になる (天与の盾)
        public int playerAttackPenalty;     // このターンのプレイヤー攻撃力減算 (麻痺毒)
        public bool playerHighDiceCrushed;  // 3 以上の出目が 1〜2 へ再抽選される (天与の指輪)
        public bool playerBlockIgnored;     // このターンのブロック配線が無視される (貫きの錐)
        public bool executeArmed;

        /// <summary>〈綻び〉このターン封印される端子。 -1 = 封印なし。 配線前に開示される。</summary>
        public int sealedTerminal;           // 攻撃終了時 HP が最大の20%以下なら即死 (処刑人の烙印)

        // ── 烈炎 (灰燼の王・2026-08-04)。 **亀戦法そのものを罰する機構** なので、
        //    「今いくつ溜まっているか」「どう配線すると増えるか」を配線前に開示する必要がある。
        //    これが無いと配線側は「守ると罰される」を判断できず、 一方的な税にしかならない。
        public int blazeStacks;             // 現在の烈炎スタック (次ターン開始時にこの値の軽減無視ダメ)
        public bool blazePenalizesBlock;    // ブロック端子への配線が閾値以上だとスタックが増える相手か
    }
}
