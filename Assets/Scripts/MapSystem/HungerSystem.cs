using System;
using UnityEngine;

namespace MapSystem
{
    /// <summary>
    /// 空腹度管理。移動ごとに1消費し、0の状態で移動するとMaxHP割合ダメージ。
    /// 前哨基地で全回復、アイテムでも回復可能。
    /// </summary>
    public class HungerSystem
    {
        public int Current { get; private set; }
        public int Max { get; private set; }

        /// <summary>空腹ダメージ = MaxHP × この倍率</summary>
        public float starvationDamageRatio = 0.1f;

        public event Action<int, int> OnHungerChanged;     // (current, max)
        public event Action<int> OnStarvationDamage;       // damage amount

        public bool IsStarving => Current <= 0;

        public void Initialize(int maxHunger)
        {
            Max = maxHunger;
            Current = maxHunger;
            OnHungerChanged?.Invoke(Current, Max);
        }

        /// <summary>
        /// 移動時に呼ぶ。空腹(=0)状態で移動すると MaxHP% ダメージ。
        /// その後空腹度を1消費。
        /// </summary>
        /// <returns>空腹ダメージ量（空腹でなければ0）</returns>
        public int OnMove(int playerMaxHP)
        {
            int damage = 0;

            if (Current <= 0)
            {
                damage = Mathf.Max(1, Mathf.FloorToInt(playerMaxHP * starvationDamageRatio));
                OnStarvationDamage?.Invoke(damage);
            }

            Current = Mathf.Max(0, Current - 1);
            OnHungerChanged?.Invoke(Current, Max);

            return damage;
        }

        /// <summary>指定量回復</summary>
        public void Restore(int amount)
        {
            Current = Mathf.Min(Max, Current + amount);
            OnHungerChanged?.Invoke(Current, Max);
        }

        /// <summary>全回復</summary>
        public void RestoreFull()
        {
            Current = Max;
            OnHungerChanged?.Invoke(Current, Max);
        }

        /// <summary>テスト専用: Current を直接セット（範囲クランプあり）</summary>
        public void SetCurrentForTest(int value)
        {
            Current = Mathf.Clamp(value, 0, Max);
            OnHungerChanged?.Invoke(Current, Max);
        }
    }
}
