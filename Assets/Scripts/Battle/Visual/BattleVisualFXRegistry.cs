using System.Collections.Generic;
using UnityEngine;

namespace Battle.Visual
{
    /// <summary>
    /// 武器ID/敵ID ごとの「攻撃エフェクト色」プレースホルダ。
    /// 実装の本筋は <see cref="PlaceholderClashFX"/> で生成される Quad の色だけを切り替える。
    /// 本番ドット絵が来たらここを prefab マップに差し替える。
    ///
    /// マッチング順:
    ///   (1) 明示的に登録された ID
    ///   (2) ID 接頭辞ヒューリスティック (sword/axe/spear/bow/staff/dagger)
    ///   (3) デフォルト色
    /// </summary>
    public static class BattleVisualFXRegistry
    {
        public struct WeaponFx
        {
            public Color color;
            public float size;
        }

        // 武器 ID → FX 色
        private static readonly Dictionary<string, WeaponFx> _weapon = new Dictionary<string, WeaponFx>();
        // 敵 ID → FX 色
        private static readonly Dictionary<string, WeaponFx> _enemy = new Dictionary<string, WeaponFx>();

        public static WeaponFx DefaultPlayer = new WeaponFx { color = new Color(0.9f, 0.95f, 1f), size = 4f };
        public static WeaponFx DefaultEnemy  = new WeaponFx { color = new Color(1f, 0.45f, 0.35f), size = 4f };

        public static WeaponFx ResolvePlayer(string weaponId)
        {
            if (!string.IsNullOrEmpty(weaponId))
            {
                if (_weapon.TryGetValue(weaponId, out var fx)) return fx;
                var heur = HeuristicByName(weaponId);
                if (heur.HasValue) return heur.Value;
            }
            return DefaultPlayer;
        }

        public static WeaponFx ResolveEnemy(string enemyId)
        {
            if (!string.IsNullOrEmpty(enemyId))
            {
                if (_enemy.TryGetValue(enemyId, out var fx)) return fx;
            }
            return DefaultEnemy;
        }

        public static void RegisterWeapon(string id, Color color, float size = 0.9f)
            => _weapon[id] = new WeaponFx { color = color, size = size };

        public static void RegisterEnemy(string id, Color color, float size = 0.9f)
            => _enemy[id] = new WeaponFx { color = color, size = size };

        private static WeaponFx? HeuristicByName(string id)
        {
            string s = id.ToLowerInvariant();
            if (s.Contains("sword") || s.Contains("blade") || s.Contains("katana"))
                return new WeaponFx { color = new Color(0.85f, 0.95f, 1f), size = 0.9f };
            if (s.Contains("axe"))
                return new WeaponFx { color = new Color(1f, 0.75f, 0.35f), size = 1.1f };
            if (s.Contains("spear") || s.Contains("lance"))
                return new WeaponFx { color = new Color(0.6f, 1f, 0.85f), size = 1.0f };
            if (s.Contains("bow"))
                return new WeaponFx { color = new Color(0.55f, 0.85f, 1f), size = 0.7f };
            if (s.Contains("staff") || s.Contains("rod") || s.Contains("wand"))
                return new WeaponFx { color = new Color(0.9f, 0.6f, 1f), size = 0.9f };
            if (s.Contains("dagger") || s.Contains("knife"))
                return new WeaponFx { color = new Color(1f, 0.9f, 0.4f), size = 0.6f };
            return null;
        }
    }
}
