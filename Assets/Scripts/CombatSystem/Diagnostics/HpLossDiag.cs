using System;
using System.Collections.Generic;

namespace CombatSystem
{
    /// <summary>
    /// 敵の HP が<b>どの経路で・どの品で</b>減ったかを、 戦闘の種類 (通常/エリート/ボス) × 層 (1〜5) 別に残す診断 (2026-11-05)。
    ///
    /// <para><b>なぜ要るか。</b> 4 層の通常戦で 1 ターンの与ダメが 450 まで伸びていたが、 攻撃の加算 (+9.3/攻撃) と倍率 (×1.5)
    /// では説明がつかなかった。 ボス戦だけの集計 (<see cref="SkillDiag.BossDmg"/>) では、 通常攻撃が敵 HP の 15〜22% しか削らず、
    /// 残りは固定ダメとその他だった。 「その他」を品名まで分けるための診断。</para>
    ///
    /// <para><b>読み方。</b> キー "経路:…" は実際に減った量 (敵 HP が 0 で頭打ちになった後の分は入らない)。
    /// キー "品:…" は<b>要求された量</b> (固定ダメの増分・預けたダメージ) で、 後段の倍率や頭打ちを掛ける前の値。
    /// 経路の合計 + 残差 = 総 HP − 終了 HP。 残差が大きければ、 まだ計装していない減り方がある。</para>
    ///
    /// <para>計測専用。 ゲームの結果は変えない (乱数も引かない)。</para>
    /// </summary>
    public static class HpLossDiag
    {
        public const int Layers = 5;
        public const int Cells = SkillDiag.Kinds * Layers;

        /// <summary>キー → [種類 × 層] の累計 (添字は <c>kind * Layers + layer</c>)。</summary>
        public static readonly Dictionary<string, double[]> Stats = new Dictionary<string, double[]>();

        private static int _kind, _layer;
        private static bool _skip;   // ファーム層 (0 層) の戦闘は数えない

        public static void Reset() { Stats.Clear(); _kind = 0; _layer = 0; }

        /// <summary>戦闘の頭: 種類と層を覚え、 総 HP と戦闘数を積む。</summary>
        public static void Begin(int kind, int depth, int enemyMaxHp)
        {
            _kind = Math.Max(0, Math.Min(SkillDiag.Kinds - 1, kind));
            int ord = MapSystem.FreeMove.FloorPlan.OrdinalOf(depth);
            _skip = ord < 1;
            if (_skip) return;
            _layer = Math.Max(0, Math.Min(Layers - 1, ord - 1));
            Add("総HP", enemyMaxHp);
            Add("戦闘", 1);
        }

        /// <summary>戦闘の終わり: 残った HP とターン数を積む (総 HP − 終了 HP が実際に削られた量)。</summary>
        public static void End(int enemyHpLeft, int turns)
        {
            Add("終了HP", Math.Max(0, enemyHpLeft));
            Add("ターン", turns);
        }

        public static void Add(string key, double v)
        {
            if (_skip || string.IsNullOrEmpty(key) || v == 0) return;
            if (!Stats.TryGetValue(key, out var a)) { a = new double[Cells]; Stats[key] = a; }
            a[_kind * Layers + _layer] += v;
        }

        /// <summary>敵 HP を減らし、 実際に減った量を経路に積む。 <paramref name="floor"/> は踏みとどまる HP。</summary>
        public static void Lose(ref int hp, int dmg, string channel, int floor = 0)
        {
            int before = hp;
            hp = Math.Max(floor, hp - dmg);
            Add("経路:" + channel, before - hp);
        }

        /// <summary>品が要求した固定ダメと預けたダメージ (実際に通る量ではなく、 倍率・頭打ちの前の量)。</summary>
        public static void NoteSource(string id, int fixedDelta, int pendingDelta)
        {
            if (string.IsNullOrEmpty(id)) return;
            if (fixedDelta > 0) Add("品:固定:" + id, fixedDelta);
            if (pendingDelta > 0) Add("品:直接:" + id, pendingDelta);
        }
    }
}
