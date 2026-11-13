using System.Collections.Generic;
using UnityEngine;

namespace CombatSystem
{
    /// <summary>
    /// ボスの機構パラメータ。 **乗算係数(×1.xx)ではなく、 各パッシブの「具体的な数値」そのもの**
    /// (断罪ダイス上乗せ・断罪係数の基礎・審判の炎の上限・灰塵の鎧の軽減量・停滞する時間の止まる確率 ほか)。
    /// 値の正本は <see cref="BossParams.Spec"/> の基準値。
    /// </summary>
    public enum BossParam
    {
        // 基準値の正本は下の Spec()。 ここの「基準」は 2026-11-11 (#30) に Spec() へ合わせた写し。
        JudgmentDice,      // 6層 灰燼の王・〈業火の予兆〉の予兆ターンの敵攻撃上乗せ (旧〈業火の断罪〉と共用)。 基準12 (2026-11-14〜・旧 8)
        JudgmentCoefBase,  // 業火の断罪の係数基礎。 係数 = base + 2n。 基準2 (1回目=4)
                           //   [読む敵なし] JudgmentBlaze はどの敵も持っていない (効果のクラスは残っている)
        ChipCap,           // 業火の審判官・審判の炎の 1 回 (毎ターン) あたりの上限。 基準10
        RegenReduction,    // 灰燼・灰塵の鎧の被ダメ軽減量 (タンク性/ジリ貧)。 基準9
                           //   [読む敵なし] AshArmor はどの敵も持っていない
        Stagnation,        // 停滞する時間: 引いた端子 1 枚あたりの止まる確率 (%)。 基準は段ごと p1 10 / p2 15 / p3 20 / p4 25 (2026-11-12 #103〜)
                           //   → **7層ヴェスカの第一レバー** (docs/GAME.md §13-4)。 旧 (〜11-11) は停滞スタック 1 つあたりの敵攻撃加算・基準 1
        IntimidateThreat,  // 威圧+ の加算。 基準3。 2026-11-11 (#104) から敵の攻撃 +N (enemyDiceTotalBonus・戦闘を通して) として効く
                           //   (それまでは ctx.enemyThreat に足して読む処理が無く、 効き先が無かった)
        PoisonStackCap,    // 毒沼の主・毒の侵蝕の毒スタック上限 (DOTの最大強度)。 基準5
        MirrorReflectCap,  // 鏡の双子・鏡映の応答の反射ダメ上限。 基準9
        GoblinCall,        // ゴブリン王・号令の毎ターンの敵攻撃加算。 基準3
        // 2026-11-14 (灰燼の王の測定の口): 末尾に足す (番号で保存している所は無いが、 並びは動かさない)
        FuryAttack,        // 6層 灰燼の王・〈憤怒の残滓〉の HP 50% 以下のターンの敵攻撃上乗せ。 基準5 (2026-11-14〜・旧 3・旧 EmberFury.Bonus)
        RegrowthRate,      // 6層 灰燼の王・〈灰の再生〉の毎ターンの回復 = 最大 HP × この値 (割合・%ではない)。 基準0.03 (= 3%・2026-11-14〜・旧 0.02)
        // 2026-11-12 (#126): RobeStacks (天衣無縫・読む側なし)・StanceAtkMult / WeakRollRatio (敵スタンス ADR-0005・呼ぶ所なし) を削除。
        //   番号で保存している所は無い (調整値の boss_tuning.json は名前で持ち、 L3 ボス調整器ごと削除)。
    }

    /// <summary>
    /// ボスの数値の正本 (機構パラメータの基準値・固有面ダイス) と、 敵データへの適用。
    ///
    /// <para><b>本体は基準値だけで完結する。</b> 外から値を差し替える口は <see cref="Overrides"/> だけで、
    /// 既定は null (= 基準値)。 BOT・測定が値を差し込むときは AutoRunner の側からだけここへ入れる
    /// (差し込んでいた L3 ボス調整器 AutoTest.BossTuning は 2026-11-12 に削除・#126。 2026-11-14 から測定の口 AutoRunner.bossParamSpec が
    /// 毎ランの始めに入れ、 ラン終了で null へ戻す ── 書式は "id:名前:値"・実数値)。
    /// 本体から AutoTest の型を参照しないための境界 (2026-10-29)。</para>
    /// </summary>
    public static class BossParams
    {
        /// <summary>基準値と、 差し替え値を受け付ける範囲。</summary>
        public readonly struct ParamSpec
        {
            public readonly float baseValue, min, max;
            public ParamSpec(float baseValue, float min, float max) { this.baseValue = baseValue; this.min = min; this.max = max; }
        }

        /// <summary>各 BossParam の基準値と範囲。</summary>
        public static ParamSpec Spec(BossParam p)
        {
            switch (p)
            {
                //                                             base  min   max
                case BossParam.JudgmentDice:      return new ParamSpec(12f, 0f, 30f);   // 2026-11-06: 15 → 8 (敵の攻撃を出目の幅へ戻した時に据え置いていた)。 2026-11-14: 8 → 12 (下の灰燼の王の注記)
                case BossParam.JudgmentCoefBase:  return new ParamSpec( 2f, 0f,  8f);
                // 2026-08-15: 一度 10 → 7 にしたが **10 へ戻した**。 「5層の主死因が Chip 95〜98%」
                //   という根拠自体が、 〈怪しい商人〉が 5層の通常抽選に混ざっていた汚染下の測定だった
                //   (FloorManager.SpecialEncounterFloor 参照)。 7 で測っても 7層クリアは
                //   5.9% → 5.8% と動かず、 効いたのは 5層ボス単体の勝率 (70.9→74.1%) だけ。
                //   出現漏れを直した土俵で測り直してから是非を判断する。
                case BossParam.ChipCap:           return new ParamSpec(10f, 1f, 20f);
                case BossParam.RegenReduction:    return new ParamSpec( 9f, 0f, 20f);
                // 2026-11-12 (#103): 〈停滞する時間〉を「引いた端子 1 枚ごとに確率で止める」へ作り替え、 この値を
                //   **1 枚あたりの止まる確率 (%)** に読み替えた。 ここの 10 は id の無い時 (段 1) の基準。 段ごとの基準は下の Spec(p, id)。
                //   (旧: 停滞スタック 1 つあたりの敵出目加算・基準 1・範囲 0〜12)
                case BossParam.Stagnation:        return new ParamSpec(10f, 0f, 50f);
                case BossParam.IntimidateThreat:  return new ParamSpec( 3f, 0f, 10f);
                case BossParam.PoisonStackCap:    return new ParamSpec( 5f, 1f, 12f);
                case BossParam.MirrorReflectCap:  return new ParamSpec( 9f, 0f, 15f);
                case BossParam.GoblinCall:        return new ParamSpec( 3f, 0f, 12f);
                // 2026-11-14: 灰燼の王の 2 つ (直書きの値を基準へ移した)。 同日、 測定の口 (AutoRunner.bossParamSpec) で測った値を作者の承認で基準へ:
                //   予兆 JudgmentDice 8 → 12・憤怒 FuryAttack 3 → 5・再生 RegrowthRate 0.02 → 0.03 (基礎の攻撃 6 → 21・最大 HP 1945 → 3800 は enemies.json)。
                //   根拠: 2026-11-14 測定 (Super・3000 ラン・嵐なし) で王の勝率 〈徒労〉なし 74.1% (docs/GAME.md §13 の灰燼の王)。
                //   RegrowthRate は割合のまま持つ ── 測定の口が読んだ "0.03" と同じ float 定数 0.03f にしておくと、 回復量の式 (最大 HP × 割合) が口で振った時と 1 ビットも変わらない
                //   (% で持って実行時に /100f すると Mono の float で丸めが割れることがある)。
                case BossParam.FuryAttack:        return new ParamSpec( 5f, 0f, 20f);
                case BossParam.RegrowthRate:      return new ParamSpec(0.03f, 0f, 0.10f);
                default:                          return new ParamSpec( 1f, 0f,  1f);
            }
        }

        /// <summary>ボス id ごとの基準値と範囲。 基準が段で違う項目だけ <see cref="Spec(BossParam)"/> から差し替える
        /// (今は〈停滞する時間〉だけ: boss_layer7 10% / _p2 15% / _p3 20% / _p4 25%)。 id が null なら <see cref="Spec(BossParam)"/> と同じ。</summary>
        public static ParamSpec Spec(BossParam p, string id)
        {
            var s = Spec(p);
            if (p == BossParam.Stagnation && !string.IsNullOrEmpty(id))
                return new ParamSpec(StagnationBasePct(id), s.min, s.max);
            return s;
        }

        /// <summary>〈停滞する時間〉の段ごとの基準 (引いた端子 1 枚あたりの止まる確率 %)。</summary>
        private static float StagnationBasePct(string id)
        {
            switch (id)
            {
                case GameLoop.BossIds.VescaP2:    return 15f;
                case GameLoop.BossIds.VescaP3:    return 20f;
                case GameLoop.BossIds.VescaFinal: return 25f;
                default:                          return 10f;      // 段 1 (VescaP1) と、 〈停滞する時間〉を持たない id
            }
        }

        // ===== 外からの差し替え (BOT・測定の口) =====

        /// <summary>ボスの数値を差し替える口。 値を持たない項目は基準値に落ちる。</summary>
        public interface IOverrides
        {
            /// <summary>param の差し替え値。 無ければ false。 範囲へのクランプは呼び出し側で行う。</summary>
            bool TryGetParam(string id, BossParam p, out float value);
            /// <summary>最大 HP の絶対値。 0 = 差し替え無し。</summary>
            int MaxHp(string id);
            /// <summary>素ダイスの目標期待値。 0 = 差し替え無し。</summary>
            float DiceExpected(string id);
            /// <summary>固有面ダイスの面。 null = 既定面。</summary>
            int[] SignatureFaces(string id);
        }

        /// <summary>null = 基準値 (本番)。</summary>
        public static IOverrides Overrides;

        public static bool IsBoss(string id) => GameLoop.BossIds.IsBoss(id);

        /// <summary>id のボスの param 現在値 (差し替え or 基準)。 パッシブが直接引く。</summary>
        public static float Param(string id, BossParam p)
        {
            var s = Spec(p, id);
            if (Overrides != null && !string.IsNullOrEmpty(id) && Overrides.TryGetParam(id, p, out float v))
                return Mathf.Clamp(v, s.min, s.max);
            return s.baseValue;
        }

        /// <summary>id のボスの param を丸めた整数値。</summary>
        public static int ParamInt(string id, BossParam p) => Mathf.RoundToInt(Param(id, p));

        /// <summary>敵のパッシブの説明文の数字を、 その敵の param の今の値で埋める (2026-11-14)。
        /// <c>{名前}</c> = <see cref="Param"/> の値 (例 <c>{JudgmentDice}</c> → 12)、 <c>{名前%}</c> = 値 ×100 の % の数 (例 <c>{RegrowthRate%}</c> → 3)。
        /// enemies.json の説明文は数字を直書きせずこの印で持つ ── 測定の口で値を変えても、 画面の数字が実際の値と食い違わない。
        /// 印の無い文はそのまま返す。 id は敵の id (ボスでなければ基準値で埋まる)。</summary>
        public static string Describe(string id, string text)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
            foreach (BossParam p in System.Enum.GetValues(typeof(BossParam)))
            {
                string name = p.ToString();
                if (text.IndexOf("{" + name, System.StringComparison.Ordinal) < 0) continue;
                float v = Param(id, p);
                text = text.Replace("{" + name + "%}", Fmt(v * 100f)).Replace("{" + name + "}", Fmt(v));
            }
            return text;
        }

        private static string Fmt(float v)
            => (Mathf.Round(v * 100f) / 100f).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        // ===== 素ダイス (期待値ベース) =====
        // ダイス期待値の範囲 (1d1=1 〜 5d9=25)。 ダイス個数≤5・最高出目≤9 の制約。
        public const int MaxDiceCount = 5;
        public const int MaxDiceFaces = 9;
        public const float DiceEMin = 1f;
        public const float DiceEMax = 25f; // 5 × (9+1)/2

        public static float DiceE(int count, int faces) => count * (faces + 1) / 2f;

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

        // ===== 固有面ダイス(署名ダイス) =====
        //  対象4ボスは均一ダイス(diceExpected/BestDiceConfig)を使わず、 ボス固有の面構成で振る。
        //  グラフィック制約により 1面の値は [SignatureFaceMin, SignatureFaceMax] にクランプ。
        //  **2026-11-11 時点: 4 ボスとも enemies.json が攻撃ロール方式 (attackRollMax > 0) なので、
        //  CombatManager.RollEnemyAttack は攻撃ロールを引き、 ここで載せた diceFaces は振られない** (旧ダイス方式の互換パスだけが読む)。
        //  下の面の平均・「灰塵の威圧 +3」(6層はもう持っていない) は当時の記録。

        public const int SignatureDiceCount = 5;   // 署名ダイスは5個振り
        public const int SignatureFaceCount = 6;   // 面は6つ
        public const int SignatureFaceMin = 1;
        public const int SignatureFaceMax = 9;     // 11等はダイスのグラフィック表現不可 → 上限9

        /// <summary>固有面ダイスの面 (正本)。</summary>
        private static readonly Dictionary<string, int[]> _signatureDefaults = new Dictionary<string, int[]>
        {
            // 5層 業火の審判官「裁火のダイス」: 二極(赦免/断罪) 平均4.83 ×5 = E24.2
            { "boss_layer5",        new[] { 2, 3, 3, 6, 7, 8 } },
            // 5層裏 シュヴァリエ「決闘のダイス」: 低分散・規律 平均5.5 ×5 = E27.5
            { "boss_layer5_hidden", new[] { 4, 5, 5, 6, 6, 7 } },
            // 6層 灰燼の王「薄火のダイス」: 上寄り 平均6.67 ×5 = E33.3 (+灰塵の威圧+3で実効E36)
            { "boss_layer6",        new[] { 4, 6, 7, 7, 8, 8 } },
            // 7層 ヴェスカ「往生呪のダイス」: 高い床 平均8.5 ×5 = E42.5 (プレイヤー1T目天井に匹敵)
            { "boss_layer7",        new[] { 7, 8, 8, 9, 9, 9 } },
        };

        public static bool IsSignatureDiceBoss(string id)
            => !string.IsNullOrEmpty(id) && _signatureDefaults.ContainsKey(id);

        /// <summary>このボスの固有面のコピー (差し替えがあればそれ、 無ければ既定)。 非対象ボスは null。</summary>
        public static int[] SignatureFaces(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var ov = Overrides?.SignatureFaces(id);
            if (IsValidSignature(ov)) return (int[])ov.Clone();
            if (_signatureDefaults.TryGetValue(id, out var def))
                return (int[])def.Clone();
            return null;
        }

        public static bool IsValidSignature(int[] faces)
            => faces != null && faces.Length == SignatureFaceCount;

        // ===== ダウンの目盛り (2026-11-13・GAME.md §6-15・規則は BossDown.cs) =====
        //  HP が目盛り以下になったターンの次のターン、 ボスはダウンする (攻撃しない・受けるダメージ ×1.5・溜めた大技は取り消し)。
        //  値は最大 HP の割合 (大きい順)。 **隠し倍率ではなく実数値**。 数・位置は敵ごとに違う (数値は仮置き・未測定)。
        //  既にある「HP で切り替わる仕掛け」と同じ位置に重ねない: 灰燼の王 50% (段の切り替え)・ヴェスカ天与 66% / 33% (〈天与の才〉の踏みとどまり)。
        //  対象は見せる層の第 2 層 (内部の深さ 3) 以降のボスだけ。 序層のトレジャーゴブリン (boss_layer1)・ファーム層の古い宝箱 (boss_layer0) には置かない。

        private static readonly Dictionary<string, float[]> _downNotches = new Dictionary<string, float[]>
        {
            // 深さ 3 (HP 505): 戦闘は 5〜8 ターン。 大技 (周期 3) を 1〜2 回消せる数
            { GameLoop.BossIds.Layer3Goblin, new[] { 0.70f, 0.35f } },   // 〈号令〉で毎ターン攻撃が乗る → 早めの目盛りで、 前のめりに割る価値を作る
            { GameLoop.BossIds.Layer3Marsh,  new[] { 0.60f } },          // 長引くほど毒が深い敵 → 目盛り 1 つ (割り時を 1 度だけ選ぶ・長引かせる理由を作らない)
            { GameLoop.BossIds.Layer3Mirror, new[] { 0.75f, 0.40f } },   // 浅い一撃を返す鏡 → 溜めて一気に割る遊びと〈鏡映の応答〉(大きく殴れ) の向きが揃う
            // 深さ 5 (HP 1835): 10 ターン前後
            { GameLoop.BossIds.Layer5Judgment, new[] { 0.75f, 0.50f, 0.25f } },   // 毎ターンの〈審判の炎〉に耐える長丁場 → 目盛り 3 つ
            { GameLoop.BossIds.Layer5Hidden,   new[] { 0.65f, 0.30f } },          // 形態の循環で間合いが変わる → 少なめ
            // 深さ 6 (HP 3800・2026-11-14〜・目盛りの位置は 1945 の時のまま): 50% は段の切り替え (再生が止まり予兆が速まる) なので避ける
            { GameLoop.BossIds.Layer6Ash, new[] { 0.80f, 0.30f } },
            // 深さ 7 ヴェスカ: 4 段で段ごとに HP が別 → 段ごとに 1 つ。 天与 (p4) は 66% / 33% が〈天与の才〉なので 50%
            { GameLoop.BossIds.VescaP1,    new[] { 0.60f } },
            { GameLoop.BossIds.VescaP2,    new[] { 0.50f } },
            { GameLoop.BossIds.VescaP3,    new[] { 0.40f } },
            { GameLoop.BossIds.VescaFinal, new[] { 0.50f } },
        };

        /// <summary>id のボスのダウンの目盛り (最大 HP の割合・大きい順)。 目盛りの無い敵・測定の口
        /// (<see cref="GameLoop.GameHooks.BossDownDisabled"/>) で止めた時は null。 返す配列を書き換えないこと。</summary>
        public static float[] DownNotches(string id)
        {
            if (GameLoop.GameHooks.BossDownDisabled || string.IsNullOrEmpty(id)) return null;
            return _downNotches.TryGetValue(id, out var n) ? n : null;
        }

        /// <summary>博士の「止まらない時計」(2026-11-13・<see cref="GameLoop.VescaClock"/>) を預かっている時、 ヴェスカの 4 つの段それぞれに足す目盛り
        /// (最大 HP の割合・全段とも同じ)。 **隠し倍率ではなく実数値の目盛りを 1 つ足す**。 上の段ごとの目盛り (p1 60%・p2 50%・p3 40%・最終 50%) とも、
        /// 〈天与の才〉の踏みとどまり (66%・33%) とも重ならない位置。 数値は仮置き・未測定 (docs/GAME.md §23)。</summary>
        public const float VescaClockNotch = 0.80f;

        /// <summary>戦闘 (ヴェスカは段) の始めに入れる目盛りの並び。 <paramref name="vescaClock"/> = このランで時計を預かっている
        /// (<see cref="GameLoop.VescaClock.Held"/>) なら、 ヴェスカの段に <see cref="VescaClockNotch"/> を 1 つ足す。 ダウンを止めた測定の口
        /// (<see cref="GameLoop.GameHooks.BossDownDisabled"/>) の時は、 時計があっても何も足さない (null のまま)。
        /// 呼ぶ所は <c>CombatManager.ResetBossDown</c> の 1 か所だけ ── 画面の目盛り・次の目盛りまでの残り・BOT の見積もりは、 戦闘が持つこの並びを読む。</summary>
        public static float[] DownNotches(string id, bool vescaClock)
        {
            var n = DownNotches(id);
            return vescaClock && GameLoop.BossIds.VescaStageOf(id) > 0 ? BossDown.WithExtraNotch(n, VescaClockNotch) : n;
        }

        // ===== 深さ 3 のボスの大技の周期 (2026-11-13・仮置き) =====
        //  深さ 5 以降と同じ形 (EnemyData.heavyPeriod / heavyMul / heavyLightMul・式は Escalation.HeavyFactor) を、 軽めの値で足す。
        //  json ではなくここに置くのは、 測定の口 (GameHooks.Depth3HeavyDisabled) で旧い作りへ戻せるようにするため。
        public const int Depth3HeavyPeriod = 3;
        public const float Depth3HeavyMul = 2.0f, Depth3HeavyLightMul = 0.6f;   // 小技 ×0.6 (2026-11-14 に Depth3HeavyWindupMul から改名・値は同じ)。 チャージのターンは攻撃しない (Escalation.IsChargeNoAttack)

        /// <summary>id が深さ 3 のボス 3 体 (ゴブリン王・毒沼の主・鏡の双子) で、 大技の周期を足すか。</summary>
        public static bool Depth3Heavy(string id)
            => !GameLoop.GameHooks.Depth3HeavyDisabled
            && (id == GameLoop.BossIds.Layer3Goblin || id == GameLoop.BossIds.Layer3Marsh || id == GameLoop.BossIds.Layer3Mirror);

        // ===== 適用 =====

        /// <summary>ボスなら固有面ダイスと差し替え (HP・ダイス期待値) を反映した敵データを返す。
        ///
        /// **戻り値は複製とは限らない。** 非ボス、 およびボスでも変更が一つも無い場合は
        /// 引数をそのまま (= EnemyDatabase の共有インスタンスを) 返す。 複製が保証されるのは
        /// 署名ダイスボスだけ。 **戻り値を書き換える側は自分で <see cref="EnemyData.Clone"/> すること。**
        /// (2026-08-04: この戻り値を複製と誤認して直接書き換えた結果、 敵 HP が遭遇ごとに
        ///  複利で膨らみ int を溢れさせた事故がある)</summary>
        public static EnemyData Apply(EnemyData e)
        {
            if (e == null || !IsBoss(e.id)) return e;
            int hpOv = Overrides?.MaxHp(e.id) ?? 0;

            // 固有面ダイス対象ボス: 均一ダイス(diceExpected/BestDiceConfig)を使わず署名面で振る。
            if (IsSignatureDiceBoss(e.id))
            {
                var c = e.Clone();
                if (hpOv > 0) c.maxHP = Mathf.Max(1, hpOv);
                var sig = SignatureFaces(e.id);
                if (IsValidSignature(sig))
                {
                    c.diceFaces = sig;
                    c.diceCount = SignatureDiceCount;
                    int mx = SignatureFaceMin;
                    foreach (var v in sig) if (v > mx) mx = v;
                    c.diceMaxValue = mx; // 弱ロール面縮小/勝率推定など均一前提コードの整合用
                }
                return c;
            }

            float diceOv = Overrides?.DiceExpected(e.id) ?? 0f;
            bool changeHp = hpOv > 0 && hpOv != e.maxHP;
            bool changeDice = diceOv > 0f;
            bool addHeavy = Depth3Heavy(e.id) && e.heavyPeriod <= 0;   // 深さ 3 のボスの大技 (2026-11-13・測定の口で止められる)
            if (!changeHp && !changeDice && !addHeavy) return e;
            var cc = e.Clone();
            if (addHeavy) { cc.heavyPeriod = Depth3HeavyPeriod; cc.heavyMul = Depth3HeavyMul; cc.heavyLightMul = Depth3HeavyLightMul; }
            if (changeHp) cc.maxHP = Mathf.Max(1, hpOv);
            if (changeDice)
            {
                var (cnt, faces) = BestDiceConfig(Mathf.Clamp(diceOv, DiceEMin, DiceEMax));
                cc.diceCount = cnt;
                cc.diceMaxValue = faces;
            }
            return cc;
        }
    }
}
