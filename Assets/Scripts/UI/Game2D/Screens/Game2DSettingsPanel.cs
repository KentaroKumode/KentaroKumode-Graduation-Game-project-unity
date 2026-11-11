using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 設定の画面 (#85・<b>仮置き</b>・2026-11-11・docs/design/gui-needed.md #41)。 共通の板のモーダルに、 値ごとのスライダーを並べる。
    /// 頁: 画面 (明るさ・走査線・湾曲・色収差・ビネット・ノイズ) / 音 (メイン・BGM・SE)。 値の置き場・範囲・既定は <see cref="GameSettings"/>、
    /// 効かせるのは <see cref="Game2DSettingsApply"/>。 動かすとその場で効き、 閉じる時に保存する (次の起動でも効く)。
    /// <para>開き方: Esc (タイトル・航行・戦闘で、 ほかの窓が出ていない時)。 もう一度 Esc か「閉じる」で閉じる。
    /// 操作: バーを押す・引く / 選んだ行を [−] [+] か ← → (Shift で 10 ずつ)。 タイトルにはまだ設定のボタンの絵が無いので Esc だけ。</para>
    /// </summary>
    public class Game2DSettingsPanel : MonoBehaviour
    {
        public Game2DModal modal;

        private static readonly GameSettings.Key[] ScreenKeys = { GameSettings.Key.Bright, GameSettings.Key.Scan, GameSettings.Key.Curve, GameSettings.Key.Chroma, GameSettings.Key.Vignette, GameSettings.Key.Noise };
        private static readonly GameSettings.Key[] SoundKeys = { GameSettings.Key.Master, GameSettings.Key.Bgm, GameSettings.Key.Se };

        private static Game2DSettingsPanel _inst;
        private ScreenDef _def;
        private int _page;
        private bool _lockedInput, _prevLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DSettingsPanel>() == null) m.gameObject.AddComponent<Game2DSettingsPanel>().modal = m;
        }

        private void Awake() { _inst = this; }
        private void OnDestroy() { if (_inst == this) _inst = null; }

        /// <summary>この画面が窓を使っているか。</summary>
        public bool IsShown => modal != null && _def != null && modal.IsOpen && modal.Current == _def;

        /// <summary>ほかの所から開く (タイトルの記録の窓など)。 開けたら true。</summary>
        public static bool OpenFromAnywhere() { if (_inst == null || _inst.modal == null || _inst.modal.IsOpen) return false; _inst.Open(); return true; }

        private static bool CanOpenNow()
        {
            if (Application.isBatchMode || GameHooks.BotDriving || Game2DLoadingScreen.Blocking || Game2DBoot.Waiting) return false;
            var gm = GameManager.Instance; if (gm == null) return true;
            var ph = gm.CurrentPhase;
            return ph == GameManager.GamePhase.Title || ph == GameManager.GamePhase.MapNavigation || ph == GameManager.GamePhase.Combat;
        }

        private void Update()
        {
            if (modal == null) return;
            // ほかの画面 (場面の画面など) が窓を取った: こちらは閉じた扱いにする
            if (_def != null && !IsShown) { _def = null; Unlock(); GameSettings.Save(); }
            bool esc = Input.GetKeyDown(KeyCode.Escape);
            if (IsShown)
            {
                if (esc) { Close(); return; }
                bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
                if (Input.GetKeyDown(KeyCode.LeftArrow)) Step(modal.Selected, shift ? -10 : -1);
                else if (Input.GetKeyDown(KeyCode.RightArrow)) Step(modal.Selected, shift ? 10 : 1);
                return;
            }
            if (esc && !modal.IsOpen && !Game2DModal.Active && CanOpenNow()) Open();      // Active = 閉じる途中の窓も含む (同じ Esc で別の窓を閉じた直後に開かない)
        }

        private GameSettings.Key[] Keys => _page == 0 ? ScreenKeys : SoundKeys;

        private void Open()
        {
            _page = 0;
            var gm = GameManager.Instance;
            if (gm == null || gm.CurrentPhase == GameManager.GamePhase.Title) { _prevLock = SpriteButton.InputLocked; SpriteButton.InputLocked = true; _lockedInput = true; }      // 後ろのタイトルのボタンを押さない
            _def = Build(); modal.Open(_def);
        }

        private void Close()
        {
            if (IsShown) modal.Close();
            _def = null; Unlock(); GameSettings.Save();
        }

        private void Unlock() { if (_lockedInput) { SpriteButton.InputLocked = _prevLock; _lockedInput = false; } }

        private void Step(int row, int delta)
        {
            var keys = Keys; if (row < 0 || row >= keys.Length) return;
            GameSettings.Set(keys[row], GameSettings.Get(keys[row]) + delta);
            Game2DSfx.Play("ui_click");
            Rebuild();
        }

        private void Rebuild() { _def = Build(); modal.Replace(_def); }

        private static string Effect(GameSettings.Key k)
        {
            int v = GameSettings.Get(k);
            if (k == GameSettings.Key.Master || k == GameSettings.Key.Bgm || k == GameSettings.Key.Se) return v >= 100 ? "最大" : "";
            if (k == GameSettings.Key.Bright) return v == 50 ? "標準" : "標準の ×" + GameSettings.Brightness.ToString("0.00");
            if (v == 0) return "切";
            return v == 50 ? "標準" : "標準の ×" + GameSettings.FxScale(k).ToString("0.00");
        }

        private ScreenDef Build()
        {
            var keys = Keys;
            var d = new ScreenDef
            {
                id = "settings_panel", jp = true, w = 472, cols = 1, rowH = 15, sliders = true, sliderNoTicks = true, sliderNameW = 74,
                title = _page == 0 ? "設定 ― 画面" : "設定 ― 音",
                sub = "動かすとすぐ効く。 閉じると保存する",
            };
            foreach (var k in keys)
            {
                var def = GameSettings.Defs[(int)k]; int v = GameSettings.Get(k);
                d.Items.Add(new ModalItem { name = def.name, count = v, max = def.max, numText = v.ToString(), desc = def.desc + " (" + def.min + "〜" + def.max + "・既定 " + def.def + ")", effect = Effect(k) });
            }
            d.buttons.Add(new ModalBtn { label = "−", action = "dec" });
            d.buttons.Add(new ModalBtn { label = "+", action = "inc" });
            d.buttons.Add(new ModalBtn { label = _page == 0 ? "音へ" : "画面へ", action = "page" });
            d.buttons.Add(new ModalBtn { label = "既定に戻す", action = "reset" });
            d.buttons.Add(new ModalBtn { label = "閉じる", action = "close", primary = true });
            d.footer = "バーを押す・引く / ← → で 1 ずつ (Shift で 10 ずつ) / Esc で閉じる";
            d.onAction = (a, sel) =>
            {
                switch (a)
                {
                    case "close": Close(); return;
                    case "dec": Step(sel, -1); return;
                    case "inc": Step(sel, 1); return;
                    case "slide":
                        if (sel >= 0 && sel < keys.Length) GameSettings.Set(keys[sel], Game2DModal.SlideTarget);
                        break;
                    case "page": _page = 1 - _page; Game2DSfx.Play("ui_tab"); break;
                    case "reset":
                        foreach (var k in keys) GameSettings.Set(k, GameSettings.Defs[(int)k].def);
                        modal.ShowToast("この頁を既定に戻した");
                        break;
                }
                Rebuild();
            };
            return d;
        }
    }
}
