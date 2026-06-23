using System.Collections.Generic;
using InventorySystem.PassiveSkills;
using UnityEngine;

namespace GameLoop.Contracts
{
    /// <summary>
    /// 契約システムの中央オーケストレータ。
    /// 仕様正本: docs/specs/contracts.md
    ///
    /// 責務:
    /// - active 契約のライフサイクル管理 (締結/維持/解除)
    /// - 各旅団の IContractEffect への hook 配信
    /// - 敵対関係: 同時契約取得時の既存契約強制解除 (L3 も含むがゴールド没収)
    /// - 協力関係: 両方契約中の判定 (各 IContractEffect が問い合わせる)
    /// - 維持費徴収: 層突入時に一括処理。 ゴールド不足は任意選択 UI を介して解除
    /// </summary>
    public class ContractManager
    {
        private static ContractManager _instance;
        public static ContractManager Instance => _instance ?? (_instance = new ContractManager());

        private readonly Dictionary<ContractKind, IContractEffect> _effects = new Dictionary<ContractKind, IContractEffect>();

        public IReadOnlyList<ContractInstance> Active(RunState run) => run?.activeContracts ?? new List<ContractInstance>();

        // ===== Effect 登録 =====

        public void RegisterEffect(IContractEffect effect)
        {
            if (effect == null) return;
            _effects[effect.Kind] = effect;
        }

        public IContractEffect GetEffect(ContractKind k)
        {
            _effects.TryGetValue(k, out var e);
            return e;
        }

        // ===== 検索/問い合わせ =====

        public ContractInstance Find(RunState run, ContractKind k)
        {
            if (run?.activeContracts == null) return null;
            foreach (var c in run.activeContracts)
                if (c.kind == k) return c;
            return null;
        }

        public bool IsActive(RunState run, ContractKind k) => Find(run, k) != null;

        /// <summary>協力関係: 指定契約とその ally が両方アクティブか。</summary>
        public bool IsAllianceActive(RunState run, ContractKind k)
        {
            if (!ContractRelations.TryGetAlly(k, out var ally)) return false;
            return IsActive(run, k) && IsActive(run, ally);
        }

        // ===== 契約締結 / 解除 =====

        /// <summary>新規契約を結ぶ。 敵対関係にある既存契約は強制解除 (L3 でも没収)。
        /// 戻り値: 解除された旅団のリスト (UI 通知用)。</summary>
        public List<ContractInstance> SignNew(RunState run, ContractKind k, int level)
        {
            var removed = new List<ContractInstance>();
            if (run == null) return removed;
            if (run.activeContracts == null) run.activeContracts = new List<ContractInstance>();

            // 敵対関係の強制解除
            if (ContractRelations.TryGetRival(k, out var rival))
            {
                var existingRival = Find(run, rival);
                if (existingRival != null)
                {
                    run.activeContracts.Remove(existingRival);
                    removed.Add(existingRival);
                    AddToExpiredPool(run, existingRival.kind);
                    SyncFlagFor(run, existingRival.kind); // フラグ自動同期
                    // L3 解除時のゴールド没収は ContractCost.For(level) を払い戻さないことで表現
                    // (層突入時に既に徴収済みのため、 ここでは何もしない)
                }
            }

            // 既に契約済みなら延長 (レベルアップ)
            var existing = Find(run, k);
            if (existing != null)
            {
                while (existing.level < level && existing.level < ContractCost.MaxLevel)
                    existing.LevelUp();
            }
            else
            {
                run.activeContracts.Add(new ContractInstance(k, level, run.currentFloor));
            }

            SyncFlagFor(run, k);
            return removed;
        }

        // 契約状態をフラグとして同期 (イベント条件参照用)。 OrphanCircus のみ "サーカス団同行" を管理。
        private void SyncFlagFor(RunState run, ContractKind k)
        {
            if (run == null) return;
            if (k != ContractKind.OrphanCircus) return; // 現状サーカスのみ
            const string flag = "サーカス団同行";
            if (run.ownedFlags == null) run.ownedFlags = new System.Collections.Generic.HashSet<string>();
            bool active = IsActive(run, k);
            if (active) run.ownedFlags.Add(flag);
            else run.ownedFlags.Remove(flag);
        }

        /// <summary>任意解除。 同層失効プールにも追加 (同層では再提示されない)。</summary>
        public bool Cancel(RunState run, ContractKind k)
        {
            var c = Find(run, k);
            if (c == null) return false;
            run.activeContracts.Remove(c);
            AddToExpiredPool(run, k);
            SyncFlagFor(run, k);
            return true;
        }

        private void AddToExpiredPool(RunState run, ContractKind k)
        {
            if (run == null) return;
            if (run.contractsExpiredThisLayer == null)
                run.contractsExpiredThisLayer = new List<ContractKind>();
            if (!run.contractsExpiredThisLayer.Contains(k))
                run.contractsExpiredThisLayer.Add(k);
        }

        // AutoRunner 等の統計収集用カウンタ (ラン跨ぎでリセットされない、 利用者側で適宜リセット)
        public int Stat_HpReleaseCount;

        /// <summary>HP20% 解除判定 (戦闘終了時)。 L3 は免除。 解除された契約を返す (同層失効プールにも追加)。</summary>
        public List<ContractInstance> CheckHpReleaseRule(RunState run, int currentHp, int maxHp)
        {
            var released = new List<ContractInstance>();
            if (run?.activeContracts == null || maxHp <= 0) return released;
            float ratio = (float)currentHp / maxHp;
            if (ratio > 0.20f) return released;

            for (int i = run.activeContracts.Count - 1; i >= 0; i--)
            {
                var c = run.activeContracts[i];
                if (c.IsImmuneToHpReleaseRule) continue;
                run.activeContracts.RemoveAt(i);
                released.Add(c);
                AddToExpiredPool(run, c.kind);
                SyncFlagFor(run, c.kind);
                Stat_HpReleaseCount++;
            }
            return released;
        }

        // ===== Hook ディスパッチ =====

        // 戦闘開始時の発動順 (仕様 docs/specs/contracts.md「発動順序」):
        //   1. 暗殺教団 → 2. 狩猟旅団 → 3. 戦術家 → その他
        private static readonly ContractKind[] BattleStartOrder = {
            ContractKind.Assassins,
            ContractKind.Hunters,
            ContractKind.Tacticians,
        };

        public void FireOnBattleStart(RunState run, CombatContext ctx)
        {
            if (run?.activeContracts == null) return;
            foreach (var k in BattleStartOrder)
            {
                var c = Find(run, k);
                if (c != null) GetEffect(k)?.OnBattleStart(c, ctx, run);
            }
            // 残りの契約を順不同で発火
            foreach (var c in run.activeContracts)
            {
                if (System.Array.IndexOf(BattleStartOrder, c.kind) >= 0) continue;
                GetEffect(c.kind)?.OnBattleStart(c, ctx, run);
            }
        }

        public void FireOnTurnEnd(RunState run, CombatContext ctx)
        {
            if (run?.activeContracts == null) return;
            foreach (var c in run.activeContracts)
            {
                var e = GetEffect(c.kind);
                e?.OnTurnEnd(c, ctx, run);
            }
        }

        // 戦闘終了時の発動順 (仕様 docs/specs/contracts.md「発動順序」):
        //   1. 医術官回復 → 2. 商業連合隊 HP≤50% 判定 (回復後HPで判定) → 3. 錬金
        //   (狩猟+医術官協力の与ダメ%回復は WanderingDoctorEffect 内で同時処理)
        private static readonly ContractKind[] BattleEndOrder = {
            ContractKind.WanderingDoctor,
            ContractKind.MerchantsLeague,
            ContractKind.Alchemist,
        };

        public void FireOnBattleEnd(RunState run, ContractBattleResult result)
        {
            if (run?.activeContracts == null) return;
            foreach (var k in BattleEndOrder)
            {
                var c = Find(run, k);
                if (c != null) GetEffect(k)?.OnBattleEnd(c, run, result);
            }
            // 残りの契約を順不同で発火
            foreach (var c in run.activeContracts)
            {
                if (System.Array.IndexOf(BattleEndOrder, c.kind) >= 0) continue;
                GetEffect(c.kind)?.OnBattleEnd(c, run, result);
            }
        }

        public void FireOnLayerStart(RunState run)
        {
            if (run?.activeContracts == null) return;
            foreach (var c in run.activeContracts)
            {
                var e = GetEffect(c.kind);
                e?.OnLayerStart(c, run);
            }
        }

        public void FireOnLayerEnd(RunState run)
        {
            if (run?.activeContracts == null) return;
            foreach (var c in run.activeContracts)
            {
                c.OnLayerEnd();
                var e = GetEffect(c.kind);
                e?.OnLayerEnd(c, run);
            }
            // 同層失効プールをクリア (次層では再提示可能に戻る)
            run.contractsExpiredThisLayer?.Clear();
        }

        public void FireOnRollWin(RunState run, CombatContext ctx, bool wasCritical)
        {
            if (run?.activeContracts == null) return;
            foreach (var c in run.activeContracts)
            {
                var e = GetEffect(c.kind);
                e?.OnRollWin(c, ctx, run, wasCritical);
            }
        }

        // ===== 維持費徴収 =====

        /// <summary>層突入時の維持費徴収。 ゴールド不足契約は返却 (任意選択UIで処理する想定)。</summary>
        public List<ContractInstance> CollectMaintenanceOrFlagShortfall(RunState run)
        {
            var shortfall = new List<ContractInstance>();
            if (run?.activeContracts == null) return shortfall;
            int total = 0;
            foreach (var c in run.activeContracts) total += c.CurrentMaintenanceCost;
            if (total <= run.coins)
            {
                run.coins -= total;
                return shortfall;
            }
            shortfall.AddRange(run.activeContracts);
            return shortfall;
        }

        /// <summary>UI でユーザーが選んだ契約を解除しつつ、 残りの維持費を徴収する。</summary>
        public void ResolveShortfall(RunState run, List<ContractKind> toCancel)
        {
            if (run == null) return;
            if (toCancel != null)
                foreach (var k in toCancel) Cancel(run, k);
            int total = 0;
            foreach (var c in run.activeContracts) total += c.CurrentMaintenanceCost;
            run.coins = Mathf.Max(0, run.coins - total);
        }

        // ===== 外部呼び出し用 helper API (各旅団効果へのアクセス点) =====

        /// <summary>影武者一座: HP0 到達時に呼ぶ。 残数があれば復活させて true、 さもなくば false。</summary>
        public bool TryReviveOnLethal(RunState run, ref int playerHp, int playerMaxHp)
        {
            var c = Find(run, ContractKind.BodyDoubles);
            if (c == null || c.bodyDoublesRemainingRevives <= 0) return false;
            playerHp = Mathf.Max(1, Mathf.CeilToInt(playerMaxHp * 0.10f));
            c.bodyDoublesRemainingRevives--;
            Debug.Log($"[影武者一座] 復活 HP={playerHp}/{playerMaxHp} (残 {c.bodyDoublesRemainingRevives} 回)");
            return true;
        }

        /// <summary>宣教師: 戦闘外希望減少のオフセット (協力中の騎士+宣教師で +1)。
        /// 呼び出し側は減少量から減算する: actualLoss = max(0, rawLoss - offset)。</summary>
        public int GetHopeLossReduction(RunState run)
        {
            var c = Find(run, ContractKind.Missionaries);
            if (c == null) return 0;
            int offset = c.level;
            if (IsAllianceActive(run, ContractKind.Missionaries)) offset += 1;
            return offset;
        }

        /// <summary>戦術家: 振り直しを1回消費する。 成功なら true。</summary>
        public bool TryConsumeReroll(RunState run)
        {
            var c = Find(run, ContractKind.Tacticians);
            if (c == null || c.tacticiansRerollsRemainingThisCombat <= 0) return false;
            c.tacticiansRerollsRemainingThisCombat--;
            return true;
        }

        public int GetRerollsRemaining(RunState run)
        {
            var c = Find(run, ContractKind.Tacticians);
            return c?.tacticiansRerollsRemainingThisCombat ?? 0;
        }

        // ===== 補給キャラバン UI 用 =====

        public bool CanUseSupplyShop(RunState run)
            => Effects.SupplyCaravanEffect.CanUseShop(Find(run, ContractKind.SupplyCaravan));
        public bool CanUseSupplyEnhance(RunState run)
            => Effects.SupplyCaravanEffect.CanUseEnhance(Find(run, ContractKind.SupplyCaravan));
        public bool CanUseSupplyRest(RunState run)
            => Effects.SupplyCaravanEffect.CanUseRest(Find(run, ContractKind.SupplyCaravan));

        public void ConsumeSupplyShop(RunState run)
        {
            var c = Find(run, ContractKind.SupplyCaravan);
            if (c != null) c.supplyShopUsedThisLayer = true;
        }
        public void ConsumeSupplyEnhance(RunState run)
        {
            var c = Find(run, ContractKind.SupplyCaravan);
            if (c != null) c.supplyEnhanceUsedThisLayer = true;
        }
        public void ConsumeSupplyRest(RunState run)
        {
            var c = Find(run, ContractKind.SupplyCaravan);
            if (c != null) c.supplyRestUsedThisLayer = true;
        }

        // ===== 効果登録ヘルパ (起動時に呼ぶ) =====

        /// <summary>全 12 旅団の効果を登録する。 起動時に 1 度だけ呼ぶ。</summary>
        public void RegisterAllEffects()
        {
            RegisterEffect(new Effects.MercenariesEffect());
            RegisterEffect(new Effects.SupplyCaravanEffect());
            RegisterEffect(new Effects.MerchantsLeagueEffect());
            RegisterEffect(new Effects.MissionariesEffect());
            RegisterEffect(new Effects.KnightsEffect());
            RegisterEffect(new Effects.AssassinsEffect());
            RegisterEffect(new Effects.AlchemistEffect());
            RegisterEffect(new Effects.WanderingDoctorEffect());
            RegisterEffect(new Effects.OrphanCircusEffect());
            RegisterEffect(new Effects.BodyDoublesEffect());
            RegisterEffect(new Effects.HuntersEffect());
            RegisterEffect(new Effects.TacticiansEffect());
        }
    }
}
