using System.Collections.Generic;
using UnityEngine;
using InventorySystem.PassiveSkills;

namespace InventorySystem.Sigils
{
    /// <summary>
    /// 刻印の隣接判定と効果解決を行うマネージャー
    /// 
    /// 戦闘開始時に呼び出し、グリッド上で武器に隣接する刻印を検出し、
    /// CombatContext.activeSigilBonuses に効果を登録する
    /// 
    /// 使い方:
    /// <code>
    /// SigilManager.Instance.ResolveSigils(gridManager, ctx);
    /// </code>
    /// </summary>
    public class SigilManager : MonoBehaviour
    {
        private static SigilManager instance;
        public static SigilManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("[SigilManager]");
                    instance = go.AddComponent<SigilManager>();
                    DontDestroyOnLoad(go);
                }
                return instance;
            }
        }

        void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
        }

        /// <summary>
        /// グリッド上の武器に隣接する刻印を検出し、CombatContextにボーナスを登録
        /// </summary>
        public void ResolveSigils(Grid.GridManager gridManager, CombatContext ctx)
        {
            if (gridManager == null || ctx == null) return;

            ctx.activeSigilBonuses.Clear();

            // 全セルをスキャンして武器のルートセル（左上）を収集
            var weaponRoots = new HashSet<Vector2Int>();
            var weaponCells = new Dictionary<Vector2Int, CompleteItemData>();

            for (int y = 0; y < Core.InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < Core.InventoryConstants.GRID_WIDTH; x++)
                {
                    var cell = gridManager.GetCell(x, y);
                    if (cell == null || !cell.IsOccupied) continue;
                    var item = cell.OccupiedItem;
                    if (item == null) continue;

                    // 武器判定: passiveSkills を持つアイテム（刻印以外）
                    if (item.passiveSkills != null && item.passiveSkills.Count > 0 && !IsSigil(item))
                    {
                        var root = new Vector2Int(x, y);
                        if (weaponRoots.Add(root))
                        {
                            // 武器が占有する全セルを記録
                            int w = item.size != null ? item.size.x : 1;
                            int h = item.size != null ? item.size.y : 1;
                            for (int dy = 0; dy < h; dy++)
                                for (int dx = 0; dx < w; dx++)
                                    weaponCells[new Vector2Int(x + dx, y + dy)] = item;
                        }
                    }
                }
            }

            // 4方向オフセット（上下左右）
            var offsets = new Vector2Int[] {
                new Vector2Int(0, -1), new Vector2Int(0, 1),
                new Vector2Int(-1, 0), new Vector2Int(1, 0)
            };

            // 全セルを再スキャンして刻印を検出
            var processedSigils = new HashSet<string>();
            for (int y = 0; y < Core.InventoryConstants.GRID_HEIGHT; y++)
            {
                for (int x = 0; x < Core.InventoryConstants.GRID_WIDTH; x++)
                {
                    var cell = gridManager.GetCell(x, y);
                    if (cell == null || !cell.IsOccupied) continue;
                    var item = cell.OccupiedItem;
                    if (item == null || !IsSigil(item)) continue;

                    // 同一刻印を重複処理しない
                    string key = $"{item.internalName}_{x}_{y}";
                    if (!processedSigils.Add(key)) continue;

                    // 隣接セルに武器があるか確認
                    bool adjacent = false;
                    var pos = new Vector2Int(x, y);
                    foreach (var offset in offsets)
                    {
                        if (weaponCells.ContainsKey(pos + offset))
                        {
                            adjacent = true;
                            break;
                        }
                    }

                    if (!adjacent) continue;

                    // 刻印の効果をCombatContextに登録
                    var sigilData = GetSigilData(item.internalName);
                    if (sigilData?.effects == null) continue;

                    foreach (var effect in sigilData.effects)
                    {
                        ctx.activeSigilBonuses.Add(new SigilBonus(
                            sigilData.id, effect.type, effect.value));
                    }

                    Debug.Log($"[SigilManager] Sigil '{sigilData.displayName}' activated at ({x},{y})");
                }
            }

            // ボーナスを集計してコンテキストに適用
            ApplySigilBonuses(ctx);
        }

        /// <summary>
        /// 集計した刻印ボーナスをCombatContextのフィールドに反映
        /// </summary>
        private void ApplySigilBonuses(CombatContext ctx)
        {
            foreach (var bonus in ctx.activeSigilBonuses)
            {
                switch (bonus.bonusType)
                {
                    case "pursuit":
                        ctx.pursuitDamage += bonus.value;
                        break;
                    case "counter":
                        ctx.fixedDamageToEnemy += bonus.value;
                        break;
                    case "might":
                        // OnPreDealDamage時に加算するためバフとして予約
                        float mightCurrent = 0f;
                        ctx.currentBuffs.TryGetValue("damageBonus", out mightCurrent);
                        ctx.currentBuffs["damageBonus"] = mightCurrent + bonus.value;
                        break;
                    case "fortitude":
                        // ダメージ軽減はパッシブスキル側で処理するためバフとして予約
                        float fortCurrent = 0f;
                        ctx.currentBuffs.TryGetValue("damageReduction", out fortCurrent);
                        ctx.currentBuffs["damageReduction"] = fortCurrent + bonus.value;
                        break;
                    case "insight":
                        float critCurrent = 0f;
                        ctx.currentBuffs.TryGetValue("criticalBonus", out critCurrent);
                        ctx.currentBuffs["criticalBonus"] = critCurrent + bonus.value;
                        break;
                    case "vitality":
                        ctx.playerCurrentHP = System.Math.Min(ctx.playerMaxHP,
                            ctx.playerCurrentHP + bonus.value);
                        break;
                    case "threatReduce":
                        ctx.enemyThreat = System.Math.Max(0, ctx.enemyThreat - bonus.value);
                        break;
                }
            }
        }

        /// <summary>アイテムが刻印かどうか判定（category="sigil" or internalName含む）</summary>
        private bool IsSigil(CompleteItemData item)
        {
            if (item == null) return false;
            return item.internalName != null && item.internalName.StartsWith("sigil_");
        }

        // ===== 刻印データ管理 =====
        private Dictionary<string, SigilItemData> sigilDatabase;

        /// <summary>刻印データをID指定で取得</summary>
        public SigilItemData GetSigilData(string sigilId)
        {
            if (sigilDatabase == null) LoadSigilDatabase();
            sigilDatabase.TryGetValue(sigilId, out var data);
            return data;
        }

        private void LoadSigilDatabase()
        {
            sigilDatabase = new Dictionary<string, SigilItemData>();
            var json = Resources.Load<TextAsset>("sigils");
            if (json == null) return;

            var list = JsonUtility.FromJson<SigilDataList>(json.text);
            if (list?.sigils == null) return;

            foreach (var s in list.sigils)
                sigilDatabase[s.id] = s;
        }

        void OnDestroy() { if (instance == this) instance = null; }
    }

    /// <summary>sigils.json のルートラッパー</summary>
    [System.Serializable]
    public class SigilDataList
    {
        public List<SigilItemData> sigils;
    }
}
