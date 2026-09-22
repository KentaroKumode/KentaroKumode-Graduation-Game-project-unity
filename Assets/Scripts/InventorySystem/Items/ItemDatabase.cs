using UnityEngine;
using System.Collections.Generic;

namespace InventorySystem
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
            [ReadOnly] public Vector2Int size;
            
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
                    entry.size = Vector2Int.one;   // サイズは 2026-09-19 に廃止 (全品 1×1)
                    
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
            
            // サイズ (sizeX/sizeY) は 2026-09-19 に items.json から削除。 グリッド系のコードが読むので 1×1 を入れておく。
            item.size = new ItemSize { x = 1, y = 1 };
            
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

        /// <summary>
        /// レガシー ID → 現行 ID マップ (2026-07-17 家系 ID 統一に伴うセーブ救済)。
        /// GetItem/セーブロード時に自動変換。
        /// </summary>
        private static readonly Dictionary<string, string> LegacyIdMap = new Dictionary<string, string>
        {
            // Might 家系
            {"strength_belt","半歩深めの力帯"},
            // Fortitude
            {"leather_chestguard","戻り数なき革胸当て"},
            // Insight
            {"hawks_eye","測量師の片眼鏡"},
            // Vitality
            {"healing_ring","黙働きの治癒環"},
            // Counter
            {"leather_handguard","Counter_1"},
            // Pursuit
            {"myriad_arms_blade","烈刃"},
            // JP families
            {"利刃II","BladeEdge_2"},
            {"賞金首狩りIII","百一人切りの鉈"},
            {"重畳III","征服者の戦旗"},
            // ※「吸血I〜IV」は下の Lifesteal 行が正。 Grievous は 治癒阻害/治癒遮断 の 2 段のみで、
            //   Grievous_3 / _4 は items.json に存在しない。 誤った 吸血→Grievous 行はここから削除済み。
            {"不屈III","敗残兵の部隊章"},
            {"シールドバッシュIV","内から落ちた城盾"},
            {"貸与された時間IV","貸与された時間"},
            // Bludgeon (新)
            {"重い一撃","鉛入りの握斧"},{"破城槌","一人抱えの破城槌"},
            // Lifesteal (旧吸血I-IV, LifestealI-IV passive)
            {"吸血II","魂喰らいの血石"},
            // Grievous (旧 治癒阻害/治癒遮断, GrievousI/II passive)
            {"治癒阻害","Grievous_1"},

            // ===== 2026-09-22: id を表示名へ統一 (115 品) =====
            //   旧 id で保存されたセーブ・学習データ・ログをここで新名へ解決する。
            //   **消さないこと** ── 旧セーブを読んだ瞬間に所持品が黙って消える。
            {"Bludgeon_1","鉛入りの握斧"},
            {"Bludgeon_4","一人抱えの破城槌"},
            {"BountyHunter_3","百一人切りの鉈"},
            {"Conqueror_3","征服者の戦旗"},
            {"Fortitude_1","戻り数なき革胸当て"},
            {"Indomitable_3","敗残兵の部隊章"},
            {"Insight_2","測量師の片眼鏡"},
            {"LentTime_4","貸与された時間"},
            {"Lifesteal_2","魂喰らいの血石"},
            {"Might_1","半歩深めの力帯"},
            {"Pursuit_4","烈刃"},
            {"ShieldBash_4","内から落ちた城盾"},
            {"Vitality_1","黙働きの治癒環"},
            {"axe_t2","猛斧"},
            {"axe_t3","血塗りの戦斧"},
            {"axe_t4","血帝廻天"},
            {"backing_plate","肩当て"},
            {"change_tray","銭皿"},
            {"chevalier_rapier","シュヴァリエのレイピア"},
            {"coarse_whetstone","荒砥"},
            {"cons_def_1","木の護符"},
            {"cons_def_2","鉄の護符"},
            {"cons_def_3","銀の護符"},
            {"cons_def_4","惜別の護符"},
            {"cons_dmg_1","鬼火の油"},
            {"cons_dmg_2","燐の油"},
            {"cons_dmg_3","業火の膏薬"},
            {"cons_dmg_4","天火の膏薬"},
            {"cons_heal_1","小回復薬"},
            {"cons_heal_2","回復薬"},
            {"cons_heal_3","上回復薬"},
            {"cons_heal_4","完全回復薬"},
            {"cons_hope_1","湯気の立つ椀"},
            {"cons_hope_2","古い手紙"},
            {"cons_hope_3","凱旋の記憶"},
            {"cons_hope_4","希望の欠片"},
            {"dagger_t2","盗賊の短刀"},
            {"dagger_t3","処刑人の曲刀"},
            {"dagger_t4","ノクタリア"},
            {"double_struck","二度目の打金"},
            {"elite_bounty_receipt","首級控え"},
            {"gateward_stance","擦り減った小盾"},
            {"ryusen","竜閃"},
            {"shield_t2","鉄盾"},
            {"shield_t3","聖騎士の盾"},
            {"shield_t4","ドーンブリンガー"},
            {"sword_t2","鍛鉄の剣"},
            {"sword_t3","銀の長剣"},
            {"sword_t4","デュランダル"},
            {"trial_toss_token","合札"},
            {"uniq_class_dagger","仕込み刃"},
            {"uniq_class_oath","不抜の聖紋"},
            {"uniq_class_painkiller","痛覚遮断剤"},
            {"uniq_class_polish","瞬間研磨剤"},
            {"サーベル・ワルツ","剣舞譜「円舞」"},
            {"ヘルメスの靴","帝国伝令の三日靴"},
            {"一心不乱","千日振りの鉢巻"},
            {"万華の賽","遅れて応える万華賽"},
            {"不冷却","恒熱の炉壁"},
            {"不朽の熱","七日目の熾"},
            {"予備電源","雷壺"},
            {"余熱","朝にも熱い竈"},
            {"共鳴","百鳴りの共振箱"},
            {"匠の手控え","三割読めぬ匠手控え"},
            {"命脈","二拍目の心臓"},
            {"安らぎの靴","継ぎ革の旅靴"},
            {"巡礼の杖飾り","四歩返しの杖飾り"},
            {"巡礼者の杖","心軽めの巡礼杖"},
            {"急所穿ち","鎧縫いの針"},
            {"末那識","末那識の仮面"},
            {"止血阻害","医書裏の開き針"},
            {"毒の刃","抜かずの控え脇差"},
            {"毒の霧","主より長い香炉"},
            {"毒塗り","緑染みの下拵え小刀"},
            {"毒殺者","石抜きの毒指輪"},
            {"毒液噴射","跡地庭師の霧吹き"},
            {"永遠の燈","鑑定済みの無害灯"},
            {"溜め打ち","重さを増す拳套"},
            {"火花","焦げ柄の点火スパナ"},
            {"災厄の指輪","傷覚えの災環"},
            {"無心の刃","読めずの無心刃"},
            {"熱伝導","炉番の火床外套"},
            {"狂った計測器","十五年目の計測器"},
            {"狂宴の仮面","笑い主不明の仮面"},
            {"発火","余分に乾いた火口箱"},
            {"発電機","無限モーター"},
            {"盾解放","捨盾"},
            {"短絡","三度不良の銅線"},
            {"神聖の靴","聖路の白靴"},
            {"紅蓮の刃","手負い追いの山刀"},
            {"絞り出し","逆綴じの止血帯"},
            {"背水の狂刃","退路喰いの狂刃"},
            {"臨界圧","溢れを取る鋳型"},
            {"蓄電池","工廠の材料箱"},
            {"蛇の血","素手禁じの蛇血瓶"},
            {"血の一撃","末頁の血花太刀"},
            {"血の匂い","血日に澄む佩玉"},
            {"血の宿命","先血の腕輪"},
            {"裂傷の刃心","医家の反り刃"},
            {"触媒","呼雷粉"},
            {"賽振りの目隠し","二分刀"},
            {"輻射","過熱石"},
            {"連環の極み","連環の指輪"},
            {"連鎖爆発","帳簿外の連鎖爆発"},
            {"過負荷","過負荷チューナー"},
            {"道銭の帯封","切り分けた帯封"},
            {"鈍器","研ぎ知らずの銑鉄棍"},
            {"鋼の心臓","脈なしの鋼心臓"},
            {"防殻の一閃","鏡返しの小盾"},
            {"降下閾値","気短な早沸かし釜"},
            {"雷撃","逆さ避雷針"},
            {"頑迷","直さずの鉢金"},
            {"麻痺毒","岸上げ用の麻痺瓶"},
            {"黄金の天秤","戦果で傾く天秤"},
            {"黄金卿の剣","溶かし金の領主剣"},
        };

        /// <summary>レガシー ID を現行 ID に変換 (未マッピング品はそのまま返す)。</summary>
        public static string ResolveLegacyId(string itemId)
            => itemId != null && LegacyIdMap.TryGetValue(itemId, out var newId) ? newId : itemId;

        /// <summary>
        /// アイテムを取得 (レガシー ID 自動変換対応)。
        /// </summary>
        public CompleteItemData GetItem(string itemId)
        {
            Initialize();
            if (string.IsNullOrEmpty(itemId)) return null;
            // 現行 ID で直接ヒットするなら即返し
            if (itemDict != null && itemDict.TryGetValue(itemId, out ItemEntry entry))
                return entry.completeData;
            // レガシー ID → 現行 ID に変換して再検索
            if (LegacyIdMap.TryGetValue(itemId, out var newId)
                && itemDict != null && itemDict.TryGetValue(newId, out entry))
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
        
        /// <summary>
        /// アイテムのカードモデル（FBX）を取得
        /// </summary>
        public GameObject GetCardModel(string itemId)
        {
            var item = GetItem(itemId);
            return item?.fbxModel;
        }
    }
}
