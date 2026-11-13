using MapSystem.FreeMove;

namespace GameLoop
{
    /// <summary>
    /// 航行アイテム 13 品 (2026-11-11・docs/design/nav-items.md の設計図を、 ターン制の航行 (ADR-0012) に合わせて読み替えたもの)。
    /// どの品を持っているかをここで読み、 航行の規則へは係数 (<see cref="FreeMapPerks"/>) にして渡す。 FreeMapSim は品の id を見ない。
    /// 本体の規則 (物資の値段・前哨基地・逃げる・追跡者の戦闘) へ掛かる分は、 下の関数を各所が呼ぶ。
    /// <para>数値は全部仮置き (§23-21 と同じ扱い ── 動かす時は採否を取ってから測定する)。</para>
    ///
    /// <para><b>設計図からの読み替え</b> (連続時間の仕組みは 2026-10-18 に廃止済み・§24):</para>
    /// <list type="bullet">
    /// <item>油布の靴 ── 道の外の移動が無い → 削る砂の前線の追加の物資 (+1) を受けない</item>
    /// <item>忍びの外套 ── 忍び足が無い → 痕跡が残るターン 4 → 2</item>
    /// <item>鏡の護符 ── 使い魔の探知 1 歩 → 0 歩 (重ならない限り見つからない)</item>
    /// <item>補給商の割符 ── 補給庫が無い → 前哨基地の物資 ×1.5 ＋ 物資の購入金額 −50%</item>
    /// <item>渡り鳥の羅盤 ── 1 歩 = 物資 1 なので ×0.5 は「終点へ近づく歩の 2 歩に 1 回が無料」</item>
    /// <item>羅盤の針 ── 魔石の方角を計器の上に 8 方位の字で出す (レーダーは 4 方向の扇)</item>
    /// <item>追っ手殺しの刃 ── 連戦が無い → 追跡者の戦闘の 2 体目 (手下・猟犬) が付かない ＋ 勝った物資 ×1.5</item>
    /// </list>
    /// </summary>
    public static class NavItems
    {
        /// <summary>航行アイテムをショップ・宝箱・戦闘の報酬に混ぜるか (2026-11-11 に入れた)。 false にすると以前の配布の山に戻る
        /// (計測で以前の条件と揃えたい時の口)。</summary>
        public static bool InRandomPools = true;

        // ── 数値 (仮置き) ──
        public const int NightVision = 2;
        public const int StoneFarBonus = 1;
        public const double TraceLifeMul = 0.5;
        public const int MirrorFamiliarSee = 0;
        public const double SketchRadius = 5.0;
        public const float OutpostSupplyMul = 1.5f;
        public const float HunterSupplyMul = 1.5f;

        public static bool IsNavItem(string id) => !string.IsNullOrEmpty(id) && ItemIds.NavItems.Contains(id);

        /// <summary>その品を持っていて、 働いているか (大罪〈色欲〉で封じられた品は働かない)。</summary>
        public static bool Has(RunState run, string id)
            => run?.ownedPassiveItems != null && run.ownedPassiveItems.Contains(id)
               && (run.sinSealedPassives == null || !run.sinSealedPassives.Contains(id));

        private static int _cacheMask = -1;
        private static FreeMapPerks _cache;

        [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _cacheMask = -1; _cache = null; InRandomPools = true; }

        /// <summary>持っている航行アイテムから、 航行の規則に掛ける係数を作る。 持ち物が変わらなければ同じものを返す。</summary>
        public static FreeMapPerks PerksFor(RunState run)
        {
            int mask = 0;
            if (Has(run, ItemIds.NavSmokeBall))   mask |= 1 << 0;
            if (Has(run, ItemIds.NavStoneShard))  mask |= 1 << 1;
            if (Has(run, ItemIds.NavMapSketch))   mask |= 1 << 2;
            if (Has(run, ItemIds.NavStealthCloak)) mask |= 1 << 3;
            if (Has(run, ItemIds.NavMirrorCharm)) mask |= 1 << 4;
            if (Has(run, ItemIds.NavNightRing))   mask |= 1 << 5;
            if (Has(run, ItemIds.NavBirdCompass)) mask |= 1 << 6;
            if (Has(run, ItemIds.NavOutpostFlag)) mask |= 1 << 7;
            if (Has(run, ItemIds.NavOiledBoots))  mask |= 1 << 8;
            if (mask == 0) return FreeMapPerks.None;
            if (mask == _cacheMask && _cache != null) return _cache;
            var p = new FreeMapPerks
            {
                smokeCharges  = (mask & 1 << 0) != 0 ? 1 : 0,
                stoneFarBonus = (mask & 1 << 1) != 0 ? StoneFarBonus : 0,
                sketchRadius  = (mask & 1 << 2) != 0 ? SketchRadius : 0,
                traceLifeMul  = (mask & 1 << 3) != 0 ? TraceLifeMul : 1.0,
                familiarSee   = (mask & 1 << 4) != 0 ? MirrorFamiliarSee : FreeMapParams.FamiliarSee,
                vision        = (mask & 1 << 5) != 0 ? NightVision : FreeMapParams.Vision,
                goalwardHalf  = (mask & 1 << 6) != 0,
                beaconCharges = (mask & 1 << 7) != 0 ? 1 : 0,
                ignoreSand    = (mask & 1 << 8) != 0,
            };
            _cacheMask = mask; _cache = p;
            return p;
        }

        /// <summary>ショップの物資 1 の値段。 〈夜目の指輪〉か〈補給商の割符〉で −50% (両方でも重ねない・1G 未満にしない)。</summary>
        public static int ProvisionPrice(RunState run)
        {
            int p = FreeMapParams.ProvisionPrice;
            if (Has(run, ItemIds.NavNightRing) || Has(run, ItemIds.NavSupplyTally)) p = System.Math.Max(1, p / 2);
            return p;
        }

        /// <summary>前哨基地の物資。 〈補給商の割符〉で ×1.5 (切り上げ)。</summary>
        public static int OutpostSupply(RunState run)
            => Has(run, ItemIds.NavSupplyTally) ? (int)System.Math.Ceiling(FreeMapParams.RewardOutpost * OutpostSupplyMul) : FreeMapParams.RewardOutpost;

        /// <summary>逃げた後の所持金。 素は半減、 〈逃げ足の護符〉で失うのが 1/4 (切り捨て)。</summary>
        public static int GoldAfterFlee(RunState run, int coins, int halved)
            => Has(run, ItemIds.NavFleetCharm) ? coins - coins / 4 : halved;

        /// <summary>〈追っ手殺しの刃〉: 追跡者の戦闘に 2 体目 (手下・猟犬) が付かない。</summary>
        public static bool PursuerSolo(RunState run) => Has(run, ItemIds.NavHunterBlade);

        /// <summary>追跡者に勝った時の物資。 〈追っ手殺しの刃〉で ×1.5 (切り上げ)。</summary>
        public static int PursuerSupply(RunState run)
            => Has(run, ItemIds.NavHunterBlade) ? (int)System.Math.Ceiling(FreeMapParams.RewardPursuer * HunterSupplyMul) : FreeMapParams.RewardPursuer;

        /// <summary>〈羅盤の針〉: 魔石の方角を 8 方位の字で計器に出す。</summary>
        public static bool FineBearing(RunState run) => Has(run, ItemIds.NavCompassNeedle);
    }
}
