using System.Collections.Generic;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// 戦闘の背景の絵 (2026-11-16)。 480×270・4 色・1 dot = 1 画素で、 作者が 1 枚ずつ選んだ物 (元は Captures/backgrounds/final/)。
    /// <b>絵は加工しない</b>: 画面には 1 画素ずつそのまま写す (旧の仮の背景 backdrop.png に掛けていた暖色の暗がり・白熱灯の光だまり・四隅の暗がりは掛けない)。
    ///
    /// <para>置き場は Resources/Game2DBackdrops/*.png.bytes (テキストの資産として読み、 <see cref="Texture2D.LoadImage(byte[])"/> で戻す)。
    /// テクスチャとして取り込まないので、 取り込みの設定 (圧縮・補間・2 の累乗への拡縮) に左右されない ── .meta は git 管理外で、
    /// 作業ツリーや新しい手元では Unity が既定 (圧縮・バイリニア・NPOT を拡縮) で作ってしまうため。 Resources/Game2DIcons と同じ読み方。</para>
    ///
    /// <para>出し分け (<see cref="NameFor"/>): ボスの専用の背景 (シュヴァリエ・灰燼の王・ヴェスカ 1〜3 段・4 段) が先、 それ以外は
    /// <b>見せる層の番号</b> (<c>RunState.currentFloor</c>・0〜5) の背景。 <c>RunState.Depth</c> (旧い深さ 0/1/3/5/6/7) は使わない。
    /// 審判官・追跡者・イベントの戦闘など、 表に無い戦闘はその層の背景。</para>
    /// </summary>
    public static class BattleBackdrops
    {
        public const string Dir = "Game2DBackdrops/";
        public const int W = BattleCanvas.W, H = BattleCanvas.H;

        /// <summary>見せる層の番号 (0 = 序層・1〜5 = 第一〜第五層) → 背景。 ファイル名の数字は深さ (0/1/3/5/6/7) で、 作者の書き出しのまま。</summary>
        private static readonly string[] ByFloor = { "bg_floor0", "bg_floor1", "bg_floor3", "bg_floor5", "bg_floor6", "bg_floor7" };
        public const string Chevalier = "bg_chevalier", King = "bg_king", Vesca = "bg_vesca", VescaFinal = "bg_vesca_p4";

        /// <summary>見せる層の番号の背景 (範囲外は両端へ丸める)。</summary>
        public static string ForFloor(int floor) => ByFloor[Mathf.Clamp(floor, 0, FloorPlan.LastOrdinal)];

        /// <summary>この戦闘の背景の名前。 floor = 見せる層の番号 (<c>RunState.currentFloor</c>)・enemyId = 今の敵の id。</summary>
        public static string NameFor(int floor, string enemyId)
        {
            if (BossIds.IsBoss(enemyId))
            {
                if (enemyId == BossIds.Layer5Hidden) return Chevalier;
                if (enemyId == BossIds.Layer6Ash) return King;
                switch (BossIds.VescaStageOf(enemyId))
                {
                    case 1: case 2: case 3: return Vesca;
                    case 4: return VescaFinal;
                }
            }
            return ForFloor(floor);
        }

        /// <summary>今のランと敵から背景の名前 (ランが無ければ序層)。</summary>
        public static string NameForCurrent(string enemyId)
            => NameFor(GameManager.Instance?.Run?.currentFloor ?? FloorPlan.FarmFloor, enemyId);

        // 戻した画素 (BattleCanvas.Pack の形・上の行から)。 10 枚で 5 MB ほど。 一度戻した物は持っておく (戦闘の入りで PNG を戻し直さない)
        private static readonly Dictionary<string, uint[]> _cache = new Dictionary<string, uint[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _cache.Clear();

        /// <summary>背景の画素 (W×H・上の行から・BattleCanvas.Pack)。 無い・大きさが違う時は null (呼ぶ側は今の背景のまま)。</summary>
        public static uint[] Load(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_cache.TryGetValue(name, out var px)) return px;
            var a = Resources.Load<TextAsset>(Dir + name + ".png");
            if (a == null) { Debug.LogWarning("[BattleBackdrops] Resources/" + Dir + name + ".png.bytes が無い"); return _cache[name] = null; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!t.LoadImage(a.bytes) || t.width != W || t.height != H)
            {
                Debug.LogWarning($"[BattleBackdrops] {name} を読めない、 か大きさが {W}×{H} でない ({t.width}×{t.height})");
                Object.Destroy(t); Resources.UnloadAsset(a); return _cache[name] = null;
            }
            var src = t.GetPixels32(); px = new uint[W * H];
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++) { var c = src[(H - 1 - y) * W + x]; px[y * W + x] = BattleCanvas.Pack(c.r, c.g, c.b); }      // Texture2D は下の行から
            Object.Destroy(t); Resources.UnloadAsset(a);
            return _cache[name] = px;
        }
    }
}
