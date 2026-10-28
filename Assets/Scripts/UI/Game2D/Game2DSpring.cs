using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// GUI の出し入れ用のばね (2026-10-24)。 目標 (0 / 1) へ向かって少し行き過ぎて戻る。 資源バーのばね (K = 190・C = 8) より減衰を強めて、 行き過ぎは 3 dot 以内 (<see cref="Value"/>)。
    /// <see cref="Go"/> で目標を変えると、 delay 秒待ってから動き出す (複数の部品を少しずつずらして動かす)。
    /// </summary>
    public struct Game2DSpring
    {
        public float x, v, tgt, wait;
        public const float K = 210f, C = 20f;      // 減衰比 0.69 (行き過ぎは約 5%)

        public void Set(float value) { x = value; v = 0; tgt = value; wait = 0; }

        public void Go(float target, float delay) { if (Mathf.Abs(target - tgt) > 0.5f) { tgt = target; wait = delay; } }

        public void Step(float dt)
        {
            if (wait > 0f) { wait -= dt; return; }
            float h = dt / 4f;
            for (int i = 0; i < 4; i++) { v += (K * (tgt - x) - C * v) * h; x += v * h; }
        }

        /// <summary>動かす距離 dist (dot) に対する値。 行き過ぎは目標の外側 maxOverPx dot 以内に収める (既定 3)。</summary>
        public float Value(float dist, float maxOverPx = 3f) => Mathf.Clamp(x, -maxOverPx / dist, 1f + maxOverPx / dist);

        /// <summary>止まっている (目標に着いて動いていない)。</summary>
        public bool Settled => wait <= 0f && Mathf.Abs(tgt - x) < 0.004f && Mathf.Abs(v) < 0.02f;
    }
}
