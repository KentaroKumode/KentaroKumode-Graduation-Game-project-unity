using System.Collections.Generic;
using CombatSystem;

namespace GameLoop
{
    /// <summary>タイトル付きの戦闘プリセット。 1 マス = 1 プリセットで、 マップ上には
    /// <see cref="title"/> が表示される (開示済みのときだけ)。
    ///
    /// <para><b>なぜプリセットか。</b> 従来は戦闘マスを踏んだ瞬間に
    /// <c>FloorManager.PickEnemy(floor)</c> が抽選していたので、 <b>踏むまでノードの中身が存在しなかった</b>。
    /// 名前を 1 マス手前で見せるには、 中身が先に決まっている必要がある。</para>
    ///
    /// <para><b>抽選は必ずノード添字を明示して引く</b> (<see cref="FloorManager.NodeRngIndex"/>)。
    /// キー内カウンタ (<c>index = -1</c>) で引くと、 <b>訪れなかったノードの抽選まで
    /// 乱数列を進めてしまい</b>、 同一シードのペアが割れる。 GameRng はキー別かつ
    /// index 指定で位置非依存なので、 添字さえ渡せば未訪問ノードは列を消費しない。</para></summary>
    public class EncounterPreset
    {
        /// <summary>安定 ID。 学習・集計のキーになるので一度公開したら変えない。</summary>
        public string id;

        /// <summary>マップに出る戦闘名。 null なら「引いた敵の名前」をそのまま出す。</summary>
        public string title;

        /// <summary>1 体目。 null = その層の通常抽選 (従来動作)。</summary>
        public string enemyA;

        /// <summary>2 体目。 null = 単体戦。 <b>enemyA と同じ ID を入れない</b> ──
        /// 攻撃ロールの乱数キーが <c>"enemy.atkRoll." + id</c> なので、
        /// 同 ID だと 2 体が毎ターン完全に同じ目を出す。</summary>
        public string enemyB;

        /// <summary>抽選の重み。 通常抽選 (<see cref="EncounterPresets.Generic"/>) が 100。</summary>
        public int weight = 5;

        public int minFloor = 1;
        public int maxFloor = 7;

        /// <summary>エリートマス専用か。 true なら通常戦闘マスには出ない。</summary>
        public bool eliteOnly;

        /// <summary>2 体戦か。</summary>
        public bool IsPair => !string.IsNullOrEmpty(enemyB);

        /// <summary>報酬倍率。 2 体戦は 2 倍 (被ダメも 2 パケットぶん来るため)。</summary>
        public float RewardMultiplier => IsPair ? 2f : 1f;

        public bool AppliesTo(int floor, bool elite)
        {
            if (floor < minFloor || floor > maxFloor) return false;
            if (eliteOnly && !elite) return false;
            return true;
        }
    }

    /// <summary>戦闘プリセットの登録簿。
    ///
    /// <para><b>2 体戦の規則</b> (2026-08-28 決定):</para>
    /// <list type="bullet">
    ///   <item>プレイヤーの与ダメージは<b>両方に全額</b>入る (標的選択は無い)</item>
    ///   <item>敵の攻撃は<b>2 パケット</b>。 ブロックは<b>各パケットから全額引く</b> (倍率なし)</item>
    ///   <item>報酬 2 倍</item>
    /// </list>
    ///
    /// <para><b>与ダメが両方に入るので戦闘長は伸びない</b> (撃破ターンは和ではなく max)。
    /// つまりエスカレーションは 1 体戦と同じ速さでしか進まず、 増えるのは毎ターンの被ダメだけ。
    /// HP を「時間」ではなく「壁」に変えないための構造で、 標的分割案を落とした理由でもある
    /// (集中砲火だと戦闘長が 2 倍になり、 エスカレーションと二重に効いて被ダメが 3 倍を超える)。</para>
    ///
    /// <para><b>パッシブは両方とも効く。 ただし同名は 1 回だけ。</b>
    /// 敵側の状態は単一の CombatContext に平置き (<c>enemyBleedStacks</c> / <c>enemyShield</c> /
    /// <c>accumulatedValues</c> の <c>sg_</c>・<c>ashen_</c>・<c>berserk_</c> 等) なので、
    /// 同名を 2 回登録すると同じキーへ二重に積んで<b>静かに 2 倍の効果になる</b>。
    /// <b>だからペアは効果の重ならない 2 体で組む</b> ── 同じパッシブを持つ 2 体を並べても、
    /// 濃くならず「片方が数値だけの同伴者」になるだけで、 組み合わせの意味が消える。</para>
    ///
    /// <para><b>Λ 環状線は 2 体戦から除外する</b> (<c>FloorManager.EnsureEncounter</c>)。
    /// Λ は 6 層への強制通過点で「このノードを避ける」選択肢が無く、
    /// 選べない場所に置いた賭けは賭けではなく固定の税になるため。</para></summary>
    public static class EncounterPresets
    {
        /// <summary>従来動作 (その層の通常抽選・単体・タイトルなし)。</summary>
        public static readonly EncounterPreset Generic = new EncounterPreset
        {
            id = "enc_generic", title = null, enemyA = null, enemyB = null,
            weight = 100, minFloor = 1, maxFloor = 7,
        };

        /// <summary>登録簿。 <b>ペアの 2 体は必ず別 ID</b>。</summary>
        public static readonly List<EncounterPreset> All = new List<EncounterPreset>
        {
            Generic,

            // ── 通常マスに混ざる 2 体戦 ──
            new EncounterPreset { id = "enc_burrow_leftovers", title = "巣穴の残り物",
                enemyA = "slime",       enemyB = "goblin",      minFloor = 1, maxFloor = 2, weight = 6 },
            new EncounterPreset { id = "enc_shaft_duty",       title = "坑道の当番",
                enemyA = "kobold",      enemyB = "skeleton",    minFloor = 2, maxFloor = 3, weight = 6 },
            new EncounterPreset { id = "enc_houndkeeper",      title = "猟犬づれ",
                enemyA = "wolf",        enemyB = "kobold",      minFloor = 2, maxFloor = 3, weight = 6 },
            new EncounterPreset { id = "enc_roll_call_gap",    title = "点呼の欠員",
                enemyA = "skeleton",    enemyB = "13th_death",  minFloor = 3, maxFloor = 4, weight = 5 },
            new EncounterPreset { id = "enc_double_watch",     title = "見張り塔の二重哨戒",
                enemyA = "orc",         enemyB = "harpy",       minFloor = 3, maxFloor = 4, weight = 5 },
            new EncounterPreset { id = "enc_bridge_and_tithe", title = "橋番と徴税人",
                enemyA = "lizardman",   enemyB = "dark_knight", minFloor = 4, maxFloor = 5, weight = 5 },
            new EncounterPreset { id = "enc_failed_castings",  title = "檻の失敗作",
                enemyA = "minotaur",    enemyB = "chimera",     minFloor = 4, maxFloor = 5, weight = 4 },
            new EncounterPreset { id = "enc_lamplit_bargain",  title = "灯火の下の取引",
                enemyA = "demon",       enemyB = "dark_knight", minFloor = 4, maxFloor = 5, weight = 4 },
            new EncounterPreset { id = "enc_mourners",         title = "喪の同席者",
                enemyA = "wraith",      enemyB = "elder_vampire", minFloor = 5, maxFloor = 6, weight = 4 },
            // 落日の騎士 ＋ 黎明騎士 (「同じ紋章の二人」) は**組ませない**。
            //   黎明騎士が Undying/CounterStance/IntimidatePlus/HoningDuel を持つのに対し、
            //   落日の騎士が持ち込む新規は DeathSentence だけ ＝ 同名弾きの後に残るのが 1 個で、
            //   実質「黎明騎士 ＋ HP バー」になる。 **ペアは効果の重ならない 2 体で組む。**
            new EncounterPreset { id = "enc_witness",          title = "執行の立会人",
                enemyA = "death_knight", enemyB = "chimera",   minFloor = 5, maxFloor = 6, weight = 4 },

            // ── エリート専用。 2 体ともエリート化されるので全ゲーム最大級の利得と危険 ──
            new EncounterPreset { id = "enc_two_failures",     title = "二枚の失敗作",
                enemyA = "minotaur",    enemyB = "demon",       minFloor = 4, maxFloor = 5,
                weight = 12, eliteOnly = true },
            new EncounterPreset { id = "enc_last_relief",      title = "最後の守衛交代",
                enemyA = "golem",       enemyB = "death_knight", minFloor = 5, maxFloor = 7,
                weight = 12, eliteOnly = true },
            new EncounterPreset { id = "enc_long_night",       title = "長い夜の主従",
                enemyA = "elder_vampire", enemyB = "wraith",    minFloor = 5, maxFloor = 7,
                weight = 12, eliteOnly = true },
        };

        /// <summary>単体戦とボス戦のタイトル。 <b>敵 ID → 戦闘名</b>の対応表。
        ///
        /// <para><b>なぜプリセットを増やさず対応表にするか。</b> 単体戦は「その層から敵を 1 体引く」
        /// 機構がすでにあり、 タイトルを別に抽選する意味がない。 ボスは層で確定するので抽選自体が無い。
        /// プリセット (= 抽選の単位) を増やすのは<b>組み合わせが名前の中身になるペアだけ</b>でよい。</para>
        ///
        /// <para><b>単体にも必ず名前を付ける。</b> ペアだけが名前を持つと
        /// 「名前がある = 2 体戦」という 1 ビットの答えになり、
        /// 「この名前は危険だった」と覚える必要が消える ── 開示の仕組みごと無意味になる。</para></summary>
        public static readonly Dictionary<string, string> SoloTitles = new Dictionary<string, string>
        {
            // ── 1 層 ──
            { "slime",          "湿った段差" },
            { "goblin",         "先客" },
            // ── 2 層 ──
            { "kobold",         "仕掛けの跡" },        // Trapper
            { "skeleton",       "片付いていない列" },  // Undying
            { "wolf",           "距離の詰め方" },      // Sprint
            { "13th_death",     "十三番の札" },        // Decree13th
            // ── 3 層 ──
            { "orc",            "押し通る者" },        // BruteForce
            { "harpy",          "梁の上の影" },        // Flight
            // ── 4 層 ──
            { "lizardman",      "鱗と尾の作法" },      // HardScales / TailStrike
            { "minotaur",       "角の届く範囲" },      // Rampage / BruteForce
            { "dark_knight",    "立ち合いの申し出" },  // HoningDuel / CounterStance
            { "chimera",        "首の数え違い" },      // MultiHead / Regeneration
            { "demon",          "熱の出どころ" },      // DemonAura / Hellfire
            // ── 5 層 ──
            { "wraith",         "名指しされる" },      // Curse / Ethereal
            { "golem",          "動かない方の門" },    // Immovable / HardScales
            { "elder_vampire",  "返ってこない血" },    // Lifesteal / NightLord
            { "death_knight",   "刑の告知" },          // DeathSentence

            // ── ボス ──
            //   **層で確定するので抽選には乗らない。** 表示のためだけの対応。
            { "boss_layer1",    "持ち逃げ" },
            { "boss_layer2",    "王の威信" },          // 3 層プール: ゴブリン王
            { "boss_layer3",    "絡みつく死" },        // 3 層プール: 毒沼の主
            { "boss_layer4",    "こちら側とあちら側" },// 3 層プール: 鏡の双子
            { "boss_layer5",    "執行" },
            { "boss_layer6",    "玉座" },
            { "boss_layer7",    "学者" },              // p2〜p4 は同じ戦闘なので段別の名は持たせない

            // ── 特殊エンカウント (floor 99・通常抽選に乗らない) ──
            { "shady_merchant", "値下げ交渉の続き" },
            { "false_merchant", "釣り銭" },
        };

        /// <summary><b>5 層裏ボスは意図的に載せない。</b> 出現条件で分岐する相手なので、
        /// マスの名前に出すと<b>分岐が解決する前に正体が漏れる</b>。
        /// ボスマスは常に層の通常ボス名を出し、 裏ボスは踏んでから明かす。</summary>
        public const string HiddenBossExcluded = GameLoop.BossIds.Layer5Hidden;

        /// <summary>単体戦・ボス戦のタイトル。 未登録なら null (呼び出し側が敵名へ落とす)。</summary>
        public static string SoloTitle(string enemyId)
        {
            if (string.IsNullOrEmpty(enemyId)) return null;
            return SoloTitles.TryGetValue(enemyId, out var t) ? t : null;
        }

        private static readonly Dictionary<string, EncounterPreset> _byId
            = new Dictionary<string, EncounterPreset>();

        static EncounterPresets()
        {
            foreach (var p in All) if (p != null && !string.IsNullOrEmpty(p.id)) _byId[p.id] = p;
        }

        public static EncounterPreset Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return _byId.TryGetValue(id, out var p) ? p : null;
        }

        /// <summary>指定ノード用のプリセットを決める。 <b>ノード添字で引くので順序非依存</b> ──
        /// 同じノードなら、 いつ・何回呼んでも同じ結果になる。</summary>
        public static EncounterPreset PickFor(int floor, bool elite, int nodeIdx)
        {
            int total = 0;
            for (int i = 0; i < All.Count; i++)
                if (All[i].AppliesTo(floor, elite)) total += System.Math.Max(0, All[i].weight);
            if (total <= 0) return Generic;

            int roll = GameRng.Range(0, total, "encounter.preset", nodeIdx);
            for (int i = 0; i < All.Count; i++)
            {
                var p = All[i];
                if (!p.AppliesTo(floor, elite)) continue;
                roll -= System.Math.Max(0, p.weight);
                if (roll < 0) return p;
            }
            return Generic;
        }

        /// <summary>プリセットが 2 体目に使う敵の実体。 未登録 ID なら null (単体戦へ縮退)。</summary>
        public static EnemyData ResolveSecondary(EncounterPreset p)
        {
            if (p == null || !p.IsPair) return null;
            var e = EnemyDatabase.Get(p.enemyB);
            return e != null ? e.Clone() : null;
        }
    }
}
