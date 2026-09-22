namespace MetaProgression.Relics
{
    /// <summary>
    /// 遺物のステータス軸。 正本: docs/GAME.md §15-5。
    ///
    /// **enum の並び順を変えない。** RolledRelic は int で保存されるため、
    /// 並べ替えると既存セーブの遺物が別の軸に化ける。 追加は必ず末尾へ。
    /// </summary>
    public enum RelicAxis
    {
        Attack = 0,          // 攻撃+N          → ctx.mutualAttackBonus
        DamagePct,           // 与ダメージ+N%    → ctx.outgoingDamageMultiplier

        /// <summary>会心率+N% → ctx.critRatePctAdd。 **2026-08-08 引退**（新規抽選に出ない）。
        /// enum は消せない（int で保存されるため並べ替え・削除は既存セーブを壊す）ので、
        /// <see cref="RelicAxisCatalog.Retired"/> で抽選プールから外している。
        /// 既存セーブに載っている分は従来どおり働く。 経緯は §24。</summary>
        CritRatePct,
        CritMultPct,         // 会心倍率+N%      → ctx.criticalMultiplier (基準2.0・2026-08-03)
        ArmorPenPct,         // 防御貫通+N%      → ctx.armorPenPct
        DamageReductionPct,  // 被ダメージ-N%    → 割合軽減 (新設フィールド)
        MaxHp,               // 最大HP+N        → run.playerMaxHP
        OpeningShield,       // 開幕シールド+N   → ctx.consShield
        Charge,              // 充電            → 開幕 + 毎ターン の複合
        Rinkai,              // 臨界            → 爆発時に敵の現在HP の N%
        OpeningBleed,        // 開幕出血+N      → ctx.enemyBleedStacks
        OpeningPoison,       // 開幕毒+N        → AddStatus(Enemy,"poison",N)

        // ── ここから下は **難易度 25 以上でのみ出現する固有メイン軸** (2026-08-08 追加) ──
        //
        //    通常の 12 軸はどの難易度でも出るので、 挑戦スコアを上げる動機が充填率の差だけしか
        //    なかった。 「その難易度でしか手に入らない報酬」を置く枠。
        //
        //    **全て「条件付きで高分散」に揃える。** 期待値を平坦に薄めるのではなく、
        //    賭けに乗った時だけ大きく返る (均して薄める方向は最適方策下では期待値の平行移動に
        //    しかならない・§24)。 4 本それぞれ **賭ける資源が違う**:
        //      Λ共鳴 = Λ探索 / 渇き = 希望 / 刻限 = 時間 / 背水 = HP
        //
        //    追加するときは RelicAxisCatalog.HighDifficultyOnly にも足すこと。

        /// <summary>〈Λ共鳴〉Λ層で戦闘を 1 回終えるたび、 そのラン中ずっと会心倍率 +N%（累積）。
        /// 賭けるのは **Λ探索** ── 突入には〈決意〉が要り、 潜れば 3 マスごとに恒久デバフを背負う。</summary>
        LambdaResonance,

        /// <summary>〈渇き〉希望が上限の 40% 以下の間、 与ダメージ +N%。
        /// 賭けるのは **希望** ── 回復を渋るほど強いが、 0 で発狂してランが終わる。
        /// 高難易度は〈絶望的な戦闘〉〈補給断絶〉で希望が構造的に枯れるので、 難易度と噛み合う。</summary>
        HopeBurn,

        /// <summary>〈刻限〉戦闘の経過ターン数 1 つにつき 与ダメージ +N%（そのターン数 × N%）。
        /// 賭けるのは **時間** ── 速攻を捨てて殴り合いを選ぶほど返る。 戦闘ごとにリセット。
        ///
        /// **エスカレーション段階への連動は棄却** (2026-08-08)。 実測 84,428 ターンで段階 0 が
        /// 77.5% ／ 段階 3 以上は 1.2% しかなく、 平均段階 0.29 ＝ 攻撃軸の 1/7 の死に軸だった。
        /// §15-5 の「ボス戦 59T」は ADR-0009 前の古い数字で、 実測はボス 8.3T / 通常 4.9T。
        /// 閾値 {5,10,15,20} に対して戦闘が短すぎる。 経過ターン数の直接参照なら
        /// ターン加重の平均ターン番号 4.06（ボス 5.74 / 通常 3.64）が期待値に乗る。</summary>
        LongBattle,

        /// <summary>〈背水〉HP が最大の 35% 以下の間、 被ダメージ −N% かつ 与ダメージの M% を回復。
        /// 賭けるのは **HP** ── 回復せずに走るほど働くが、 一撃で足りなくなれば死ぬ。
        /// 高難易度で 1〜3 層の事故死が支配的になる問題に、 天井を上げずに効く。
        ///
        /// **吸収は自己減衰する** ── 回復して 35% を超えた瞬間に条件が切れるので、
        /// 閾値の周りで振動する。 際限なく硬くなる方向へは走らない。</summary>
        LastBreath,
    }

    /// <summary>刻印の発動条件。 **並び順を変えない**（保存値が int のため）。
    ///
    /// 条件は「守ろうと思えば常に守れる」ものに限る。 損益分岐は成立率 85.7% (=6/7) で、
    /// それを下回ると刻印が段6 の通常遺物より弱くなり、純粋な劣化になるため。
    /// 一度破ると戻せない条件（武器強化・所持アイテム数）は、天工開物やイベントの強制付与など
    /// **プレイヤーの意思と無関係に破られる**ので置かない。 詳細は §24。</summary>
    public enum RelicCurse
    {
        None = 0,
        NoConsumables,    // 消耗品を 1 つも持っていない間        (常時判定・自分で回復可)
        LowGold,          // ゴールドが 10 以下の間               (常時判定・自分で回復可)
        NoBlockWiring,    // そのターン、ブロック端子に未配線      (毎ターン判定・リセットされる)
        NoRoleFired,      // そのターン、役を 1 つも切っていない    (毎ターン判定・リセットされる)
    }

    /// <summary>
    /// 軸ごとの表示名・段別の値・遺物名。 **数値の正本は docs/GAME.md §15-5 の表**で、
    /// ここはその写し。 検証ツール docs/tools/relic-roller.html も同じ表を持つので、
    /// **数値を変えたら 3 箇所すべてを直す**。
    ///
    /// 単位の定義: 段1 = 参照状態での期待与ダメージ 6%（＝攻撃+1）。
    /// 参照状態は 4〜5層・通常敵・段階0〜1、 atkBase 17 / 会心係数 1.40 / 敵防御 10%。
    /// </summary>
    public static class RelicAxisCatalog
    {
        /// <summary>メインの下限が 3 なのは構造下限（3枠で 5 ＝ 3+1+1）を成立させるため。
        /// 総点が低いときだけメインが 3 に落ちる。 刻印時のみ CursedStep まで上がる。
        ///
        /// **2026-08-08 大幅強化**: 6/3 → 9/5、 刻印 7 → 10。 理由は §15-2 の高難易度帯が
        /// 「見返りも手段も無い」状態だったこと。 挑戦 50pt は最良遺物込みでも 1 層で 38.7% が
        /// 終わり、 7 層クリアは 0.0%。 しかも `DifficultyPoint` が 16 分の 2 しか動かないため、
        /// 難易度を上げても遺物が強くならなかった。 段上限を上げて周回成長の天井を引き上げる。
        /// 理論最良は 総点 18 (期待与ダメ +108% 相当) → **総点 29 (+174% 相当)**。</summary>
        public const int MainStepMin = 3, MainStepMax = 9, CursedStep = 10;
        public const int SubStepMin  = 1, SubStepMax  = 5;

        /// <summary>〈渇き〉が働く希望の閾値。 **絶対値**であって hopeCap に対する比ではない。
        ///
        /// **比にしてはいけない** (2026-08-09 修正)。 hopeCap は**一方向のラチェット**で、
        /// 希望が 45 以下になると上限 45、 20 以下になると上限 20 に固定される
        /// （＝それ以上は回復できなくなる・<see cref="GameLoop.HopeSystem"/> の UpdateCapLock）。
        /// 比で取ると **閾値が希望と一緒に下へ逃げて永久に届かない**:
        /// <code>
        ///   開始      hope 60 / cap 89 → 閾値 35.6   まだ発動しない
        ///   45 以下   hope 44 / cap 45 → 閾値 18     閾値が下がる
        ///   20 以下   hope 19 / cap 20 → 閾値  8     さらに下がる
        /// </code>
        /// 実測 (計装・33,864 判定): 発動率 **6.6%**、 判定時の平均希望 42.3 に対し平均閾値 25.5。
        /// 倍率を +63%→+126%→+180% と 3 度上げても与ダメが動かなかった真因がこれ。</summary>
        /// 2026-08-09: 40 → **60**。 <see cref="HopeBurnStartHope"/> と同値にして、
        /// **開幕から発動圏に居る**形にした。 40 では発動率 40.5% で、 残り 6 割の攻撃が
        /// 素の火力のまま ＝ 希望を前借りした対価が返ってこなかった。
        public const int HopeBurnThreshold = 60;
        /// <summary>〈背水〉が働く HP の閾値（playerMaxHP に対する比）。</summary>
        public const float LastBreathThreshold = 0.35f;

        /// <summary>〈渇き〉装備時の**開幕希望**（既定 60・上限 hopeCap は動かさない）。
        /// <see cref="HopeBurnThreshold"/> と同値なので **開幕から発動圏**に居る。
        /// 賭けの対価は「発動までの猶予」ではなく **希望という資源そのものを前借りすること**
        /// （横移動の自由度・発狂までの余裕・上限ラチェットの早期発動）に置いている。</summary>
        public const int HopeBurnStartHope = 60;

        public static readonly RelicAxis[] All =
            (RelicAxis[])System.Enum.GetValues(typeof(RelicAxis));

        // ── 段 1〜10 の値。 index = step-1。 段8〜10 は 2026-08-08 の上限引き上げで追加 ──
        //    既存 7 段の増分をそのまま延長してある (線形性を保つ)。
        private static readonly int[] Attack      = {  1,  2,  3,  4,  5,  6,   7,   8,   9,  10 };
        private static readonly int[] DamagePct   = {  5, 10, 15, 20, 25, 30,  35,  40,  45,  50 };
        private static readonly int[] CritRate    = {  4,  8, 12, 16, 20, 24,  28,  32,  36,  40 };
        /// <summary>会心倍率+N%。 **上限 110%** (2026-08-09 に 150% から引き下げ)。
        /// 敵会心バグ (§24) を直した後の単軸スイープで、 段9 (旧 +135%) が 5F 84/27・
        /// 7F 95/32 と全 16 軸で最強になり、 物差しの攻撃+9 (65/34) を大きく超えていた。</summary>
        private static readonly int[] CritMult    = { 11, 22, 33, 44, 55, 66,  77,  88,  99, 110 };
        private static readonly int[] ArmorPen    = {  4,  8, 12, 16, 20, 24,  28,  32,  36,  40 };
        /// <summary>被ダメージ−N%。 **上限 30%** (2026-08-08 に 50% から引き下げ)。
        /// 単軸スイープ実測で段9 (旧 −45%) が 5層クリア 74.6% ＝ 物差しの攻撃+9 (59.4%) を
        /// 15pt 上回り、 改善/悪化 119/14 と全 16 軸で突出した。 ADR-0009 の相互攻撃モデルでは
        /// 被弾を減らすと殴り合いの往復回数そのものが減るので、 防御は複利で効く。</summary>
        private static readonly int[] DmgReduce   = {  3,  6,  9, 12, 15, 18,  21,  24,  27,  30 };
        private static readonly int[] MaxHp       = {  4,  8, 12, 16, 20, 24,  28,  32,  36,  40 };
        private static readonly int[] Shield      = {  2,  4,  6,  8, 10, 12,  14,  16,  18,  20 };
        private static readonly int[] Bleed       = {  3,  4,  5,  6,  7,  8,   9,  10,  11,  12 };
        private static readonly int[] Poison      = {  1,  2,  3,  4,  5,  6,   7,   8,   9,  10 };
        private static readonly int[] RinkaiPct   = {  1,  2,  3,  4,  5,  6,   7,   8,   9,  10 };
        /// <summary>Λ共鳴: Λ層の戦闘 1 回ごとに会心倍率 +N%（ラン中累積）。
        /// Λ の 1 周は 3 マスで期待 1.5 戦なので、 20 マス潜れば約 10 戦 = 段6 で +140%。
        /// 潜らなければ 0。 **難易度 25 以上でしか出ない**。</summary>
        private static readonly int[] LambdaReso  = {  6,  8, 10, 12, 14, 14,  16,  18,  20,  22 };
        /// <summary>渇き: 希望が上限の 40% 以下の間だけ 与ダメージ +N%。
        ///
        /// **2026-08-08 に倍化** (7N% → 14N%)。 旧値 (段9 で +63%) では、 BOT に
        /// 帯 [18, hopeCap×38%] を維持させて条件を常時成立させても **5F の McNemar が
        /// 33/64・p=0.002 と有意に有害**だった ── 希望を低く保つコスト（横移動の制限・
        /// 発狂リスク・希望デバフの累積）が与ダメの上乗せを上回っていた。
        /// 通常の与ダメ軸 (5N%) の **2.8 倍**。 賭けの対価としてはこの水準が要る。
        ///
        /// **2026-08-09 にさらに増量** (14N% → 20N%) し、 同時に <see cref="HopeBurnStartHope"/> で
        /// 開幕希望を 60 へ落として**発動圏から始める**ようにした。 倍化だけでは効かなかった ──
        /// 1〜3層の 1攻撃与ダメが遺物なし比 +2.5% で、 **そもそも発動していなかった**ため。
        /// 発動しない区間では倍率をいくら上げても 0 のまま、 という当たり前の壁だった。</summary>
        private static readonly int[] HopeBurnPct = { 20, 40, 60, 80, 100, 120, 140, 160, 180, 200 };
        /// <summary>刻限: 経過ターン数 1 つにつき 与ダメージ +N%（そのターン数 × N%）。
        /// ターン加重の平均ターン番号が 4.06 なので、 段9 (15%) の期待は +61% ＝
        /// 与ダメ軸段9 (+45%) の **1.35 倍**。 ボス戦なら +86%、 通常戦なら +55%。</summary>
        private static readonly int[] LongBattlePct= {  3,  5,  6,  8,  9, 11,  12,  14,  15,  17 };
        /// <summary>背水: HP が最大の 35% 以下の間だけ 被ダメージ −N%。
        /// **段1 から 30% で始まり 60% で止める** ── 高難易度限定軸はメイン枠専用で、
        /// メインの下限は 3（=段1・2 は構造上出ない）。 さらに実測では挑戦 25pt 以上の
        /// メインは 段8・9 が 6 割強を占めるので、 低段から効く形にしないと軸が立たない。
        /// 上を 60% で止めるのは、 これ以上だと低HP が安全地帯になり戦闘が間延びするため。</summary>
        private static readonly int[] LastBreathPct= { 30, 33, 37, 40, 43, 47,  50,  53,  57,  60 };
        /// <summary>背水の吸収: HP が最大の 35% 以下の間、 与ダメージの N% を回復。
        /// 回復して 35% を超えれば条件が切れる＝**自己減衰する**ので、 際限なく硬くならない。</summary>
        private static readonly int[] LastBreathLs = {  2,  4,  5,  7,  8, 10,  11,  13,  14,  16 };
        /// <summary>充電は複合。 開幕成分は通常戦（2.3T）で効き、毎ターン成分はボス戦（59T）で効く。
        /// **毎ターンは +5 で構造的な天井** ── オーバーロードは 1 ターン 1 回・コスト 5 なので、
        /// それ以上入れた充電は捨てられる。 充電上限の拡張は実測 0.00 単位なので軸にしない。</summary>
        private static readonly int[] ChargeOpen  = {  2,  3,  4,  4,  4,  4,   4,   4,   4,   4 };
        private static readonly int[] ChargeTurn  = {  0,  0,  0,  2,  3,  4,   5,   5,   5,   5 };

        private static readonly string[] Names =
        {
            "攻撃", "与ダメージ", "会心率", "会心倍率", "防御貫通", "被ダメージ",
            "最大HP", "開幕シールド", "充電", "臨界", "開幕出血", "開幕毒",
            "Λ共鳴", "渇き", "刻限", "背水",
        };

        /// <summary>**難易度 25 以上でのみ出現する固有メイン軸**。 §15-5。
        /// 追加したら RelicAxis の enum 末尾へ足し、 ここにも登録すること。</summary>
        public static readonly RelicAxis[] HighDifficultyOnly =
        {
            RelicAxis.LambdaResonance, RelicAxis.HopeBurn,
            RelicAxis.LongBattle,      RelicAxis.LastBreath,
        };

        public static bool IsHighDifficultyOnly(RelicAxis a)
        {
            for (int i = 0; i < HighDifficultyOnly.Length; i++)
                if (HighDifficultyOnly[i] == a) return true;
            return false;
        }

        /// <summary>**引退した軸** ── 新規抽選には出ないが、 既存セーブでは従来どおり働く。
        ///
        /// enum から消すことはできない（RolledRelic が int で保存するため、 削除も並べ替えも
        /// 既存の遺物を別の軸に化けさせる）。 出現側だけを止める形にする。
        ///
        /// **会心率** (2026-08-08): 単軸スイープ 500ラン・同一シードで 5F 35/37 (p=0.906) ＝
        /// 物差しの攻撃+9 (65/26) に対して有意差なし。 会心特化ビルド (ペルソナCrit +
        /// メタPrecisionApex) で測り直すと **31/75 (p=0.000) と逆に有害**だった。
        /// 原因は 2 段構え:
        ///   ① ResolveCritRate の逓減 (knee 20%) が、 素の会心率が高いビルドほど上乗せを捨てる
        ///      ＝ 会心率軸は会心ビルドで最も価値が下がるという逆転。
        ///      <b>この逓減は 2026-09-15 に撤去した</b>ので、 ①はもう成り立たない。
        ///   ② より本質的に、 **会心は確率的なので平均は上がるが分散も上がり、 超過分が
        ///      オーバーキルで捨てられる**。 キル所要ターンがほぼ縮まず敵の攻撃回数も減らない。
        ///      決定的に毎ターン乗る 防御貫通 (80/22) や 与ダメ% と結果が正反対になる。
        /// ②は逓減と無関係に残るので引退は維持するが、 <b>復帰を検討するなら測り直しから</b>
        /// ── 上の 31/75 は逓減下の測定値で、 現在の条件では無効。 §24 参照。</summary>
        public static readonly RelicAxis[] Retired = { RelicAxis.CritRatePct };

        public static bool IsRetired(RelicAxis a)
        {
            for (int i = 0; i < Retired.Length; i++)
                if (Retired[i] == a) return true;
            return false;
        }

        /// <summary>遺物名。 [軸][総点帯] で、帯は 低(5-9) / 中(10-14) / 高(15-29)。
        /// 鑑定所が発行する鑑定書の記載、という体で「断片 → 本体 → 本来の姿」に揃えてある。</summary>
        private static readonly string[][] RelicNames =
        {
            new[] { "鉄片",     "据えの鉄芯",     "鍛えの芯金"   },  // 攻撃
            new[] { "鳴り環",   "共振環",         "倍音環"       },  // 与ダメージ
            new[] { "覗き窓",   "間隙測り",       "寸分の測り"   },  // 会心率
            new[] { "下げ錘",   "一打の分銅",     "極みの分銅"   },  // 会心倍率
            new[] { "錐先",     "甲割りの錐",     "徹しの錐"     },  // 防御貫通
            new[] { "当て革",   "緩衝綿",         "受けの厚み"   },  // 被ダメージ
            new[] { "継ぎ布",   "継ぎ足しの器",   "満ちの器"     },  // 最大HP
            new[] { "端切れ",   "初手の当て布",   "先手の帷"     },  // 開幕シールド
            new[] { "帯電片",   "余剰電位",       "満ちの電位"   },  // 充電
            new[] { "兆し",     "臨界前夜",       "臨界の刻"     },  // 臨界
            new[] { "かすり傷", "止血の失敗例",   "塞がらぬ傷"   },  // 開幕出血
            new[] { "一匙",     "遅効の匙",       "量り切りの匙" },  // 開幕毒
            new[] { "狭間の残響", "重なる残響",   "狭間の合唱"   },  // Λ共鳴
            new[] { "乾いた縄",   "涸れの釣瓶",   "底の見える井" },  // 渇き
            new[] { "欠けた砂時計", "返しの砂",   "尽きぬ砂"     },  // 刻限
            new[] { "罅",         "割れ止め",     "割れぬ地肌"   },  // 背水
        };

        private static int[] Table(RelicAxis a)
        {
            switch (a)
            {
                case RelicAxis.Attack:             return Attack;
                case RelicAxis.DamagePct:          return DamagePct;
                case RelicAxis.CritRatePct:        return CritRate;
                case RelicAxis.CritMultPct:        return CritMult;
                case RelicAxis.ArmorPenPct:        return ArmorPen;
                case RelicAxis.DamageReductionPct: return DmgReduce;
                case RelicAxis.MaxHp:              return MaxHp;
                case RelicAxis.OpeningShield:      return Shield;
                case RelicAxis.OpeningBleed:       return Bleed;
                case RelicAxis.OpeningPoison:      return Poison;
                case RelicAxis.Rinkai:             return RinkaiPct;
                case RelicAxis.LambdaResonance:    return LambdaReso;
                case RelicAxis.HopeBurn:           return HopeBurnPct;
                case RelicAxis.LongBattle:         return LongBattlePct;
                case RelicAxis.LastBreath:         return LastBreathPct;
                default:                           return null;   // Charge は複合なので個別
            }
        }

        private static int Idx(int step) => UnityEngine.Mathf.Clamp(step, 1, CursedStep) - 1;

        /// <summary>段 (1〜7) に対応する主値。 **充電だけは複合なので 0 を返す** ──
        /// 呼び出し側は ChargeOpening / ChargePerTurn を使うこと。</summary>
        public static int ValueOf(RelicAxis axis, int step)
        {
            var t = Table(axis);
            return t != null ? t[Idx(step)] : 0;
        }

        public static int ChargeOpening(int step) => ChargeOpen[Idx(step)];
        public static int ChargePerTurn(int step) => ChargeTurn[Idx(step)];
        /// <summary>背水の吸収率(%)。 主値 (被ダメージ−N%) と対になる第 2 の値なので個別に引く。</summary>
        public static int LastBreathLifesteal(int step) => LastBreathLs[Idx(step)];

        public static string NameOf(RelicAxis axis)
        {
            int a = (int)axis;
            return (a >= 0 && a < Names.Length) ? Names[a] : "?";
        }

        /// <summary>総点 → 名前の帯。 低(5-9)=0 / 中(10-14)=1 / 高(15-29)=2。</summary>
        public static int TierOf(int totalPoints) => totalPoints <= 9 ? 0 : (totalPoints <= 14 ? 1 : 2);

        /// <summary>遺物名。 メイン軸と総点で決まる。 刻印付きは接頭辞「刻印の」。</summary>
        public static string RelicNameOf(RelicAxis mainAxis, int totalPoints, bool cursed)
        {
            int a = (int)mainAxis;
            string n = (a >= 0 && a < RelicNames.Length)
                     ? RelicNames[a][UnityEngine.Mathf.Clamp(TierOf(totalPoints), 0, 2)]
                     : "?";
            return cursed ? "刻印の" + n : n;
        }

        /// <summary>「被ダメージ−15%」のような効果文。 符号の向きと単位は軸ごとに違う。</summary>
        public static string Describe(RelicAxis axis, int step)
        {
            switch (axis)
            {
                case RelicAxis.Charge:
                {
                    // **区切りに " / " を使わない** ── 枠の区切りと同じ記号だと、
                    // 複合効果 1 枠が 2 枠に見える。 内部は中黒で繋ぐ。
                    int o = ChargeOpening(step), t = ChargePerTurn(step);
                    return t > 0 ? $"開幕充電+{o}・毎ターン+{t}" : $"開幕充電+{o}";
                }
                case RelicAxis.Rinkai:
                    return $"臨界爆発時 敵の現在HP の {ValueOf(axis, step)}%";

                // ── 高難易度限定軸。 条件が効果の半分なので、 効果文に条件を必ず書く ──
                case RelicAxis.LambdaResonance:
                    return $"Λ層の戦闘 1 回ごとに 会心倍率+{ValueOf(axis, step)}%（累積）";
                case RelicAxis.HopeBurn:
                    return $"希望 {HopeBurnThreshold} 以下の間 与ダメージ+{ValueOf(axis, step)}%"
                         + $"（開幕希望 {HopeBurnStartHope}）";
                case RelicAxis.LongBattle:
                    return $"経過ターン 1 つにつき 与ダメージ+{ValueOf(axis, step)}%";
                case RelicAxis.LastBreath:
                    return $"HP が最大の {(int)(LastBreathThreshold * 100)}% 以下の間 "
                         + $"被ダメージ−{ValueOf(axis, step)}%・与ダメージの{LastBreathLifesteal(step)}%を回復";

                case RelicAxis.DamageReductionPct:
                    return $"被ダメージ−{ValueOf(axis, step)}%";
                case RelicAxis.DamagePct:
                case RelicAxis.CritRatePct:
                case RelicAxis.CritMultPct:
                case RelicAxis.ArmorPenPct:
                    return $"{NameOf(axis)}+{ValueOf(axis, step)}%";
                default:
                    return $"{NameOf(axis)}+{ValueOf(axis, step)}";
            }
        }

        public static string DescribeCurse(RelicCurse c)
        {
            switch (c)
            {
                case RelicCurse.NoConsumables: return "消耗品を 1 つも持っていない間";
                case RelicCurse.LowGold:       return "ゴールドが 10 以下の間";
                case RelicCurse.NoBlockWiring: return "そのターン、ブロック端子に 1 本も配線していない";
                case RelicCurse.NoRoleFired:   return "そのターン、役を 1 つも切っていない";
                default:                       return "—";
            }
        }
    }
}
