using UnityEngine;

namespace UI.Game2D.Battle
{
    /// <summary>
    /// コマを並べたエフェクトの絵 (スプライトシート) を戦闘演出の作図面へ貼る (試作・2026-11-01 / 2026-11-14 にボスと武器の絵を足した)。
    /// 絵は pixelfx (https://github.com/patch-gg/pixelfx・MIT) の効果を、 液晶の見た目へ寄せて書き出したもの
    /// (光のぼかし → 網点・色は 5 色の段・半透明なし)。 書き出しの道具:
    /// 会心・毒・出血は Tools/pixelfx/dicebound/export_game.py、 ボスの大技・通常の技・武器の攻撃は export_fx.py
    /// (見本と同じ Tools/pixelfx/lab/lcd_frames.mjs で焼き、 画素を変えずにコマごとの中身のある範囲だけ切り出して並べた物)。
    ///
    /// <para><b>置き場所</b> (2026-11-15 に替えた): 書き出した絵 (<c>名前.png.bytes</c> + <c>名前.json</c>) は Tools/pixelfx/dicebound/sheets/ に置き、
    /// ゲームが読むのは Resources/Game2DFx/<c>名前.fxb.bytes</c> = 読み込んだ後の形 (下の「持ち方」) まで書き出しの時に作って詰めた物
    /// (Tools/pixelfx/dicebound/bake_fxb.py・形の説明もそこ)。 ゲームの中で PNG を展開して画素を走査すると大きい絵 1 枚で 100〜250 ms 止まったため。
    /// 読む側は解凍して配列へ写すだけで、 Unity の API を使わないので主スレッドの外でできる (<see cref="FromFxb"/>)。 拡縮はしない (1 画素 = 1 dot)。</para>
    ///
    /// <para><b>持ち方</b> (2026-11-14 に軽くした): コマごとに「不透明な画素の位置と色」を持つ (書き出しは不透明か透明かの 2 つだけ)。
    /// ただし、 コマの中で一番多い色が横に続いている所 (<see cref="RunMin"/> 画素以上) は、 1 画素ずつではなく「横の帯 (始まりと長さ) + その色 1 つ」で持つ
    /// (白飛び・消灯・暗転のべた塗りが 1 行 1 本の帯になる)。 帯にした方が軽い時だけ選ぶ。 帯と残りの画素は重ならず、 どれも上から塗る (または足す) だけなので、
    /// 貼った結果は 1 画素ずつ持った時と同じ (順番に依らない)。 色は絵ごとの一覧の番号 1 バイト (256 色を超える絵は 4 バイト)。
    /// どれを帯にするか・色の番号の振り方の規則は bake_fxb.py が持つ (2026-11-15 まではここで PNG から作っていた・同じ規則)。</para>
    ///
    /// <para><b>読み込みと捨て方</b>は FxSheet.Loading.cs。</para>
    /// </summary>
    public sealed partial class FxSheet
    {
        /// <summary>帯にする最短の長さ (画素)。 これより短い同色の並びは 1 画素ずつ持つ (bake_fxb.py の RUN_MIN)。</summary>
        public const int RunMin = 4;

        /// <summary>コマ 1 枚: 不透明な画素の、 コマの中の位置 (x | y &lt;&lt; 16) と色 (<see cref="BattleCanvas.Pack"/> と同じ形・または色の一覧の番号)、
        /// と、 下地の色 <c>runCol</c> の横の帯 (始まり x | y &lt;&lt; 16 と長さ)。 帯が無いコマは runPos = null。</summary>
        private struct Frame { public int[] pos; public uint[] col; public byte[] ci; public int[] runPos; public ushort[] runLen; public uint runCol; }
        /// <summary>この絵の色の一覧 (色が 256 以下の時・コマの画素は番号 1 バイトで持つ)。 null = 色を 4 バイトで持つ。</summary>
        private readonly uint[] _palette;
        private readonly Frame[] _frames;
        public readonly int W, H, PivotX, PivotY;
        public readonly bool Loop;
        public readonly string Name;

        /// <summary>1 コマの長さ (秒)。 書き出しの fps (2026-11-15〜 全部 1 秒 24 コマ)。</summary>
        public readonly float FrameSeconds;
        public int FrameCount => _frames.Length;
        /// <summary>1 秒のコマ数 (書き出しの fps・整数)。 経過秒からコマを出す時はこれを掛ける (1/24 の float で割ると、 ちょうどの時刻で 1 コマ前に落ちる)。</summary>
        public readonly int Fps;
        /// <summary>全部のコマの長さ (秒・倍精度)。</summary>
        public double Seconds => _frames.Length / (double)Fps;
        public float Duration => _frames.Length * FrameSeconds;

        /// <summary>読み込んだ後に持っている量の見積もり (バイト・配列の中身だけ) / 帯も色の番号も使わなかった時の量 (1 画素 8 バイト) /
        /// 読み込みに掛かった時間 (ミリ秒・頼んでから使えるようになるまで) / そのうち主スレッドで使った時間 (ミリ秒)。</summary>
        public long Bytes { get; private set; }
        public long BytesPlain { get; private set; }
        public float LoadMs { get; private set; }
        public float MainMs { get; private set; }

        private FxSheet(string name, int w, int h, int px, int py, int fps, bool loop, uint[] palette, Frame[] frames)
        {
            Name = name; W = w; H = h; PivotX = px; PivotY = py; Loop = loop;
            Fps = System.Math.Max(1, fps); FrameSeconds = 1f / Fps;
            _palette = palette; _frames = frames;
            foreach (var f in _frames)
            {
                long opaque = f.pos.Length; if (f.runLen != null) foreach (var l in f.runLen) opaque += l;
                BytesPlain += opaque * 8L;
                Bytes += f.pos.Length * (4L + (f.ci != null ? 1 : 4)) + (f.runPos != null ? f.runPos.Length * 6L : 0);
            }
        }

        /// <summary>bake_fxb.py が作った <c>.fxb</c> の中身から絵を組み立てる。 Unity の API を使わない (主スレッドの外で呼んでよい)。 形が違えば例外。</summary>
        public static FxSheet FromFxb(string name, byte[] data)
        {
            if (data == null || data.Length < 8 || data[0] != 'F' || data[1] != 'X' || data[2] != 'B' || data[3] != '1') throw new System.IO.InvalidDataException("[FxSheet] " + name + ": .fxb の形でない");
            int rawLen = System.BitConverter.ToInt32(data, 4);
            var b = new byte[rawLen];
            using (var z = new System.IO.Compression.DeflateStream(new System.IO.MemoryStream(data, 8, data.Length - 8), System.IO.Compression.CompressionMode.Decompress))
            {
                int got = 0; while (got < rawLen) { int r = z.Read(b, got, rawLen - got); if (r <= 0) throw new System.IO.InvalidDataException("[FxSheet] " + name + ": .fxb が途中で切れている"); got += r; }
            }
            int o = 0;
            int I32() { int v = System.BitConverter.ToInt32(b, o); o += 4; return v; }
            int w = I32(), h = I32(), px = I32(), py = I32(), fps = I32(); bool loop = b[o++] != 0; int nf = I32(), np = I32();
            uint[] pal = null;
            if (np > 0) { pal = new uint[np]; System.Buffer.BlockCopy(b, o, pal, 0, np * 4); o += np * 4; }
            var frames = new Frame[nf];
            for (int k = 0; k < nf; k++)
            {
                int n = I32(), m = I32();
                var f = new Frame();
                if (m > 0) { if (pal != null) f.runCol = pal[b[o++]]; else { f.runCol = System.BitConverter.ToUInt32(b, o); o += 4; } }
                f.pos = Positions(b, ref o, n, w);
                if (pal != null) { f.ci = new byte[n]; System.Buffer.BlockCopy(b, o, f.ci, 0, n); o += n; }
                else { f.col = new uint[n]; System.Buffer.BlockCopy(b, o, f.col, 0, n * 4); o += n * 4; }
                if (m > 0)
                {
                    f.runPos = Positions(b, ref o, m, w);
                    f.runLen = new ushort[m]; System.Buffer.BlockCopy(b, o, f.runLen, 0, m * 2); o += m * 2;
                }
                frames[k] = f;
            }
            if (o != rawLen) throw new System.IO.InvalidDataException("[FxSheet] " + name + ": .fxb の長さが合わない");
            return new FxSheet(name, w, h, px, py, fps, loop, pal, frames);
        }

        /// <summary>位置の差分 (幅 2 / 4 バイト・y * w + x の 1 つ前との差) → x | y &lt;&lt; 16。</summary>
        private static int[] Positions(byte[] b, ref int o, int n, int w)
        {
            int wide = b[o++]; var r = new int[n]; int lin = 0;
            for (int i = 0; i < n; i++)
            {
                if (wide == 2) { lin += b[o] | (b[o + 1] << 8); o += 2; }
                else { lin += System.BitConverter.ToInt32(b, o); o += 4; }
                int y = lin / w; r[i] = (lin - y * w) | (y << 16);
            }
            return r;
        }

        // ── 貼る ──

        /// <summary>経過 t 秒のコマを、 基準点が (x, y) に来るように貼る。 終わっていたら false。
        /// add = 光として足し込む (会心の光。 戦闘演出の発光と同じ加算) / false = 上から塗る (毒・出血の滴・ボスと武器の絵 ── 見本の paste と同じ)。</summary>
        public bool Draw(BattleCanvas c, float t, int x, int y, bool add = true)
        {
            int k = (int)System.Math.Floor(t * (double)Fps + 1e-6);      // 1/24 の float で割ると、 ちょうどの時刻で 1 コマ前に落ちる
            if (k < 0) return true;
            if (k >= _frames.Length) { if (!Loop) return false; k %= _frames.Length; }
            DrawFrame(c, k, x - PivotX, y - PivotY, add);
            return true;
        }

        /// <summary>コマ k (範囲外は何もしない) を、 コマの左上が (left, top) に来るように貼る。 帯 → 残りの画素の順 (重ならないので順は結果に効かない)。</summary>
        public void DrawFrame(BattleCanvas c, int k, int left, int top, bool add = false)
        {
            if (k < 0 || k >= _frames.Length) return;
            var f = _frames[k]; var fb = c.FB;
            if (f.runPos != null)
            {
                uint v = f.runCol;
                for (int i = 0; i < f.runPos.Length; i++)
                {
                    int y = top + (f.runPos[i] >> 16);
                    if (y < 0 || y >= BattleCanvas.H) continue;
                    if (!add && c.clipY0 >= 0 && (y < c.clipY0 || y >= c.clipY1)) continue;
                    int x0 = left + (f.runPos[i] & 0xffff), x1 = x0 + f.runLen[i];
                    if (x0 < 0) x0 = 0; if (x1 > BattleCanvas.W) x1 = BattleCanvas.W;
                    int row = y * BattleCanvas.W;
                    if (add) for (int x = x0; x < x1; x++) c.PutRgb(x, y, v & 255, (v >> 8) & 255, (v >> 16) & 255, 1f, true);
                    else for (int x = x0; x < x1; x++) fb[row + x] = v;
                }
            }
            for (int i = 0; i < f.pos.Length; i++)
            {
                int x = left + (f.pos[i] & 0xffff), y = top + (f.pos[i] >> 16);
                if (x < 0 || y < 0 || x >= BattleCanvas.W || y >= BattleCanvas.H) continue;
                uint cv = f.ci != null ? _palette[f.ci[i]] : f.col[i];
                if (add) c.PutRgb(x, y, cv & 255, (cv >> 8) & 255, (cv >> 16) & 255, 1f, true);
                else if (c.clipY0 < 0 || (y >= c.clipY0 && y < c.clipY1)) fb[y * BattleCanvas.W + x] = cv;
            }
        }

        /// <summary>コマ k の不透明な画素の数 (帯の分を含む)。</summary>
        public int PixelCount(int k)
        {
            if (k < 0 || k >= _frames.Length) return 0;
            var f = _frames[k]; int n = f.pos.Length;
            if (f.runLen != null) foreach (var l in f.runLen) n += l;
            return n;
        }

        /// <summary>絵の実質の長さ (秒): 画素の数が一番多いコマの tailFrac を下回る末尾のコマを数えない。</summary>
        public float ActiveSeconds(float tailFrac)
        {
            int max = 0; for (int k = 0; k < _frames.Length; k++) max = Mathf.Max(max, PixelCount(k));
            int last = -1; for (int k = 0; k < _frames.Length; k++) if (PixelCount(k) >= max * tailFrac) last = k;
            return (last + 1) * FrameSeconds;
        }

        /// <summary>コマ k に画素が 1 つも無いか (範囲外も true)。</summary>
        public bool Empty(int k) => k < 0 || k >= _frames.Length || (_frames[k].pos.Length == 0 && _frames[k].runPos == null);
    }
}
