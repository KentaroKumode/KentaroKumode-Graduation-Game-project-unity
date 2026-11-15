using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;

namespace ItemSystem.PassiveSkills
{
    /// <summary>
    /// 秘宝の発動を画面に出すための見張り (#320・2026-11-15)。 秘宝の効果 (<see cref="IPassiveSkillEffect.Execute"/>) を動かす前後で、
    /// 戦闘の状態 (<see cref="CombatContext"/> の数の欄・辞書・リストの長さ・ランの HP / 最大 HP / ゴールド) の指紋を比べ、
    /// <b>変わった時だけ</b>「その秘宝が発動した」と知らせる (<see cref="OnRelicFired"/>・引数 = 品の id)。
    ///
    /// <para><b>何を発動と数えるか</b>: 効果を動かした (トリガーが来た) だけでは数えない ── 条件を満たさず何もしなかった時・
    /// 毎ターン同じ値を書き直すだけの時は光らせない。 値が変わった (攻撃・出目・シールド・充電・毒・HP・ゴールド・溜まり・次のターンの印…) 時だけ。
    /// ターンごとに値を足し直す効果 (毎ターン攻撃 +N など) は、 足した値が前のターンと同じでも、 その場で 0 → N に書くので毎ターン光る。</para>
    ///
    /// <para><b>重さ</b>: 指紋は反射で欄を読むので軽くはない。 <see cref="Active"/> (画面が購読していて BOT でない) の時だけ取る。
    /// BOT の周回・バッチモードでは取らない (戦闘の結果・乱数には何も効かない)。</para>
    /// </summary>
    public static class RelicFireProbe
    {
        /// <summary>秘宝が発動した (引数 = 品の id・<see cref="PassiveSkillManager.OwnerItemOf"/>)。 画面 (Game2DBattleHud) が購読する。</summary>
        public static event Action<string> OnRelicFired;

        /// <summary>見張るか: 購読があり、 BOT がランを動かしていない。</summary>
        public static bool Active => OnRelicFired != null && !GameLoop.GameHooks.BotDriving;

        private static FieldInfo[] _num, _dict, _list;

        private static void Init()
        {
            if (_num != null) return;
            var num = new List<FieldInfo>(); var dict = new List<FieldInfo>(); var list = new List<FieldInfo>();
            foreach (var f in typeof(CombatContext).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                var t = f.FieldType;
                if (t == typeof(int) || t == typeof(float) || t == typeof(bool) || t == typeof(long) || t == typeof(double)) num.Add(f);
                else if (typeof(IDictionary).IsAssignableFrom(t)) dict.Add(f);
                else if (typeof(ICollection).IsAssignableFrom(t)) list.Add(f);
            }
            _num = num.ToArray(); _dict = dict.ToArray(); _list = list.ToArray();
        }

        private static ulong Mix(ulong h, ulong v) { unchecked { h ^= v + 0x9e3779b97f4a7c15UL + (h << 6) + (h >> 2); return h; } }

        /// <summary>今の状態の指紋。</summary>
        public static ulong Take(CombatContext ctx)
        {
            if (ctx == null) return 0;
            Init();
            ulong h = 1469598103934665603UL;
            foreach (var f in _num)
            {
                object o = f.GetValue(ctx);
                ulong v = o is int i ? (ulong)(uint)i : o is float fl ? (ulong)BitConverter.ToInt32(BitConverter.GetBytes(fl), 0) : o is bool b ? (b ? 1UL : 0UL)
                        : o is long l ? (ulong)l : o is double d ? (ulong)BitConverter.DoubleToInt64Bits(d) : 0UL;
                h = Mix(h, v);
            }
            foreach (var f in _dict)
            {
                if (!(f.GetValue(ctx) is IDictionary dd)) { h = Mix(h, 7); continue; }
                ulong s = (ulong)dd.Count;
                foreach (DictionaryEntry e in dd) unchecked { s += (ulong)(e.Key?.GetHashCode() ?? 0) * 31UL ^ (ulong)(e.Value?.GetHashCode() ?? 0); }      // 並びに依らない和
                h = Mix(h, s);
            }
            foreach (var f in _list) h = Mix(h, (ulong)((f.GetValue(ctx) as ICollection)?.Count ?? -1));
            var run = GameLoop.GameManager.Instance?.Run;
            if (run != null) { h = Mix(h, (ulong)(uint)run.playerHP); h = Mix(h, (ulong)(uint)run.playerMaxHP); h = Mix(h, (ulong)(uint)run.coins); }
            return h;
        }

        /// <summary>効果を動かした後: 指紋が変わっていれば、 その効果の持ち主の品を知らせる (持ち主が分からない効果は知らせない)。</summary>
        public static void After(string skillId, ulong before, CombatContext ctx)
        {
            if (Take(ctx) == before) return;
            var owner = PassiveSkillManager.Instance?.OwnerItemOf(skillId);
            if (!string.IsNullOrEmpty(owner)) OnRelicFired?.Invoke(owner);
        }
    }
}
