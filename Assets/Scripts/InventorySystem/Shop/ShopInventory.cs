using System.Collections.Generic;

namespace InventorySystem.Shop
{
    /// <summary>
    /// 1ショップ訪問あたりの在庫。
    /// パッシブ5 / 消費3 / 武器1 / 出目パーツ2 / 強化素材1 の計12スロット
    /// (2026-09-03 に武器 2→1・旧拡張枠を撤去してパッシブへ振り替え。 正本は ShopManager.OpenShop)。
    /// </summary>
    public class ShopInventory
    {
        public List<ShopSlot> slots = new List<ShopSlot>();

        /// <summary>武器強化素材を購入した回数（価格を倍々にするため）</summary>
        public int materialPurchaseCount;

        /// <summary>強化素材スロットの基準価格 (1/5 デノミ後: 旧15→3、2^N倍で 3/6/12/24...)</summary>
        // 2026-08-10 経済リスケール: 3 → 15 (価格 ×5。 二重デノミ解消に合わせる)
        public int materialBasePrice = 15;

        /// <summary>このショップの価格倍率（フロアデバフ等から設定）</summary>
        public float priceMultiplier = 1f;

        /// <summary>このショップで購入された通常品の数（嫉妬デバフが1で打ち止めにする）</summary>
        public int purchaseCount;

        /// <summary>強化素材スロットの現在価格 = base × 2^purchaseCount × priceMultiplier</summary>
        public int CurrentMaterialPrice
            => UnityEngine.Mathf.CeilToInt(materialBasePrice * (1 << materialPurchaseCount) * priceMultiplier);

        /// <summary>このショップで実施したリロール回数。</summary>
        public int rerollCount;

        /// <summary>
        /// 次のリロール価格。 <b>5, 10, 20, 30, 40, 50, ... G の線形</b> (2026-09-12)。
        ///
        /// <para><b>priceMultiplier は適用しない</b> (強盗の割増も含む) ──
        /// リロールは「品物の値付け」ではなく「在庫操作」のコストなので一律。</para>
        ///
        /// <para><b>旧: 三角数 ×5 (5/15/30/50/75/105/...) は品物の価格と桁が合っていなかった。</b>
        /// 実カタログの中央値は パッシブ 8G / 消耗品 5G / 武器 20G で、
        /// <b>棚 11 枠を全部買っても約 100G</b> (実測の平均ピーク所持 120.6G とほぼ同じ)。
        /// 旧曲線は 6 回目で 105G ＝ <b>棚を入れ替える権利が棚の中身より高い</b>状態だった。
        /// 品物が 5〜20G の 4 倍幅なのに、 リロールだけ 5〜275G の 55 倍幅で伸びていた。</para>
        ///
        /// <para>初回 5G は「とりあえず一度は回せる」導入価格として据え置き、
        /// 2 回目以降を 10G 刻みの線形に。 10 回目で 90G ＝ <b>棚 1 枚とほぼ同額</b>まで来るので、
        /// 深追いの上限が棚の価値と揃う。 旧曲線が同じ 90G 帯に達するのは 5 回目 (75G) だった。</para>
        /// </summary>
        public int CurrentRerollPrice
        {
            get
            {
                int n = rerollCount + 1;             // 1回目=5, 2回目=10, 3回目=20, 4回目=30...
                // A/B: 線形曲線 first + step×(n−1) に差し替える (step < 0 なら製品曲線)。
                int raw = RerollCurveStep >= 0
                    ? RerollCurveFirst + RerollCurveStep * (n - 1)
                    : (n <= 1 ? 5 : 10 * (n - 1));
                if (RerollPriceScale == 1f) return raw;
                // **下限 1G。 0 にしてはいけない** ── AutoRunner は `price > 0` を
                //   「まだ回せる」の判定に使っている (AutoRunner.cs:8227/8408/8463/8480) ので、
                //   0 にすると値下げのつもりが「回せない」に化ける。
                return UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(raw * RerollPriceScale));
            }
        }

        /// <summary>[A/B 用ノブ] リロール価格の倍率。 <b>1.0 = 製品</b> (2026-09-17)。
        ///
        /// <para>「リロール代として失った価値」を単独で測るためだけにある。
        /// 回数を削るアームは <b>金が浮く効果と供給が減る効果が混ざる</b> ──
        /// 棚は 12 枠固定 (ShopManager.Generate) なので、 リロールは補給を増やす唯一の手段を
        /// 兼ねているため。 価格だけを動かせば供給は現状のまま代金だけが消える。</para>
        ///
        /// <para>下限 1G なので <b>「無料」ではなく「実質無料」</b>。 測定結果を書くときに混同しない。</para>
        /// </summary>
        public static float RerollPriceScale = 1f;

        /// <summary>[A/B 用ノブ] リロール価格を <c>first + step×(n−1)</c> の線形曲線にする (2026-09-18)。
        /// <b>step &lt; 0 = 製品曲線 (5, 10, 20, 30…)</b>。 例: first=5, step=0 で一律 5G /
        /// step=3 で 5, 8, 11, 14…。 製品曲線は「棚の価値は毎回ほぼ同じなのに価格だけ倍々で上がる」
        /// ので 3 回目で損に転じる (実測 −1.3pt) ── 緩い曲線で余剰金 (クリア時 440G) を供給へ回せるかを測る。</summary>
        public static int RerollCurveFirst = 5;
        public static int RerollCurveStep = -1;
    }
}
