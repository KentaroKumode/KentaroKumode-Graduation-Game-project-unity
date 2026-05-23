using System;
using System.Collections.Generic;
using UnityEngine;

namespace InventorySystem.Sigils
{
    /// <summary>
    /// 〈刻印〉非ユニークパッシブアイテム取得時にロールされるサブ効果。
    /// 発動条件: 「自身の指定方向(上下左右)の隣接マスに、指定レアリティのアイテムが置かれている」。
    /// 効果: 10種の中から1つランダム(Bronze級相当=メイン効果に追いつかない控えめサブ効果)。
    ///
    /// バランス指針:
    /// - 1ラン中にアイテム取得ごとに1つ付与 → 10〜20個並ぶ前提
    /// - 条件発動率 ≒ 1/16 程度 (実プレイなら配置最適化で2-3個発動が現実的)
    /// - Bot 計測時はインベントリ配置不明 → 「該当レアリティ所持なら 80%」 で簡易判定
    /// </summary>
    public enum SigilDirection { Up = 0, Down = 1, Left = 2, Right = 3 }

    /// <summary>10種の刻印効果。値で識別する。</summary>
    public enum SigilEffectId
    {
        Pursuit,           // 1: ロール勝利時 追撃 +1
        Insight,           // 2: 会心ダイス補正 +1
        Vitality2,         // 3: 戦闘開始時 HP +2 (盾辺)
        Shield3,           // 4: 戦闘開始時 シールド+3 (破壊まで永続) (鉛)
        DiceTotal1,        // 5: ダイス合計 +1 (吹き荒れ)
        GoldOnBoss,        // 6: ボス勝利時 +1G (守銭)
        Vitality1,         // 7: 戦闘開始時 HP +1 (小回復)
        T1Reduce2,         // 8: T1のみ被ダメ -2 (静寂)
        LowHpAtk2,         // 9: HP残25%以下のとき 与ダメ +2 (粘り)
        Corrode3p,         // 10: ロール勝利時 敵MaxHPの3%余剰ダメ (腐食)
    }

    /// <summary>1個のパッシブに紐づく刻印インスタンス(取得時1回ロール、ラン中不変)。</summary>
    [Serializable]
    public class PassiveSigil
    {
        public SigilEffectId effect;
        public SigilDirection direction;
        public InventorySystem.ItemRarity requiredRarity; // 条件で要求するレアリティ

        public string DisplayLabel => $"{EffectName(effect)}[{RarityKanji(requiredRarity)}・{DirectionKanji(direction)}]";

        public static string EffectName(SigilEffectId e) => e switch
        {
            SigilEffectId.Pursuit     => "追撃の刻印",
            SigilEffectId.Insight     => "集中の刻印",
            SigilEffectId.Vitality2   => "盾辺の刻印",
            SigilEffectId.Shield3     => "鉛の刻印",
            SigilEffectId.DiceTotal1  => "吹き荒れの刻印",
            SigilEffectId.GoldOnBoss  => "守銭の刻印",
            SigilEffectId.Vitality1   => "小回復の刻印",
            SigilEffectId.T1Reduce2   => "静寂の刻印",
            SigilEffectId.LowHpAtk2   => "粘りの刻印",
            SigilEffectId.Corrode3p   => "腐食の刻印",
            _ => e.ToString()
        };

        public static string DirectionKanji(SigilDirection d) => d switch
        {
            SigilDirection.Up    => "上",
            SigilDirection.Down  => "下",
            SigilDirection.Left  => "左",
            SigilDirection.Right => "右",
            _ => "?"
        };

        public static string RarityKanji(InventorySystem.ItemRarity r) => r switch
        {
            InventorySystem.ItemRarity.BRONZE    => "銅",
            InventorySystem.ItemRarity.SILVER    => "銀",
            InventorySystem.ItemRarity.GOLD      => "金",
            InventorySystem.ItemRarity.LEGENDARY => "伝説",
            _ => r.ToString()
        };
    }

    /// <summary>
    /// 刻印生成器: アイテム取得時に効果+条件をランダムロール。
    /// 条件は 4方向 × 4レアリティ = 16通り、効果は10種。
    /// </summary>
    public static class SigilRoller
    {
        // 条件で使うレアリティ(MYTHIC はパッシブとして極希少なので除外)
        private static readonly InventorySystem.ItemRarity[] ConditionRarities = new[]
        {
            InventorySystem.ItemRarity.BRONZE,
            InventorySystem.ItemRarity.SILVER,
            InventorySystem.ItemRarity.GOLD,
            InventorySystem.ItemRarity.LEGENDARY,
        };

        private static readonly SigilEffectId[] AllEffects = (SigilEffectId[])Enum.GetValues(typeof(SigilEffectId));
        private static readonly SigilDirection[] AllDirections = (SigilDirection[])Enum.GetValues(typeof(SigilDirection));

        public static PassiveSigil Roll()
        {
            return new PassiveSigil
            {
                effect = AllEffects[UnityEngine.Random.Range(0, AllEffects.Length)],
                direction = AllDirections[UnityEngine.Random.Range(0, AllDirections.Length)],
                requiredRarity = ConditionRarities[UnityEngine.Random.Range(0, ConditionRarities.Length)],
            };
        }
    }
}
