using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>
    /// ボス別 Bot 戦術プレイブック (2026-07-16 追加)。
    /// 目的: AutoRunner の MutualWiringPolicy が「相手を見て」配線を変えられるようにする。
    /// レイヤ 1 (露出): 予兆T/反射/大玉無効 等のフラグを列挙。
    /// レイヤ 2 (プレイブック): ボス ID → 端子重みバイアス + 条件付きブースト。
    ///
    /// 使い方:
    ///   var play = BossPlaybook.Get(bossId);
    ///   float wAtk = baseAtk * play.atkBias;
    ///   if (play.IsOmenTurn(turn)) wAtk *= 0.6f, wDef *= 1.8f;  // 予兆Tは防御シフト
    ///
    /// 未登録ボス = Default (バイアス 1.0)。
    /// </summary>
    public struct BossPlaybook
    {
        /// <summary>攻撃端子重みの倍率 (1.0 = 変化なし)。</summary>
        public float atkBias;
        /// <summary>ブロック端子重みの倍率。</summary>
        public float defBias;
        /// <summary>充電端子重みの倍率。</summary>
        public float chgBias;

        /// <summary>予兆T のクロージャ (turn, bossHpRatio → is-omen)。null = 予兆なし。
        /// hpRatio は Phase 依存の周期切替 (6F 灰燼の王: >0.5=4T / ≤0.5=2T) を Bot に見せるため。</summary>
        public System.Func<int, float, bool> omenPredicate;
        /// <summary>予兆T の追加 defBias 倍率 (例: 1.8f で通常の 1.8 倍のブロック重み)。</summary>
        public float omenDefBoost;
        /// <summary>予兆T の追加 atkBias 倍率 (例: 0.6f で通常の 60%)。</summary>
        public float omenAtkDamp;

        /// <summary>大玉が無効化されるボス (6F 灰塵の外殻, 5F ChipCap)。
        /// true なら wiring 探索時に「攻撃端子 1個あたりの期待値」を減衰させ、複数小玉を優遇。</summary>
        public bool damageCapped;
        /// <summary>被ダメがキャップされる閾値 (damageCapped=true の時のみ意味を持つ・6F=15/5F=ChipCap)。</summary>
        public int damageCapThreshold;

        /// <summary>反射系: 与ダメがそのまま返る (4F 鏡双子, 7F-p3)。攻撃を控えめに。</summary>
        public bool reflectsAttack;
        /// <summary>反射率 (0.5f 等)。reflectsAttack=true の時のみ。</summary>
        public float reflectRatio;

        /// <summary>DoT 効かない (再生系ボス)。DoT ビルドが判断材料にする。</summary>
        public bool immuneToDot;

        /// <summary>予兆判定 (null 安全)。hpRatio=1 は Phase1 相当。</summary>
        public bool IsOmenTurn(int turn, float hpRatio = 1f)
            => omenPredicate != null && omenPredicate(turn, hpRatio);

        public static BossPlaybook Default => new BossPlaybook
        {
            atkBias = 1.0f,
            defBias = 1.0f,
            chgBias = 1.0f,
            omenDefBoost = 1.0f,
            omenAtkDamp = 1.0f,
            reflectRatio = 0.0f,
        };

        // ================================================================
        //  ボス別プレイブック定義 (Get で切り替え)
        // ================================================================
        private static readonly Dictionary<string, BossPlaybook> _book = new Dictionary<string, BossPlaybook>
        {
            // 1〜3層: 特殊対策不要 (Default で押し切り)
            // 4層: 鏡双子 — 反射に注意 (火力控えめ)
            ["boss_layer4"] = new BossPlaybook
            {
                atkBias = 0.85f, defBias = 1.15f, chgBias = 1.0f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
                reflectsAttack = true, reflectRatio = 0.5f,
            },
            // 5層: 審判官 — ChipCap で大玉が丸められる。 小玉連打有利
            ["boss_layer5"] = new BossPlaybook
            {
                atkBias = 1.0f, defBias = 1.1f, chgBias = 1.0f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
                damageCapped = true, damageCapThreshold = 10,
            },
            // 5層裏: シュヴァリエ — 特殊 (レイピア切替は別処理)
            ["boss_layer5_hidden"] = new BossPlaybook
            {
                atkBias = 1.1f, defBias = 1.0f, chgBias = 1.0f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
            },
            // 6層 v2 二相型 (2026-07-16): 灰燼の王
            //   Phase1 (HP>50%): 周期4T 予兆・回復8%/T (削り合い抑制)
            //   Phase2 (HP≤50%): 周期2T 予兆・回復停止・敵ダイス+5常時 (瞬発力勝負)
            //   → Phase1 は削り控えめ + Phase2 前の温存、 Phase2 は火力全開で押し切り
            ["boss_layer6"] = new BossPlaybook
            {
                atkBias = 1.0f, defBias = 1.05f, chgBias = 1.0f,
                omenPredicate = (t, hp) =>
                {
                    if (t < 1) return false;
                    int p = hp <= 0.5f ? 3 : 4; // Phase2 で周期3Tに加速 (2T→3Tに緩和)
                    return ((t - 1) % p) == 0;
                },
                omenDefBoost = 1.8f, omenAtkDamp = 0.7f,
                damageCapped = true, damageCapThreshold = 15,
                immuneToDot = false,
            },
            // !! 要再較正 (2026-08-24): 以下 4 段のバイアスは **廃止済みの覚者連戦**
            //    (逆観/業火残響/鏡映/一閃返し) に合わせて決めた値で、 現在の 7 層ボスである
            //    ヴェスカ (連続実験/遺物学者/停滞する時間/解析演算/裂け目/回帰する真理/天与の才) の
            //    機構とは対応していない。 特に p3 の reflectsAttack はヴェスカに存在しない。
            //    数値を動かすのはバランス変更なので、 承認と測定を経てから行う (docs/GAME.md §23-9)。
            // 7層 p1
            ["boss_layer7"] = new BossPlaybook
            {
                atkBias = 0.9f, defBias = 1.15f, chgBias = 1.3f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
            },
            // 7層 p2
            ["boss_layer7_p2"] = new BossPlaybook
            {
                atkBias = 0.9f, defBias = 1.4f, chgBias = 1.0f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
            },
            // 7層 p3
            ["boss_layer7_p3"] = new BossPlaybook
            {
                atkBias = 0.7f, defBias = 1.2f, chgBias = 1.1f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
                reflectsAttack = true, reflectRatio = 1.0f,
            },
            // 7層 p4
            ["boss_layer7_p4"] = new BossPlaybook
            {
                atkBias = 0.85f, defBias = 1.15f, chgBias = 1.0f,
                omenDefBoost = 1.0f, omenAtkDamp = 1.0f,
                damageCapped = true, damageCapThreshold = 12,
            },
        };

        /// <summary>ボス ID → プレイブック。 未登録は Default。</summary>
        public static BossPlaybook Get(string bossId)
        {
            if (string.IsNullOrEmpty(bossId)) return Default;
            return _book.TryGetValue(bossId, out var pb) ? pb : Default;
        }
    }
}
