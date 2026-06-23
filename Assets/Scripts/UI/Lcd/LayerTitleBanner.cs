using System.Collections;
using TMPro;
using UnityEngine;

namespace UI.Lcd
{
    /// <summary>
    /// 層進入時の「層タイトル＋サブタイトル」バナー。
    /// <see cref="GameLoop.LayerTitles.OnShow"/> を購読し、 LCD コンテンツ中央にフェードで重ねる。
    ///
    /// 配置: contentCamera の子に専用 GameObject を1つ作り、 そこに TMP テキスト2行(タイトル/サブタイトル)を立てる。
    /// アニメ: fade in 0.4s → hold 3.0s → fade out 0.6s（テキストのみ。 背景補助なし）。
    /// </summary>
    [DisallowMultipleComponent]
    public class LayerTitleBanner : MonoBehaviour
    {
        [Header("配線")]
        [Tooltip("LCD コンテンツ用カメラ（LcdScreen.contentCamera と同じもの）")]
        public Camera contentCamera;
        [Tooltip("タイトル/サブタイトルの TMP フォント（mihiPixelmoji_v1 等を推奨）")]
        public TMP_FontAsset font;

        [Header("レイアウト")]
        [Tooltip("タイトルのフォントサイズ(world)")]
        public float titleSize = 2.6f;
        [Tooltip("サブタイトルのフォントサイズ(world)")]
        public float subtitleSize = 1.4f;
        [Tooltip("タイトルとサブタイトルの行間(world)")]
        public float gap = 1.6f;
        [Tooltip("中央位置のYオフセット(world)。 0 で完全中央。")]
        public float yOffset = 0f;
        [Tooltip("sortingLayer / sortingOrder。 LCD 内コンテンツの最前面に出す。")]
        public string sortingLayer = "Default";
        public int sortingOrder = 1000;

        [Header("アニメ")]
        [Range(0f, 2f)] public float fadeIn = 0.4f;
        [Range(0f, 8f)] public float hold = 3.0f;
        [Range(0f, 2f)] public float fadeOut = 0.6f;

        private GameObject _overlayGo;
        private TextMeshPro _titleTmp;
        private TextMeshPro _subtitleTmp;
        private Coroutine _running;

        private void Awake()
        {
            if (contentCamera == null)
            {
                var lcd = FindObjectOfType<LcdScreen>();
                if (lcd != null) contentCamera = lcd.contentCamera;
            }
            if (contentCamera == null) { enabled = false; return; }
            BuildOverlay();
            HideImmediate();
        }

        private void OnEnable()  => GameLoop.LayerTitles.OnShow += HandleShow;
        private void OnDisable() => GameLoop.LayerTitles.OnShow -= HandleShow;

        private void BuildOverlay()
        {
            _overlayGo = new GameObject("LayerTitleBannerOverlay");
            _overlayGo.transform.SetParent(contentCamera.transform, false);
            float z = Mathf.Max(contentCamera.nearClipPlane + 0.05f, 0.1f);
            _overlayGo.transform.localPosition = new Vector3(0f, yOffset, z);
            _overlayGo.transform.localRotation = Quaternion.identity;
            _overlayGo.layer = ResolveContentLayer();

            _titleTmp    = CreateTmp("Title",    new Vector3(0f,  gap * 0.5f, 0f), titleSize);
            _subtitleTmp = CreateTmp("Subtitle", new Vector3(0f, -gap * 0.5f, 0f), subtitleSize);
        }

        private int ResolveContentLayer()
        {
            int lcdLayer = LayerMask.NameToLayer("LcdContent");
            if (lcdLayer >= 0) return lcdLayer;
            return contentCamera != null ? contentCamera.gameObject.layer : 0;
        }

        private TextMeshPro CreateTmp(string name, Vector3 localPos, float fontSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_overlayGo.transform, false);
            go.transform.localPosition = localPos;
            go.layer = ResolveContentLayer();

            var tmp = go.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            tmp.fontSize = fontSize;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.enableWordWrapping = false;
            tmp.text = string.Empty;
            tmp.color = new Color(1f, 1f, 1f, 0f);

            var r = tmp.GetComponent<Renderer>();
            r.sortingOrder = sortingOrder;
            if (!string.IsNullOrEmpty(sortingLayer)) r.sortingLayerName = sortingLayer;
            return tmp;
        }

        private void HandleShow(string title, string subtitle)
        {
            if (_titleTmp == null || _subtitleTmp == null) return;
            _titleTmp.text    = title    ?? string.Empty;
            _subtitleTmp.text = subtitle ?? string.Empty;
            if (_running != null) StopCoroutine(_running);
            _running = StartCoroutine(Animate());
        }

        private IEnumerator Animate()
        {
            yield return Lerp(0f, 1f, Mathf.Max(0.0001f, fadeIn));
            float t = 0f;
            while (t < hold) { t += Time.unscaledDeltaTime; yield return null; }
            yield return Lerp(1f, 0f, Mathf.Max(0.0001f, fadeOut));
            _running = null;
        }

        private IEnumerator Lerp(float a, float b, float dur)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / dur);
                SetAlpha(Mathf.Lerp(a, b, k));
                yield return null;
            }
            SetAlpha(b);
        }

        private void SetAlpha(float a)
        {
            if (_titleTmp != null)    { var c = _titleTmp.color;    c.a = a; _titleTmp.color    = c; }
            if (_subtitleTmp != null) { var c = _subtitleTmp.color; c.a = a; _subtitleTmp.color = c; }
        }

        private void HideImmediate() => SetAlpha(0f);
    }
}
