using System.Collections.Generic;
using System.Globalization;
using CombatSystem;
using UnityEngine;

namespace AutoTest
{
    /// <summary>
    /// [測定 2026-11-14] ボスの機構の値 (<see cref="BossParam"/>) を測定で差し替える口 <see cref="BossParams.Overrides"/> の中身。
    /// 書式は <see cref="AutoRunner.bossParamSpec"/> の <c>"id:名前:値,id:名前:値,…"</c> (例 <c>"boss_layer6:JudgmentDice:12,boss_layer6:FuryAttack:5"</c>)。
    /// 値は <b>実数値</b> (倍率ではない)。 範囲の外は <see cref="BossParams.Param"/> が Spec の最小・最大へ詰める。
    /// 最大 HP・基礎の攻撃はここでは持たない ── 既存の <see cref="AutoRunner.enemyHpSpec"/> / <see cref="AutoRunner.enemyAttackSpec"/>
    /// (敵の DB の読み直し・BOT の見積もりも同じ値を見る) を使う。
    /// </summary>
    public sealed class BossParamSpec : BossParams.IOverrides
    {
        private readonly Dictionary<(string, BossParam), float> _v = new Dictionary<(string, BossParam), float>();

        /// <summary>書式を読む。 空・読める項目が 1 つも無い時は null (= 口を開けない・本番と同じ)。 読めない項目は警告して飛ばす。</summary>
        public static BossParamSpec Parse(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;
            var o = new BossParamSpec();
            foreach (var raw in spec.Split(','))
            {
                string tok = raw.Trim();
                if (tok.Length == 0) continue;
                var kv = tok.Split(':');
                if (kv.Length != 3
                    || !System.Enum.TryParse(kv[1].Trim(), false, out BossParam p)
                    || !System.Enum.IsDefined(typeof(BossParam), p)
                    || !float.TryParse(kv[2].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float v))
                {
                    Debug.LogWarning($"[BossParamSpec] 読めない項目を飛ばす: '{tok}' (書式は id:名前:値)");
                    continue;
                }
                o._v[(kv[0].Trim(), p)] = v;
            }
            return o._v.Count > 0 ? o : null;
        }

        public bool TryGetParam(string id, BossParam p, out float value) => _v.TryGetValue((id ?? "", p), out value);
        public int MaxHp(string id) => 0;                  // 最大 HP は enemyHpSpec の口
        public float DiceExpected(string id) => 0f;
        public int[] SignatureFaces(string id) => null;

        /// <summary>id のボスの param の効く値 (差し替えがあれば範囲へ詰めた値・無ければ基準)。 <see cref="BossParams.Param"/> と同じ規則を、
        /// 口を本体へ入れていない時 (ラン終了後のサマリー) にも読めるように写したもの。</summary>
        public static float Effective(BossParamSpec o, string id, BossParam p)
        {
            var s = BossParams.Spec(p, id);
            return o != null && o.TryGetParam(id, p, out float v) ? Mathf.Clamp(v, s.min, s.max) : s.baseValue;
        }

        public bool Touches(string id)
        {
            foreach (var k in _v.Keys) if (k.Item1 == id) return true;
            return false;
        }
    }
}
