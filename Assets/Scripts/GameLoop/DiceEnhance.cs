using System.Collections.Generic;
using InventorySystem;

namespace GameLoop
{
    /// <summary>
    /// ダイス強化システム (2026-07-18 追加)。
    /// 弱ダイスに素材を投資して面を底上げする仕組み。 素の LEGENDARY には期待値/効果で及ばないが
    /// 「引きが悪くてもクリア可能圏」に到達させることが目的。
    ///
    /// 強化ルール:
    ///   Lv1 につき 現在の面配列で最も低い 2 面に +1 (面数が奇数なら最小 1 面のみ)。
    ///   例) dice_wood [1,2,3,4,5,6] Lv1 → [2,3,3,4,5,6], Lv2 → [3,3,4,4,5,6], ...,
    ///       Lv4 (BRONZE MAX) → [4,4,5,5,6,6] avg 5.00 (素 LEG 5.3-6.5 に届かず)
    ///
    /// レア別上限:
    ///   BRONZE: 4 / SILVER: 3 / GOLD: 2 / LEGENDARY: 1
    ///
    /// コスト (Lv → 必要素材数):
    ///   BRONZE  : Lv1=1, Lv2=1, Lv3=2, Lv4=2  (計6)
    ///   SILVER  : Lv1=1, Lv2=2, Lv3=2         (計5)
    ///   GOLD    : Lv1=2, Lv2=3                (計5)
    ///   LEGENDARY: Lv1=3                       (計3)
    ///
    /// 面数 3 以下の変則ダイス (dice_bone, dice_copper, dice_destiny) は Lv 上限を 2 に固定。
    /// </summary>
    public static class DiceEnhance
    {
        /// <summary>レア別の強化上限 Lv (通常ダイス)。</summary>
        public static int MaxLevelFor(InventorySystem.ItemRarity rarity)
        {
            switch (rarity)
            {
                case ItemRarity.BRONZE: return 4;
                case ItemRarity.SILVER: return 3;
                case ItemRarity.GOLD:   return 2;
                case ItemRarity.LEGENDARY: return 1;
                default: return 0;
            }
        }

        /// <summary>Lv → コスト (1 Lv 分だけ上げるのに必要な素材数)。</summary>
        public static int CostForLevel(InventorySystem.ItemRarity rarity, int nextLevel)
        {
            // nextLevel は "1 になるためのコスト" / "2 になるためのコスト" ... (1 始まり)
            switch (rarity)
            {
                case ItemRarity.BRONZE:
                    switch (nextLevel) { case 1: return 1; case 2: return 1; case 3: return 2; case 4: return 2; }
                    return int.MaxValue;
                case ItemRarity.SILVER:
                    switch (nextLevel) { case 1: return 1; case 2: return 2; case 3: return 2; }
                    return int.MaxValue;
                case ItemRarity.GOLD:
                    switch (nextLevel) { case 1: return 2; case 2: return 3; }
                    return int.MaxValue;
                case ItemRarity.LEGENDARY:
                    if (nextLevel == 1) return 3; return int.MaxValue;
                default: return int.MaxValue;
            }
        }

        /// <summary>ダイス ID の現在の強化 Lv (未強化=0)。</summary>
        public static int GetLevel(RunState run, string diceId)
        {
            if (run == null || string.IsNullOrEmpty(diceId)) return 0;
            EnsureDictSynced(run);
            return run.diceEnhanceLevels.TryGetValue(diceId, out int lv) ? lv : 0;
        }

        /// <summary>ダイス ID の強化 Lv を直接設定 (デバッグ/ロード用)。</summary>
        public static void SetLevel(RunState run, string diceId, int level)
        {
            if (run == null || string.IsNullOrEmpty(diceId)) return;
            EnsureDictSynced(run);
            if (level <= 0) run.diceEnhanceLevels.Remove(diceId);
            else run.diceEnhanceLevels[diceId] = level;
            FlushDictToLists(run);
        }

        /// <summary>装備ダイス (equippedDiceId) の 1 Lv 強化を試行。素材不足/上限到達で false。
        /// free=true で素材消費なし (鍛冶の霊薬等)。</summary>
        public static bool TryUpgradeEquipped(RunState run, bool free = false)
        {
            // 【2026-08-15 廃止】ダイス強化そのものを止める。 <see cref="ApplyEnhance"/> が
            //   無変更を返すので Lv を上げても効果が無く、 **素材を捨てるだけ**になる。
            //   ここで false を返せば、 休憩マスの武器強化フォールバックも空振りせず素材が残る。
            return false;
#pragma warning disable 0162
            if (run == null || string.IsNullOrEmpty(run.equippedDiceId)) return false;
            var d = ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            if (d == null) return false;
            int cur = GetLevel(run, run.equippedDiceId);
            int max = MaxLevelForItem(d);
            if (cur >= max) return false;
            int cost = CostForNextLevel(d, cur + 1);
            if (cost == int.MaxValue) return false;
            if (!free && run.weaponMaterials < cost) return false;
            if (!free) run.weaponMaterials -= cost;
            SetLevel(run, run.equippedDiceId, cur + 1);
            UnityEngine.Debug.Log($"[DiceEnhance] {run.equippedDiceId} Lv{cur}→{cur+1} (残素材 {run.weaponMaterials})");
            // 天工開物: 強化で素材1返還 (武器強化と対称)
            if (!free && run.OwnsPassive(ItemIds.HeavenlyCraft))
            {
                run.weaponMaterials += 1;
                UnityEngine.Debug.Log("[天工開物] ダイス強化で素材+1 返還");
            }
            return true;
#pragma warning restore 0162
        }

        /// <summary>アイテム個別の Lv 上限。 items.json の enhanceMaxLevel を優先、 未指定なら レア別デフォルト。
        /// 面数 3 以下は特殊で 2 に制限 (デフォルト経路のみ)。</summary>
        public static int MaxLevelForItem(CompleteItemData d)
        {
            if (d == null || d.diceFaces == null) return 0;
            if (d.enhanceMaxLevel > 0) return d.enhanceMaxLevel; // items.json 側の指定を優先
            int baseMax = MaxLevelFor(d.rarity);
            if (d.diceFaces.Length <= 3) return System.Math.Min(baseMax, 2);
            return baseMax;
        }

        /// <summary>アイテム個別のコスト取得。 items.json の enhanceCosts[nextLevel-1] を優先、 未指定ならレア別デフォルト。</summary>
        public static int CostForNextLevel(CompleteItemData d, int nextLevel)
        {
            if (d == null || nextLevel <= 0) return int.MaxValue;
            if (d.enhanceCosts != null && nextLevel - 1 < d.enhanceCosts.Length)
                return d.enhanceCosts[nextLevel - 1];
            return CostForLevel(d.rarity, nextLevel);
        }

        /// <summary>【2026-08-15 廃止】面配列への強化適用。 **常に無変更で返す**。
        ///
        /// <para>旧実装は「各 Lv で最も低い 2 面に +1」。 平均出目を上げる意図だったが、
        /// <b>面が潰れて同値だらけになる</b>という副作用があった。
        /// <c>1 2 3 4 5 6</c> → Lv3 で <c>4 4 4 4 5 6</c> ── 4 が 4 面。
        /// 5 個中 5 個が同値になる確率が <c>(4/6)^5 = 13%</c> に跳ね上がる。</para>
        ///
        /// <para>実測 (2026-08-15): 初回ロールの 5 個同値が <b>2.75%</b>（理論値 0.077% の 36 倍）、
        /// 出目分布は <c>1=7.2% 2=8.0% 3=12.8% 4=29.6% 5=25.7% 6=16.7%</c> と低い目が消えていた。
        /// 〈極〉が 1 ラン 12.7 回（ADR-0010 の想定 0.12 回）出ていた原因はこれ。
        /// items.json の面を全て 1-6 に統一しても変化しなかったのは、
        /// <b>実行時にここで書き換えられていた</b>ため。</para>
        ///
        /// <para>ダイスの個性（連番階梯・パリティ特化・配線特化）も強化するほど失われていた。
        /// 平均出目を上げたいなら<b>全面に +1</b>（1-6 → 2-7）にすること ──
        /// 相対関係が保存され、役の確率が一切変わらない。</para></summary>
        public static int[] ApplyEnhance(int[] baseFaces, int level) => baseFaces;

        /// <summary>強化 Lv 情報を dict → lists に flush (セーブ用)。</summary>
        public static void FlushDictToLists(RunState run)
        {
            if (run == null) return;
            run.diceEnhanceKeys.Clear();
            run.diceEnhanceValues.Clear();
            foreach (var kv in run.diceEnhanceLevels)
            {
                run.diceEnhanceKeys.Add(kv.Key);
                run.diceEnhanceValues.Add(kv.Value);
            }
        }

        /// <summary>lists → dict 再構築 (ロード直後 or NonSerialized リセット後)。</summary>
        public static void EnsureDictSynced(RunState run)
        {
            if (run == null) return;
            if (run.diceEnhanceLevels == null)
                run.diceEnhanceLevels = new Dictionary<string, int>();
            if (run.diceEnhanceLevels.Count == 0 && run.diceEnhanceKeys != null && run.diceEnhanceKeys.Count > 0)
            {
                for (int i = 0; i < run.diceEnhanceKeys.Count && i < run.diceEnhanceValues.Count; i++)
                    run.diceEnhanceLevels[run.diceEnhanceKeys[i]] = run.diceEnhanceValues[i];
            }
        }
    }
}
