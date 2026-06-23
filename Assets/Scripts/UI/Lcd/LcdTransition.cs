using System.Collections;
using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// LCD 画面のフェーズ切替（タイトル⇄スキルツリー等）に挟む「走査線崩れ」風トランジション。
    /// contentCamera の前面に貼った Quad に procedural Texture を毎フレーム流し込み、
    /// 短時間（既定 0.35 秒）で「ノイズ強化 → ピーク（中身入替） → 沈静化」を実行する。
    ///
    /// 規約: localScale 倍率は使わず、 Quad のスケールは contentCamera の orthographicSize / aspect から
    /// 自動計算（液晶コンテンツ空間を完全に覆う）。 PPU=32 のピクセル要素には触らない。
    /// </summary>
    [DisallowMultipleComponent]
    public class LcdTransition : MonoBehaviour
    {
        [Tooltip("LCD コンテンツ用カメラ（LcdScreen.contentCamera と同じもの）")]
        public Camera contentCamera;
        [Tooltip("オーバーレイ Quad の sortingLayer / sortingOrder。 ノード/線より前面に出す。")]
        public string sortingLayer = "Default";
        public int sortingOrder = 999;
        [Tooltip("ノイズテクスチャの縦解像度。 LCD ドット数程度（例 128〜256）")]
        public int textureHeight = 160;
        [Tooltip("走査線の流れ速度（行/秒）")]
        public float scrollRowsPerSec = 240f;

        private SpriteRenderer _sr;
        private Texture2D _tex;
        private Color32[] _px;
        private Sprite _sprite;
        private Coroutine _running;
        private GameObject _overlayGo; // 自身ではなく、 専用の子 GO に SpriteRenderer を持たせる（this を再ペアレントしない）

        private void Awake()
        {
            if (contentCamera == null) { enabled = false; return; }
            BuildOverlay();
            HideImmediate();
        }

        private void BuildOverlay()
        {
            // 専用 GO を作り contentCamera の子に配置。 this 自体は触らない（this は TitleLcdRig 等に置かれている想定）。
            _overlayGo = new GameObject("LcdTransitionOverlay");
            _overlayGo.transform.SetParent(contentCamera.transform, false);
            float z = Mathf.Max(contentCamera.nearClipPlane + 0.05f, 0.1f);
            _overlayGo.transform.localPosition = new Vector3(0f, 0f, z);
            _overlayGo.transform.localRotation = Quaternion.identity;

            // PPU=textureHeight にして sprite 世界サイズを 1/textureHeight × 1 に正規化、
            // localScale で カメラ視野(ow × oh) を覆う。
            _tex = new Texture2D(1, textureHeight, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "LcdTransitionTex" };
            _px = new Color32[textureHeight];
            for (int i = 0; i < _px.Length; i++) _px[i] = new Color32(255, 255, 255, 0);
            _tex.SetPixels32(_px); _tex.Apply(false, false);
            _sprite = Sprite.Create(_tex, new Rect(0, 0, 1, textureHeight), new Vector2(0.5f, 0.5f), textureHeight);

            _sr = _overlayGo.AddComponent<SpriteRenderer>();
            _sr.sprite = _sprite;
            _sr.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) _sr.sortingLayerName = sortingLayer;
            _sr.color = Color.white; // alpha 制御は texture 側

            float oh = contentCamera.orthographic ? contentCamera.orthographicSize * 2f : 20f;
            float ow = oh * contentCamera.aspect;
            _overlayGo.transform.localScale = new Vector3(ow * textureHeight, oh, 1f);

            // contentCamera が描画するレイヤーに合わせる（cullingMask で除外されないように）
            _overlayGo.layer = contentCamera.gameObject.layer;
        }

        private void HideImmediate()
        {
            if (_px == null || _tex == null) return;
            for (int i = 0; i < _px.Length; i++) _px[i] = new Color32(255, 255, 255, 0);
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }

        private void OnDestroy()
        {
            if (_overlayGo != null) Destroy(_overlayGo);
            if (_tex != null) Destroy(_tex);
            if (_sprite != null) Destroy(_sprite);
        }

        /// <summary>
        /// トランジション開始。 中間時点で <paramref name="onMidpoint"/> を呼ぶ（中身入替に使う）。
        /// 既に実行中なら一旦キャンセルして再開始する。
        /// </summary>
        public Coroutine Begin(System.Action onMidpoint, float duration = 0.35f)
        {
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Run(onMidpoint, duration));
            return _running;
        }

        private IEnumerator Run(System.Action onMidpoint, float dur)
        {
            float t = 0f;
            bool fired = false;
            while (t < dur)
            {
                // 1フレでの dt を 50ms にキャップ（重いフレームでコルーチンが一気に終わるのを防ぐ）
                t += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
                float p = Mathf.Clamp01(t / dur);
                // 0→0.5→1 で 0→1→0 に動くピーク強度
                float intensity = Mathf.Sin(p * Mathf.PI);
                DrawScanlines(intensity);
                if (!fired && p >= 0.5f) { fired = true; try { onMidpoint?.Invoke(); } catch { } }
                yield return null;
            }
            HideImmediate();
            _running = null;
        }

        private void DrawScanlines(float intensity)
        {
            if (_tex == null || _px == null) return;
            int h = textureHeight;
            float now = Time.unscaledTime;
            int scroll = Mathf.RoundToInt(now * scrollRowsPerSec);
            // ピーク時にはほぼ全画面が走査線で覆われるくらいに密度/輝度を上げる。
            for (int y = 0; y < h; y++)
            {
                int sy = (y + scroll) % h; if (sy < 0) sy += h;
                uint hash = unchecked((uint)sy * 2654435761u) ^ unchecked((uint)Mathf.FloorToInt(now * 30f) * 374761393u);
                float rnd = ((hash >> 8) & 0xFFFFu) / 65535f;

                // 中間付近で密度が増す（ピーク時はほぼ全行が点灯）
                float threshold = Mathf.Lerp(0.95f, 0.05f, intensity);
                float a = (rnd > threshold) ? Mathf.Lerp(0.0f, 1.0f, intensity) : 0f;

                // 縦に走るスイープ（明るい4-5行）
                int sweepPos = Mathf.RoundToInt(intensity * h * 1.5f) % h;
                if (Mathf.Abs(y - sweepPos) <= 3) a = Mathf.Max(a, 1.0f * intensity);

                // 全体ベースのフィル（ピーク付近で薄い白フラッシュ）
                a = Mathf.Max(a, intensity * 0.35f);

                byte ab = (byte)(Mathf.Clamp01(a) * 255f);
                _px[y] = new Color32(255, 255, 255, ab);
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
        }
    }
}
