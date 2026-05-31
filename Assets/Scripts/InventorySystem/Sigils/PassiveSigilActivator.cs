using System.Collections.Generic;
using UnityEngine;
using GameLoop;
using InventorySystem.PassiveSkills;

namespace InventorySystem.Sigils
{
    /// <summary>
    /// パッシブ刻印の発動判定と効果適用。
    /// UIモード: GridManager のセル隣接を実走査。
    /// Bot/ヘッドレス: 該当レアリティの所持アイテムが存在すれば 80% で発動。
    /// </summary>
    public static class PassiveSigilActivator
    {
        private const float BotActivationChance = 0.8f;

        /// <summary>戦闘開始時に呼ぶ。 該当する刻印効果を ctx に適用。
        /// per-roll 評価が必要なもの (LowHpAtk2, Corrode3p) は accumulator フラグだけ立てる。</summary>
        public static void ApplyOnBattleStart(RunState run, CombatContext ctx)
        {
            if (run == null || ctx == null) return;
            if (run.passiveSigils == null || run.passiveSigils.Count == 0) return;

            var active = CollectActive(run);
            if (active.Count == 0) return;

            int shieldAdd = 0;
            int diceBonus = 0;
            int hpHeal = 0;
            int pursuitBonus = 0;
            int critBonus = 0;
            int t1Reduce = 0;
            int lowHpAtkStacks = 0;
            int corrodeStacks = 0;

            foreach (var sigil in active)
            {
                switch (sigil.effect)
                {
                    case SigilEffectId.Pursuit:     pursuitBonus += 1; break;
                    case SigilEffectId.Insight:     critBonus += 1; break;
                    case SigilEffectId.Vitality2:   hpHeal += 2; break;
                    case SigilEffectId.Vitality1:   hpHeal += 1; break;
                    case SigilEffectId.Shield3:     shieldAdd += 3; break;
                    case SigilEffectId.DiceTotal1:  diceBonus += 1; break;
                    case SigilEffectId.T1Reduce2:   t1Reduce += 2; break;
                    case SigilEffectId.LowHpAtk2:   lowHpAtkStacks += 1; break;
                    case SigilEffectId.Corrode3p:   corrodeStacks += 1; break;
                    // GoldOnBoss は CombatEnd 側で別判定
                }
            }

            if (hpHeal > 0)
                ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP, ctx.playerCurrentHP + hpHeal);
            if (shieldAdd > 0)
            {
                // 鉛の刻印: 破壊まで永続 (consShieldExpireTurn=-1)
                ctx.consShield += shieldAdd;
                ctx.shieldGainedTotal += shieldAdd; // 検証計測
                ctx.consShieldExpireTurn = -1;
                Debug.Log($"[Sigil] 鉛の刻印: シールド +{shieldAdd} (永続)");
            }
            if (pursuitBonus > 0)
                ctx.activeSigilBonuses.Add(new SigilBonus("sigil_pursuit", "pursuit", pursuitBonus));
            if (critBonus > 0)
                ctx.activeSigilBonuses.Add(new SigilBonus("sigil_insight", "insight", critBonus));
            if (diceBonus > 0)
            {
                float cur; ctx.currentBuffs.TryGetValue("diceBonus", out cur);
                ctx.currentBuffs["diceBonus"] = cur + diceBonus;
            }
            if (t1Reduce > 0)
            {
                ctx.playerFlatDamageReduction += t1Reduce;
                ctx.accumulatedValues["sigil_t1Reduce_remaining"] = t1Reduce; // T2 開始時に剥がす量
            }
            if (lowHpAtkStacks > 0)
                ctx.accumulatedValues["sigil_lowHpAtk"] = lowHpAtkStacks;
            if (corrodeStacks > 0)
                ctx.accumulatedValues["sigil_corrode"] = corrodeStacks;
        }

        /// <summary>ProcessPostRoll 完了直後、 player が勝った場合に呼ぶ。
        /// LowHpAtk2 / Corrode3p の per-roll 効果を fixedDamageToEnemy に積む。</summary>
        public static void ApplyOnRollWin(CombatContext ctx)
        {
            if (ctx == null) return;
            int lowHp = (int)ctx.GetAccumulated("sigil_lowHpAtk");
            if (lowHp > 0 && ctx.playerMaxHP > 0 && ctx.playerCurrentHP * 4 <= ctx.playerMaxHP)
                ctx.fixedDamageToEnemy += 2 * lowHp;

            int corrode = (int)ctx.GetAccumulated("sigil_corrode");
            if (corrode > 0 && ctx.enemyMaxHP > 0)
            {
                int per = Mathf.Max(1, Mathf.CeilToInt(ctx.enemyMaxHP * 0.03f));
                ctx.fixedDamageToEnemy += per * corrode;
            }
        }

        /// <summary>T2 開始時に T1Reduce を剥がす。 CombatManager の BeginTurn 後に呼ぶ。</summary>
        public static void ApplyOnTurnStart(CombatContext ctx)
        {
            if (ctx == null) return;
            int rem = (int)ctx.GetAccumulated("sigil_t1Reduce_remaining");
            if (rem > 0 && ctx.currentTurn >= 2)
            {
                ctx.playerFlatDamageReduction = System.Math.Max(0, ctx.playerFlatDamageReduction - rem);
                ctx.accumulatedValues["sigil_t1Reduce_remaining"] = 0;
            }
        }

        /// <summary>ボス勝利時のゴールド付与に使う、該当する刻印の発動数を返す。</summary>
        public static int CountGoldOnBossSigils(RunState run)
        {
            if (run?.passiveSigils == null) return 0;
            int n = 0;
            foreach (var sigil in CollectActive(run))
                if (sigil.effect == SigilEffectId.GoldOnBoss) n++;
            return n;
        }

        // ============================================================
        //  発動判定 (UI = グリッド隣接 / Bot = 確率)
        // ============================================================

        private static List<PassiveSigil> CollectActive(RunState run)
        {
            var result = new List<PassiveSigil>();
            if (run == null || run.passiveSigils == null) return result;

            var db = InventorySystem.ItemDatabase.Instance;

            // インベントリ内の所持レアリティセット (Bot 判定用)
            var ownedRarities = new HashSet<InventorySystem.ItemRarity>();
            if (db != null && run.ownedPassiveItems != null)
            {
                foreach (var id in run.ownedPassiveItems)
                {
                    var it = db.GetItem(id);
                    if (it != null) ownedRarities.Add(it.rarity);
                }
            }

            // GridManager がアクティブなら隣接判定、そうでなければ Bot モード
            var grid = UnityEngine.Object.FindObjectOfType<InventorySystem.GridManager>();
            bool useGrid = grid != null;

            int n = run.passiveSigils.Count;
            for (int i = 0; i < n; i++)
            {
                var sigil = run.passiveSigils[i];
                if (sigil == null) continue; // ユニーク等で刻印なし

                bool active;
                if (useGrid)
                {
                    string ownerId = (i < run.ownedPassiveItems.Count) ? run.ownedPassiveItems[i] : null;
                    active = IsActiveByGrid(grid, ownerId, sigil, db);
                }
                else
                {
                    if (!ownedRarities.Contains(sigil.requiredRarity)) active = false;
                    else active = UnityEngine.Random.value < BotActivationChance;
                }
                if (active) result.Add(sigil);
            }
            return result;
        }

        private static bool IsActiveByGrid(InventorySystem.GridManager grid, string ownerItemId,
            PassiveSigil sigil, InventorySystem.ItemDatabase db)
        {
            if (grid == null || db == null || string.IsNullOrEmpty(ownerItemId)) return false;
            var ownerData = db.GetItem(ownerItemId);
            if (ownerData == null) return false;

            for (int x = 0; x < InventoryConstants.GRID_WIDTH; x++)
            {
                for (int y = 0; y < InventoryConstants.GRID_HEIGHT; y++)
                {
                    var cell = grid.GetCell(x, y);
                    if (cell == null || !cell.IsOccupied) continue;
                    if (cell.OccupiedItem != ownerData) continue;

                    int dx = 0, dy = 0;
                    switch (sigil.direction)
                    {
                        case SigilDirection.Up:    dy = -1; break;
                        case SigilDirection.Down:  dy = 1; break;
                        case SigilDirection.Left:  dx = -1; break;
                        case SigilDirection.Right: dx = 1; break;
                    }
                    var neighbor = grid.GetCell(x + dx, y + dy);
                    if (neighbor != null && neighbor.IsOccupied && neighbor.OccupiedItem != null
                        && neighbor.OccupiedItem.rarity == sigil.requiredRarity)
                        return true;
                }
            }
            return false;
        }
    }
}
