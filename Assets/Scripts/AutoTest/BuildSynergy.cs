using System.Collections.Generic;
using GameLoop;
using InventorySystem;
using InventorySystem.PassiveSkills;

namespace AutoTest
{
    /// <summary>
    /// BOT の購入判断に「いま組んでいるビルドとの噛み合い」を持ち込む層 (2026-09-05)。
    ///
    /// <para><b>なぜ要るか。</b> 準パワーは全ランの平均寄与で、
    /// <b>交互作用を表現できない</b> ── regβ は主効果だけの線形モデルである。
    /// 鈍器 (「非会心攻撃時 +N%」) の期待値は <c>(1 − 会心率)</c> に比例して落ちるのに、
    /// 平均で 1 つの値を当てるので「会心ビルドでは無価値」という事実が消える。</para>
    ///
    /// <para>実測 (250,000 ラン): 会心系アイテムの所持数で層別すると、鈍器所持の band 差は
    /// <b>0 個で +3.24 → 3 個以上で +0.71</b> と 4.5 倍に減衰する。 そして鈍器を持つランの
    /// 会心系所持数は平均 <b>1.50 個</b> (非所持ラン 0.91 個) ── <b>会心ビルドが大量に拾っている</b>。
    /// 80% のランが会心系を 1 つ以上持つので、その母集団の平均が regβ に出て
    /// 「鈍器は弱い」という誤った結論になっていた。</para>
    ///
    /// <para><b>2 つ目の用途が 2026-09-06 に加わった</b>: 軸の噛み合い (<see cref="AxisAffinity"/>)。
    /// 同じ趣旨 ── 平均で 1 つの値を当てる指標では表現できない「組合せの価値」を、
    /// 買い方の側で補う。</para>
    ///
    /// <para><b>ゲーム側は変えない。</b> 排他ロックもキーストーンも入れていない
    /// (2026-09-05 に検討して不採用)。 直すのは<b>買い方</b>だけ ──
    /// 「今より良いか見てから買う」という <see cref="Loadout.WouldUpgrade"/> と同じ筋である。</para>
    /// </summary>
    public static class BuildSynergy
    {
        /// <summary>「非会心のときだけ効く」パッシブ。 会心率が上がるほど期待値が落ちる。
        /// 正本は items.json の説明文 (「非会心攻撃時」「会心を発生させない」)。</summary>
        private static readonly HashSet<string> NonCritOnly = new HashSet<string>
        {
            "鉛入りの握斧",   // 鉛入りの握斧      非会心 +20%
            "一人抱えの破城槌",  // 一人抱えの破城槌  非会心 +60%
            "研ぎ知らずの銑鉄棍",   // 研ぎ知らずの銑鉄棍 会心倍率-0.5 / 非会心 +40%
            "千日振りの鉢巻",  // 千日振りの鉢巻    3T連続非会心で +50% (会心でリセット)
            "重さを増す拳套",        // 重さを増す拳套    蓄積×10% (会心でリセット)
            "直さずの鉢金",  // 直さずの鉢金      HP50%以下で非会心 +40%
            // MindlessBlade (読めずの無心刃) は**自分で会心を封印する**ので対象外 ──
            //   会心率が高いほど「捨てる量」が増えて価値が上がる、逆向きの品である。
        };

        /// <summary>会心率 1.0 (=100%) あたり、鈍器系の準パワーから引く量。
        ///
        /// <para><b>2026-09-06: 単位が変わったので再設定。</b> 準パワーは z-score から
        /// band 単位 (実効レンジ −0.15〜+0.90 / SD 0.15 / 帯の間隔 0.10) へ移行した。
        /// 旧値 2.0 のままだと会心 33% で −0.66 ＝ 分布の 4 倍を引くことになり、
        /// 鈍器が問答無用で最下位へ落ちる。 <b>0.30</b> なら会心 33% で −0.10 ＝ 帯 1 つぶん。
        /// <b>暫定値・要測定</b>。 0 にすると「噛み合いを見ない」買い方に戻る。</para></summary>
        public static float NonCritPenaltyPerCritRate = 0.30f;

        // 武器分岐の探索ホールドアウト (WeaponBranchExploreRate) は 2026-09-21 削除。
        //   複合武器の廃止で T3+ → T4 の分岐そのものが無くなり、 探索する対象が無い。

        // ============================================================
        //  軸の噛み合い (2026-09-06 導入 → 2026-09-07 **無効化**)
        //
        //  導入時の理屈: レアリティは「効果量の階段」ではなく<b>「条件の階段」</b>で、
        //  GOLD はほぼ全部が<b>増幅器</b> (過充電中 与ダメ+30% / 臨界爆発 50→80 等)。
        //  供給しているのは BRONZE 側。 供給側の所持数で層別すると増幅器の band 差が
        //  「1〜2個」で最大 (+2.0 前後) になり、 そこに居るランが少ない ──
        //  だから BOT に専門化させれば GOLD が生きる、 と読んだ。
        //
        //  **前提が間違っていた (2026-09-07 / 250,000 ラン)。** 層別のバケツの中身:
        //      charge 0個 n=4,401  band 1.93 総所持 16.2
        //             1個 n=8,097  band 2.94 総所持 21.9
        //             2個 n=10,989 band 4.39 総所持 29.3
        //             3+個 n=226,513 band 9.97 総所持 63.3
        //  「供給側が少ないラン」＝<b>早死にして何も買えなかったラン</b>であって、
        //  専門化したランではない。 <b>層別変数がラン長の代理になっていた</b>。
        //  実際の増幅器の band 差は 3+個の帯が最大 (臨界 +2.39 / 毒 +2.43) で、
        //  「薄く広げるから本領を出さない」という筋書きは成立しない。
        //
        //  そして補正を入れた 250,000 ランは、 狙った当の指標を動かしていない:
        //      集中度 HHI          0.2774 → 0.2770   (0.25 = 4軸完全に均等)
        //      2個以上持つ軸の数    3.776 → 3.793    (最大 4)
        //      3個以上の軸を持つラン 96.4% → 97.0%
        //  用量不足ではなく、<b>向かうべき「帯」が存在しない</b>。 よって 0 にして殺してある。
        //  <see cref="AxisAffinity"/> の実装は残す ── 別の根拠が出たときに用量だけで戻せるように。
        //  軸の定義は <see cref="MetaProgression.SparkStarterPicker"/> のキーワード集合を借りている。
        // ============================================================

        /// <summary>同軸 1 個あたりの加点 (band 単位)。
        /// <b>0 = 無効</b> (2026-09-07 / 上のブロックの実測により)。 復活させるなら 0.03 が旧値。</summary>
        public static float AxisBonusPerOwned = 0f;
        /// <summary>同軸加点の上限。 これ以上は伸びない ── 専門化を促す一方で、
        /// <b>本当に弱い品を軸だけで押し上げない</b>ための蓋。</summary>
        public static float AxisBonusCap = 0.12f;

        private static readonly string[] Axes = { "charge", "rinkai", "poison", "bleed" };

        /// <summary>この品が属する軸 (無ければ null)。 複数該当なら最初の 1 つ。</summary>
        private static string AxisOf(CompleteItemData data)
        {
            if (data == null) return null;
            foreach (var ax in Axes)
                if (MetaProgression.SparkStarterPicker.HasKeyword(data, ax)) return ax;
            return null;
        }

        /// <summary>所持品のうち同じ軸に属する数。</summary>
        private static int OwnedInAxis(RunState run, string axis)
        {
            if (run?.ownedPassiveItems == null) return 0;
            var db = ItemDatabase.Instance;
            if (db == null) return 0;
            int n = 0;
            foreach (var id in run.ownedPassiveItems)
            {
                var d = db.GetItem(id);
                if (d != null && MetaProgression.SparkStarterPicker.HasKeyword(d, axis)) n++;
            }
            return n;
        }

        /// <summary>購入スコアへの補正 (加算)。 噛み合わなければ負を返す。
        ///
        /// <para><b>乗算ではなく加算にしてある。</b> 準パワーは z-score で負値を取りうるので、
        /// 係数を掛けると<b>負の品ほど値が上がる</b>という逆転が起きる。</para></summary>
        public static float ScoreAdjust(string itemId, RunState run)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return 0f;
            float adj = 0f;
            if (NonCritPenaltyPerCritRate > 0f && IsNonCritOnly(itemId))
                adj -= EstimateCritRate(run) * NonCritPenaltyPerCritRate;
            adj += AxisAffinity(itemId, run);
            return adj;
        }

        /// <summary>同じ軸を既に持っているほど、 その軸の品を高く評価する (専門化の促進)。</summary>
        public static float AxisAffinity(string itemId, RunState run)
        {
            if (AxisBonusPerOwned <= 0f) return 0f;
            var data = ItemDatabase.Instance?.GetItem(itemId);
            string ax = AxisOf(data);
            if (ax == null) return 0f;
            // 自分自身は数えない (未所持なので通常は含まれないが、 再評価経路で混ざりうる)
            int owned = OwnedInAxis(run, ax);
            if (run?.ownedPassiveItems != null && run.ownedPassiveItems.Contains(itemId)) owned--;
            if (owned <= 0) return 0f;
            return UnityEngine.Mathf.Min(AxisBonusCap, owned * AxisBonusPerOwned);
        }

        /// <summary>その品が「非会心のときだけ効く」ものか。</summary>
        public static bool IsNonCritOnly(string itemId)
        {
            var data = ItemDatabase.Instance?.GetItem(itemId);
            if (data?.passiveSkills == null) return false;
            foreach (var p in data.passiveSkills)
                if (p != null && NonCritOnly.Contains(p.internalName)) return true;
            return false;
        }

        /// <summary>ラン時点の実効会心率の見積り (戦闘外・0.0〜1.0)。
        ///
        /// <para>戦闘中の <see cref="PassiveSkillManager"/> と同じ順序で積んでから
        /// <see cref="CombatContext.ResolveCritRate"/> を通す ── 逓減は 2026-09-15 に撤去したので
        /// 現在は 0〜1 のクランプだが、 <b>変換は必ずこの 1 箇所を通す</b>。
        /// カーブが戻ったときに見積りだけ取り残されるのを防ぐため。</para>
        ///
        /// <para><b>拾えないもの</b>: 戦闘中の状態に依存する会心 (出血スタック連動・臨界爆発時・
        /// ゾロ目・低HP など)。 購入判断のための概算なので、 常時乗る分だけを見ている。</para></summary>
        public static float EstimateCritRate(RunState run)
        {
            if (run == null) return 0f;
            float add = 0f;

            var db = ItemDatabase.Instance;
            var w = string.IsNullOrEmpty(run.equippedWeaponId) ? null : db?.GetItem(run.equippedWeaponId);
            if (w != null) add += w.critRatePct / 100f;

            add += SumInsightRate(run);
            add += MetaProgression.MetaBuffApplicator.GetCritRatePctBonus();
            add -= MetaProgression.MetaDebuffApplicator.GetPlayerCritRatePenalty();

            if (add <= 0f) return 0f;
            return CombatContext.ResolveCritRate(add);
        }

        /// <summary>所持パッシブのうち 心眼 (Insight I-IV) の会心率合計 (小数)。</summary>
        private static float SumInsightRate(RunState run)
        {
            if (run?.ownedPassiveItems == null) return 0f;
            var db = ItemDatabase.Instance;
            if (db == null) return 0f;
            float n = 0f;
            foreach (var id in run.ownedPassiveItems)
            {
                var d = db.GetItem(id);
                if (d?.passiveSkills == null) continue;
                foreach (var p in d.passiveSkills)
                {
                    if (p == null) continue;
                    switch (p.internalName)
                    {
                        case "十五年目の計測器":   n += 0.05f; break;
                        case "測量師の片眼鏡":  n += 0.10f; break;
                        case "InsightIII": n += 0.15f; break;
                        case "InsightIV":  n += 0.20f; break;
                    }
                }
            }
            return n;
        }
    }
}
