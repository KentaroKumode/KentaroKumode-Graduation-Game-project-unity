using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>
    /// 全敵データを管理する静的データベース
    /// Resources/enemies.json から自動ロード
    /// </summary>
    public static class EnemyDatabase
    {
        private static Dictionary<string, EnemyData> database;
        private static bool initialized = false;

        /// <summary>
        /// データベースを初期化（必要時に自動呼び出し）
        /// </summary>
        public static void EnsureInitialized()
        {
            if (initialized) return;
            Load();
            initialized = true;
        }

        /// <summary>
        /// JSONからロード
        /// </summary>
        private static void Load()
        {
            database = new Dictionary<string, EnemyData>();

            var jsonAsset = Resources.Load<TextAsset>("enemies");
            if (jsonAsset == null)
            {
                Debug.LogError("[EnemyDatabase] Resources/enemies.json not found!");
                return;
            }

            var dataList = JsonUtility.FromJson<EnemyDataList>(jsonAsset.text);
            if (dataList?.enemies == null)
            {
                Debug.LogError("[EnemyDatabase] Failed to parse enemies.json!");
                return;
            }

            foreach (var enemy in dataList.enemies)
            {
                if (string.IsNullOrEmpty(enemy.id)) continue;
                database[enemy.id] = enemy;
            }

            Debug.Log($"[EnemyDatabase] Loaded {database.Count} enemies.");
            ApplyStatOverrides();
        }

        /// <summary>敵ステータスの上書き指定。 <c>"id:atk,id:atk,..."</c> 形式。 空なら何もしない。
        ///
        /// <para><b>較正スイープ専用の穴 (2026-09-14)。</b> 難易度の梯子を層ごとに振るには
        /// <c>baseAttack</c> を 1 層ずつ動かして測る必要があるが、 enemies.json を直接書くと
        /// 1 案ごとに Player の作り直しが要り、 §13-5 の「攻撃倍率の応答は極端に非線形・
        /// 0.1 刻みより粗く振るな」に応えられない。 ここに刺せば 1 バッチで全案を掃ける。</para>
        ///
        /// <para><b>製品の値は enemies.json が正本。</b> ここは空が既定で、
        /// 書き換えるのは <c>AutoRunner.enemyAttackSpec</c> だけ
        /// (実効値は <c>[実効状態]</c> に印字される)。 決まった値は json へ書き戻すこと。</para></summary>
        public static string AttackOverrideSpec = "";

        /// <summary>全敵の攻撃側ステータスへ掛ける倍率。 1.0 = 何もしない。
        ///
        /// <para><b>動かすのは 4 つ揃えて</b> ── <c>baseAttack</c> / <c>attackRollMin</c> /
        /// <c>attackRollMax</c> / <c>threat</c>。 2026-08-09 の再スケール (§13-5) が
        /// この 4 つを ×1.25 で動かした前例に合わせる。 一部だけ動かすと
        /// 「攻撃値は上がったが威圧は据え置き」のような歪んだ条件になる。</para>
        ///
        /// <para><b>応答は極端に非線形</b> (§13-5: ×1.00 → 73.4% / ×1.25 → 25.8% /
        /// ×1.35 → 18.4%)。 <b>0.1 刻みより粗く振らないこと。</b>
        /// <c>AttackOverrideSpec</c> の後に掛かるので、 層別の梯子を組んでから
        /// 全体の締め具合だけをこれで動かせる。</para></summary>
        public static float AttackScale = 1f;

        /// <summary>較正スイープ専用: 敵の最大 HP の上書き。 <c>"id:hp,id:hp,..."</c>。 空なら何もしない (2026-09-19)。
        /// 攻撃の上書き (<see cref="AttackOverrideSpec"/>) と同じく、 決まった値は enemies.json へ書き戻すこと。</summary>
        public static string HpOverrideSpec = "";

        /// <summary>較正スイープ専用: 全敵の最大 HP へ掛ける倍率 (2026-11-06)。 1.0 = 素のまま。
        /// 決まった値は enemies.json へ書き戻すこと (隠し倍率を製品に残さない)。</summary>
        public static float HpScale = 1f;

        private static void ApplyStatOverrides()
        {
            if (database == null) return;
            ApplySpec();
            ApplyScale();
            ApplyHpSpec();
            ApplyHeavySpec();
        }

        /// <summary>[測定 2026-11-14] 大技の周期の倍率と周期の上書き。 <c>"id:小技:大技[:周期],…"</c> (例 <c>"boss_layer7:0.7:1.75"</c>・<c>"*:0.8:3.0:4"</c>)。
        /// id に <c>*</c> で、 大技の周期を持つ敵 (enemies.json の heavyPeriod &gt; 0) 全部。 前から順に入れる (後の指定が勝つ)。 値は実数値 (倍率に掛ける倍率ではない)。
        /// 周期 (4 つ目) は省略できる (省略 = enemies.json の周期)。 1 以上の整数 (2 = チャージ・大技 / 4 = 小技・小技・チャージ・大技)。
        /// 灰燼の王の〈業火〉(heavyCycle = ember・周期は HP で 4 / 3) の周期は変えない (倍率は今までどおり上書きする)。
        /// 空 = enemies.json のまま。 書き換えるのは <c>AutoRunner.enemyHeavySpec</c> だけ。 決まった値は enemies.json へ書き戻すこと。
        /// 深さ 3 のボスの大技 (<see cref="BossParams.Depth3Heavy"/>・データではなく定数で足す) には効かない。 読み方は <see cref="HeavyCycle.ParseSpec"/>。</summary>
        public static string HeavyOverrideSpec = "";

        private static void ApplyHeavySpec()
        {
            if (string.IsNullOrEmpty(HeavyOverrideSpec)) return;
            var ci = System.Globalization.CultureInfo.InvariantCulture;
            var bad = new List<string>();
            foreach (var sp in HeavyCycle.ParseSpec(HeavyOverrideSpec, bad))
            {
                int n = 0, np = 0;
                foreach (var e in database.Values)
                {
                    if (e == null || e.heavyPeriod <= 0 || (sp.id != "*" && e.id != sp.id)) continue;
                    e.heavyLightMul = sp.light; e.heavyMul = sp.heavy; n++;
                    if (sp.period > 0)
                    {
                        if (e.heavyCycle == EnemyData.HeavyCycleEmber)
                        {
                            if (sp.id != "*") Debug.LogWarning($"[EnemyDatabase] {e.id} の周期は HP で決まる (〈業火〉) ので、 周期 {sp.period} の指定は使わない (倍率だけ上書き)");
                        }
                        else { e.heavyPeriod = sp.period; np++; }
                    }
                }
                if (n == 0) Debug.LogWarning($"[EnemyDatabase] 大技の倍率の上書き対象が居ない (大技の周期を持たない): {sp.id}");
                else Debug.Log($"[EnemyDatabase] 大技の倍率 {sp.id}: 小技 ×{sp.light.ToString(ci)}・大技 ×{sp.heavy.ToString(ci)} ({n} 体)"
                             + (sp.period > 0 ? $"・周期 {sp.period} ({np} 体)" : "・周期はデータのまま"));
            }
            foreach (var tok in bad) Debug.LogWarning($"[EnemyDatabase] 大技の倍率の指定が読めない: {tok}");
        }

        private static void ApplyHpSpec()
        {
            if (string.IsNullOrEmpty(HpOverrideSpec)) return;
            foreach (var tok in HpOverrideSpec.Split(','))
            {
                var kv = tok.Split(':');
                if (kv.Length != 2 || !int.TryParse(kv[1].Trim(), out int hp)) continue;
                if (!database.TryGetValue(kv[0].Trim(), out var e) || e == null)
                {
                    Debug.LogWarning($"[EnemyDatabase] HP 上書き対象が居ない: {kv[0]}");
                    continue;
                }
                e.maxHP = Mathf.Max(1, hp);
            }
        }

        private static void ApplyScale()
        {
            foreach (var e in database.Values)
            {
                if (e == null) continue;
                float hs = HpScale;
                float a = AttackScale;
                if (Mathf.Abs(hs - 1f) >= 0.0001f) e.maxHP = Mathf.Max(1, Mathf.RoundToInt(e.maxHP * hs));
                if (Mathf.Abs(a - 1f) < 0.0001f) continue;
                e.baseAttack     = Mathf.Max(0, Mathf.RoundToInt(e.baseAttack * a));
                e.attackRollMin  = Mathf.Max(0, Mathf.RoundToInt(e.attackRollMin * a));
                e.attackRollMax  = Mathf.Max(e.attackRollMin, Mathf.RoundToInt(e.attackRollMax * a));
                e.threat         = Mathf.Max(0, Mathf.RoundToInt(e.threat * a));
            }
            Debug.Log($"[EnemyDatabase] 攻撃側 ×{AttackScale:F2} を {database.Count} 体へ適用");
        }

        private static void ApplySpec()
        {
            if (string.IsNullOrEmpty(AttackOverrideSpec)) return;
            foreach (var tok in AttackOverrideSpec.Split(','))
            {
                var kv = tok.Split(':');
                if (kv.Length != 2) continue;
                string id = kv[0].Trim();
                if (!int.TryParse(kv[1].Trim(), out int atk)) continue;
                if (!database.TryGetValue(id, out var e) || e == null)
                {
                    Debug.LogWarning($"[EnemyDatabase] 上書き対象が居ない: {id}");
                    continue;
                }
                int before = e.baseAttack;
                e.baseAttack = atk;
                Debug.Log($"[EnemyDatabase] 上書き {id}: baseAttack {before} → {atk}");
            }
        }

        /// <summary>上書き指定を差してから読み直す。 <c>AutoRunner.Begin</c> が呼ぶ。</summary>
        public static void ReloadWithOverrides(string spec, float scale = 1f, string hpSpec = "", float hpScale = 1f, string heavySpec = "")
        {
            HeavyOverrideSpec = heavySpec ?? "";
            HpScale = hpScale;
            AttackOverrideSpec = spec ?? "";
            HpOverrideSpec = hpSpec ?? "";
            AttackScale = scale;
            initialized = false;
            EnsureInitialized();
        }

        /// <summary>
        /// IDから敵データを取得
        /// </summary>
        public static EnemyData Get(string enemyId)
        {
            EnsureInitialized();
            // **null/空は null を返す。** Dictionary.TryGetValue は null キーで
            //   ArgumentNullException("key") を投げるので、 「その敵は居ない」を表す
            //   null を素直に渡せる呼び出し側（2 体目が居ない = encounterEnemyB が null 等）が
            //   全部クラッシュ経路になっていた。 存在しない ID と同じ扱いにする。
            if (string.IsNullOrEmpty(enemyId)) return null;
            database.TryGetValue(enemyId, out var data);
            return data;
        }

        /// <summary>全ての敵の id (確認用の窓・演出テストが一覧に出す)。</summary>
        public static List<string> AllIds() { EnsureInitialized(); return new List<string>(database.Keys); }

        /// <summary>
        /// 指定階層に出現する敵一覧を取得
        /// </summary>
        public static List<EnemyData> GetByFloor(int floor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                if (kvp.Value.floor <= floor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// 指定フロア範囲 [minFloor, maxFloor] に出現する敵一覧。
        /// 低層雑魚が後半まで居座って道中を薄める問題を防ぐため、
        /// 抽選候補を直近フロアに限定する用途で使う。
        /// </summary>
        public static List<EnemyData> GetByFloorRange(int minFloor, int maxFloor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                int f = kvp.Value.floor;
                if (f >= minFloor && f <= maxFloor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// 指定階層にちょうど初登場する敵一覧
        /// </summary>
        public static List<EnemyData> GetNewOnFloor(int floor)
        {
            EnsureInitialized();
            var result = new List<EnemyData>();
            foreach (var kvp in database)
            {
                if (kvp.Value.floor == floor)
                    result.Add(kvp.Value);
            }
            return result;
        }

        /// <summary>
        /// ランダムに敵を1体取得
        /// </summary>
        public static EnemyData GetRandom()
        {
            EnsureInitialized();
            if (database == null || database.Count == 0) return null;
            var list = new List<EnemyData>(database.Values);
            return list[GameLoop.GameRng.RangeAuto("EnemyDatabase.1", 0, list.Count)];
        }

        /// <summary>
        /// データベースをリロード
        /// </summary>
        public static void Reload()
        {
            initialized = false;
            EnsureInitialized();
        }
    }
}
