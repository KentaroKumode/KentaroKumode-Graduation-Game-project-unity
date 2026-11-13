using UnityEngine;
using System.Collections.Generic;

namespace ItemSystem
{
    /// <summary>
    /// ReadOnly属性（エディター用）
    /// </summary>
    public class ReadOnlyAttribute : PropertyAttribute { }

    /// <summary>
    /// 統合型アイテムデータベース
    /// JSON読み込み + FBX管理を一元化
    /// </summary>
    [CreateAssetMenu(fileName = "ItemDatabase", menuName = "Inventory/Item Database")]
    public class ItemDatabase : ScriptableObject
    {
        [System.Serializable]
        public class ItemEntry
        {
            [Header("JSON Data")]
            [ReadOnly] public string itemId;
            [ReadOnly] public string displayName;
            [ReadOnly] public string description;
            [ReadOnly] public ItemCategory category;
            [ReadOnly] public ItemRarity rarity;
            
            [Header("Unity Assets (手動設定)")]
            [Tooltip("3Dカードモデル")] 
            public GameObject cardModel;
            [Tooltip("アイコンスプライト")] 
            public Sprite icon;
            [Tooltip("装備マークプレハブ")] 
            public GameObject equipMarkPrefab;
            
            [HideInInspector] public CompleteItemData completeData;
        }
        
        [Header("データソース")]
        [Tooltip("JSONファイルまたはフォルダを設定")]
        public TextAsset itemsJsonFile;
        
        [Header("登録済みアイテム")]
        public List<ItemEntry> items = new List<ItemEntry>();
        
        private Dictionary<string, ItemEntry> itemDict;
        private static ItemDatabase instance;
        
        public static ItemDatabase Instance 
        { 
            get 
            {
                if (instance == null)
                {
                    // 1. Resourcesフォルダから検索
                    instance = Resources.Load<ItemDatabase>("ItemDatabase");
                    
                    // 2. 見つからない場合、全Resourcesフォルダを検索
                    if (instance == null)
                    {
                        var all = Resources.LoadAll<ItemDatabase>("");
                        if (all.Length > 0)
                        {
                            instance = all[0];
                        }
                    }
                    
                    #if UNITY_EDITOR
                    // 3. エディタ専用: AssetDatabaseから検索
                    if (instance == null)
                    {
                        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:ItemDatabase");
                        if (guids.Length > 0)
                        {
                            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                            instance = UnityEditor.AssetDatabase.LoadAssetAtPath<ItemDatabase>(path);
                            
                            if (instance != null)
                            {
                                Debug.LogWarning($"[ItemDatabase] Resourcesフォルダ外で発見: {path}\n" +
                                    "Tools > Inventory System > Fix ItemDatabase Location で修正できます");
                            }
                        }
                    }
                    #endif
                    
                    // 4. 初期化
                    if (instance != null)
                    {
                        instance.Initialize();
                    }
                }
                return instance;
            } 
        }
        
        /// <summary>
        /// 初期化（辞書構築 + completeData がなければJSONから再構築）
        /// </summary>
        public void Initialize()
        {
            // completeData はシリアライズされないため、ランタイム起動時に再構築が必要
            bool needsRebuild = items.Count > 0 && items[0].completeData == null;

            if (needsRebuild && itemsJsonFile != null)
            {
                itemDict = null; // LoadFromJson 内で再構築される
                LoadFromJson();
                return;
            }

            if (itemDict == null)
            {
                itemDict = new Dictionary<string, ItemEntry>();
                foreach (var item in items)
                {
                    if (!string.IsNullOrEmpty(item.itemId))
                    {
                        itemDict[item.itemId] = item;
                    }
                }
            }
        }
        
        /// <summary>
        /// JSONからアイテムを読み込み・登録
        /// </summary>
        public void LoadFromJson()
        {
            if (itemsJsonFile == null)
            {
                Debug.LogWarning("[ItemDatabase] JSON file not assigned");
                return;
            }
            
            try
            {
                var jsonData = JsonUtility.FromJson<ItemDataListJson>(itemsJsonFile.text);
                if (jsonData?.items == null)
                {
                    Debug.LogError("[ItemDatabase] Failed to parse JSON");
                    return;
                }
                
                // 既存エントリを辞書化（FBX割り当てを保持するため）
                var existingEntries = new Dictionary<string, ItemEntry>();
                foreach (var existingItem in items)
                {
                    if (!string.IsNullOrEmpty(existingItem.itemId))
                    {
                        existingEntries[existingItem.itemId] = existingItem;
                    }
                }
                
                // 共有パッシブ表 (武器のラダー)
                _skillDefs = new Dictionary<string, SkillDefJson>();
                if (jsonData.skills != null)
                    foreach (var d in jsonData.skills)
                        if (d != null && !string.IsNullOrEmpty(d.id)) _skillDefs[d.id] = d;

                // 新しいリストを作成
                items.Clear();
                
                foreach (var jsonItem in jsonData.items)
                {
                    ItemEntry entry;
                    
                    // 既存エントリがあればFBX割り当てを保持
                    if (existingEntries.TryGetValue(jsonItem.id, out var existing))
                    {
                        entry = existing;
                    }
                    else
                    {
                        entry = new ItemEntry();
                    }
                    
                    // JSONデータを設定
                    entry.itemId = jsonItem.id;
                    entry.displayName = jsonItem.name;
                    entry.description = jsonItem.description;
                    
                    // Enum変換
                    System.Enum.TryParse(jsonItem.category, true, out entry.category);
                    System.Enum.TryParse(jsonItem.rarity, true, out entry.rarity);
                    
                    // CompleteItemDataを作成
                    entry.completeData = ConvertToCompleteItemData(jsonItem, entry);
                    
                    items.Add(entry);
                }
                
                // 辞書を再構築
                itemDict = null;
                Initialize();
                
                Debug.Log($"[ItemDatabase] Loaded {items.Count} items from JSON");
                
                #if UNITY_EDITOR
                UnityEditor.EditorUtility.SetDirty(this);
                #endif
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ItemDatabase] Error loading JSON: {e.Message}");
            }
        }
        
        /// <summary>
        /// JSONデータをCompleteItemDataに変換
        /// </summary>
        private CompleteItemData ConvertToCompleteItemData(ItemDataJson jsonItem, ItemEntry entry)
        {
            var item = new CompleteItemData();
            
            // 基本データ設定（プロパティではなくフィールドに直接設定）
            item.internalName = jsonItem.id;
            item.displayName = jsonItem.name;
            item.description = jsonItem.description;
            item.flavorText = jsonItem.flavorText;
            
            // Enum変換
            System.Enum.TryParse(jsonItem.category, true, out item.category);
            System.Enum.TryParse(jsonItem.rarity, true, out item.rarity);

            // 構造フィールド (2026-09-22)。 **id からパースし直さないこと** ── 詳細は ItemDataJson 側。
            item.family     = jsonItem.family ?? "";
            item.tier       = jsonItem.tier;
            item.consFamily = jsonItem.consFamily ?? "";
            item.unique     = jsonItem.unique;
            item.axis       = jsonItem.axis ?? "";

            // サイズ (sizeX/sizeY) は 2026-09-19 に items.json から削除、 型ごと 2026-10-28 に削除 (グリッドの廃止)。

            // Unity Assets設定
            item.fbxModel = entry.cardModel;
            item.icon = entry.icon;
            item.equipMarkPrefab = entry.equipMarkPrefab;
            
            // 価格設定（中央価格から±25%の範囲で購入/売却額を算出）
            // 1/5 デノミ: 表示価格はすべて 1/5 (basePrice 自体は別用途で保持)
            if (jsonItem.basePrice > 0)
            {
                // **2026-08-10: 1/5 デノミの除算を撤去。**
                //   items.json 側が既に小さい価格帯 (武器 T1..T4 = 8/12/16/20) へ書き直されて
                //   いるのに、 ここでさらに 1/5 していた ＝ 二重デノミ。 結果、 店頭価格が
                //   1〜5G に潰れ、 **パーセント倍率が CeilToInt で表現不能**になっていた
                //   (名目 ×1.01〜×1.20 がすべて「全品 +1G」＝実効 ×1.386 に化ける)。
                //   これが挑戦デバフ〈搾取経済〉に軽い段を作れなかった直接の原因。
                int buyMin  = Mathf.Max(1, Mathf.RoundToInt(jsonItem.basePrice * 1.00f));
                int buyMax  = Mathf.Max(1, Mathf.RoundToInt(jsonItem.basePrice * 1.25f));
                int sellMin = Mathf.Max(1, Mathf.RoundToInt(jsonItem.basePrice * 0.50f));
                int sellMax = Mathf.Max(1, Mathf.RoundToInt(jsonItem.basePrice * 0.75f));
                item.buyPrice = new PriceRange { min = buyMin, max = buyMax };
                item.sellPrice = new PriceRange { min = sellMin, max = sellMax };
            }
            // 売値 0G の品 (2026-11-13)。 買値と sellPrice の幅は basePrice のまま (CompleteItemData.basePrice を変えない)
            item.noSellValue = jsonItem.noSellValue;
            
            // 武器ダイス設定（JSON の diceCount / diceMax から生成）
            if (jsonItem.diceCount > 0)
            {
                item.weaponDice = new DiceConfig 
                { 
                    count = jsonItem.diceCount,
                    minValue = 1,
                    maxValue = jsonItem.diceMax 
                };
            }
            
            // 会心率 (%)。 5% 刻み・上限 50%
            item.critRatePct = Mathf.Clamp(jsonItem.critRatePct, 0f, GameLoop.GameManager.MaxWeaponCritPct);

            // 武器の素火力（#2 案A'）
            item.attackPower = Mathf.Max(0, jsonItem.attackPower);
            // 武器の防御・貫通 (ADR-0013)
            item.defense = jsonItem.defense;
            item.armorPen = jsonItem.armorPen;

            // ダイス面設定（Diceカテゴリ）
            if (jsonItem.diceFaces != null && jsonItem.diceFaces.Length > 0)
            {
                item.diceFaces = jsonItem.diceFaces;
            }
            // ADR-0010〈無銘の賽〉: 端子役を成立させないダイス。
            item.suppressTerminalRoles = jsonItem.suppressTerminalRoles;
            
            // 武器ロール設定
            item.roleName = jsonItem.roleName ?? "";
            item.roleDescription = jsonItem.roleDescription ?? "";
            
            // パッシブスキル設定
            // 2026-09-19: パッシブ品は品そのものが 1 つのパッシブ (id = 品の id / 説明 = 品の説明)。
            //   武器は共有パッシブ表の id を並べる。 どちらもここで PassiveSkill に展開するので、
            //   下流 (PassiveSkillManager / レジストリ / UI) は従来どおり passiveSkills を読めばよい。
            item.passiveSkills = new System.Collections.Generic.List<PassiveSkill>();
            if (!string.IsNullOrEmpty(jsonItem.skill))
            {
                item.passiveSkills.Add(new PassiveSkill(jsonItem.id, jsonItem.skill, StripPeriod(jsonItem.description))
                {
                    stats = (jsonItem.stats != null && jsonItem.stats.Length > 0) ? jsonItem.stats : null,
                });
            }
            if (jsonItem.skills != null)
            {
                foreach (var sid in jsonItem.skills)
                {
                    if (_skillDefs == null || !_skillDefs.TryGetValue(sid, out var d))
                    {
                        Debug.LogError($"[ItemDatabase] {jsonItem.id}: skills 表に無いパッシブ '{sid}'");
                        continue;
                    }
                    item.passiveSkills.Add(new PassiveSkill(d.id, d.name, d.description)
                    {
                        stats = (d.stats != null && d.stats.Length > 0) ? d.stats : null,
                    });
                }
            }
            
            return item;
        }
        
        /// <summary>items.json 最上位の共有パッシブ表。 LoadFromJson が作り直す。</summary>
        private Dictionary<string, SkillDefJson> _skillDefs;

        /// <summary>パッシブ説明は旧データで末尾の「。」を持たなかった (UI が補う) ので揃える。</summary>
        private static string StripPeriod(string s) => string.IsNullOrEmpty(s) ? s : s.TrimEnd('。');

        /// <summary>画面に出す品名。 items.json の name を引き、 無ければ id をそのまま返す (id 定数から表示名を引く口・#133)。</summary>
        public static string DisplayNameOf(string itemId)
        {
            var data = string.IsNullOrEmpty(itemId) ? null : Instance?.GetItem(itemId);
            return DisplayNameOf(data, itemId);
        }

        /// <summary>引いた品 <paramref name="data"/> の画面に出す品名。 null か name が空なら <paramref name="fallbackId"/> を返す
        /// (品を引き済みの画面が、 同じ品をもう一度引かずに使う口・2026-11-12)。</summary>
        public static string DisplayNameOf(CompleteItemData data, string fallbackId)
            => data != null && !string.IsNullOrEmpty(data.displayName) ? data.displayName : fallbackId;

        /// <summary>
        /// アイテムを取得 (今の id だけを引く)。 無い id は警告を出して null ── 呼び側は null を「その品は無い」として扱う。
        /// <para>旧 id の対応表 (LegacyIdMap) は 2026-11-14 に削除 (発売前・後方互換なし・docs/GAME.md §24)。
        /// 古い中断セーブ・学習データに残る改名前の名前は、 ここで見つからない品になる。</para>
        /// </summary>
        public CompleteItemData GetItem(string itemId)
        {
            Initialize();
            if (string.IsNullOrEmpty(itemId)) return null;
            if (itemDict != null && itemDict.TryGetValue(itemId, out ItemEntry entry))
                return entry.completeData;
            Debug.LogWarning($"[ItemDatabase] Item not found: {itemId}");
            return null;
        }
        
        /// <summary>家系と段から進行武器を引く (2026-09-22)。 無ければ null。
        ///
        /// <para><b>id を組み立てない。</b> 以前は <c>$"{family}_t{tier}"</c> で id を作って
        /// 引いていたが、 id は表示名になったので綴りから作れない。 <c>family</c> /
        /// <c>tier</c> フィールドを走査する ── 12 品しかないので線形で足りる。</para></summary>
        public CompleteItemData GetWeapon(string family, int tier)
        {
            Initialize();
            if (string.IsNullOrEmpty(family) || tier <= 0) return null;
            foreach (var entry in items)
            {
                var d = entry.completeData;
                if (d != null && d.family == family && d.tier == tier) return d;
            }
            return null;
        }

        /// <summary>系統と段から消費アイテムを引く (2026-09-22)。 無ければ null。
        /// <see cref="GetWeapon"/> と同じ理由で id を組み立てない。</summary>
        public CompleteItemData GetConsumable(string consFamily, int tier)
        {
            Initialize();
            if (string.IsNullOrEmpty(consFamily) || tier <= 0) return null;
            foreach (var entry in items)
            {
                var d = entry.completeData;
                if (d != null && d.consFamily == consFamily && d.tier == tier) return d;
            }
            return null;
        }

        /// <summary>
        /// 全アイテムを取得
        /// </summary>
        public List<CompleteItemData> GetAllItems()
        {
            Initialize();
            
            var result = new List<CompleteItemData>();
            foreach (var entry in items)
            {
                if (entry.completeData != null)
                {
                    result.Add(entry.completeData);
                }
            }
            return result;
        }
        
        /// <summary>
        /// カテゴリーでフィルタリング
        /// </summary>
        public List<CompleteItemData> GetItemsByCategory(ItemCategory category)
        {
            Initialize();
            
            var result = new List<CompleteItemData>();
            foreach (var entry in items)
            {
                if (entry.completeData != null && entry.category == category)
                {
                    result.Add(entry.completeData);
                }
            }
            return result;
        }
        
    }
}
