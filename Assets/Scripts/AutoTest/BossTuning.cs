using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using CombatSystem;

namespace AutoTest
{
    /// <summary>BOT のボス調整器が使う各 BossParam の基準値・範囲・ゲイン (1バッチの最大変化量)。
    /// 基準値と範囲の正本は <see cref="BossParams.Spec"/>。 ここで足すのはチューナーの歩幅だけ。</summary>
    public readonly struct ParamMeta
    {
        public readonly float baseValue, min, max, gain, maxStep;
        public ParamMeta(float baseValue, float min, float max, float gain, float maxStep)
        { this.baseValue = baseValue; this.min = min; this.max = max; this.gain = gain; this.maxStep = maxStep; }
    }

    /// <summary>
    /// BOT のボス調整器が持つ調整値テーブル。 enemies.json と <see cref="BossParams"/> の基準値は触らず調整値を別管理。
    ///
    ///   <b>本番のゲームはこのクラスを読まない。</b> 調整値は <see cref="Install"/> したときだけ
    ///   <see cref="BossParams.Overrides"/> 経由で効く (AutoRunner の起動時)。
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
        public const int MaxDiceCount = BossParams.MaxDiceCount;
        public const int MaxDiceFaces = BossParams.MaxDiceFaces;
        public const float DiceEMin = BossParams.DiceEMin;
        public const float DiceEMax = BossParams.DiceEMax;

        // HP 絶対値 override のクランプ (基準HPに対する倍率) と1バッチ変化フラクション。
        public const float MinHpFrac = 1f;
        public const float MaxHpFrac = 10f;

        // ===== 調整パラメータの有効/無効 (手動トグル) =====
        // オートチューナーが各パラメータを調整してよいか。 false にすると基準値固定 (そのパラメータは一切動かさない)。
        // GUIは無し。 調整したい対象に応じてこのファイルを書き換え、 再コンパイルして手動でオン/オフする。
        public const bool TuneJudgmentDice      = true;  // 断罪ターンの敵ダイス上乗せ
        public const bool TuneJudgmentCoefBase  = true;  // 断罪係数の基礎
        public const bool TuneRobeStacks        = true;  // 天衣無縫の上限スタック
        public const bool TuneChipCap           = true;  // 審判の炎の継続ダメ上限
        public const bool TuneRegenReduction    = true;  // 灰塵の鎧の軽減量
        public const bool TuneDice              = true;  // ボス素ダイスの期待値 (平均出目+offset)
        public const bool TuneHp                = true;  // HP絶対値 (Dice飽和時の受け皿)
        public const bool TuneStagnation        = true;  // 停滞する時間 (7層ヴェスカの第一レバー)
        public const bool TuneIntimidateThreat  = true;  // 威圧+ の脅威加算 (5層業火の審判官ほか)
        public const bool TunePoisonStackCap    = true;  // 毒の侵蝕の毒スタック上限 (3層毒沼の主)
        public const bool TuneMirrorReflectCap  = true;  // 鏡映の応答の反射上限 (4層鏡の双子)
        public const bool TuneGoblinCall        = true;  // 号令の自ダイス合計加算 (2層ゴブリン王)
        public const bool TuneStanceAtkMult     = true;  // 高火力スタンスの被ダメ倍率=ボス攻撃力 (全ボス)
        public const bool TuneWeakRollRatio     = true;  // 弱ロールの面縮小比 (弱ロール時ロール勝率の制御)

        // ===== ボスごとの学習 有効/無効 (手動トグル) =====
        // false にするとそのボスはオートチューナーが一切調整しない (係数は現状維持)。
        // パラメータ別トグル(上)との AND: 両方 true のときだけ調整される。
        public const bool TuneBoss_Layer1       = true;
        public const bool TuneBoss_Layer2       = true;
        public const bool TuneBoss_Layer3       = true;
        public const bool TuneBoss_Layer4       = true;
        public const bool TuneBoss_Layer5       = true;
        public const bool TuneBoss_Layer5Hidden = true;
        public const bool TuneBoss_Layer6       = true;
        // 7層 ヴェスカ 4段連戦 (2026-07-27 リワーク)。 特殊勝利機構が無くなったため、
        // 「7層は通常調整を行わない」という旧除外規約も解消し、 全段を通常のチューニング対象にする。
        public const bool TuneBoss_Layer7       = true;  // 段1 小手調べ
        public const bool TuneBoss_Layer7P2     = true;  // 段2 遺物学者
        public const bool TuneBoss_Layer7P3     = true;  // 段3 神話
        public const bool TuneBoss_Layer7P4     = true;  // 段4 天与

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
                default:                    return false; // 未知のボスは調整しない
            }
        }

        // ===== ボスごとの目標ロール勝率 (手動設定) =====
        // ロール勝率を寄せる先。 現在これを制御点にする調整器は無い (7層の「真我」は
        // 覚者ごと廃止済み・docs/GAME.md §24)。 レポート表示と将来の制御用に値だけ残す。
        public const float RollTarget_Layer1       = 0.70f;
        public const float RollTarget_Layer2       = 0.70f;
        public const float RollTarget_Layer3       = 0.80f;
        public const float RollTarget_Layer4       = 0.80f;
        public const float RollTarget_Layer5       = 0.80f;
        public const float RollTarget_Layer5Hidden = 0.60f;
        public const float RollTarget_Layer6       = 0.40f;
        public const float RollTarget_Layer7       = 0.90f;  // 段1 小手調べ
        public const float RollTarget_Layer7P2     = 0.90f;  // 段2 遺物学者
        public const float RollTarget_Layer7P3     = 0.90f;  // 段3 神話
        public const float RollTarget_Layer7P4     = 0.90f;  // 段4 天与

        // ===== ボスのスタンス別 ロール勝率レンジ (設定可能・1〜6層) =====
        //  ボスが opposed ロールで「勝つ」割合(=プレイヤー敗北割合)を、 強ロール/弱ロールスタンス別に帯で管理する。
        //  ・強ロール時の roll-win → ダイス/固有面で [Floor,Ceil] に収める。
        //  ・弱ロール時の roll-win → 弱ロール比(WeakRollRatio)で [Floor,Ceil] に収める (弱ロールは高火力との交換なので低め)。
        //  難度(クリア率)はスキル/攻撃力倍率/HPが担い、 ロール勝率はこのレンジで制御する。
        //  値はボス別に編集可。 既定は層帯で設定。
        public struct RollWinRange { public float floor, ceil; public RollWinRange(float f, float c) { floor = f; ceil = c; } }

        /// <summary>強ロールスタンス時の ボスroll-win 許容帯。</summary>
        public static RollWinRange StrongRollWinRange(string id)
        {
            switch (id)
            {
                case "boss_layer1":
                case "boss_layer2":         return new RollWinRange(0.30f, 0.50f);
                case "boss_layer3":
                case "boss_layer4":         return new RollWinRange(0.40f, 0.55f);
                case "boss_layer5":
                case "boss_layer5_hidden":  return new RollWinRange(0.45f, 0.60f);
                case "boss_layer6":         return new RollWinRange(0.55f, 0.70f);
                default:                    return new RollWinRange(0.40f, 0.60f);
            }
        }

        /// <summary>弱ロールスタンス時の ボスroll-win 許容帯 (高火力との交換ぶん強ロールより低め)。</summary>
        public static RollWinRange WeakRollWinRange(string id)
        {
            switch (id)
            {
                case "boss_layer1":
                case "boss_layer2":         return new RollWinRange(0.10f, 0.25f);
                case "boss_layer3":
                case "boss_layer4":         return new RollWinRange(0.15f, 0.30f);
                case "boss_layer5":
                case "boss_layer5_hidden":  return new RollWinRange(0.20f, 0.35f);
                case "boss_layer6":         return new RollWinRange(0.30f, 0.45f);
                default:                    return new RollWinRange(0.15f, 0.35f);
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
                case BossParam.ChipCap:           return TuneChipCap;
                case BossParam.RegenReduction:    return TuneRegenReduction;
                case BossParam.Stagnation:        return TuneStagnation;
                case BossParam.IntimidateThreat:  return TuneIntimidateThreat;
                case BossParam.PoisonStackCap:    return TunePoisonStackCap;
                case BossParam.MirrorReflectCap:  return TuneMirrorReflectCap;
                case BossParam.GoblinCall:        return TuneGoblinCall;
                case BossParam.StanceAtkMult:     return TuneStanceAtkMult;
                case BossParam.WeakRollRatio:     return TuneWeakRollRatio;
                default: return false;
            }
        }

        /// <summary>各 BossParam の基準値/範囲 (本体の正本) とチューナーのゲイン。</summary>
        public static ParamMeta Meta(BossParam p)
        {
            var s = BossParams.Spec(p);
            float gain, maxStep;
            switch (p)
            {
                //                                   gain  maxStep/batch
                case BossParam.JudgmentDice:      gain = 25f;  maxStep = 1.5f;  break;
                case BossParam.JudgmentCoefBase:  gain =  6f;  maxStep = 0.5f;  break;
                case BossParam.RobeStacks:        gain = 20f;  maxStep = 1.5f;  break;
                case BossParam.ChipCap:           gain = 20f;  maxStep = 1.5f;  break;
                case BossParam.RegenReduction:    gain = 20f;  maxStep = 1.5f;  break;
                case BossParam.Stagnation:        gain = 20f;  maxStep = 1f;    break;
                case BossParam.IntimidateThreat:  gain = 20f;  maxStep = 1f;    break;
                case BossParam.PoisonStackCap:    gain = 20f;  maxStep = 1f;    break;
                case BossParam.MirrorReflectCap:  gain = 20f;  maxStep = 1.5f;  break;
                case BossParam.GoblinCall:        gain = 20f;  maxStep = 1f;    break;
                case BossParam.StanceAtkMult:     gain = 1.0f; maxStep = 0.15f; break;
                case BossParam.WeakRollRatio:     gain = 0.5f; maxStep = 0.05f; break;
                default:                          gain = 1f;   maxStep = 0.1f;  break;
            }
            return new ParamMeta(s.baseValue, s.min, s.max, gain, maxStep);
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
            public int[] diceFaces = null;  // 固有面ダイス(署名ダイス)の学習値。null=コード既定面を使用。各値∈[1,9]
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

        public static bool IsBoss(string id) => BossParams.IsBoss(id);

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

        /// <summary>id のボスの param 現在値 (調整値 or 基準)。 BOT のレポートと調整器が引く (本番のパッシブは <see cref="BossParams.Param"/>)。</summary>
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

        public static float DiceE(int count, int faces) => BossParams.DiceE(count, faces);

        public static float BaseDiceExpected(string id)
        {
            var e = EnemyDatabase.Get(id);
            if (e == null) return 0f;
            // 攻撃ロール方式なら範囲の期待値、 旧ダイス方式なら個数×平均。
            if (e.UsesAttackRoll) return e.AttackRollExpected;
            if (e.diceCount <= 0 || e.diceMaxValue <= 0) return 0f;
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

        public static (int count, int faces) BestDiceConfig(float targetE) => BossParams.BestDiceConfig(targetE);

        // ===== 固有面ダイス(署名ダイス) =====
        //  既定面の正本は BossParams。 面はオートチューナー(BossBalanceTuner)が各値を学習し、
        //  boss_tuning.json の Knob.diceFaces に保存する。

        public const int SignatureDiceCount = BossParams.SignatureDiceCount;
        public const int SignatureFaceCount = BossParams.SignatureFaceCount;
        public const int SignatureFaceMin = BossParams.SignatureFaceMin;
        public const int SignatureFaceMax = BossParams.SignatureFaceMax;

        public static bool IsSignatureDiceBoss(string id) => BossParams.IsSignatureDiceBoss(id);

        /// <summary>このボスの現在有効な固有面(学習値があればそれ、無ければ既定)。 非対象ボスは null。</summary>
        public static int[] SignatureFaces(string id)
        {
            if (string.IsNullOrEmpty(id) || !BossParams.IsSignatureDiceBoss(id)) return null;
            EnsureLoaded();
            if (_map.TryGetValue(KeyFor(id), out var k) && IsValidSignature(k.diceFaces))
                return (int[])k.diceFaces.Clone();
            var saved = BossParams.Overrides;
            BossParams.Overrides = null;           // 既定面を本体から引く
            try { return BossParams.SignatureFaces(id); }
            finally { BossParams.Overrides = saved; }
        }

        public static bool IsValidSignature(int[] faces)
            => faces != null && faces.Length == SignatureFaceCount;

        public static void ClampSignature(int[] faces)
        {
            if (faces == null) return;
            for (int i = 0; i < faces.Length; i++)
                faces[i] = Mathf.Clamp(faces[i], SignatureFaceMin, SignatureFaceMax);
            Array.Sort(faces); // 昇順に保つ(可読性・診断ログ用)
        }

        public static float SignatureExpected(int[] faces)
        {
            if (!IsValidSignature(faces)) return 0f;
            int s = 0; foreach (var v in faces) s += v;
            return SignatureDiceCount * (float)s / faces.Length;
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

        /// <summary>この調整表を反映した敵データ (BOT のレポート・調整器用)。 本番は <see cref="BossParams.Apply"/>。</summary>
        public static EnemyData Apply(EnemyData e)
        {
            var saved = BossParams.Overrides;
            BossParams.Overrides = Provider;
            try { return BossParams.Apply(e); }
            finally { BossParams.Overrides = saved; }
        }

        // ===== 本体への差し込み =====

        private sealed class TuningOverrides : BossParams.IOverrides
        {
            public bool TryGetParam(string id, BossParam p, out float value)
            {
                EnsureLoaded();
                if (_map.TryGetValue(KeyFor(id), out var k) && k.overrides != null)
                {
                    string name = p.ToString();
                    foreach (var o in k.overrides)
                        if (o != null && o.param == name) { value = o.value; return true; }
                }
                value = 0f;
                return false;
            }
            public int MaxHp(string id)
            {
                EnsureLoaded();
                return _map.TryGetValue(KeyFor(id), out var k) && k.maxHP > 0 ? k.maxHP : 0;
            }
            public float DiceExpected(string id)
            {
                EnsureLoaded();
                return _map.TryGetValue(KeyFor(id), out var k) && k.diceExpected > 0f ? k.diceExpected : 0f;
            }
            public int[] SignatureFaces(string id)
            {
                EnsureLoaded();
                return _map.TryGetValue(KeyFor(id), out var k) && IsValidSignature(k.diceFaces) ? k.diceFaces : null;
            }
        }

        private static readonly TuningOverrides Provider = new TuningOverrides();

        /// <summary>調整表を本体のボスに効かせる。 AutoRunner の起動時に呼ぶ。</summary>
        public static void Install() => BossParams.Overrides = Provider;

        /// <summary>差し込みを外す (本体は基準値に戻る)。</summary>
        public static void Uninstall() { if (BossParams.Overrides == Provider) BossParams.Overrides = null; }

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
                if (IsValidSignature(k.diceFaces))
                {
                    parts.Add($"Dice→{SignatureDiceCount}d[{string.Join(",", k.diceFaces)}](E{SignatureExpected(k.diceFaces).ToString("F1", CultureInfo.InvariantCulture)})");
                }
                else if (k.diceExpected > 0f)
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
