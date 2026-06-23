using System.Collections.Generic;

namespace MapSystem.AbyssPhenomena
{
    /// <summary>15 件の異常現象定義 (正本: docs/specs/abyss-phenomena.md)。</summary>
    public static class AbyssPhenomenonDatabase
    {
        private static readonly Dictionary<AbyssPhenomenon, AbyssPhenomenonDef> defs =
            new Dictionary<AbyssPhenomenon, AbyssPhenomenonDef>
        {
            { AbyssPhenomenon.ReverseFalls, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.ReverseFalls, kind = AbyssPhenomenonKind.Buff,
                displayName = "逆行する滝",
                description = "層内マップで、 1 度だけ前ノードへ戻れる。",
            } },
            { AbyssPhenomenon.IronMeltingSun, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.IronMeltingSun, kind = AbyssPhenomenonKind.Debuff,
                displayName = "鉄を溶かす太陽",
                description = "戦闘中 5T 毎にランダムな T で直射: HP-10、 当該T 自分の行動無効。",
            } },
            { AbyssPhenomenon.BoulderHail, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.BoulderHail, kind = AbyssPhenomenonKind.Debuff,
                displayName = "落石のような雹",
                description = "マップでノード移動するたび HP-1。",
            } },
            { AbyssPhenomenon.CrimsonSnow, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.CrimsonSnow, kind = AbyssPhenomenonKind.Debuff,
                displayName = "朱の雪",
                description = "層内、 自分の与えるダメージ -1 (全武器)。",
            } },
            { AbyssPhenomenon.EclipsedNight, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.EclipsedNight, kind = AbyssPhenomenonKind.Mixed,
                displayName = "蝕夜",
                description = "戦闘開始時 30%で発動: 双方とも開幕 1T 行動不能。",
            } },
            { AbyssPhenomenon.UnceasingBell, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.UnceasingBell, kind = AbyssPhenomenonKind.Debuff,
                displayName = "鳴りやまない鐘",
                description = "戦闘中、 各T 20%でダイス 1 個の出目に -1 (最低1)。",
            } },
            { AbyssPhenomenon.SinkingSilence, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.SinkingSilence, kind = AbyssPhenomenonKind.Mixed,
                displayName = "沈む静寂",
                description = "戦闘中、 双方のスリップ／持続効果が無効化される。",
            } },
            { AbyssPhenomenon.NoonWithoutShadow, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.NoonWithoutShadow, kind = AbyssPhenomenonKind.Buff,
                displayName = "影が落ちない正午",
                description = "戦闘 1T目、 敵の攻撃が 50%で空振りする。",
            } },
            { AbyssPhenomenon.BurningRiver, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.BurningRiver, kind = AbyssPhenomenonKind.Buff,
                displayName = "燃える河",
                description = "全戦闘で敵に毎T、 敵最大HPの 2% (最低 1) のスリップ。",
            } },
            { AbyssPhenomenon.CollapsingHorizon, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.CollapsingHorizon, kind = AbyssPhenomenonKind.Debuff,
                displayName = "崩れる地平",
                description = "戦闘 5T 目以降、 毎T 自HP-2。",
            } },
            { AbyssPhenomenon.AbrasiveSand, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.AbrasiveSand, kind = AbyssPhenomenonKind.Debuff,
                displayName = "削る砂",
                description = "戦闘中、 自分に毎T HP-1。",
            } },
            { AbyssPhenomenon.SinkingLake, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.SinkingLake, kind = AbyssPhenomenonKind.Debuff,
                displayName = "沈み続ける湖面",
                description = "マップから 1 ノードがランダムに水没消失する。",
            } },
            { AbyssPhenomenon.IntermittentFall, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.IntermittentFall, kind = AbyssPhenomenonKind.Mixed,
                displayName = "間歇の崩落",
                description = "戦闘中、 5T 経過毎に双方のHP-3。",
            } },
            { AbyssPhenomenon.InvertedLightning, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.InvertedLightning, kind = AbyssPhenomenonKind.Buff,
                displayName = "逆さ雷",
                description = "戦闘中、 毎T 15%で敵に追加 3 ダメージ。",
            } },
            { AbyssPhenomenon.FadingPerson, new AbyssPhenomenonDef {
                id = AbyssPhenomenon.FadingPerson, kind = AbyssPhenomenonKind.Debuff,
                displayName = "薄れる人",
                description = "〈希望〉 が減少するたび、 追加で -1。",
            } },
        };

        public static AbyssPhenomenonDef Get(AbyssPhenomenon p)
            => defs.TryGetValue(p, out var d) ? d : null;

        public static IReadOnlyCollection<AbyssPhenomenon> All => defs.Keys;
    }
}
