namespace MapSystem.AbyssPhenomena
{
    /// <summary>
    /// 大穴の異常現象。 層突入時に重み付き抽選で 1〜2 件が「層モディファイア」 として適用される。
    /// 正本: docs/specs/abyss-phenomena.md
    /// 番号は spec 準拠 (4/10/12/13/14 は lore のみ ─ enum に含めない)。
    /// </summary>
    public enum AbyssPhenomenon
    {
        None = 0,
        ReverseFalls       = 1,   // 逆行する滝 (BUFF)
        IronMeltingSun     = 2,   // 鉄を溶かす太陽 (DEBUFF, 致命寄り)
        BoulderHail        = 3,   // 落石のような雹 (DEBUFF)
        CrimsonSnow        = 5,   // 朱の雪 (DEBUFF)
        EclipsedNight      = 6,   // 蝕夜 (MIXED)
        UnceasingBell      = 7,   // 鳴りやまない鐘 (DEBUFF)
        SinkingSilence     = 8,   // 沈む静寂 (MIXED)
        NoonWithoutShadow  = 9,   // 影が落ちない正午 (BUFF)
        BurningRiver       = 11,  // 燃える河 (BUFF)
        CollapsingHorizon  = 15,  // 崩れる地平 (DEBUFF, 致命寄り)
        AbrasiveSand       = 16,  // 削る砂 (DEBUFF)
        SinkingLake        = 17,  // 沈み続ける湖面 (DEBUFF)
        IntermittentFall   = 18,  // 間歇の崩落 (MIXED)
        InvertedLightning  = 19,  // 逆さ雷 (BUFF)
        FadingPerson       = 20,  // 薄れる人 (DEBUFF)
    }

    public enum AbyssPhenomenonKind { Buff, Debuff, Mixed }

    public class AbyssPhenomenonDef
    {
        public AbyssPhenomenon id;
        public string displayName;
        public string description;
        public AbyssPhenomenonKind kind;
    }
}
