using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 端子の山札 (ADR-0014) の見せ方。 種類の色・種類の呼び名・条件の短い表記・印 (レア・使い捨て・常設・軸) を、
    /// 戦闘画面 (<see cref="Game2DBattleHud"/>) と報酬の画面 (<see cref="Game2DPhaseScreens"/>) で同じにする。
    /// 端子の名前・条件・効果の文は定義 (<see cref="TerminalCards"/>) から読む ── ここに端子ごとの文を持たない。
    /// </summary>
    public static class Game2DTerminalLook
    {
        /// <summary>補助の色 (攻撃 = 赤・防御 = 青・充電 = 金と並べて見分けられる、 くすんだ緑)。</summary>
        public static readonly Color32 SupportCol = new Color32(0x6c, 0xb0, 0x7a, 255);

        public static Color32 KindColor(TerminalKind k)
            => k == TerminalKind.Attack ? Game2DKit.Red : k == TerminalKind.Block ? Game2DKit.Blue : k == TerminalKind.Charge ? Game2DKit.Gold : SupportCol;

        public static string KindLabel(TerminalKind k)
            => k == TerminalKind.Attack ? "攻撃" : k == TerminalKind.Block ? "防御" : k == TerminalKind.Charge ? "充電" : "補助";

        /// <summary>種類の 1 字 (戦闘画面の行の頭)。</summary>
        public static string KindShort(TerminalKind k)
            => k == TerminalKind.Attack ? "攻" : k == TerminalKind.Block ? "防" : k == TerminalKind.Charge ? "充" : "補";

        /// <summary>印の並び (種類・条件・レア・使い捨て・常設・軸)。 例: 「攻撃・3以下・レア」。</summary>
        public static string Marks(TerminalDef d, RunState run, bool withKind = true)
        {
            if (d == null) return "";
            var l = new List<string>();
            if (withKind) l.Add(KindLabel(d.kind));
            if (!string.IsNullOrEmpty(d.condLabel)) l.Add(d.condLabel);
            if (d.capacity > 1) l.Add(d.capacity + " 個まで");
            if (d.rare) l.Add("レア");
            if (d.disposable) l.Add("使い捨て");
            if (d.permanent) l.Add("常設");
            if (d.axis != null)
            {
                string word = ItemSystem.PassiveSkills.AxisRules.WordOf(d.axis);
                l.Add("軸 " + d.axis + (string.IsNullOrEmpty(word) ? "" : " " + word + " " + ItemSystem.PassiveSkills.AxisRules.Count(run, d.axis)));
            }
            return string.Join("・", l);
        }

        /// <summary>説明の全文: 「名前（印）効果」。</summary>
        public static string Describe(TerminalDef d, RunState run)
            => d == null ? "" : d.id + "（" + Marks(d, run) + "）" + d.description;

        public static Color32 Mix(Color32 a, Color32 b, float k)
            => new Color32((byte)(a.r + (b.r - a.r) * k), (byte)(a.g + (b.g - a.g) * k), (byte)(a.b + (b.b - a.b) * k), 255);
    }
}
