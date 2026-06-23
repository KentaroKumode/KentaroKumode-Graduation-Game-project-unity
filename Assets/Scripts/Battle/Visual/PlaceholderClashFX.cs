using System.Collections;
using UnityEngine;

namespace Battle.Visual
{
    /// <summary>
    /// 武器/敵 攻撃エフェクト本体。 SpriteRenderer ベース。
    /// 素材スプライト(黒+α)は <see cref="Battle/AlphaMaskColor"/> シェーダを通して任意色で表示する。
    ///
    /// 使い方:
    ///   var fx = PlaceholderClashFX.Spawn(parent, sprite, color, sizeWorld, layer);
    ///   yield return fx.PlayClash(from, to, duration);
    ///   yield return fx.PlayLand(target, duration);   // 勝ち
    ///   yield return fx.PlayBreak(duration);          // 負け
    /// </summary>
    public class PlaceholderClashFX : MonoBehaviour
    {
        private SpriteRenderer _renderer;
        private Material _mat;
        private Vector3 _baseScale;

        public static PlaceholderClashFX Spawn(Transform parent, Sprite sprite, Color color, float sizeWorld, int layer, string label = "ClashFX")
        {
            var go = new GameObject(label);
            go.transform.SetParent(parent, false);
            go.layer = layer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;

            var sh = Shader.Find("Battle/AlphaMaskColor");
            if (sh != null)
            {
                _ApplyMaskMaterial(sr, sh, color);
            }
            else
            {
                // フォールバック: スプライトの黒は黒のまま、 色は alpha だけ反映される。
                sr.color = color;
            }

            // sizeWorld = 1 想定: スプライトはネイティブの PPU=32 で 1 ユニット相当の大きさに。
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, sizeWorld);

            var fx = go.AddComponent<PlaceholderClashFX>();
            fx._renderer = sr;
            fx._mat = sr.sharedMaterial;
            fx._baseScale = go.transform.localScale;
            return fx;
        }

        private static void _ApplyMaskMaterial(SpriteRenderer sr, Shader sh, Color color)
        {
            var mat = new Material(sh);
            mat.SetTexture("_MainTex", sr.sprite != null ? sr.sprite.texture : null);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Emission", 1f);
            sr.sharedMaterial = mat;
        }

        public void SetColor(Color c)
        {
            if (_mat != null && _mat.HasProperty("_Color")) _mat.SetColor("_Color", c);
            else if (_renderer != null) _renderer.color = c;
        }

        public void SetEmission(float k)
        {
            if (_mat != null && _mat.HasProperty("_Emission")) _mat.SetFloat("_Emission", k);
        }

        /// <summary>from → to の途中で停止し、その場で小刻みに揺れる(鍔迫り合い)。</summary>
        public IEnumerator PlayClash(Vector3 from, Vector3 to, float duration)
        {
            transform.position = from;
            float t = 0f;
            Vector3 mid = Vector3.Lerp(from, to, 0.5f);
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                if (k < 0.5f)
                {
                    float u = k / 0.5f;
                    transform.position = Vector3.Lerp(from, mid, EaseOutCubic(u));
                }
                else
                {
                    float shake = Mathf.Sin(t * 24f) * 0.06f;
                    transform.position = mid + new Vector3(shake, shake * 0.4f, 0f);
                    float pulse = 1f + Mathf.Sin(t * 18f) * 0.05f;
                    transform.localScale = _baseScale * pulse;
                    SetEmission(1.4f + Mathf.Sin(t * 14f) * 0.4f);
                }
                yield return null;
            }
            transform.localScale = _baseScale;
            transform.position = mid;
            SetEmission(1f);
        }

        /// <summary>中央から target へ突き抜けて着弾。 着弾時に拡大→消滅。</summary>
        public IEnumerator PlayLand(Vector3 target, float duration)
        {
            Vector3 from = transform.position;
            float t = 0f;
            while (t < duration * 0.7f)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / (duration * 0.7f));
                transform.position = Vector3.Lerp(from, target, EaseInCubic(k));
                SetEmission(1f + k * 2f);
                yield return null;
            }
            float burst = 0f;
            float burstDur = duration * 0.3f;
            Vector3 hitScale = _baseScale * 1.8f;
            while (burst < burstDur)
            {
                burst += Time.deltaTime;
                float k = Mathf.Clamp01(burst / burstDur);
                transform.localScale = Vector3.Lerp(hitScale, Vector3.zero, k);
                SetEmission(3f * (1f - k));
                yield return null;
            }
            Object.Destroy(gameObject);
        }

        /// <summary>その場で破砕→消滅(押し返され破壊)。</summary>
        public IEnumerator PlayBreak(float duration)
        {
            float t = 0f;
            Vector3 start = _baseScale;
            while (t < duration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / duration);
                float shake = Mathf.Sin(t * 60f) * (1f - k) * 0.15f;
                transform.position += new Vector3(shake, 0f, 0f) * Time.deltaTime * 8f;
                transform.localScale = Vector3.Lerp(start, Vector3.zero, k);
                SetEmission(Mathf.Lerp(1.5f, 0f, k));
                yield return null;
            }
            Object.Destroy(gameObject);
        }

        private static float EaseOutCubic(float x) => 1f - Mathf.Pow(1f - x, 3f);
        private static float EaseInCubic(float x)  => x * x * x;
    }
}
