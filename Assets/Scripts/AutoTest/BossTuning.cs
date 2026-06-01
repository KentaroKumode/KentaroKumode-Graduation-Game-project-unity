using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CombatSystem;

namespace AutoTest
{
    /// <summary>
    /// ボス難易度の調整パラメータ。 **乗算係数(×1.xx)ではなく、 各パッシブの「具体的な数値」そのもの**。
    ///
    /// 設計思想 (2026-06-01 改訂):
    ///   旧版は judgmentMul=0.85 のような隠し倍率を実行時に掛けていたが、 これはゲーム内に明示されず
    ///   「強引なバランス調整」に見える。 そこで各機構パッシブの **実数値** (断罪ダイス上乗せ・断罪係数の基礎・
    ///   天衣無縫の上限スタック・反射率%・審判の炎の上限・爆ぜ火の固定ダメ・灰塵の鎧の軽減量・サドンデスダメ)
    ///   を直接の調整対象とする。 チューナーは目標勝率から具体値を増減し、 パッシブはその値を直接使う。
    ///   ログには「断罪ダイス 10→8」のように実数値の変化が出る。
    /// </summary>
    public enum BossParam
    {
        JudgmentDice,      // 灰燼/業火・断罪ターンの敵ダイス上乗せ (見切りロール難度)。 基準10
        JudgmentCoefBase,  // 業火の断罪の係数基礎。 係数 = base + 2n。 基準2 (1回目=4)
        RobeStacks,        // 覚者・天衣無縫の回復/シールド減衰の上限スタック。 基準10
        ReflectPct,        // 覚者・鏡映の反射率% (上限90)。 基準50
        ChipCap,           // 業火の審判官・審判の炎の毎ターン継続ダメ上限。 基準13
        BurstDamage,       // 覚者・爆ぜ火のロール敗北時固定ダメ。 基準5
        RegenReduction,    // 灰燼・灰塵の鎧の被ダメ軽減量 (タンク性/ジリ貧)。 基準9
        SuddenDeathDamage, // 覚者・妙覚のサドンデスダメ。 基準99 (7層専用=通常は不調整)
        TrueSelf,          // 真我: 7層覚者の全形態に付与する素ロール固定加算 (ダイス上限を超える難度調整用)。 基準1
    }

    /// <summary>各 BossParam の基準値・範囲・チューナーのゲイン (1バッチの最大変化量)。</summary>
    public readonly struct ParamMeta
    {
        public readonly float baseValue, min, max, gain, maxStep;
        public ParamMeta(float baseValue, float min, float max, float gain, float maxStep)
        { this.baseValue = baseValue; this.min = min; this.max = max; this.gain = gain; this.maxStep = maxStep; }
    }

    /// <summary>
    /// ボス難易度の「外部変数」テーブル。 enemies.json のベース値は触らず調整値を別管理。
    ///
    ///   ・機構パラメータ = <see cref="BossParam"/> の具体値 (override が無ければ基準値)。
    ///   ・HP = 絶対値 override (0=基準)。
    ///   ・Dice = 目標期待値。 期待値に最も近い (個数≤5・最高出目≤9) のダイス構成を算出して上書き。
    ///     ボス期待値 = プレイヤー平均出目 + diceOffset。 範囲 [1,25] を超える要求は HP 調整に回す。
    ///   ・全プロファイル共通の1ファイル (boss_tuning.json) に永続化。 基準プロファイルでのみ調整。
    /// </summary>
    public static class BossTuning
    {
        // ダイス期待値の範囲 (1d1=1 〜 5d9=25)。 ダイス個数≤5・最高出目≤9 の制約。
        public const int MaxDiceCount = 5;
        public const int MaxDiceFaces = 9;
        public const float DiceEMin = 1f;
        public const float DiceEMax = 25f; // 5 × (9+1)/2

        // HP 絶対値 override のクランプ (基準HPに対する倍率) と1バッチ変化フラクション。
        public const float MinHpFrac = 1f;
        public const float MaxHpFrac = 10f;

        // ===== 調整パラメータの有効/無効 (手動トグル) =====
        // オートチューナーが各パラメータを調整してよいか。 false にすると基準値固定 (そのパラメータは一切動かさない)。
        // GUIは無し。 調整したい対象に応じてこのファイルを書き換え、 再コンパイルして手動でオン/オフする。
        public const bool TuneJudgmentDice      = true;  // 断罪ターンの敵ダイス上乗せ
        public const bool TuneJudgmentCoefBase  = true;  // 断罪係数の基礎
        public const bool TuneRobeStacks        = true;  // 天衣無縫の上限スタック
        public const bool TuneReflectPct        = true;  // 鏡映の反射率%
        public const bool TuneChipCap           = true;  // 審判の炎の継続ダメ上限
        public const bool TuneBurstDamage       = true;  // 爆ぜ火の固定ダメ
        public const bool TuneRegenReduction    = true;  // 灰塵の鎧の軽減量
        public const bool TuneSuddenDeathDamage = true; // サドンデスダメ (7層専用=通常は不調整)
        public const bool TuneDice              = true;  // ボス素ダイスの期待値 (平均出目+offset)
        public const bool TuneHp                = true;  // HP絶対値 (Dice飽和時の受け皿)
        public const bool TuneTrueSelf          = true;  // 真我 (7層: ロール勝率が90%張り付き時の素ロール加算)

        // ===== ボスごとの学習 有効/無効 (手動トグル) =====
        // false にするとそのボスはオートチューナーが一切調整しない (係数は現状維持)。
        // パラメータ別トグル(上)との AND: 両方 true のときだけ調整される。
        public const bool TuneBoss_Layer1       = true;
        public const bool TuneBoss_Layer2       = true;
        public const bool TuneBoss_Layer3       = true;
        public const bool TuneBoss_Layer4       = true;
        public const bool TuneBoss_Layer5       = false;
        public const bool TuneBoss_Layer5Hidden = false;
        public const bool TuneBoss_Layer6       = false;
        public const bool TuneBoss_Layer7       = false;  // 7層p1 (初眼)
        public const bool TuneBoss_Layer7P2     = false;  // 業火・残響
        public const bool TuneBoss_Layer7P3     = false;  // 無相
        public const bool TuneBoss_Layer7P4     = false;  // シュヴァリエ・残影
        public const bool TuneBoss_Layer7P5     = false;  // 寂照
        public const bool TuneBoss_Layer7P6     = false;  // 薄火
        public const bool TuneBoss_Layer7P7     = false; // 妙覚 (サドンデス専用=常に学習しない。 IsMyokaku でも別途除外)

        /// <summary>このボスをオートチューナーが調整してよいか (ボス別の手動トグル)。</summary>
        public static bool BossEnabled(string id)
        {
            switch (id)
            {
                case "boss_layer1":         return TuneBoss_Layer1;
                case "boss_layer2":         return TuneBoss_Layer2;
                case "boss_layer3":         return TuneBoss_Layer3;
                case "boss_layer4":         return TuneBoss_Layer4;
                case "boss_layer5":         return TuneBoss_Layer5;
                case "boss_layer5_hidden":  return TuneBoss_Layer5Hidden;
                case "boss_layer6":         return TuneBoss_Layer6;
                case "boss_layer7":         return TuneBoss_Layer7;
                case "boss_layer7_p2":      return TuneBoss_Layer7P2;
                case "boss_layer7_p3":      return TuneBoss_Layer7P3;
                case "boss_layer7_p4":      return TuneBoss_Layer7P4;
                case "boss_layer7_p5":      return TuneBoss_Layer7P5;
                case "boss_layer7_p6":      return TuneBoss_Layer7P6;
                case "boss_layer7_p7":      return TuneBoss_Layer7P7;
                default:                    return false; // 未知のボスは調整しない
            }
        }

        // ===== ボスごとの目標ロール勝率 (手動設定) =====
        // プレイヤーのロール勝率がこの値を超えて張り付くと「真我」を上げ (ボスのロール強化)、
        // 下回れば下げる。 現在ロール勝率を制御点にするのは 7層 (真我) のみ。
        // 1〜6層の値は予約 (将来 1〜6層でもロール勝率制御を有効化した場合に使用)。
        public const float RollTarget_Layer1       = 0.70f;
        public const float RollTarget_Layer2       = 0.70f;
        public const float RollTarget_Layer3       = 0.80f;
        public const float RollTarget_Layer4       = 0.80f;
        public const float RollTarget_Layer5       = 0.80f;
        public const float RollTarget_Layer5Hidden = 0.60f;
        public const float RollTarget_Layer6       = 0.50f;
        public const float RollTarget_Layer7       = 0.90f;  // 7層p1 (初眼)
        public const float RollTarget_Layer7P2     = 0.90f;  // 業火・残響
        public const float RollTarget_Layer7P3     = 0.90f;  // 無相
        public const float RollTarget_Layer7P4     = 0.90f;  // シュヴァリエ・残影
        public const float RollTarget_Layer7P5     = 0.90f;  // 寂照
        public const float RollTarget_Layer7P6     = 0.90f;  // 薄火
        public const float RollTarget_Layer7P7     = 0.90f;  // 妙覚 (真我無効=実際には未使用)

        /// <summary>このボスの目標ロール勝率 (真我が寄せる先)。</summary>
        public static float TargetRollWinRate(string id)
        {
            switch (id)
            {
                case "boss_layer1":         return RollTarget_Layer1;
                case "boss_layer2":         return RollTarget_Layer2;
                case "boss_layer3":         return RollTarget_Layer3;
                case "boss_layer4":         return RollTarget_Layer4;
                case "boss_layer5":         return RollTarget_Layer5;
                case "boss_layer5_hidden":  return RollTarget_Layer5Hidden;
                case "boss_layer6":         return RollTarget_Layer6;
                case "boss_layer7":         return RollTarget_Layer7;
                case "boss_layer7_p2":      return RollTarget_Layer7P2;
                case "boss_layer7_p3":      return RollTarget_Layer7P3;
                case "boss_layer7_p4":      return RollTarget_Layer7P4;
                case "boss_layer7_p5":      return RollTarget_Layer7P5;
                case "boss_layer7_p6":      return RollTarget_Layer7P6;
                case "boss_layer7_p7":      return RollTarget_Layer7P7;
                default:                    return 0.90f;
            }
        }

        /// <summary>この BossParam をオートチューナーが調整してよいか。</summary>
        public static bool Enabled(BossParam p)
        {
            switch (p)
            {
                case BossParam.JudgmentDice:      return TuneJudgmentDice;
                case BossParam.JudgmentCoefBase:  return TuneJudgmentCoefBase;
                case BossParam.RobeStacks:        return TuneRobeStacks;
                case BossParam.ReflectPct:        return TuneReflectPct;
                case BossParam.ChipCap:           return TuneChipCap;
                case BossParam.BurstDamage:       return TuneBurstDamage;
                case BossParam.RegenReduction:    return TuneRegenReduction;
                case BossParam.SuddenDeathDamage: return TuneSuddenDeathDamage;
                case BossParam.TrueSelf:          return TuneTrueSelf;
                default: return false;
            }
        }

        /// <summary>各 BossParam の基準値/範囲/ゲイン。</summary>
        public static ParamMeta Meta(BossParam p)
        {
            switch (p)
            {
                //                          base  min  max  gain  maxStep/batch
                case BossParam.JudgmentDice:      return new ParamMeta(10f, 0f, 25f, 20f, 1.5f);
                case BossParam.JudgmentCoefBase:  return new ParamMeta( 2f, 0f,  8f,  6f, 0.5f);
                case BossParam.RobeStacks:        return new ParamMeta(10f, 1f, 20f, 20f, 1.5f);
                case BossParam.ReflectPct:        return new ParamMeta(50f, 0f, 90f, 80f, 5f);
                case BossParam.ChipCap:           return new ParamMeta(13f, 1f, 20f, 20f, 1.5f);
                case BossParam.BurstDamage:       return new ParamMeta( 5f, 0f, 20f, 20f, 1.5f);
                case BossParam.RegenReduction:    return new ParamMeta( 9f, 0f, 20f, 20f, 1.5f);
                case BossParam.SuddenDeathDamage: return new ParamMeta(99f, 1f, 99f, 99f, 6f);
                case BossParam.TrueSelf:          return new ParamMeta( 1f, 1f, 30f, 30f, 2f);
                default:                          return new ParamMeta( 1f, 0f,  1f,  1f, 0.1f);
            }
        }

        [Serializable]
        public class ParamOverride { public string param; public float value; }

        [Serializable]
        public class Knob
        {
            public string key;
            public int maxHP = 0;           // 0 = 基準 (enemies.json)。 >0 = 絶対値 override
            public float diceExpected = 0f; // 0 = 未調整 (ベースダイス)。 >0 = 目標期待値
            public float diceOffset = 0f;   // ボス期待値 = プレイヤー平均出目 + diceOffset
            public bool diceTuned = false;
            public List<ParamOverride> overrides = new List<ParamOverride>();
        }

        [Serializable]
        public class TuningFile
        {
            public string updatedAt = "";
            public int batches;
            public List<Knob> knobs = new List<Knob>();
        }

        private static Dictionary<string, Knob> _map = new Dictionary<string, Knob>();
        private static bool _loaded;

        // ===== 判定/キー =====

        public static bool IsBoss(string id)
            => !string.IsNullOrEmpty(id) && id.StartsWith("boss_layer", StringComparison.Ordinal);

        /// <summary>妙覚 (覚者最終形態)。 サドンデス専用のため真我は無効化・学習対象外。</summary>
        public const string MyokakuId = "boss_layer7_p7";
        public static bool IsMyokaku(string id) => id == MyokakuId;

        public static string KeyFor(string id) => id;

        public static int FloorOf(string id)
        {
            if (string.IsNullOrEmpty(id)) return 0;
            const string prefix = "boss_layer";
            if (!id.StartsWith(prefix, StringComparison.Ordinal)) return 0;
            int i = prefix.Length;
            int v = 0; bool any = false;
            while (i < id.Length && char.IsDigit(id[i])) { v = v * 10 + (id[i] - '0'); i++; any = true; }
            return any ? v : 0;
        }

        // ===== 機構パラメータ (具体値) =====

        /// <summary>Knob の override から param の現在値を取得 (無ければ基準値)。</summary>
        public static float GetParam(Knob k, BossParam p)
        {
            var meta = Meta(p);
            if (k?.overrides != null)
            {
                string name = p.ToString();
                foreach (var o in k.overrides)
                    if (o != null && o.param == name)
                        return Mathf.Clamp(o.value, meta.min, meta.max);
            }
            return meta.baseValue;
        }

        /// <summary>Knob の override に param の値を設定 (基準値ちょうどなら override を除去)。</summary>
        public static void SetParam(Knob k, BossParam p, float value)
        {
            if (k == null) return;
            var meta = Meta(p);
            value = Mathf.Clamp(value, meta.min, meta.max);
            string name = p.ToString();
            if (k.overrides == null) k.overrides = new List<ParamOverride>();
            var existing = k.overrides.Find(o => o != null && o.param == name);
            if (Mathf.Abs(value - meta.baseValue) < 0.001f)
            {
                if (existing != null) k.overrides.Remove(existing);
                return;
            }
            if (existing != null) existing.value = value;
            else k.overrides.Add(new ParamOverride { param = name, value = value });
        }

        /// <summary>id のボスの param 現在値 (override or 基準)。 パッシブが直接引く。</summary>
        public static float Param(string id, BossParam p)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(id) && _map.TryGetValue(KeyFor(id), out var k))
                return GetParam(k, p);
            return Meta(p).baseValue;
        }

        /// <summary>id のボスの param を丸めた整数値。</summary>
        public static int ParamInt(string id, BossParam p) => Mathf.RoundToInt(Param(id, p));

        // ===== HP (絶対値) =====

        public static int BaseMaxHp(string id)
        {
            var e = EnemyDatabase.Get(id);
            return e != null ? e.maxHP : 0;
        }

        /// <summary>有効な最大HP (override or 基準)。</summary>
        public static int MaxHpFor(string id)
        {
            EnsureLoaded();
            if (!string.IsNullOrEmpty(id) && _map.TryGetValue(KeyFor(id), out var k) && k.maxHP > 0)
                return k.maxHP;
            return BaseMaxHp(id);
        }

        // ===== Dice (期待値ベース) =====

        public static float DiceE(int count, int faces) => count * (faces + 1) / 2f;

        public static float BaseDiceExpected(string id)
        {
            var e = EnemyDatabase.Get(id);
            if (e == null || e.diceCount <= 0 || e.diceMaxValue <= 0) return 0f;
            return DiceE(e.diceCount, e.diceMaxValue);
        }

        public static float CurrentDiceExpected(string id)
        {
            EnsureLoaded();
            if (_map.TryGetValue(KeyFor(id), out var k) && k.diceExpected > 0f)
                return Mathf.Clamp(k.diceExpected, DiceEMin, DiceEMax);
            return BaseDiceExpected(id);
        }

        public static void SetDiceExpected(Knob k, float e)
        {
            k.diceExpected = e <= 0f ? 0f : Mathf.Clamp(e, DiceEMin, DiceEMax);
        }

        /// <summary>目標期待値に最も近い (個数≤5・最高出目≤9) のダイス構成。 同値なら個数多い方(低分散)を優先。</summary>
        public static (int count, int faces) BestDiceConfig(float targetE)
        {
            int bestC = 1, bestF = 1; float bestDiff = float.MaxValue;
            for (int c = 1; c <= MaxDiceCount; c++)
                for (int f = 1; f <= MaxDiceFaces; f++)
                {
                    float diff = Mathf.Abs(DiceE(c, f) - targetE);
                    if (diff < bestDiff - 0.001f || (Mathf.Abs(diff - bestDiff) <= 0.001f && c > bestC))
                    { bestDiff = diff; bestC = c; bestF = f; }
                }
            return (bestC, bestF);
        }

        // ===== 永続化 =====

        private static string PathFor(string root)
            => Path.Combine(MetaProfileHelper.SharedLearningRoot(), "boss_tuning.json");

        private static void SanitizeKnob(Knob k)
        {
            if (k.overrides == null) k.overrides = new List<ParamOverride>();
            // 範囲外の override をクランプ (基準値ちょうどは SetParam が除去)
            for (int i = k.overrides.Count - 1; i >= 0; i--)
            {
                var o = k.overrides[i];
                if (o == null || string.IsNullOrEmpty(o.param)) { k.overrides.RemoveAt(i); continue; }
                if (Enum.TryParse<BossParam>(o.param, out var p))
                    SetParam(k, p, o.value);
                else
                    k.overrides.RemoveAt(i);
            }
            if (k.maxHP < 0) k.maxHP = 0;
            if (k.diceExpected < 0f) k.diceExpected = 0f;
            if (k.diceExpected > 0f) k.diceExpected = Mathf.Clamp(k.diceExpected, DiceEMin, DiceEMax);
        }

        public static void Reload(string root = null)
        {
            _loaded = true;
            _map = new Dictionary<string, Knob>();
            try
            {
                string path = PathFor(root);
                if (!File.Exists(path)) return;
                var tf = JsonUtility.FromJson<TuningFile>(File.ReadAllText(path, Encoding.UTF8));
                if (tf?.knobs == null) return;
                foreach (var k in tf.knobs)
                {
                    if (k == null || string.IsNullOrEmpty(k.key)) continue;
                    SanitizeKnob(k);
                    _map[k.key] = k;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[BossTuning] load fail: {e.Message}"); }
        }

        public static void Save(string root = null, int batches = 0)
        {
            try
            {
                string path = PathFor(root);
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                var tf = new TuningFile
                {
                    updatedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    batches = batches,
                    knobs = new List<Knob>(_map.Values),
                };
                File.WriteAllText(path, JsonUtility.ToJson(tf, true), new UTF8Encoding(false));
            }
            catch (Exception e) { Debug.LogWarning($"[BossTuning] save fail: {e.Message}"); }
        }

        private static void EnsureLoaded() { if (!_loaded) Reload(); }

        // ===== アクセサ =====

        public static Knob GetOrCreate(string key)
        {
            EnsureLoaded();
            if (!_map.TryGetValue(key, out var k)) { k = new Knob { key = key }; _map[key] = k; }
            return k;
        }

        public static IEnumerable<Knob> All() { EnsureLoaded(); return _map.Values; }

        // ===== 適用 =====

        /// <summary>ボスなら maxHP override と diceExpected を反映した複製を返す。</summary>
        public static EnemyData Apply(EnemyData e)
        {
            if (e == null || !IsBoss(e.id)) return e;
            EnsureLoaded();
            _map.TryGetValue(KeyFor(e.id), out var k);
            bool changeHp = k != null && k.maxHP > 0 && k.maxHP != e.maxHP;
            bool changeDice = k != null && k.diceExpected > 0f;
            if (!changeHp && !changeDice) return e;
            var c = e.Clone();
            if (changeHp) c.maxHP = Mathf.Max(1, k.maxHP);
            if (changeDice)
            {
                var (cnt, faces) = BestDiceConfig(Mathf.Clamp(k.diceExpected, DiceEMin, DiceEMax));
                c.diceCount = cnt;
                c.diceMaxValue = faces;
            }
            return c;
        }

        public static string Summary()
        {
            EnsureLoaded();
            if (_map.Count == 0) return "(調整なし)";
            var sb = new StringBuilder();
            foreach (var k in _map.Values)
            {
                var parts = new List<string>();
                foreach (BossParam p in Enum.GetValues(typeof(BossParam)))
                {
                    float v = GetParam(k, p);
                    if (Mathf.Abs(v - Meta(p).baseValue) > 0.001f)
                        parts.Add($"{p}={v.ToString("0.#", CultureInfo.InvariantCulture)}");
                }
                if (k.maxHP > 0) parts.Add($"HP={k.maxHP}");
                if (k.diceExpected > 0f)
                {
                    var (cnt, faces) = BestDiceConfig(k.diceExpected);
                    parts.Add($"Dice→{cnt}d{faces}(E{k.diceExpected.ToString("F1", CultureInfo.InvariantCulture)})");
                }
                if (parts.Count > 0) sb.Append($"{k.key}[{string.Join(" ", parts)}] ");
            }
            string s = sb.ToString().TrimEnd();
            return string.IsNullOrEmpty(s) ? "(全パラメータ基準)" : s;
        }
    }
}
