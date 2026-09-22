namespace InventorySystem
{
    /// <summary>
    /// 複数の品が共有するパッシブ (items.json 最上位の <c>skills</c> 表・2026-09-19)。
    /// 武器のラダー (〈間合III〉を T4 武器 4 本が持つ 等) だけがここに入る。 武器は <c>skills</c> に id を並べて参照する。
    /// </summary>
    [System.Serializable]
    public class SkillDefJson
    {
        public string id;
        public string name;
        public string description;
        /// <summary>ステータスとして表せる効果。 これを持つスキルは専用クラスを持たず、
        /// <see cref="PassiveSkills.Effects.StatModifierEffect"/> が 1 件ずつ適用する。</summary>
        public StatJson[] stats;
    }

    /// <summary>ステータス 1 件。 <c>stat</c> は <see cref="PassiveSkills.StatKeys"/> のいずれか。
    /// <c>value</c> は画面に出す単位のまま (攻撃 +5 → 5 / 与ダメ +10% → 10 / 被ダメ −3 → −3)。</summary>
    [System.Serializable]
    public class StatJson
    {
        public string stat;
        public float value;
        /// <summary>このステータスが乗る条件 (2026-09-19)。 書いた項目はすべて AND。
        /// 省略すると JsonUtility が既定値 (＝条件なし) で埋める。</summary>
        public StatWhen when;
    }

    /// <summary>ステータスの発動条件。 <b>既定値 (0 / false) は「その条件を使わない」</b>。
    /// 判定の実装は <see cref="PassiveSkills.Effects.StatModifierEffect"/>。</summary>
    [System.Serializable]
    public class StatWhen
    {
        public bool solo;             // 単体戦 (2 体戦でない)
        public int  turnMax;          // 戦闘の N ターン目まで
        public bool firstRoll;        // 各戦闘の初回ロール
        public int  hpPctMax;         // 自HP が最大の N% 以下
        public int  hpPctAbove;       // 自HP が最大の N% 超 (段階を排他にする下限)
        public int  enemyHpPctMax;    // 敵HP が最大の N% 以下
        public bool behind;           // 自HP 割合が敵HP 割合より低い
        public bool hitLastTurn;      // 前のターンに被弾した
        public bool enemyPoisoned;    // 敵が毒状態
        public int  enemyBleedMin;    // 敵の出血が N 以上
        public bool overcharged;      // 過充電中
        public bool allEven;          // 出目が全て偶数
        public bool kaleido;          // 出目が 全同値・全て異なる・階段 のいずれか
        public int  weaponPlusMin;    // 武器強化が N 段以上
        public int  nonCritStreakMin; // N 回連続で非会心 (会心でリセット)
        public bool hit;              // この攻撃が当たる (与ダメが 0 より大きい)
        public bool strongFoe;        // エリート戦・ボス戦
        public string hopeTierMin;    // 希望が この段階以下 (HopeTier 名: "Pessimism" / "Despair" …)
        public string hopeTierBelow;  // 希望が この段階より上 (段階を排他にする上限)

        public bool IsEmpty =>
            !solo && turnMax == 0 && !firstRoll && hpPctMax == 0 && hpPctAbove == 0 && enemyHpPctMax == 0
            && !behind && !hitLastTurn && !enemyPoisoned && enemyBleedMin == 0 && !overcharged
            && !allEven && !kaleido && weaponPlusMin == 0 && nonCritStreakMin == 0 && !hit
            && !strongFoe && string.IsNullOrEmpty(hopeTierMin) && string.IsNullOrEmpty(hopeTierBelow);
    }

    /// <summary>
    /// JSONからのデシリアライズ用データ構造
    /// JsonUtility.FromJson で使用
    /// </summary>
    [System.Serializable]
    public class ItemDataJson
    {
        public string id;
        public string name;          // JSONキー "name" に対応
        public string description;
        public string flavorText;
        public string category;
        public string rarity;
        public string roleName;      // ロール名（タンク/ナイト/バーサーカー/アサシン）
        public string roleDescription; // ロール説明
        public int diceCount;        // ダイス数（Weaponのみ）
        public int diceMax;          // ダイス最大出目（Weaponのみ）
        public float critRatePct;    // 武器の会心率 (%)。 11 = 11%（Weaponのみ）
        public int attackPower;      // 武器の素火力（#2 案A'：勝利base = attackPower + floor(|差|/3)。Weaponのみ）
        public int basePrice;        // 設定中央価格（購入/売却額はシステムが±25%で算出）
        public int[] diceFaces;      // ダイスアイテムの面配列
        /// <summary>ADR-0010〈無銘の賽〉: このダイスでは**端子役が成立しない**。
        /// 手札役・配線役は通常どおり成立する。
        ///
        /// 「役を捨てて火力」という archetype は**面の分布では作れない**ため機構で作っている ──
        /// 対を半分に落とすには 16 面が要り、 相異なる正整数 N 個の平均は必ず (N+1)/2 以上なので、
        /// 役が薄いダイスは構造上かならず高火力＝純粋な上位互換になってしまう。</summary>
        public bool suppressTerminalRoles;

        /// <summary><b>パッシブ品のパッシブ名</b> (2026-09-19)。 品そのものが 1 つのパッシブで、
        /// パッシブ id は品の <c>id</c>、 説明文は品の <c>description</c> を使う。 空ならパッシブを持たない。</summary>
        public string skill;
        /// <summary><see cref="skill"/> のステータス。</summary>
        public StatJson[] stats;
        /// <summary><b>武器が持つ共有パッシブの id</b> (最上位 <c>skills</c> 表を参照)。 並び順 = 発火登録の順。</summary>
        public string[] skills;

        // ============================================================
        //  構造フィールド (2026-09-22)
        //
        //  **id からパースしていた構造を data 側へ出したもの。** 以前は
        //  `sword_t2` / `cons_heal_1` / `uniq_*` という綴りにロジックが依存しており、
        //  12 ファイル 20 箇所が id を切り刻んでいた。 id を表示名へ統一するにあたり、
        //  「id が何であっても壊れない」状態を先に作るための明示フィールド。
        //  **id からの再パースを復活させないこと** ── 復活させた瞬間に
        //  表示名の変更 (調整で普通に起きる) がロジックを黙って壊す。
        // ============================================================

        /// <summary>進行武器の家系。 sword / axe / dagger / shield。 武器以外は空。</summary>
        public string family;
        /// <summary>段。 武器は 2〜4、 消費アイテムは 1〜4。 持たない品は 0。</summary>
        public int tier;
        /// <summary>消費アイテムの系統。 heal / def / dmg / hope。 それ以外は空。</summary>
        public string consFamily;
        /// <summary>ユニーク品 (旧 <c>uniq_</c> 接頭辞)。 <b>昇華の対象外</b>
        /// (<see cref="GameLoop.SublimationSystem.CanSublimate"/>)。</summary>
        public bool unique;
    }
    
    /// <summary>
    /// JSON配列のルートオブジェクト
    /// </summary>
    [System.Serializable]
    public class ItemDataListJson
    {
        public SkillDefJson[] skills;
        public ItemDataJson[] items;
    }
}
