using GameLoop;
using TMPro;
using UI.Lcd;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 起動 (2026-10-19)。 ゲームを立ち上げた直後は<b>真っ暗な画面に「TOUCH DISPLAY」</b>だけが出ていて、 画面を<b>クリック</b>すると液晶が点く。
    ///
    /// <para>システム自体の起動なので、 ゲームの演出 (光の線・閃光・ちらつき) は付けない。 実機の液晶がバックライトを点けるのと同じ、
    /// 一瞬の暗転のあとに、 明るさが<b>なめらかに単調に</b>上がるだけ (<c>_Power</c> = Quad のシェーダ)。</para>
    ///
    /// <para>「TOUCH DISPLAY」は<b>液晶の中身として</b>描く (タイトルの液晶ゾーンに、 既存のピクセルフォント mihiPixelmoji で。 黒の覆いの前)。
    /// だから、 画素の格子・ブルーム・ノイズなど液晶の効果がそのまま掛かる。 待っている間は BGM を止め (<c>AudioListener.pause</c>)・
    /// タイトルのボタンも押せない (<c>SpriteButton.InputLocked</c>)。 最初のクリックが後ろのボタンを押してしまわないため。 クリックの瞬間に BGM が始まる。</para>
    ///
    /// <para>出るのは、 ゲームを立ち上げて最初のタイトルだけ。 ランの途中でタイトルへ戻った時、 バッチ実行 (BOT)、 タイトル以外から始まった時は飛ばす。</para>
    /// </summary>
    public class Game2DBoot : MonoBehaviour
    {
        [Tooltip("液晶 (中身を描くカメラを持つ)")] public LcdScreen lcd;
        [Tooltip("液晶の Quad (_Power を入れる)")] public MeshRenderer lcdSurface;
        [Tooltip("ピクセルフォント (既存: mihiPixelmoji_v1)")] public TMP_FontAsset font;
        public bool enableBoot = true;
        [Min(0.3f), Tooltip("点灯の長さ (秒)。 暗転 → バックライトが上がる")] public float bootSeconds = 1.5f;
        [Min(1f), Tooltip("文字の高さ (液晶の dot・ 1 dot = 4 RT 画素)")] public float fontSizeDots = 8f;
        public Color promptColor = new Color32(176, 204, 224, 255);
        [Tooltip("TOUCH DISPLAY のゆっくりした明滅 (秒)")] public float blinkSec = 2.4f;

        [Header("点灯直後の液晶のざらつき (点いた瞬間だけ濃く、 すぐ通常に戻る)")]
        [Range(0f, 1f), Tooltip("点灯直後の残像の濃さ (通常は Game2DLcdScreen.mat の _Ghost)")] public float ghostPeak = 0.8f;
        [Min(0.1f), Tooltip("点灯が終わってから通常に戻るまで (秒)")] public float settleSeconds = 0.24f;

        [Header("点灯の瞬間の歪み")]
        [Range(0f, 1f), Tooltip("歪みの最大の強さ")] public float distortPeak = 1f;
        [Min(0f), Tooltip("クリックから、 歪みが最大になるまで (秒)。 絵が見え始める頃に合わせる")] public float distortDelay = 0.25f;
        [Min(0.05f), Tooltip("最大から消えるまでの速さ (秒・短いほど一瞬)")] public float distortDecay = 0.1f;

        /// <summary>起動待ち (TOUCH DISPLAY) の間は true。 横線ノイズはこの間は出さない (まだ画面が点いていないので)。</summary>
        public static bool Waiting { get; private set; }

        /// <summary>点灯直後の「ざらつき」の強さ (1 = 最も強い・0 = 通常)。 横線ノイズ (Game2DLcdNoise) が読む。</summary>
        public static float Settle { get; private set; }

        /// <summary>この起動でもう点けたか。</summary>
        public static bool Booted;
        private static readonly int PowerId = Shader.PropertyToID("_Power");

        private enum State { Waiting, Booting, Settling, Done }
        private State _state = State.Done;
        private float _t, _enabledAt, _settleT, _ghostBase = -1f;
        private const float IgnoreSec = 0.4f;
        private MaterialPropertyBlock _mpb;
        private GameObject _cover, _promptGo;
        private TextMeshPro _prompt;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Booted = false; Settle = 0f; Waiting = false; }

        private void OnEnable()
        {
            var gm = GameManager.Instance;
            bool skip = !enableBoot || Booted || Application.isBatchMode || (gm != null && gm.CurrentPhase != GameManager.GamePhase.Title);
            if (skip) { Finish(); return; }
            _state = State.Waiting; _enabledAt = Time.unscaledTime;
            SpriteButton.InputLocked = true;
            AudioListener.pause = true;
            BuildCover();
        }

        private void OnDisable()
        {
            if (_state != State.Done) { SpriteButton.InputLocked = false; AudioListener.pause = false; }
            DestroyCover();
        }

        private int ContentLayer()
        {
            int l = LayerMask.NameToLayer("LcdContent");
            if (l >= 0) return l;
            return lcd != null && lcd.contentCamera != null ? lcd.contentCamera.gameObject.layer : 0;
        }

        /// <summary>タイトルの液晶ゾーンに、 黒の覆いと TOUCH DISPLAY を置く (液晶の中身として描かれる = 液晶の効果が掛かる)。</summary>
        private void BuildCover()
        {
            var cam = lcd != null ? lcd.contentCamera : null;
            if (cam == null) return;
            int layer = ContentLayer();
            var basePos = cam.transform.position + cam.transform.forward * 8f;

            _cover = new GameObject("BootCover"); _cover.layer = layer;
            _cover.transform.position = basePos; _cover.transform.rotation = cam.transform.rotation;
            var sr = _cover.AddComponent<SpriteRenderer>();
            sr.sprite = Sprite.Create(Texture2D.whiteTexture, new Rect(0, 0, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height), new Vector2(0.5f, 0.5f), Texture2D.whiteTexture.width);
            sr.color = Color.black; sr.sortingOrder = 30000;
            _cover.transform.localScale = new Vector3(80f, 50f, 1f);

            _promptGo = new GameObject("BootPrompt"); _promptGo.layer = layer;
            _promptGo.transform.position = basePos - cam.transform.forward * 0.5f; _promptGo.transform.rotation = cam.transform.rotation;
            _prompt = _promptGo.AddComponent<TextMeshPro>();
            if (font != null) _prompt.font = font;
            float dotWorld = cam.orthographicSize * 2f / 139f;           // 液晶の 1 dot (タイトルの絵と同じ密度) = ワールドの大きさ
            _prompt.fontSize = fontSizeDots * dotWorld * 10f;            // TMP のワールド fontSize ≒ 文字高 (ワールド単位) × 10
            _prompt.alignment = TextAlignmentOptions.Center;
            _prompt.enableWordWrapping = false;
            _prompt.text = "TOUCH DISPLAY";
            _prompt.color = promptColor;
            _prompt.GetComponent<Renderer>().sortingOrder = 30001;
        }

        private void DestroyCover()
        {
            if (_cover != null) Destroy(_cover);
            if (_promptGo != null) Destroy(_promptGo);
            _cover = null; _promptGo = null; _prompt = null;
        }

        private void Finish()
        {
            _state = State.Done; Booted = true;
            SpriteButton.InputLocked = false; AudioListener.pause = false;
            DestroyCover();
            SetPower(1f);
        }

        private void Update()
        {
            switch (_state)
            {
                case State.Waiting:
                {
                    // ゲームがすでに進んでいる (テスト・BOT がランを始めた) なら飛ばす
                    var gm = GameManager.Instance;
                    if (gm != null && gm.CurrentPhase != GameManager.GamePhase.Title) { Finish(); return; }
                    if (_prompt != null)
                    {
                        float a = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 2f * Mathf.PI / Mathf.Max(0.5f, blinkSec));
                        var c = promptColor; c.a = Mathf.Lerp(0.35f, 1f, a); _prompt.color = c;
                    }
                    // 起動直後の誤入力 (ウィンドウを選んだクリックなど) を拾わないよう、 少しの間は受けない。 クリックか Space / Enter で点く
                    bool pressed = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
                    if (pressed && Time.unscaledTime - _enabledAt > IgnoreSec)
                    {
                        _state = State.Booting; _t = 0f;
                        DestroyCover();                              // 覆いと文字を外し、 すぐ暗転 (_Power = 0) から上げる
                        SetPower(0f);
                        AudioListener.pause = false;                 // クリックの瞬間に BGM が始まる
                    }
                    break;
                }
                case State.Booting:
                    _t += Time.unscaledDeltaTime;
                    SetPower(Mathf.Clamp01(_t / bootSeconds));
                    if (_t >= bootSeconds)
                    {
                        _state = State.Settling; _settleT = 0f; Booted = true;
                        SpriteButton.InputLocked = false; AudioListener.pause = false;
                        SetPower(1f);
                    }
                    break;
                case State.Settling:
                    _settleT += Time.unscaledDeltaTime;
                    if (_settleT >= settleSeconds) { Settle = 0f; ApplyGhost(0f); _state = State.Done; }
                    break;
            }
        }

        private void SetPower(float p)
        {
            if (lcdSurface == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            lcdSurface.GetPropertyBlock(_mpb);
            _mpb.SetFloat(PowerId, p);
            lcdSurface.SetPropertyBlock(_mpb);
        }

        /// <summary>残像の濃さを、 通常 (マテリアルの値) から点灯直後の濃さへ寄せる。 s = 0..1。</summary>
        private void ApplyGhost(float s)
        {
            if (lcdSurface == null) return;
            if (_ghostBase < 0f) _ghostBase = lcdSurface.sharedMaterial != null ? lcdSurface.sharedMaterial.GetFloat("_Ghost") : 0.38f;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            lcdSurface.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Ghost", Mathf.Lerp(_ghostBase, ghostPeak, s));
            lcdSurface.SetPropertyBlock(_mpb);
        }

        private void ApplyDistort()
        {
            if (lcdSurface == null) return;
            float d = 0f;
            if (_state == State.Booting) d = _t < distortDelay ? 0f : Mathf.Exp(-(_t - distortDelay) / distortDecay) * distortPeak;
            else if (_state == State.Settling) d = Mathf.Exp(-(bootSeconds + _settleT - distortDelay) / distortDecay) * distortPeak;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            lcdSurface.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Distort", d < 0.01f ? 0f : d);
            lcdSurface.SetPropertyBlock(_mpb);
        }

        private void LateUpdate()
        {
            Waiting = _state == State.Waiting;
            if (_state == State.Booting || _state == State.Settling || _state == State.Done) ApplyDistort();
            // ざらつき: 点灯の間は最大、 点灯が終わってから通常へ (なめらかに減らす)
            if (_state == State.Booting) Settle = 1f;
            else if (_state == State.Settling) { float u = 1f - Mathf.Clamp01(_settleT / settleSeconds); Settle = u * u; }
            else if (_state == State.Done) Settle = 0f;
            if (_state == State.Booting || _state == State.Settling) ApplyGhost(Settle);

            // 他の部品 (残像・ノイズ) も同じ Renderer のプロパティを書くので、 毎フレーム入れ直して確実に反映する
            if (_state == State.Booting) SetPower(Mathf.Clamp01(_t / bootSeconds));
            else if (_state == State.Waiting) SetPower(1f);
        }
    }
}
