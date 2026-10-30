using System;
using System.Collections.Generic;
using UnityEngine;
using Codex;
using CombatSystem;
using EventSystem;
using ItemSystem;
using ItemSystem.PassiveSkills;
using ItemSystem.Shop;
using MapSystem;

namespace GameLoop
{
    /// <summary>GameManager: 休憩・武器の段の強化・面の除去・前哨基地の強化。</summary>
    public partial class GameManager
    {
        /// <summary>休憩でHP回復を選択（回復と強化は択一）。</summary>
        public void RestHeal()
        {
            if (CurrentPhase != GamePhase.RestStop) return;
            float ratio = ActiveModifier?.restHealMultiplier ?? 0.3f;
            // 〈遅い回復〉: **あらゆる回復に掛かる**ので休憩地点にも適用する。
                int heal = Mathf.CeilToInt(Run.playerMaxHP * ratio
                                           * MetaProgression.MetaDebuffApplicator.GetHealMultiplier());
            heal = MetaProgression.MetaDebuffApplicator.ApplyJudgmentHealReduction(heal, Run);
            int beforeRestHP = Run.playerHP;
            Run.playerHP = Mathf.Min(Run.playerMaxHP, Run.playerHP + heal);
            // 〈浅い眠り〉: 回復スポットの頭打ち。 **対象は前哨基地と休憩地点だけ**。
            Run.playerHP = MetaProgression.MetaDebuffApplicator.ApplyHealSpotCap(Run.playerHP, Run.playerMaxHP, beforeRestHP);
            MetaProgression.MetaDebuffApplicator.NoteHeal(heal, Run.playerHP - beforeRestHP, Run);
            RunChronicle.Rest(Run, Run.playerHP - beforeRestHP, beforeRestHP, Run.playerMaxHP);
            Log($"休憩回復: +{Run.playerHP - beforeRestHP}HP ({(int)(ratio*100)}%) (現在: {Run.playerHP}/{Run.playerMaxHP})");
            ReturnToMapOrClearFloor();
        }

        // ===== 武器Tier強化＆限界突破システム =====
        public const int MaxLimitBreak = 10;

        /// <summary>その武器の家系と段 (items.json の family / tier)。 進行武器でなければ (null, 0)。
        ///
        /// <para><b>id を綴りで切らない</b> (2026-09-22)。 id は表示名なので、
        /// 正規表現で <c>_tN</c> を取り出す旧実装は品名を変えた瞬間に黙って外れる。</para></summary>
        private static (string family, int tier) FamilyTierOf(string weaponId)
        {
            var d = string.IsNullOrEmpty(weaponId) ? null : ItemDatabase.Instance?.GetItem(weaponId);
            return d == null ? (null, 0) : (d.family, d.tier);
        }

        /// <summary>同家系の次に存在するTier武器ID。無ければ null（=最上位Tier）。</summary>
        public static string NextTierWeaponId(string weaponId)
        {
            var (family, n) = FamilyTierOf(weaponId);
            if (string.IsNullOrEmpty(family) || n <= 0) return null;  // 竜閃等は強化対象外
            var db = ItemDatabase.Instance;
            if (db == null) return null;
            for (int k = n + 1; k <= n + 5; k++)
            {
                var next = db.GetWeapon(family, k);
                if (next != null) return next.id;
            }
            return null;
        }

        /// <summary>強化の行き先。 <b>純家系のラダーを 1 段上がるだけ</b>。
        ///
        /// <para>2026-09-21 に複合武器を廃止するまでは、 ここが「純 T4 と複合 T4 の分岐」で、
        /// 学習済み regβ による選択・同点の乱択・探索ホールドアウトを抱えていた。
        /// 分岐先が無くなったので、 判断そのものが消えている (GAME.md §24)。</para></summary>
        public static string ChooseUpgradeTarget(RunState run)
            => NextTierWeaponId(run?.equippedWeaponId);

        /// <summary>現在の総合強化段階。進行武器: (tier-1)*2 + plus + 業物。非進行Tier武器: tier番号-1。</summary>
        private static int OverallStage(RunState run)
        {
            var (fam, n) = FamilyTierOf(run.equippedWeaponId);
            if (string.IsNullOrEmpty(fam) || n <= 0) return 0;
            // **2026-09-05: T1 廃止で起点が T2 になった** ── WeaponProgression と同じ数え方に揃える。
            //   T2=0 / T2+=1 / T3=2 / T3+=3 / T4=4 / T4+=5
            if (ItemSystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId))
                return System.Math.Max(0, (n - 2) * 2 + (run.weaponPlus > 0 ? 1 : 0) + run.limitBreakStage);
            return n - 1;
        }

        /// <summary>T4+ までの段階別コスト表 (stage 0..4 の 5 段)。
        ///
        /// <para><b>2026-09-05 再ベース。</b> T1 廃止で段が 8 → 6 になり、 必要な強化回数が
        /// 7 → 5 に減った。 旧表 <c>{1,1,2,2,3,3,4}</c> の合計 16 をそのまま維持すると
        /// 素材が余りすぎる (素材の使い道は武器強化ただ 1 つしかない) ので、
        /// <b>合計 16 を保ったまま 5 段へ配り直した</b> ── <c>{2,2,3,4,5}</c>。
        /// 後ろほど重くしてあるので、 T4 へ届かせるかどうかが判断になる。</para></summary>
        private static readonly int[] WeaponUpgradeCostTable = { 2, 2, 3, 4, 5 };

        /// <summary>次の1段階強化に必要な素材数。 強化不可なら int.MaxValue。
        /// コスト表は T4+ までの {1,1,2,2,3,3,4} だけ。
        ///
        /// **2026-08-10: 業物 (限界突破) を廃止した。** 進行武器の強化は **T4+ が終点**。
        /// 廃止理由は §24 ── 業物は「与ダメ +20%/lv」の素の倍率で、 素材の唯一の
        /// 後半シンクでもあったため、 ショップ価格 (搾取経済) の効きが素材価格の
        /// 2^N カーブを通って **log で潰れ**、 挑戦デバフに段差を作れなくなっていた。
        /// 代償は敵HPの後半層 −10% (<see cref="CombatSystem.CombatManager"/> 側)。</summary>
        public static int WeaponUpgradeCost(RunState run)
        {
            if (run == null) return int.MaxValue;
            bool prog = ItemSystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId);
            int stage = OverallStage(run);
            if (stage >= WeaponUpgradeCostTable.Length) return int.MaxValue;
            int cost = WeaponUpgradeCostTable[stage];
            if (prog)
            {
                // T4+ (次 Tier 無し かつ + 済み) が終点。 これ以上は素材を受け付けない。
                bool atTopPlus = run.weaponPlus > 0 && NextTierWeaponId(run.equippedWeaponId) == null;
                if (atTopPlus) return int.MaxValue;
                return cost;
            }
            if (NextTierWeaponId(run.equippedWeaponId) != null) return cost;
            return int.MaxValue;
        }

        /// <summary>素材を消費できる先が残っているか (武器強化 or 装備ダイス強化)。
        ///
        /// **業物廃止 (2026-08-10) で必要になった判定。** T4+ 到達後の進行武器は素材を
        /// 受け付けなくなったので、 ダイス強化も上限なら素材は完全な死に資源になる。
        /// ショップは在庫無限で売り続けるため、 **買う側**がここを見て止まること。</summary>
        public static bool CanSpendMaterials(RunState run)
        {
            if (run == null) return false;
            if (WeaponUpgradeCost(run) != int.MaxValue) return true;
            var d = ItemSystem.ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            if (d == null) return false;
            int cur = DiceEnhance.GetLevel(run, run.equippedDiceId);
            return cur < DiceEnhance.MaxLevelForItem(d)
                && DiceEnhance.CostForNextLevel(d, cur + 1) != int.MaxValue;
        }

        /// <summary>武器強化で Tier が進んだ際に通知される (旧ID, 新ID)。
        /// L1 学習が中間 Tier を acquiredItemsEver に記録するためのフック。
        /// 同一 Tier 内の + 昇格は通知しない (Tier ID が変わらないため)。</summary>
        public static event System.Action<string, string> OnWeaponTierUpgraded;

        /// <summary>武器を1段階強化。進行武器: T_n→T_n+→次Tier→…→**T4+ で終点**。
        /// 非進行Tier武器: 従来のTier置換。free=true で素材消費なし（鍛冶の霊薬）。
        /// 2026-08-10: 業物 (T4+ 以降の限界突破) を廃止。 T4+ 到達後は
        /// <see cref="WeaponUpgradeCost"/> が int.MaxValue を返すのでここへ来ない。</summary>
        public static bool TryUpgradeWeapon(RunState run, bool free = false)
        {
            if (run == null) return false;
            int cost = WeaponUpgradeCost(run);
            if (cost == int.MaxValue) return false;
            if (!free && run.weaponMaterials < cost) return false;

            string prevWeaponId = run.equippedWeaponId;
            bool prog = ItemSystem.PassiveSkills.WeaponProgression.IsProgressionWeapon(run.equippedWeaponId);
            if (prog)
            {
                if (run.weaponPlus == 0)
                {
                    run.weaponPlus = 1;                 // T_n → T_n+
                }
                else
                {
                    // T_n+ → T_{n+1}。 T4+ は WeaponUpgradeCost が int.MaxValue を返すのでここへ来ない
                    //   (業物は 2026-08-10 に、 純／複合の分岐は 2026-09-21 に複合武器ごと廃止・GAME.md §24)。
                    string next = ChooseUpgradeTarget(run);
                    if (next == null) return false;
                    run.equippedWeaponId = next; run.weaponPlus = 0;
                }
            }
            else
            {
                string next = NextTierWeaponId(run.equippedWeaponId);
                if (next == null) return false;
                run.equippedWeaponId = next;
            }
            if (!free) run.weaponMaterials -= cost;
            if (prevWeaponId != run.equippedWeaponId)
            {
                OnWeaponTierUpgraded?.Invoke(prevWeaponId, run.equippedWeaponId);
                // 行動台帳: Tier が上がったときだけ積む。 素材を注いだだけで
                //   段が上がらない回は「山」ではないので載せない。
                bool apex = NextTierWeaponId(run.equippedWeaponId) == null;
                RunChronicle.Forge(run,
                    apex ? RunChronicle.ForgeApex : RunChronicle.ForgeUpgrade,
                    run.equippedWeaponId, cost);
            }
            // 天工開物 (2026-10-31): 武器を強化するたび、 以後の戦闘の開始時の臨界 +5
            if (run != null && run.OwnsPassive(ItemIds.HeavenlyCraft))
            {
                run.heavenlyCraftUpgrades++;
                Debug.Log($"[天工開物] 武器強化 → 以後の戦闘の開始時 臨界 +{5 * run.heavenlyCraftUpgrades}");
            }
            return true;
        }

        public void RestUpgrade()
        {
            if (CurrentPhase != GamePhase.RestStop) return;

            // 2026-05-31 v3: 1 Rest = 1 強化のみ (前哨基地強化 (各層1回) と組み合わせて運用)
            // 2026-07-18: 武器が強化不可なら ダイス強化にフォールバック (共通素材)
            int wcost = WeaponUpgradeCost(Run);
            bool weaponPossible = wcost != int.MaxValue && Run.weaponMaterials >= wcost;
            if (!weaponPossible)
            {
                if (DiceEnhance.TryUpgradeEquipped(Run))
                {
                    Log($"武器強化不可 → ダイス強化: {Run.equippedDiceId} Lv{DiceEnhance.GetLevel(Run, Run.equippedDiceId)} (残素材 {Run.weaponMaterials})");
                }
                else
                {
                    Log($"武器強化 不可 (必要{wcost}/所持{Run.weaponMaterials}) → 回復に切替");
                    RestHeal();
                    return;
                }
            }
            else
            {
                int before = Run.weaponMaterials;
                if (TryUpgradeWeapon(Run))
                {
                    int used = before - Run.weaponMaterials;
                    Log($"武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
                }
            }
            SetPhase(GamePhase.MapNavigation);
        }

        /// <summary>ADR-0011: 面を 1 枚外す価格。 外せない (最少枚数) なら false。</summary>
        public bool TryGetFaceRemovalCost(out int gold, out int materials)
        {
            gold = 0; materials = 0;
            if (Run == null || Run.diceFaceParts == null) return false;
            return DiceFaceParts.RemovalCost(Run.faceRemovals, Run.diceFaceParts.Count, out gold, out materials);
        }

        /// <summary>ADR-0011: 休憩の「強化」の一つとして、 ゴールドと強化素材を払って面を 1 枚外す。
        /// **回復・武器強化と択一** ── 1 回の休憩で外せるのは 1 枚で、 休憩の行動を消費する。
        /// 外すと出目の合計と、 その目の役 (T2 以上のパーツ) を失う。 払えない / 最少枚数なら何も起きない。</summary>
        public bool RestRemoveFace(int face)
        {
            if (CurrentPhase != GamePhase.RestStop || Run == null || Run.diceFaceParts == null) return false;
            if (!TryGetFaceRemovalCost(out int gold, out int materials)) { Log("面はこれ以上外せない"); return false; }

            int at = -1;
            for (int i = 0; i < Run.diceFaceParts.Count; i++)
                if (Run.diceFaceParts[i].face == face) { at = i; break; }
            if (at < 0) { Log($"出目 {face} の面を持っていない"); return false; }
            if (Run.weaponMaterials < materials) { Log($"強化素材不足 (必要{materials}/所持{Run.weaponMaterials})"); return false; }
            if (!ProvisionPayment.TryPay(Run, gold, "面を外す")) { Log("ゴールド不足"); return false; }

            Run.weaponMaterials -= materials;
            var removed = Run.diceFaceParts[at];
            Run.diceFaceParts.RemoveAt(at);
            Run.faceRemovals++;
            Log($"面を外した: {DiceFaceParts.Label(removed)} (-{gold}G, 素材-{materials}, 残り{Run.diceFaceParts.Count}面, 通算{Run.faceRemovals}回目)");
            ReturnToMapOrClearFloor();
            return true;
        }

        /// <summary>前哨基地強化 (2026-05-31 新規): 各層 (Outpost マス) で 1 回のみ実行可能な武器強化。
        /// 素材消費は通常の WeaponUpgradeCost に従う。 RestUpgrade とは独立してフラグ管理される。
        /// 戻り値: true=強化成功、 false=既に使用済 or 素材不足 or 強化不可。</summary>
        public bool OutpostUpgrade()
        {
            if (Run == null) return false;
            if (Run.outpostUpgradeUsedThisFloor) return false;
            int cost = WeaponUpgradeCost(Run);
            if (cost != int.MaxValue && Run.weaponMaterials >= cost)
            {
                int before = Run.weaponMaterials;
                if (!TryUpgradeWeapon(Run)) return false;
                Run.outpostUpgradeUsedThisFloor = true;
                int used = before - Run.weaponMaterials;
                Log($"[前哨基地] 武器強化 ×1: {Run.equippedWeaponId}{(Run.weaponPlus > 0 ? "+" : "")} 業物Lv{Run.limitBreakStage} (素材-{used}, 残{Run.weaponMaterials})");
                return true;
            }
            // 2026-07-18: 武器強化不可ならダイス強化にフォールバック
            if (DiceEnhance.TryUpgradeEquipped(Run))
            {
                Run.outpostUpgradeUsedThisFloor = true;
                Log($"[前哨基地] ダイス強化: {Run.equippedDiceId} Lv{DiceEnhance.GetLevel(Run, Run.equippedDiceId)} (残素材 {Run.weaponMaterials})");
                return true;
            }
            return false;
        }
    }
}
