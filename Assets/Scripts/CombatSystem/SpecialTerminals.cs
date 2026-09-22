using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>特殊端子の識別子。 **並び順を変えない** (RunState に文字列で持つが、
    /// ショップ抽選の決定性が並び順に依存するため)。 正本の効果表は docs/GAME.md §6-5。</summary>
    public enum SpecialTerminalKind
    {
        None = 0,
        HeavyStrike,   // 重攻撃: 接続ダイスの出目を 1.5 倍して攻撃へ加算
        FullGuard,     // 完全防御: 接続ダイスの防御値を 2 倍
        Bleed,         // 出血: 接続合計値分の出血を付与し、 接続本数分だけ即時発動
        Heal,          // 治癒: 接続合計値分 HP 回復
        Deathwish,     // 決死: 接続合計値分の自傷 + 同数を攻撃へ + 不足HPの5%を攻撃へ
        VitalPoint,    // 急所: 接続ダイスがそのロールの最小値のときだけ 軽減不可ダメ 出目×2
        Aim,           // 照準: 接続出目 ÷2 (切上) を会心率へ加算 (そのターンのみ)
        Battery,       // 蓄電池: 充電獲得が 1 ダイスあたり +2
        Coolant,       // 冷却材: 接続合計値を蓄積、 閾値到達でエスカレーションを 1 ターン遅延
        Foundry,       // 鋳造口: 接続合計値を蓄積、 N 到達ごとにこの戦闘中の出目 +1
        Riposte,       // 反攻: このターン受けた最終ダメージのうち接続合計値までを敵へ反射
    }

    /// <summary>1 種の定義。 <see cref="ConnectLimit"/> が中核 ──
    /// **パワー上限と枠圧を一語で制御する**ので、 効果量ではなくここでレアリティ差を作る。</summary>
    public sealed class SpecialTerminalDef
    {
        public SpecialTerminalKind kind;
        public string id;
        public string displayName;
        /// <summary>接続制限 N。 この端子へ同時に配線できるダイス数の上限 (§6-5)。</summary>
        public int connectLimit;
        /// <summary>ショップ価格 (暫定・スイープ対象)。</summary>
        public int price;
        public string description;
    }

    /// <summary>
    /// ADR-0009 §6-5 の特殊端子 11 種。 **ショップで買った 1 つだけを装着**する
    /// (`RunState.equippedSpecialTerminalId`)。 未購入なら第 4 端子は存在しない。
    ///
    /// **接続制限 N がこのシステムの安全装置。** 効果量ではなく「何本挿せるか」で
    /// 強さを縛るので、 治癒のような一択化しやすい効果でも上限で自然に殺せる (§6-5)。
    ///
    /// 数値は全て暫定で AutoRunner のスイープ対象 (§23-7)。
    /// </summary>
    public static class SpecialTerminals
    {
        /// <summary>制限Ⅰ / Ⅱ / Ⅲ の実数値。 レアリティと連動 (Ⅰ=B〜S / Ⅱ=G / Ⅲ=L 目安)。</summary>
        public const int LimitI = 1;
        public const int LimitII = 2;
        public const int LimitIII = 3;

        /// <summary>〈重攻撃〉の倍率。</summary>
        public const float HeavyStrikeMultiplier = 1.5f;
        /// <summary>〈完全防御〉の倍率。</summary>
        public const int FullGuardMultiplier = 2;
        /// <summary>〈決死〉が加算する「不足 HP」の割合。</summary>
        public const float DeathwishMissingHpPct = 0.05f;
        /// <summary>〈急所〉の出目倍率 (軽減不可の固定ダメージ枠へ入る)。</summary>
        public const int VitalPointMultiplier = 2;
        /// <summary>〈蓄電池〉の 1 ダイスあたり上乗せ充電。</summary>
        public const int BatteryPerDice = 2;
        /// <summary>〈冷却材〉がエスカレーションを 1 ターン遅らせるのに要る蓄積量。</summary>
        public const int CoolantThreshold = 24;
        /// <summary>〈鋳造口〉が出目 +1 を配るのに要る蓄積量。</summary>
        public const int FoundryThreshold = 20;

        private static SpecialTerminalDef D(SpecialTerminalKind k, string id, string name,
                                            int limit, int price, string desc)
            => new SpecialTerminalDef
            {
                kind = k, id = id, displayName = name,
                connectLimit = limit, price = price, description = desc,
            };

        public static readonly List<SpecialTerminalDef> All = new List<SpecialTerminalDef>
        {
            D(SpecialTerminalKind.HeavyStrike, "term_heavy",   "重攻撃",   LimitI,   14,
              "接続したダイスの出目を 1.5 倍して攻撃へ加算する。"),
            D(SpecialTerminalKind.FullGuard,   "term_guard",   "完全防御", LimitI,   14,
              "接続したダイスの出目を 2 倍してブロックへ加算する。"),
            D(SpecialTerminalKind.Bleed,       "term_bleed",   "出血",     LimitII,  16,
              "接続合計値ぶんの出血を敵へ付与し、 接続した本数ぶんだけ即座に発動させる。"),
            D(SpecialTerminalKind.Heal,        "term_heal",    "治癒",     LimitI,   16,
              "接続合計値ぶん HP を回復する。"),
            D(SpecialTerminalKind.Deathwish,   "term_death",   "決死",     LimitIII, 20,
              "接続合計値ぶん自傷し、 同数と「失った HP の 5%」を攻撃へ加算する。"),
            D(SpecialTerminalKind.VitalPoint,  "term_vital",   "急所",     LimitI,   16,
              "接続したダイスがそのロールの最小値のときだけ、 出目×2 の軽減不可ダメージ。"),
            D(SpecialTerminalKind.Aim,         "term_aim",     "照準",     LimitI,   14,
              "接続出目の半分 (切り上げ) を、 そのターンの会心率へ加算する。"),
            D(SpecialTerminalKind.Battery,     "term_battery", "工廠の材料箱",   LimitII,  12,
              "この端子の充電獲得が 1 ダイスあたり +2 される (出目 1 → 3)。"),
            D(SpecialTerminalKind.Coolant,     "term_coolant", "冷却材",   LimitIII, 18,
              "接続合計値を蓄積し、 閾値に達するごとにエスカレーションを 1 ターン遅らせる。"),
            D(SpecialTerminalKind.Foundry,     "term_foundry", "鋳造口",   LimitI,   18,
              "接続合計値を蓄積し、 閾値に達するごとにこの戦闘中の出目を +1 する。"),
            D(SpecialTerminalKind.Riposte,     "term_riposte", "反攻",     LimitII,  16,
              "このターン受けた最終ダメージのうち、 接続合計値までを敵へ反射する。"),
        };

        public static SpecialTerminalDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < All.Count; i++) if (All[i].id == id) return All[i];
            return null;
        }

        public static SpecialTerminalDef Get(SpecialTerminalKind kind)
        {
            for (int i = 0; i < All.Count; i++) if (All[i].kind == kind) return All[i];
            return null;
        }

        /// <summary>装着中の定義。 未購入なら null ＝ **第 4 端子そのものが存在しない**。</summary>
        public static SpecialTerminalDef Equipped(GameLoop.RunState run)
            => run == null ? null : Get(run.equippedSpecialTerminalId);

        /// <summary>装着中の接続制限。 未装着は 0 (＝1 本も挿せない)。</summary>
        public static int EquippedLimit(GameLoop.RunState run)
        {
            var d = Equipped(run);
            return d == null ? 0 : d.connectLimit;
        }
    }
}
