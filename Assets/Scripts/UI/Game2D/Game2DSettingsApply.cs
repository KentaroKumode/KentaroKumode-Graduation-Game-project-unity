using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 設定 (<see cref="GameSettings"/>) を実際に効かせる係 (#85・2026-11-11)。 実行時に 1 つ作り、 シーンを跨いで残る。
    /// <list type="bullet">
    /// <item>音: メイン = <c>AudioListener.volume</c>・SE = <see cref="Game2DSfx.Volume"/>・BGM = <see cref="TitleBgm"/> が自分で読む。</item>
    /// <item>液晶の Quad (Game2D/LcdScreen): 走査線 <c>_Scan</c>・湾曲 <c>_Curve</c>・色収差 <c>_Chroma</c>・フィルムの粒 <c>_Grain</c>。
    ///   アセットのマテリアルは書き換えず、 <b>実行時の複製</b>を Quad に付けて、 そこへ「元の値 × 倍率」を入れる。
    ///   MaterialPropertyBlock にしないのは、 地図の入力 (Game2DMapView.Input) が湾曲を sharedMaterial から読んでクリック位置を直すため ──
    ///   複製を sharedMaterial にすれば、 湾曲を変えてもクリックの位置がずれない。</item>
    /// <item>横線ノイズ: <see cref="Game2DLcdNoise.gain"/> に倍率を掛ける。</item>
    /// <item>外枠の仕上げ (Game2D/LcdFx): ビネット <c>_Vignette</c>・明るさ <c>_Bright</c> (寒色の乗算に載せた・液晶と GUI の両方に掛かる)。</item>
    /// </list>
    /// 足し戻し禁止の効果 (画素の格子・サブピクセル・ドットの芯・フチ) はここでも扱わない。
    /// </summary>
    public class Game2DSettingsApply : MonoBehaviour
    {
        private static Game2DSettingsApply _instance;
        private static bool _shuttingDown;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _shuttingDown = false; _instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            if (_shuttingDown || _instance != null || Application.isBatchMode) return;
            var go = new GameObject("Game2DSettingsApply") { hideFlags = HideFlags.HideAndDontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<Game2DSettingsApply>();
        }

        private void OnApplicationQuit() { _shuttingDown = true; GameSettings.Save(); }

        private int _applied = -1;
        private float _nextFind;

        // 液晶の Quad
        private Renderer _lcd; private Material _lcdMat;
        private float _scan0, _curve0, _chroma0, _grain0;
        // 横線ノイズ
        private Game2DLcdNoise _noise; private float _gain0;
        // 外枠の仕上げ
        private Graphic _fx; private Material _fxMat; private float _vig0;

        private static readonly int ScanId = Shader.PropertyToID("_Scan"), CurveId = Shader.PropertyToID("_Curve"), ChromaId = Shader.PropertyToID("_Chroma"),
            GrainId = Shader.PropertyToID("_Grain"), VigId = Shader.PropertyToID("_Vignette"), BrightId = Shader.PropertyToID("_Bright");

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            if (_lcdMat != null) Destroy(_lcdMat);
            if (_fxMat != null) Destroy(_fxMat);
        }

        private void Update()
        {
            bool lost = _lcd == null || _noise == null || _fx == null;
            if (lost && Time.unscaledTime >= _nextFind) { _nextFind = Time.unscaledTime + 1f; if (Find()) _applied = -1; }
            if (_applied == GameSettings.Version) return;
            _applied = GameSettings.Version;
            Apply();
        }

        /// <summary>シーンの液晶・ノイズ・仕上げの板を探して、 元の値を覚える。 新しく見つけたら true。</summary>
        private bool Find()
        {
            bool found = false;
            if (_lcd == null)
            {
                var ghost = FindObjectOfType<Game2DLcdGhost>();
                var r = ghost != null ? ghost.surface : null;
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.shader != null && r.sharedMaterial.shader.name == "Game2D/LcdScreen")
                {
                    var src = r.sharedMaterial;
                    if (_lcdMat != null) Destroy(_lcdMat);
                    _scan0 = src.GetFloat(ScanId); _curve0 = src.GetFloat(CurveId); _chroma0 = src.GetFloat(ChromaId); _grain0 = src.GetFloat(GrainId);
                    _lcdMat = new Material(src) { name = src.name + " (settings)" };
                    r.sharedMaterial = _lcdMat; _lcd = r; found = true;
                }
            }
            if (_noise == null)
            {
                _noise = FindObjectOfType<Game2DLcdNoise>();
                if (_noise != null) { _gain0 = _noise.gain; found = true; }
            }
            if (_fx == null)
            {
                foreach (var g in FindObjectsOfType<Graphic>())
                {
                    var m = g.material;
                    if (m == null || m.shader == null || m.shader.name != "Game2D/LcdFx") continue;
                    if (_fxMat != null) Destroy(_fxMat);
                    _vig0 = m.GetFloat(VigId);
                    _fxMat = new Material(m) { name = m.name + " (settings)" };
                    g.material = _fxMat; _fx = g; found = true;
                    break;
                }
            }
            return found;
        }

        private void Apply()
        {
            AudioListener.volume = GameSettings.Master01;
            Game2DSfx.Volume = GameSettings.Se01;
            if (_lcdMat != null)
            {
                _lcdMat.SetFloat(ScanId, Mathf.Clamp01(_scan0 * GameSettings.FxScale(GameSettings.Key.Scan)));
                _lcdMat.SetFloat(CurveId, Mathf.Clamp(_curve0 * GameSettings.FxScale(GameSettings.Key.Curve), 0f, 0.2f));
                _lcdMat.SetFloat(ChromaId, Mathf.Clamp(_chroma0 * GameSettings.FxScale(GameSettings.Key.Chroma), 0f, 0.02f));
                _lcdMat.SetFloat(GrainId, Mathf.Clamp(_grain0 * GameSettings.FxScale(GameSettings.Key.Noise), 0f, 0.3f));
            }
            if (_noise != null) _noise.gain = _gain0 * GameSettings.FxScale(GameSettings.Key.Noise);
            if (_fxMat != null)
            {
                _fxMat.SetFloat(VigId, Mathf.Clamp01(_vig0 * GameSettings.FxScale(GameSettings.Key.Vignette)));
                _fxMat.SetFloat(BrightId, GameSettings.Brightness);
            }
        }
    }
}
