using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// 流れ星（ピクセルアート版）。 たまに画面端から短い光の筋が斜めに走り、 進みながら消える。
    /// 筋は「頭が明るく尾が透ける」横グラデの px テクスチャを生成し、 進行方向へ回転させて流す。
    /// 一定間隔でランダムに発生（プール再利用）。 ローコスト・外部アセット不要。
    ///
    /// 規約: localScale は使わない（大きさは生成テクスチャの px と PPU で決める）。 回転は一過性演出として許容。
    /// 配置: LcdContent レイヤー。 sortingOrder は星(1)より少し前の 2〜3 を推奨。
    /// </summary>
    [DisallowMultipleComponent]
    public class ShootingStars : MonoBehaviour
    {
        [Header("発生")]
        [Tooltip("発生間隔(秒)の最小/最大（この範囲でランダムに次を抽選）")]
        public float intervalMin = 2.5f;
        public float intervalMax = 7.0f;
        [Tooltip("同時に存在できる最大本数")]
        [Min(1)] public int maxConcurrent = 3;

        [Header("発生位置（画面上端の空のみ）")]
        [Tooltip("スポーンする高さ Y(local)。 画面上端＝コンテンツ上端(約 +8.69)。")]
        public float spawnY = 8.7f;
        [Tooltip("消滅する高さ Y(local)。 ここに達したら消す。 上端から3割下＝約 +3.5。 これより下（町中）には落ちない。")]
        public float killY = 3.5f;
        [Tooltip("スポーン X の中心(local)")]
        public float spawnXCenter = 0f;
        [Tooltip("スポーン X の片側振れ幅(local)。 ±この範囲に出る。")]
        public float spawnXHalf = 13f;

        [Header("進路")]
        [Tooltip("進行方向の角度(度)。 0=右,90=上,180=左,270=下。 既定は左下へ降る。 jitter でばらつき。")]
        public float angleDeg = 250f;
        public float angleJitter = 18f;
        [Tooltip("速度(world/sec) 最小/最大")]
        public float speedMin = 14f;
        public float speedMax = 22f;
        [Tooltip("寿命の上限(秒)。 通常は killY 到達で消えるが、 万一の保険。")]
        public float lifeMax = 2.0f;

        [Header("見た目（ピクセル）")]
        public int pixelsPerUnit = 32;
        [Tooltip("筋の長さ(px)")]
        [Min(2)] public int lengthPx = 18;
        [Tooltip("筋の太さ(px)")]
        [Min(1)] public int thickPx = 1;
        public Color headColor = new Color(1f, 1f, 1f, 1f);
        public Color tailColor = new Color(0.8f, 0.9f, 1f, 0f);

        [Header("描画順")]
        public int sortingOrder = 2;
        public string sortingLayer = "Default";

        [Tooltip("ポーズ中(timeScale=0)でも流れる")]
        public bool useUnscaledTime = true;

        private class Shot { public Transform t; public SpriteRenderer sr; public Vector2 vel; public float life, age, startY; public bool active; }
        private readonly List<Shot> _pool = new List<Shot>();
        private Sprite _streak;
        private float _nextAt;

        private void OnEnable()
        {
            BuildSprite();
            ScheduleNext(true);
        }

        private void OnDisable()
        {
            for (int i = 0; i < _pool.Count; i++)
                if (_pool[i] != null && _pool[i].t != null)
                {
                    if (Application.isPlaying) Destroy(_pool[i].t.gameObject);
                    else DestroyImmediate(_pool[i].t.gameObject);
                }
            _pool.Clear();
            _streak = null;
        }

        private void Update()
        {
            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;

            if (now >= _nextAt && CountActive() < maxConcurrent)
            {
                Spawn();
                ScheduleNext(false);
            }

            float span = Mathf.Max(0.001f, spawnY - killY); // 上端→消滅Y の落差
            for (int i = 0; i < _pool.Count; i++)
            {
                var s = _pool[i];
                if (!s.active) continue;
                s.age += dt;
                s.t.localPosition += (Vector3)(s.vel * dt);

                float y = s.t.localPosition.y;
                // killY 到達（=上から3割の高さ）or 保険の寿命で消す。 町中までは落ちない。
                if (y <= killY || s.age >= s.life) { s.active = false; s.sr.enabled = false; continue; }

                // 高さで明滅エンベロープ: 上端=0 → 中間で最大 → killY で0（スッと消える）。
                float t01 = Mathf.Clamp01((spawnY - y) / span); // 0(上端)..1(killY)
                float a = Mathf.Clamp01(Mathf.Sin(t01 * Mathf.PI));
                var c = s.sr.color; c.a = a; s.sr.color = c;
            }
        }

        private void Spawn()
        {
            var s = GetFree();
            float ang = (angleDeg + Random.Range(-angleJitter, angleJitter)) * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(ang), Mathf.Sin(ang));
            if (dir.y > -0.05f) dir.y = -0.2f; // 必ず下向き成分を持たせる（killY へ向かう）
            dir.Normalize();
            float spd = Random.Range(Mathf.Min(speedMin, speedMax), Mathf.Max(speedMin, speedMax));
            s.vel = dir * spd;
            s.life = Mathf.Max(0.1f, lifeMax);
            s.age = 0f;
            s.startY = spawnY;

            float x = spawnXCenter + Random.Range(-spawnXHalf, spawnXHalf);
            s.t.localPosition = new Vector3(x, spawnY, 0f);
            s.t.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);

            var c = s.sr.color; c.a = 0f; s.sr.color = c;
            s.sr.enabled = true;
            s.active = true;
        }

        private int CountActive()
        {
            int n = 0;
            for (int i = 0; i < _pool.Count; i++) if (_pool[i].active) n++;
            return n;
        }

        private Shot GetFree()
        {
            for (int i = 0; i < _pool.Count; i++) if (!_pool[i].active) return _pool[i];
            var go = new GameObject("shootingstar");
            go.transform.SetParent(transform, false);
            go.layer = gameObject.layer;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = _streak;
            sr.color = Color.white;
            sr.enabled = false;
            sr.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) sr.sortingLayerName = sortingLayer;
            var shot = new Shot { t = go.transform, sr = sr, active = false };
            _pool.Add(shot);
            return shot;
        }

        private void ScheduleNext(bool first)
        {
            float now = useUnscaledTime ? Time.unscaledTime : Time.time;
            float wait = Random.Range(Mathf.Min(intervalMin, intervalMax), Mathf.Max(intervalMin, intervalMax));
            if (first) wait *= Random.value; // 起動直後の足並みをずらす
            _nextAt = now + wait;
        }

        // 横グラデの筋テクスチャ: 右端=頭(headColor), 左端=尾(tailColor)。 ピボットは頭側(右中央)。
        private void BuildSprite()
        {
            int w = Mathf.Max(2, lengthPx);
            int h = Mathf.Max(1, thickPx);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, name = "StreakTex", wrapMode = TextureWrapMode.Clamp };
            var cols = new Color32[w * h];
            for (int x = 0; x < w; x++)
            {
                float u = (float)x / (w - 1);           // 0=左(尾) .. 1=右(頭)
                Color c = Color.Lerp(tailColor, headColor, u);
                for (int y = 0; y < h; y++) cols[y * w + x] = c;
            }
            tex.SetPixels32(cols);
            tex.Apply();
            // 頭(右端)中央をピボット＝position が頭の先端。 進行方向(+X)へ回転して流す。
            _streak = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(1f, 0.5f), Mathf.Max(1, pixelsPerUnit));
        }
    }
}
