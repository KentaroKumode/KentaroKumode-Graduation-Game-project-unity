using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// エンディングの画面 (#328・2026-11-15・<b>仮置き</b>)。 ランを踏破した時 (フェーズ RunClear) に、 <see cref="Endings.Resolve"/> の結果の題と本文を、
    /// <b>液晶の中に</b>ドット字 (<see cref="Game2DJpFont"/>・字の指定は GameFont.cs) で頁送りで見せる。 EndTrue の後は、 続けて後日談 (<see cref="Endings.Epilogue"/>) の頁も見せる
    /// (額縁が閉じるのは TRUE だけ・EndGood では本が書かれないので後日談は無い)。 読み終わると、 今までの「踏破」の窓 (到達の数字・入手した遺物 → タイトルへ) に続く。
    ///
    /// <para><b>額縁の決まり</b>: エンディングの文は物語なので液晶の中に描く (筐体の窓 = Game2DModal には出さない)。 出している間は計器盤 (GUI) を隠す
    /// (<see cref="Game2DHud"/> がロード画面と同じく <see cref="Blocking"/> を見る)。</para>
    ///
    /// <para><b>送り方</b>: 頁ごとに行が上から順に浮かぶ (1 行 <see cref="LineReveal"/> 秒)。 クリック・Space・Enter で、 浮かびきる前なら全部を出し、 浮かびきっていれば次の頁へ。
    /// 本文の「———」の行は頁の区切りに使う (描かない)。 頁に入りきらない段落は次の頁へ送る。</para>
    ///
    /// <para><b>曲</b>: 出している間は <see cref="Game2DBgm"/> が <see cref="Track"/> を読む (EndTrue = bgm_end_true / EndGood = bgm_end_good / ほか = bgm_end_other / 後日談 = bgm_epilogue・曲はまだ無いので無音)。
    /// 描き方はロード画面 (<see cref="Game2DLoadingScreen"/>) と同じ: 地図の板の複製を少し手前に置き、 Game2D/LoadingBlend の網目で出入りする。 BOT がランを動かしている間は出さない。</para>
    /// </summary>
    public class Game2DEndingScreen : MonoBehaviour
    {
        public const int W = Game2DLayout.Width, H = Game2DLayout.Height;
        /// <summary>1 行が浮かぶ間 (秒)。</summary>
        public const float LineReveal = 0.12f;
        private const float FadeSec = 0.35f;
        // 配置 (dot): 液晶の湾曲で端が切れるので、 左右 40・上 30・下 26 の内側
        //   題は 2 倍のドット字で中央 (2026-11-15 撮影: 小さく左上に寄りすぎていた)
        private const int Left = 40, TextW = 400, TitleY = 26, TitleScale = 2, BodyY = 64, FootY = 232, LinesPerPage = 12;

        // 読みやすさ (2026-11-15 撮影: 液晶のにじみで本文が読みにくかった): 液晶の効果全体は弱めず、 字の側で抑える ──
        //   ① 字の色を明るさ 8 割ほどに落とす (白に近い字ほどにじみが広がる)  ② 字の周り 1 dot (8 方向) に地より暗い縁取りを打ち、 にじみが字の間を埋めないようにする
        private static readonly Color32 Bg = new Color32(0x07, 0x10, 0x15, 255), Ink = new Color32(0xb4, 0xc8, 0xce, 255),
            Dim = new Color32(0x4d, 0x6a, 0x76, 255), Amber = new Color32(0xd8, 0x9c, 0x48, 255), Rim = new Color32(0x02, 0x05, 0x07, 255);

        /// <summary>読み終わって閉じた回数 (場面の窓 Game2DPhaseScreens が見て、 閉じた後に「踏破」の窓を必ず作り直す)。</summary>
        public static int ClosedSerial { get; private set; }

        /// <summary>出している間 (と、 閉じたフレーム) は true。 GUI・場面の窓・キー入力がこれを見る。</summary>
        public static bool Blocking => _active || _endFrame == Time.frameCount;
        private static bool _active; private static int _endFrame = -1;
        /// <summary>出している間に鳴らす曲の名前 (<see cref="Game2DBgm"/> の名前・出していなければ null)。</summary>
        public static string Track { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _active = false; _endFrame = -1; Track = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DEndingScreen>() != null) return;
            var mv = FindObjectOfType<Game2DMapView>(); if (mv == null) return;
            mv.gameObject.AddComponent<Game2DEndingScreen>();
        }

        private struct Page { public string title; public List<string> lines; public string track; }
        private readonly List<Page> _pages = new List<Page>();
        private int _page; private float _t, _fadeT; private bool _closing;
        private RunState _doneRun;
        private MeshRenderer _quad; private Texture2D _tex; private KitPix _pix; private MaterialPropertyBlock _mpb;

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_quad != null) { if (_quad.sharedMaterial != null && _quad.sharedMaterial.name == "Game2DEndingBlend") Destroy(_quad.sharedMaterial); Destroy(_quad.gameObject); }
            if (_active) { _active = false; GameManager.InputBlockedByScreen = false; }
        }

        /// <summary>液晶の中身のカメラを、 踏破 (RunClear) と裂け目の一択 (RiftChoice) の間も生かす (シーンの PhaseDirector がこの 2 つのフェーズで止めていて、
        /// エンディングの字が写らず、 一択の窓が出るまで液晶が真っ暗だった・2026-11-15)。</summary>
        private void Start()
        {
            var mv = FindObjectOfType<Game2DMapView>();
            if (mv != null && mv.plotQuad != null) PhaseDirector.AlsoShowFor(mv.plotQuad.transform, GameManager.GamePhase.RunClear, GameManager.GamePhase.RiftChoice);
        }

        private bool EnsureQuad()
        {
            if (_quad != null) return true;
            var mv = FindObjectOfType<Game2DMapView>();
            if (mv == null || mv.plotQuad == null) return false;
            var src = mv.plotQuad;
            var go = new GameObject("EndingPlotQuad") { layer = src.gameObject.layer };
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition + new Vector3(0, 0, -0.12f);      // ロード画面 (−0.1) より手前
            go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            _quad = go.AddComponent<MeshRenderer>(); _quad.enabled = false;
            var sh = Shader.Find("Game2D/LoadingBlend");
            _quad.sharedMaterial = sh != null ? new Material(sh) { name = "Game2DEndingBlend" } : src.sharedMaterial;
            return true;
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (GameHooks.BotDriving || Application.isBatchMode) { if (_active) Stop(); return; }
            // 踏破の瞬間を拾う (ラン 1 つに 1 回)
            if (!_active && gm != null && gm.CurrentPhase == GameManager.GamePhase.RunClear && gm.Run != null && gm.Run != _doneRun)
            {
                _doneRun = gm.Run;
                Play(Endings.Resolve(gm.Run));
            }
            if (gm != null && gm.CurrentPhase == GameManager.GamePhase.Title) _doneRun = null;
            if (!_active) return;

            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _t += dt; _fadeT += dt;
            bool press = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
            if (_closing) { if (_fadeT >= FadeSec) { Stop(); return; } }
            else if (press && _fadeT >= FadeSec)
            {
                GameManager.ScreenUsedSpaceFrame = Time.frameCount;      // 押した入力を後ろへ流さない
                var pg = _pages[_page];
                if (_t < pg.lines.Count * LineReveal) _t = 999f;          // 浮かびきる前: 全部を出す
                else if (_page + 1 < _pages.Count) { _page++; _t = 0f; Track = _pages[_page].track; Game2DSfx.Play("ui_tab", 0.6f); }
                else { _closing = true; _fadeT = 0f; Game2DSfx.Play("ui_confirm"); }
            }
            Draw();
            ApplyFade();
        }

        /// <summary>エンディング e の頁を作って出す。 EndTrue なら後日談を続ける。</summary>
        private void Play(Endings.Ending e)
        {
            if (string.IsNullOrEmpty(e.body) || !EnsureQuad()) return;
            _pages.Clear();
            string track = e.id == Endings.EndTrueId ? Game2DBgm.EndTrue : e.id == Endings.EndGoodId ? Game2DBgm.EndGood : e.id == Endings.Epilogue.id ? Game2DBgm.Epilogue : Game2DBgm.EndOther;
            AddPages(e, track);
            if (e.id == Endings.EndTrueId) AddPages(Endings.Epilogue, Game2DBgm.Epilogue);      // 額縁が閉じる: 後日談 (棚の隅)
            if (_pages.Count == 0) return;
            _page = 0; _t = 0f; _fadeT = 0f; _closing = false; _active = true; Track = _pages[0].track;
            GameManager.InputBlockedByScreen = true;
            if (_tex == null) { _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DEnding" }; _pix = new KitPix(W, H); }
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_MainTex", _tex); _mpb.SetVector("_Dots", new Vector4(W, H, 0, 0)); _mpb.SetFloat("_UsePrev", 0f);
            _quad.SetPropertyBlock(_mpb);
            _quad.enabled = true;
            Draw(); ApplyFade();
            Game2DSfx.Play("gui_out");
        }

        /// <summary>本文を頁に割る: 「———」の行で区切り、 段落 (空行まで) が頁に入りきらなければ次の頁へ送る。 1 頁目に題。</summary>
        private void AddPages(Endings.Ending e, string track)
        {
            var cur = new List<string>(); bool first = true;
            var para = new List<string>();
            void FlushPage() { if (cur.Count > 0) { while (cur.Count > 0 && cur[cur.Count - 1].Length == 0) cur.RemoveAt(cur.Count - 1); if (cur.Count > 0) _pages.Add(new Page { title = first ? e.title : null, lines = cur, track = track }); first = false; } cur = new List<string>(); }
            void FlushPara()
            {
                if (para.Count == 0) return;
                if (cur.Count > 0 && cur.Count + 1 + para.Count > LinesPerPage) FlushPage();
                if (cur.Count > 0) cur.Add("");
                foreach (var l in para) { if (cur.Count >= LinesPerPage) FlushPage(); cur.Add(l); }
                para.Clear();
            }
            foreach (var raw in e.body.Split('\n'))
            {
                string s = raw.Trim();
                if (s.Length > 0 && s.Trim('—', '-', ' ', '　').Length == 0) { FlushPara(); FlushPage(); continue; }      // 「———」= 頁の区切り
                if (s.Length == 0) { FlushPara(); continue; }
                para.AddRange(Game2DJpFont.Lines(s, TextW));
            }
            FlushPara(); FlushPage();
        }

        private void ApplyFade()
        {
            if (_quad == null) return;
            float f = Mathf.Clamp01(_fadeT / FadeSec); float fade = _closing ? 1f - f : f;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb); _mpb.SetFloat("_Fade", fade); _quad.SetPropertyBlock(_mpb);
        }

        private void Draw()
        {
            if (_pix == null || _pages.Count == 0) return;
            var p = _pix; p.Rect(0, 0, W, H, Bg);
            var pg = _pages[_page];
            if (!string.IsNullOrEmpty(pg.title))
            {
                int tw = Game2DJpFont.Width(pg.title) * TitleScale;
                Text(p, pg.title, (W - tw) / 2, TitleY, Amber, TitleScale);
            }
            int shown = Mathf.Min(pg.lines.Count, Mathf.FloorToInt(_t / LineReveal) + 1);
            for (int i = 0; i < shown; i++) Text(p, pg.lines[i], Left, BodyY + i * Game2DJpFont.LineH, Ink, 1);
            bool done = shown >= pg.lines.Count;
            string foot = (_page + 1) + " / " + _pages.Count;
            Game2DJpFont.Draw(p, foot, Left + TextW - Game2DJpFont.Width(foot), FootY, Dim);
            if (done && (int)(Time.unscaledTime * 2.5f) % 2 == 0) Game2DJpFont.Draw(p, "クリックで進む", Left, FootY, Dim);      // 浮かびきったら明滅
            _tex.SetPixels32(p.px); _tex.Apply(false);
        }

        /// <summary>字を打つ: 先に周り 1 dot (8 方向) を暗い縁取り、 その上に字。 scale = 1 dot を scale×scale で打つ (整数倍・補間しない)。</summary>
        private static void Text(KitPix p, string s, int x, int y, Color32 c, int scale)
        {
            var dots = new List<Vector2Int>();
            Game2DJpFont.Draw((px, py) => dots.Add(new Vector2Int(px, py)), s, x, y);
            foreach (var d in dots)
            {
                int bx = x + (d.x - x) * scale, by = y + (d.y - y) * scale;
                p.Rect(bx - 1, by - 1, scale + 2, scale + 2, Rim);
            }
            foreach (var d in dots) p.Rect(x + (d.x - x) * scale, y + (d.y - y) * scale, scale, scale, c);
        }

        private void Stop()
        {
            _active = false; _endFrame = Time.frameCount; _closing = false; Track = null; ClosedSerial++;
            GameManager.InputBlockedByScreen = false;
            if (_quad != null) _quad.enabled = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[実機の確認用・2026-11-15] エンディングの画面を直接出す (フェーズもランも変えない・セーブを止める)。
        /// id = end1 / end2 / end3 / end_true (後日談つき) / end_good / epilogue (後日談だけ)。 例: <c>UI.Game2D.Game2DEndingScreen.QaShow("end_true")</c>。
        /// 読み終わって閉じると元の画面に戻る (踏破の窓は出ない)。 無い id なら false。</summary>
        public static bool QaShow(string id)
        {
            SaveGuard.Suppressed = true;
            var s = FindObjectOfType<Game2DEndingScreen>(); if (s == null) return false;
            Endings.Ending e;
            switch (id)
            {
                case "end1": e = Endings.End1; break;
                case "end2": e = Endings.End2; break;
                case "end3": e = Endings.End3; break;
                case Endings.EndTrueId: e = Endings.EndTrue; break;
                case Endings.EndGoodId: e = Endings.EndGood; break;
                case "epilogue": e = Endings.Epilogue; break;
                default: return false;
            }
            if (_active) s.Stop();
            s.Play(e);
            return _active;
        }
#endif
    }
}
