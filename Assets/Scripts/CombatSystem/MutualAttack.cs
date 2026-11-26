namespace CombatSystem
{
    /// <summary>
    /// ADR-0009 柱1: ダイス配線先の端子種別。 端子の山札 (ADR-0014) では、 挿した端子の種類 (TerminalKind) から作る
    /// (<see cref="TerminalCards.LegacyKind"/>)。 <b>値の並びを変えない</b> (添字・記録の文字に使う所がある)。
    /// </summary>
    public enum DiceTerminal
    {
        Attack = 0,  // 置いた出目分、自攻撃ダメージへ加算 (未配置ダイスの自動合流先)
        Block = 1,   // 置いた出目分、敵攻撃を軽減
        Charge = 2,  // 置いたダイス個数分、充電を獲得 (柱5)
        /// <summary>補助の種類の端子 (ADR-0014・<see cref="TerminalKind.Support"/>) に挿した出目。
        /// 既存の品が読む「特殊に挿した個数」(MutualKeys.SpecialDiceCount) はこの個数。</summary>
        Special = 3,
        /// <summary>挿していない (端子の山札・ADR-0014)。 どの端子にも入らない出目。 **末尾に置く** (0〜3 を添字に使う所がある)。</summary>
        None = 4,
    }

    /// <summary>1 ターンぶんの配線。 添字 = ダイス番号、 値 = 接続先の端子。
    ///
    /// <para>(2026-10-18 ADR-0011: 出目パーツ T3/T4 の「2 端子へ同時接続」＝ゴーストは廃止した。
    /// 役は端子の置き方を条件にしないので、 配線は合計値の割り振りだけを決める。)</para></summary>
    public struct WiringPlan
    {
        /// <summary>接続先。 添字 = ダイス番号。</summary>
        public DiceTerminal[] main;
        /// <summary>端子の山札 (ADR-0014): 出目ごとの挿し先 (場の端子の添字・−1 = 挿さない)。 山札で戦う時はこちらを読む。</summary>
        public int[] slots;
        /// <summary>端子の山札: 〈影写し〉〈火葬の剣〉〈有効活用〉の対象 (添字 = その端子の場の位置・値 = 対象の場の位置・−1 = 選ばない)。 null = 左端の候補。</summary>
        public int[] targets;
        /// <summary>手札の規則 Pick (2026-11-13): 次のターンも手札に残す印 (添字 = 場の位置)。 残せるのは挿さなかった手札の端子だけ (本体が KeepFromMarks で直す)。 null = 何も残さない。</summary>
        public bool[] keep;
        /// <summary>〈二股の導線〉【再起動】の対象 (場の添字・<see cref="ChargeCore.RebootAuto"/> = おまかせ)。 null = 再起動しない。</summary>
        public int? reboot;
        /// <summary>〈雷鳴の太鼓〉【オーバークロック】を使う (条件を満たさなければ本体が使わない)。</summary>
        public bool overclock;
        /// <summary>撃の順 (2026-11-25): 出目ごとの挿した順 (小さいほど先・添字 = ダイス番号)。 攻撃の端子の撃はこの順に当たる。 null = 場の順。</summary>
        public int[] plugRank;

        public static WiringPlan Of(DiceTerminal[] main) => new WiringPlan { main = main };
        public static WiringPlan OfSlots(int[] slots, int[] targets = null, bool[] keep = null) => new WiringPlan { slots = slots, targets = targets, keep = keep };
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
        /// <summary>このターンは大技のチャージで、 敵は攻撃しない (<see cref="Escalation.IsChargeNoAttack"/>・2026-11-14)。 攻撃値は 0 で載っている。
        /// ダウンのターンは false (ダウンの欄が出る)。</summary>
        public bool enemyCharging;
        /// <summary>ヴェスカの天与 (2026-11-15・<see cref="TenyoHeavy.Gift"/> の番号・0 = 無し)。 チャージのターンと大技のターンに、 どの天与が来るか。</summary>
        public int tenyoGift;
        /// <summary>このターンが灰燼の王の大技〈業火〉なら、 主攻撃とは別に入る烈炎の分 (烈炎の数 × BossParam.BlazeBurstPerStack・軽減無視・撃ったら烈炎 0・2026-11-14)。
        /// それ以外は 0。 <see cref="enemyAttackValue"/> には含まない (ブロックで減らない)。</summary>
        public int heavyBurst;
        /// <summary>次の構えまで何ターンか (0 = このターンは構え / −1 = 構えない)。 EnemyData.guardPeriod。</summary>
        public int turnsToGuard;
        /// <summary>このターンの敵の防御 (構え・割れる装甲・積み上がる装甲を反映した後)。</summary>
        public int enemyDefense;
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
        public bool enemyAttackIgnoresGuard; // 〈狂暴化〉(51T〜): 敵の攻撃値 (防御の前) がブロック・シールド・軽減を通さずそのまま HP に入る
        public bool executeArmed;

        /// <summary>〈綻び〉このターン封印される端子。 -1 = 封印なし。 配線前に開示される。</summary>
        public int sealedTerminal;           // 攻撃終了時 HP が最大の20%以下なら即死 (処刑人の烙印)

        // ── 烈炎 (灰燼の王・2026-08-04)。 **亀戦法そのものを罰する機構** なので、
        //    「今いくつ溜まっているか」「どう配線すると増えるか」を配線前に開示する必要がある。
        //    これが無いと配線側は「守ると罰される」を判断できず、 一方的な税にしかならない。
        public int blazeStacks;             // 現在の烈炎スタック (次ターン開始時にこの値の軽減無視ダメ)
        public bool blazePenalizesBlock;    // ブロック端子への配線が閾値以上だとスタックが増える相手か

        // ── ボスのダウン (HP の目盛り・2026-11-13・BossDown.cs)。 完全情報 (予告と同じく配線の前に開示する)。
        /// <summary>このターン、 敵はダウンしている (攻撃しない・受けるダメージ ×<see cref="BossDown.TakenMul"/>)。 このとき <see cref="enemyAttackValue"/> は 0。</summary>
        public bool enemyDown;
        /// <summary>このターンの後にまだ続くダウンのターンの数 (割った目盛りの残り。 0 = 次のターンはダウンしない ── このターンに目盛りを割らなければ)。</summary>
        public int downPending;
        /// <summary>このターンのダウンは弱い (ひびで割れた・受けるダメージ ×1.5 なし・2026-11-14)。</summary>
        public bool enemyDownWeak;
        /// <summary>後に続くダウンの先頭が弱いか (次のターンのダウンの種類)。</summary>
        public bool downPendingHeadWeak;
        /// <summary>[2026-11-26] 次のターンに来るダウンの種類 (<see cref="DownKind"/> の値・−1 = 来ない)。 溜めで無効になる分も Resisted として出す。</summary>
        public int downNextKind;
        /// <summary>[2026-11-26] このターンに通常攻撃で目盛りを割ったら、 次のターンのダウンの種類 (<see cref="DownKind"/> の値・−1 = 割れる目盛りが無い / もう次のダウンが待っている)。
        /// 配線の前に「今割ると…」を出すための完全情報。</summary>
        public int downBreakNowKind;
        /// <summary>目盛りごとのひびの入ったターン (−1 = ひびなし・本体の状態を読むだけ・書き換えない)。 null = 目盛りなし。</summary>
        public int[] downCrackTurns;
        /// <summary>まだ割っていない一番上の目盛りのひびが、 ひとりでに割れるターン (そのターンの終わり・−1 = ひびなし)。</summary>
        public int downCrackBreakTurn;
        /// <summary>次に割る目盛りの HP (敵の HP がこれ以下になったら割れる・−1 = 目盛りが無い/残っていない)。</summary>
        public int downNextNotchHp;
        /// <summary>目盛りの一覧 (最大 HP の割合・大きい順・HP の棒の刻み)。 null = ダウンしない敵。 書き換えないこと。</summary>
        public float[] downNotches;
        /// <summary>割った目盛りの数 (<see cref="downNotches"/> の先頭から)。</summary>
        public int downBroken;
        /// <summary>[2026-11-26] 混乱ゲージ: 今の混乱値 / 満タンの量 (0 = ゲージを持たない敵) / 満ちた後に最大値に掛ける増え方。</summary>
        public int downGauge, downGaugeMax;
        public float downGaugeGrowth;
    }
}
