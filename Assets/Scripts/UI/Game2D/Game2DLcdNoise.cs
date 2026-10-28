using System.Collections.Generic;
using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶の横線ノイズ (map-prototype.html の <c>NOISE</c> / <c>NZ</c>)。 参考動画 HighFleet 2026.10.13 10.47.55 (358 コマ・60fps) の解析:
    ///
    /// <para>【塊】 横線は画面の高さを上から下へ約 3.1 秒 (115〜130 行/秒) で通り抜ける塊で、 1 周の間に 0.4 秒ほど空白がある。 塊の中は帯の幅 ±5% の狭い所に
    /// 8〜17 本が詰まり、 隣の線との間隔は中央値 3 行 (半分が 3 行以内)。 画面全体の「はっきりした線」は平均 8.4 本 / コマ (被覆率 25% 以上)。</para>
    /// <para>【線】 窓の幅の 96% まで届く点線 (被覆率 平均 48%・10〜90% で 28〜72%)。 断片は窓幅の 1% 弱 (中央値 3 画素 / 窓 370) の短い点が中心で、
    /// 隙間は中央値 4 画素 (長さの分布はどちらも指数に近い)。 断片の並びは約 33ms (2 コマ) ごとに入れ替わる。 同じ行に平均 0.2 秒 (中央値 0.17 秒・p90 0.48 秒) とどまり、
    /// 行のずれは平均 0.6 行。 明るさは裾が長い (中央値 23・p90 56・p99 85) = 大半は薄く、 数本だけ突き抜けて明るい。</para>
    /// <para>【明るさの時間変化】 動画では 80ms ほどかけて立ち上がり、 山を越えると約 0.2 秒で消えていた。 ここでは<b>指定に従って、 生まれた瞬間が最も明るく、
    /// そこから薄くなる</b>形にしている (立ち上がりは <see cref="attackMs"/>)。</para>
    ///
    /// <para>作り方: 1 周ぶんの線の予定を種つきで最初に 1 度だけ作り、 毎周それを再生する (毎フレームの乱数なし)。 結果は RT と同じ大きさの濃淡 (R8) にして、
    /// 液晶の Quad のシェーダ (<c>_NoiseTex</c>) が湾曲の前の絵に乗せる。</para>
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class Game2DLcdNoise : MonoBehaviour
    {
        public Renderer surface;
        [Tooltip("点灯直後の濃さの倍率 (Game2DBoot.Settle が 1 の時)。 通常は 1")] public float bootNoise = 3.5f;
        [Range(0f, 1f), Tooltip("いちばん明るい線の濃さ (インクの色への混ざり具合)")] public float gain = 0.55f;
        [Tooltip("生まれてから最も明るくなるまで (ms)。 0 に近いほど「生まれた瞬間が最も明るい」")] public float attackMs = 16f;
        [Range(0.5f, 3f), Tooltip("薄くなる速さの曲がり (1 = 直線・大きいほど最初に速く薄くなる)")] public float decayPow = 1.3f;

        // ── 動画の解析から ──
        private const float CycleMs = 3100, Ease = 1.2f, LifeBase = 60, LifeSpan = 500, Spread = 7, Free = 0.08f, Second = 0.20f, TexMs = 33, Steps = 0.9f,
                            DensMin = 0.05f, DensMax = 1.0f;
        private const int Lines = 130, Variants = 6;
        private static readonly float[] Levels = { 0.15f, 0.55f, 1.0f, 1.6f };

        private const int TW = 960, TH = 540;      // RT と同じ大きさ
        private const int Sx = 2;                   // 縦の揺れ (HTML の dot) をこの画素数に直す
        private const float WinScale = 2.6f;        // 動画の窓 (370 画素) → RT (960 画素)

        private struct Line { public float t, life, off, rank, u, peak; public List<(int x, int w)>[] segs; public float[] hops; }

        private readonly List<Line> _lines = new List<Line>();
        private uint _q = 20261013;
        private float Rnd() { _q ^= _q << 13; _q ^= _q >> 17; _q ^= _q << 5; return _q / 4294967296f; }
        private float Rr(float a, float b) => a + Rnd() * (b - a);
        private float Gauss() => (Rnd() + Rnd() + Rnd() - 1.5f) * 1.15f;
        private float Exp(float mean) => -Mathf.Log(1f - Rnd() * 0.9999f) * mean;

        private byte[] _px;
        private Texture2D _tex;
        private MaterialPropertyBlock _mpb;
        private float _i = Levels[3];
        private float _last;
        private static readonly int NoiseId = Shader.PropertyToID("_NoiseTex");

        private void OnDestroy() { if (_tex != null) Destroy(_tex); }

        /// <summary>1 本の線の断片の並び。 断片は短い点が中心 (指数分布・窓 370 画素で中央値 3)、 隙間は 2 画素以上 (中央値 4)。 密度 d が高いほど長く・詰まる。</summary>
        private List<(int x, int w)> SegsFor(float d)
        {
            var segs = new List<(int, int)>();
            float segMean = 1.5f + 6f * d, gapMean = 6f * (1.1f - d);
            int x = Mathf.FloorToInt(Rnd() * 6 * WinScale);
            while (x < TW)
            {
                int w = Mathf.Max(2, Mathf.RoundToInt((1 + Exp(segMean)) * WinScale));
                segs.Add((x, Mathf.Min(w, TW - x)));
                x += w + Mathf.Max(2, Mathf.RoundToInt((2 + Exp(gapMean)) * WinScale));
            }
            return segs;
        }

        private void Build()
        {
            _lines.Clear();
            int guard = 0;
            while (_lines.Count < Lines && guard++ < 8000)
            {
                float u = Rnd();                                                  // 出る時刻は、 周の中ほどで多く両端 (空白) で少なくなるよう、 受け入れ確率で選ぶ
                if (Rnd() > Mathf.Pow(Mathf.Sin(Mathf.PI * u), 1.6f) * 0.97f) continue;
                float d = Rr(DensMin, DensMax), kind = Rnd();
                float row0 = kind < Free ? Rnd() : (kind < Free + (1 - Free) * Second ? (u + 0.5f) % 1f : u);       // 単発 = 一様 / 2 つ目の塊 = 半周ずれ / 主の塊 = 時刻どおり
                var l = new Line
                {
                    t = u * CycleMs, off = Gauss() * Spread, u = row0,
                    life = LifeBase + Mathf.Pow(Rnd(), 2f) * LifeSpan,                     // 中央値 ≒ 185ms・p90 ≒ 465ms
                    peak = 0.25f + 0.75f * Mathf.Pow(Rnd(), 2.6f),                         // 裾が長い: 大半は薄く、 数本だけ突き抜ける (p90 / 中央値 ≒ 2.5)
                    segs = new List<(int, int)>[Variants],
                };
                for (int k = 0; k < Variants; k++) l.segs[k] = SegsFor(d);
                l.rank = Rnd();
                var hops = new List<float>(); int n = Rnd() < Steps ? 1 + (Rnd() < 0.4f ? 1 : 0) : 0;
                while (n-- > 0) hops.Add(Rnd());
                hops.Sort(); l.hops = hops.ToArray();
                _lines.Add(l);
            }
        }

        private void Ensure()
        {
            if (_tex != null) return;
            _px = new byte[TW * TH];
            _tex = new Texture2D(TW, TH, TextureFormat.R8, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLcdNoise" };
            Build();
        }

        private void Update()
        {
            if (surface == null) return;
            Ensure();
            float now = Time.unscaledTime, dtS = _last > 0 ? Mathf.Min(0.2f, now - _last) : 0f; _last = now;

            // 強度: 天候があると強め (基準 = 天候 2・なめらかに追う)。 弱い時は順位の低い線から出なくなる
            var gm = GameManager.Instance; var sim = gm != null ? gm.FreeMap : null;
            int wxN = sim != null && sim.Weather != WeatherKind.None ? 3 : 2;
            float target = wxN == 3 ? Levels[3] : Levels[2] * 1.35f;
            _i += (target - _i) * Mathf.Min(1, dtS * Ease);
            float frac = _i / Levels[3], aMul = Mathf.Clamp(0.75f + 0.25f * _i, 0.75f, 1.15f);

            System.Array.Clear(_px, 0, _px.Length);
            if (Game2DBoot.Waiting) { _tex.SetPixelData(_px, 0); _tex.Apply(false); return; }     // 起動前 (まだ点いていない) は横線を出さない
            float ph = (now * 1000f) % CycleMs;
            foreach (var l in _lines)
            {
                float age = ph - l.t; if (age < 0) age += CycleMs;
                if (age >= l.life) continue;
                float vis = Mathf.Clamp01((frac - l.rank) * 10f);
                if (vis <= 0) continue;
                float u = age / l.life;
                // 生まれた瞬間が最も明るく (立ち上がりは attackMs)、 そこから薄くなる
                float attack = attackMs <= 1f ? 1f : Mathf.Clamp01(age / attackMs);
                float env = attack * Mathf.Pow(1f - u, decayPow) * vis * l.peak;
                int hopsDone = 0; foreach (float h in l.hops) if (u >= h) hopsDone++;
                int y = Mathf.Clamp(Mathf.FloorToInt(l.u * TH + l.off * Sx) + hopsDone * Sx, 0, TH - 1);       // 同じ行にとどまる。 動く時は下へ
                float boost = 1f + Game2DBoot.Settle * (bootNoise - 1f);       // 点灯直後だけ濃く
                byte v = (byte)Mathf.Clamp(env * aMul * gain * boost * 255f, 0, 255);
                if (v == 0) continue;
                var segs = l.segs[Mathf.FloorToInt(age / TexMs) % l.segs.Length];       // 33ms ごとに断片の並びが入れ替わる
                int row = (TH - 1 - y) * TW;
                foreach (var (sx, w) in segs)
                    for (int x = sx; x < sx + w && x < TW; x++) if (_px[row + x] < v) _px[row + x] = v;
            }
            _tex.SetPixelData(_px, 0);
            _tex.Apply(false);

            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            surface.GetPropertyBlock(_mpb);
            _mpb.SetTexture(NoiseId, _tex);
            surface.SetPropertyBlock(_mpb);
        }
    }
}
