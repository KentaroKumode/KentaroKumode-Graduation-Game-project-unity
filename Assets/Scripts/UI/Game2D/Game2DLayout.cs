using System.Collections.Generic;

namespace UI.Game2D
{
    /// <summary>
    /// 2D 画面の配置表。 正本は docs/design/gui-full-layout.html の <c>NAV</c> / <c>BAT</c>（2026-10-13・番号つき）。
    /// 座標は <b>480×270 の格子（dot）</b>で、 左上が原点・y は下向き。 実画面では 1 dot ＝ 整数画素の倍率
    /// （<see cref="Game2DScreen"/>）。
    ///
    /// <para><see cref="State"/> は HTML の色分けと同じ: Done = 実装済みの絵がある / Plan = 合意した形 / Temp = 仮置き。
    /// Temp の枠は<b>場所取りの帯</b>で、 絵ができたら差し替える。</para>
    /// </summary>
    public static class Game2DLayout
    {
        public const int Width = 480, Height = 270;

        public enum State { Done, Plan, Temp }

        public struct Region
        {
            public string name, label;
            public int x, y, w, h;
            public State state;
            public Region(string name, string label, int x, int y, int w, int h, State s)
            { this.name = name; this.label = label; this.x = x; this.y = y; this.w = w; this.h = h; state = s; }
        }

        /// <summary>航行（地図）画面の配置。</summary>
        public static readonly List<Region> Nav = new List<Region>
        {
            new Region("GOLD",        "GOLD の入れ物",                   0,   0,  76, 96, State.Done),
            new Region("RADAR",       "魔石のレーダー",                391,   0,  89, 89, State.Done),
            new Region("SUPPLY",      "物資バー（強調・全長）",        224, 251, 256, 19, State.Done),
            new Region("HP",          "HP バー（半分）",               352, 232, 128, 19, State.Done),
            new Region("WX",          "天気の予報",                    212, 219, 140, 32, State.Plan),
            new Region("TIME",        "手番の表示",                     96,   0, 100, 22, State.Temp),
            new Region("LAYER",       "層・進行",                      200,   0,  44, 22, State.Temp),
            new Region("ALERT",       "気配・追跡の警告",              248,   0,  60, 22, State.Temp),
            new Region("WAIT",        "待つ",                          312,   0,  60, 22, State.Temp),
            new Region("FLR SCT TRP", "照明・偵察・囮罠",                0,  98,  90, 30, State.Temp),
            new Region("STATUS",      "状態異常",                        0, 130, 128, 18, State.Temp),
            new Region("CLASS",       "契約・職業",                      0, 150,  76, 22, State.Temp),
            new Region("ITEMS",       "航行アイテム",                  430,  92,  50, 70, State.Temp),
            new Region("EXIT",        "脱出",                          440, 164,  40, 24, State.Temp),
            new Region("LEGEND",      "地図の凡例・ズーム",            440, 190,  40, 20, State.Temp),
            new Region("PASSIVE",     "パッシブのアイコン列",            0, 252, 192, 18, State.Temp),
            new Region("CONSUME",     "消耗品",                          0, 228, 100, 22, State.Temp),
            new Region("WPN DICE",    "武器・ダイス",                  104, 228,  44, 22, State.Temp),
        };

        /// <summary>戦闘画面の配置（HP は中央下・物資と天気は画面の外）。 配線盤などの戦闘の計器は別設計。</summary>
        public static readonly List<Region> Battle = new List<Region>
        {
            new Region("GOLD",     "GOLD の入れ物",        0,   0,  76,  96, State.Done),
            new Region("RADAR",    "魔石のレーダー",     391,   0,  89,  89, State.Done),
            new Region("HP",       "HP バー（中央下・全長）", 112, 251, 256, 19, State.Done),
            new Region("CHARGE",   "充電ゲージ",          96,   0, 120,  12, State.Temp),
            new Region("ESCALATE", "エスカレーション",   220,   0, 120,  12, State.Temp),
            new Region("ENEMY HP", "敵の HP",             96,  16, 240,  10, State.Temp),
            new Region("STATUS",   "状態異常",              0, 100,  76,  14, State.Temp),
        };
    }
}
