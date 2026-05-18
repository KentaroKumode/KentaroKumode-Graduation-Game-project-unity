using InventorySystem;

namespace GameLoop
{
    /// <summary>
    /// 取得した武器/ダイスを RunState に「自動装備」する軽量ロジック（期待値ベース）。
    ///
    /// 戦闘モデル: 毎ターン両者がダイス合計を振り、高い方がロール勝利→相手にダメージ。
    /// よって装備の優劣は「1ロールの期待合計」で評価するのが妥当。
    ///
    ///   - ダイス: 出目テーブル(diceFaces)を差し替える。1ダイスの期待値 = mean(faces)。
    ///             未装備時は武器の素の面 (1..diceMax) ＝ 期待値 (diceMax+1)/2。
    ///   - 武器  : 振る個数(count)・素の面(diceMax)・会心率(criticalRate)を決める。
    ///             カスタムダイス装備中は faces が面を上書きするため、武器の差は
    ///             「振り個数」と「会心」に集約される。
    ///
    /// 武器EV = count × max(素の面平均, 装備中ダイスの面平均) + criticalRate
    /// ダイス は「1ダイス面平均」が現在値を上回る時のみ装備（武器素面より低い
    /// ダイスを掴んで弱体化するのを防ぐ）。
    /// </summary>
    public static class Loadout
    {
        private const float Eps = 0.01f;

        // デフォルト（武器未装備）の戦闘値: 2d6 / 会心1
        private const int DefWeaponCount = 2;
        private const int DefWeaponDiceMax = 6;
        private const int DefWeaponCrit = 1;

        /// <summary>武器なら期待値が上なら、ダイスなら面平均が上なら自動装備する。</summary>
        public static void TryAutoEquip(RunState run, string itemId)
        {
            if (run == null || string.IsNullOrEmpty(itemId)) return;
            var item = ItemDatabase.Instance?.GetItem(itemId);
            if (item == null) return;

            if (item.category == ItemCategory.Weapon && item.hasWeaponStats && item.weaponDice != null)
            {
                if (WeaponEV(item, run) > CurrentWeaponEV(run) + Eps)
                    run.equippedWeaponId = itemId;
            }
            else if (item.category == ItemCategory.Dice && item.diceFaces != null && item.diceFaces.Length > 0)
            {
                if (Mean(item.diceFaces) > CurrentPerDieMean(run) + Eps)
                    run.equippedDiceId = itemId;
            }
        }

        // ===== 期待値計算 =====

        /// <summary>1ダイスあたりの現在期待値。カスタムダイス装備中はその面平均、
        /// 無ければ装備中(or デフォルト)武器の素の面平均 (diceMax+1)/2。</summary>
        private static float CurrentPerDieMean(RunState run)
        {
            float dm = EquippedDiceMean(run);
            if (dm > 0f) return dm;
            return (CurrentWeaponDiceMax(run) + 1) / 2f;
        }

        /// <summary>装備中カスタムダイスの面平均。無ければ 0。</summary>
        private static float EquippedDiceMean(RunState run)
        {
            if (string.IsNullOrEmpty(run.equippedDiceId)) return 0f;
            var d = ItemDatabase.Instance?.GetItem(run.equippedDiceId);
            return (d?.diceFaces != null && d.diceFaces.Length > 0) ? Mean(d.diceFaces) : 0f;
        }

        private static int CurrentWeaponDiceMax(RunState run)
        {
            var w = string.IsNullOrEmpty(run.equippedWeaponId)
                ? null : ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
            return (w != null && w.hasWeaponStats && w.weaponDice != null)
                ? w.weaponDice.maxValue : DefWeaponDiceMax;
        }

        /// <summary>武器の期待値: 振り個数 × max(素の面平均, 装備中ダイス面平均) + 会心率。</summary>
        private static float WeaponEV(CompleteItemData w, RunState run)
        {
            if (w == null || !w.hasWeaponStats || w.weaponDice == null) return -1f;
            float nativeMean = (w.weaponDice.maxValue + 1) / 2f;
            float diceMean = EquippedDiceMean(run);            // 装備中ダイスがあれば面を上書き
            float perDie = System.Math.Max(nativeMean, diceMean);
            return w.weaponDice.count * perDie + w.criticalRate;
        }

        private static float CurrentWeaponEV(RunState run)
        {
            if (!string.IsNullOrEmpty(run.equippedWeaponId))
            {
                var cur = ItemDatabase.Instance?.GetItem(run.equippedWeaponId);
                if (cur != null && cur.hasWeaponStats && cur.weaponDice != null)
                    return WeaponEV(cur, run);
            }
            // デフォルト武器 2d6/会心1
            float nativeMean = (DefWeaponDiceMax + 1) / 2f;
            float perDie = System.Math.Max(nativeMean, EquippedDiceMean(run));
            return DefWeaponCount * perDie + DefWeaponCrit;
        }

        private static float Mean(int[] faces)
        {
            if (faces == null || faces.Length == 0) return 0f;
            long s = 0;
            for (int i = 0; i < faces.Length; i++) s += faces[i];
            return (float)s / faces.Length;
        }
    }
}
