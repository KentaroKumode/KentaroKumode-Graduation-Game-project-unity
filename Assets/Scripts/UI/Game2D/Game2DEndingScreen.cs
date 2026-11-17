using System.Collections.Generic;
using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// エンディングの画面 (#328・2026-11-15・<b>仮置き</b>)。 ランを踏破した時 (フェーズ RunClear) に、 <see cref="Endings.Resolve"/> の結果の本文を、
    /// <b>液晶の中に</b>ドット字 (<see cref="Game2DJpFont"/>・字の指定は GameFont.cs) で頁送りで見せ、 最後に結びの絵の上でエンディング名を見せる。
    /// 読み終わると、 今までの「踏破」の窓 (到達の数字・入手した遺物 → タイトルへ) に続く。
    ///
    /// <para><b>額縁の決まり</b>: エンディングの文は物語なので液晶の中に描く (筐体の窓 = Game2DModal には出さない)。 出している間は計器盤 (GUI) を隠す
    /// (<see cref="Game2DHud"/> がロード画面と同じく <see cref="Blocking"/> を見る)。</para>
    ///
    /// <para><b>送り方</b>: 頁ごとに行が上から順に浮かぶ (1 行 <see cref="LineReveal"/> 秒)。 クリック・Space・Enter で、 浮かびきる前なら全部を出し、 浮かびきっていれば次の頁へ。
    /// 本文の「———」の行は頁の区切りに使う (描かない)。 頁に入りきらない段落は次の頁へ送る。 本文の頁に題 (エンディング名) は出さない (2026-11-16・作者の指示)
    /// ── どの頁も同じ組み方で、 1 頁 <see cref="LinesPerPage"/> 行 (y 26 から頁数の行の手前まで・15 行)。 右下の頁数と左下の「クリックで進む」にも本文と同じ暗い縁取り。</para>
    ///
    /// <para><b>曲</b>: 出している間は <see cref="Game2DBgm"/> が <see cref="Track"/> を読む (EndTrue = bgm_end_true / EndGood = bgm_end_good / ほか = bgm_end_other・曲はまだ無いので無音)。
    /// 描き方はロード画面 (<see cref="Game2DLoadingScreen"/>) と同じ: 地図の板の複製を少し手前に置き、 Game2D/LoadingBlend の網目で出入りする。 BOT がランを動かしている間は出さない。</para>
    ///
    /// <para><b>結びの絵</b> (2026-11-16・作者の決定): 結末ごとに字の無い絵が 1 枚ある。 絵は作者が描いた物・作者が決めた物を加工せずに
    /// Resources/<see cref="ArtDir"/>*.png.bytes に置き (戦闘の背景・Game2DIcons と同じく、 取り込みの設定に左右されないようテキストの資産として読む)、
    /// <see cref="ArtNameFor"/> で引く。 見せ方はタイトル画面の背景 (TitleDayNight の BG・幅 256 の絵を PPU 8 で置き、 液晶の高さ 16.875 単位 = 270 dot に写す
    /// = 絵の 1 画素が 2×2 dot) に揃え、 <b>整数倍で液晶を覆う最小の倍率</b> (幅 256 なら 2 倍・帰路の cross のように 480×270 なら 1 倍 = 1 画素 1 dot) で最近傍に拡大して中央に置き、 はみ出した端は切る (余りが出れば黒)。
    /// 写した絵は 1 度だけ作り (<see cref="LoadArt"/> → _artPx)、 本文の後ろも最後も同じ配列から色を作るので、 明るくなった時に 1 画素もずれない。</para>
    ///
    /// <para><b>本文の後ろの絵</b> (2026-11-16・作者の依頼): 本文を読んでいる間 (最初の頁への入りも)、 その結末の結びの絵を
    /// 「元の色 × <see cref="BackdropDim"/>」に暗くして字の後ろに敷く (黒へ寄せるだけ・全面のディザや市松は使わない)。 字と同じ 1 枚の画像 (_tex) に、
    /// 毎コマ地として先に写してから字を打つ。 暗い絵は係数ごとに 1 度だけ作り (_backPx)、 頁をめくっても同じ配列を写すので背景は変わらない。
    /// 係数 0・絵の無い結末・読めなかった時は今までどおりの暗い地 (Bg)。</para>
    ///
    /// <para><b>最後</b>: 最後の頁を読み終えたら、 字だけが消えて、 同じ絵が係数 <see cref="BackdropDim"/> から 1.0 まで <see cref="ArtRise"/> 秒で明るくなる
    /// (板の網目は使わない・係数 0 なら暗い地から絵が明るくなる)。 明るくなりきってから <see cref="NameDelay"/> 秒置いて、 絵の上の<b>画面の中央の上寄り</b>にエンディング名が浮かぶ
    /// (2026-11-16・作者の決定): 上に通し番号 (<see cref="NumberFor"/>・「ENDING:5」など・名前の半分の倍率)、 下に名前 (<see cref="Endings"/> の題・<see cref="NameScale"/> 倍)。
    /// 色は名前も番号も同じ白 (#F0F4F5・番号を暗くしない)。 2 段を合わせた塊の中心を y = <see cref="NameCenterY"/> (液晶の高さ 270 の上から 1/4 あたり・左右は中央) に置く
    /// (帰路の絵は黒い地の中央に十字の光があるだけで、 名前が中央にあると絵の大部分が隠れるため)。 本文の行と同じく 1 段 <see cref="LineReveal"/> 秒で上から浮かぶ。
    /// 縁取りは既定で無し (<see cref="NameRim"/> = 0)・帯や板は敷かない。
    /// 名前が浮かびきってから <see cref="ArtHold"/> 秒見せ、 網目で閉じる。 クリック・Space・Enter は、 明るくなる途中 → 明るくなりきった所へ / 名前が出きる前 → 名前を出す / 名前が出た後 → 閉じる。
    /// 絵の無い結末 (絵が読めなかった時も) は、 最後の頁の後に暗い地 (Bg) のまま名前だけを出してから閉じる。</para>
    /// </summary>
    public class Game2DEndingScreen : MonoBehaviour
    {
        public const int W = Game2DLayout.Width, H = Game2DLayout.Height;
        /// <summary>1 行が浮かぶ間 (秒)。</summary>
        public const float LineReveal = 0.12f;
        private const float FadeSec = 0.35f;
        /// <summary>結びの絵を見せる長さ (秒・明るくなりきってから・仮置き)。 この間のクリックで閉じる。</summary>
        public const float ArtHold = 4f;
        /// <summary>最後の頁を読み終えてから、 結びの絵が <see cref="BackdropDim"/> から 1.0 まで明るくなる長さ (秒・仮置き)。</summary>
        public const float ArtRise = 1.5f;
        /// <summary><see cref="BackdropDim"/> の初期値。</summary>
        public const float DefaultBackdropDim = 0.25f;
        /// <summary>本文の後ろに敷く結びの絵の明るさの係数 (元の色 × これ・0..1)。 0 なら今までどおりの暗い地。 調整はこの 1 か所。</summary>
        public static float BackdropDim = DefaultBackdropDim;
        /// <summary>結びの絵の置き場 (Resources の下・*.png.bytes)。</summary>
        public const string ArtDir = "Game2DEndingArt/";
        /// <summary>結びの絵が明るくなりきってから、 エンディング名が浮かび始めるまでの間 (秒・仮置き)。 絵の無い結末は最後の頁を送ってからの間。</summary>
        public const float NameDelay = 0.6f;
        /// <summary><see cref="NameScale"/> の初期値。</summary>
        public const int DefaultNameScale = 3;
        /// <summary>エンディング名の倍率 (元のドット字の 1 dot を NameScale×NameScale で打つ・最近傍の整数倍・ぼかさない)。 通し番号はこの半分 (最小 1)。
        /// 安全域の幅 (x <see cref="SafeL"/>〜<see cref="SafeR"/>) に縁取りごと収まらない倍率は、 描く時に収まる最大へ丸める。</summary>
        public static int NameScale = DefaultNameScale;
        /// <summary><see cref="NameRim"/> の初期値。</summary>
        public const int DefaultNameRim = 0;
        /// <summary>エンディング名の縁取りの太さ (dot・0 = 縁取りなし (既定・2026-11-16 作者の決定) / 1 / 2)。 番号 (「ENDING:n」) の縁取りは、 これが 0 なら 0・それ以外は本文と同じ 1 dot。
        /// 縁取りの色は本文と同じ暗い色。
        /// 安全域に収める計算 (<see cref="FitNameScale"/>) もこれに従う。</summary>
        public static int NameRim = DefaultNameRim;
        /// <summary><see cref="NameCenterY"/> の初期値 (液晶の高さ 270 の上から 1/4 あたり)。</summary>
        public const int DefaultNameCenterY = 68;
        /// <summary>エンディング名 (番号と名前を合わせた塊) の上下の中心 (dot・上から)。 左右はいつも中央。 塊が安全域 (y <see cref="SafeT"/>〜<see cref="SafeB"/>) からはみ出す時は、 描く時に収まる所へずらす。
        /// 調整はこの 1 か所 (2026-11-16・作者の決定: 中央でなく上寄り)。</summary>
        public static int NameCenterY = DefaultNameCenterY;
        // 配置 (dot): 液晶の湾曲で端が切れるので、 本文は左右 40・上 26・下 26 の内側 (旧い合わせ方 f = 0 の頃の理由。 今の既定 f = 1 では端は切れないが、 作者が承認した配置なので変えない)
        //   本文の頁に題は出さない (2026-11-16・作者の指示: 「上のエンディング名は、 本文を読んでいる時は要らない」)。 題の場所 (y 26〜63) まで本文を上げ、
        //   1 頁の行数は「本文の上端から頁数の行 (FootY) の手前まで」で決める (前と同じ規則: 64 から 12 行 → 26 から 15 行・どの頁も同じ行数)
        private const int Left = 40, TextW = 400, BodyY = 26, FootY = 232;
        private static int LinesPerPage => Mathf.Max(1, (FootY - BodyY - 1) / Game2DJpFont.LineH);
        // 液晶の安全域 (エンディング名を収める枠・dot)
        public const int SafeL = LcdSafeArea.L, SafeR = LcdSafeArea.R, SafeT = LcdSafeArea.T, SafeB = LcdSafeArea.B;      // 値は LcdSafeArea (イベントの画面と共通)
        // エンディング名の段: 番号と名前の間 (dot)。 縁取りの太さは NameRim
        private const int NameGap = 8;

        // 読みやすさ (2026-11-15 撮影: 液晶のにじみで本文が読みにくかった): 液晶の効果全体は弱めず、 字の側で抑える ──
        //   ① 字の色を明るさ 8 割ほどに落とす (白に近い字ほどにじみが広がる)  ② 字の周り 1 dot (8 方向) に地より暗い縁取りを打ち、 にじみが字の間を埋めないようにする
        private static readonly Color32 Bg = new Color32(0x07, 0x10, 0x15, 255), Ink = new Color32(0xb4, 0xc8, 0xce, 255),
            Dim = new Color32(0x4d, 0x6a, 0x76, 255), Rim = new Color32(0x02, 0x05, 0x07, 255);
        // エンディング名の白 (名前も番号もこの色・2026-11-16 作者の決定で白に確定): 本文の字 (Ink) より一段明るい白。 純白は液晶のにじみで飛ぶのでごく僅かに落とし、 本文の字と同じく僅かに青みへ寄せる
        private static readonly Color32 NameWhite = new Color32(0xf0, 0xf4, 0xf5, 255);

        /// <summary>読み終わって閉じた回数 (場面の窓 Game2DPhaseScreens が見て、 閉じた後に「踏破」の窓を必ず作り直す)。</summary>
        public static int ClosedSerial { get; private set; }

        /// <summary>出している間 (と、 閉じたフレーム) は true。 GUI・場面の窓・キー入力がこれを見る。</summary>
        public static bool Blocking => _active || _endFrame == Time.frameCount;
        private static bool _active; private static int _endFrame = -1;
        /// <summary>出している間に鳴らす曲の名前 (<see cref="Game2DBgm"/> の名前・出していなければ null)。</summary>
        public static string Track { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _active = false; _endFrame = -1; Track = null; BackdropDim = DefaultBackdropDim; NameScale = DefaultNameScale; NameRim = DefaultNameRim; NameCenterY = DefaultNameCenterY; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DEndingScreen>() != null) return;
            var mv = FindObjectOfType<Game2DMapView>(); if (mv == null) return;
            mv.gameObject.AddComponent<Game2DEndingScreen>();
        }

        /// <summary>エンディングの id → 結びの絵の名前 (<see cref="ArtDir"/> の下のファイル名・拡張子 .png.bytes を除く)。 絵の無いエンディングは null (最後の頁の後、 暗い地のまま名前だけを出して閉じる)。
        /// 英雄の帰還 (end1) はタイトル画面の昼の絵 (day)。</summary>
        public static string ArtNameFor(string endingId)
        {
            if (endingId == Endings.End1.id) return "day";            // 英雄の帰還: タイトル画面の昼の大穴 (作者の決定・2026-11-16)
            if (endingId == Endings.End2.id) return "night";          // 無明の剣: 夜の大穴を見下ろしつつ、 ひっそりと別れを告げる後ろ姿
            if (endingId == Endings.End3.id) return "ash";            // 灰の玉座: 灰と炎
            if (endingId == Endings.EndGoodId) return "cross";        // 帰路: 灰燼の王の大技の十字の光を写し、 段で柔らかくして青白いにじみを足した止め絵 (480×270・1 倍で置く・2026-11-16 作者の決定)。 元の絵 dark は残してある
            if (endingId == Endings.EndTrueId) return "true_end";     // 終始 (+ 棚の隅): 物語を物語として閉じる挿絵
            return null;
        }

        /// <summary>エンディングの通し番号 (結びの絵の上で、 エンディング名の上に小さく出す)。 番号の表は <see cref="Endings.NumberOf"/> の 1 か所 (実績の END1〜5 も同じ表を読む)。 名前は <see cref="Endings"/> の題をそのまま使う (ここには持たない)。</summary>
        public static string NumberFor(string endingId)
        {
            int n = Endings.NumberOf(endingId);
            return n > 0 ? "ENDING:" + n : null;
        }

        private struct Page { public List<string> lines; public string track; }
        private readonly List<Page> _pages = new List<Page>();
        private int _page; private float _t, _fadeT; private bool _closing;
        private RunState _doneRun;
        // 出した時のフェーズとラン (#328): どちらかが変わったら、 閉じる動きの途中でもすぐ片付ける
        private GameManager.GamePhase _playPhase; private RunState _playRun;
        private GameManager _subscribed;
        private MeshRenderer _quad; private Texture2D _tex; private KitPix _pix; private MaterialPropertyBlock _mpb;
        // 結びの絵: _artPx = 液晶の大きさに写した絵 (Play で読む・無ければ null)・_artName = その名前・_inArt = 字を消して絵を見せている所
        //   _artT = 絵の段に入ってからの秒・_qaRise = 確認用に明るさの進みを止めた値 (負 = 止めない)
        //   _backPx = 本文の後ろに敷く暗い絵 (_backK の係数で作った物・係数が変わった時だけ作り直す)
        private Color32[] _artPx, _backPx; private string _artName; private float _backK = -1f;
        private bool _inArt; private float _artT, _qaRise = -1f;
        private bool ArtReady => _artPx != null;
        // エンディング名: _name = 題 (Endings の title)・_nameNo = 通し番号 (NumberFor・無ければ番号の段なし)
        private string _name, _nameNo;

        private void OnDisable() { Unsubscribe(); if (_active) Stop(); }      // 載っている物ごと止まった時も重ね絵と入力の遮断を残さない

        private void OnDestroy()
        {
            Unsubscribe();
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
            Subscribe();
        }

        private void Subscribe()
        {
            var gm = GameManager.Instance;
            if (gm == null || _subscribed == gm) return;
            Unsubscribe();
            gm.OnPhaseChanged += OnPhaseChanged; _subscribed = gm;
        }

        private void Unsubscribe() { if (_subscribed != null) _subscribed.OnPhaseChanged -= OnPhaseChanged; _subscribed = null; }

        /// <summary>フェーズが変わった<b>その瞬間</b>に見る (#328・2026-11-16)。 次のフレームの Update で拾うと、 場面の窓 (Game2DPhaseScreens) の Update が
        /// 先に回ったフレームでは <see cref="Blocking"/> がまだ false で、 「踏破」の窓が 1 コマ描かれていた (Update 同士の順は決まっていない)。
        /// 出している間にフェーズかランが変わったら (タイトルへ・新しいラン)、 閉じる動きの途中でもここで片付ける。</summary>
        private void OnPhaseChanged(GameManager.GamePhase _) => CheckPhase();

        private void CheckPhase()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (GameHooks.BotDriving || Application.isBatchMode) { if (_active) Stop(); return; }
            if (_active && (gm.CurrentPhase != _playPhase || gm.Run != _playRun)) Stop();
            if (gm.CurrentPhase == GameManager.GamePhase.Title) _doneRun = null;
            // 踏破の瞬間を拾う (ラン 1 つに 1 回)
            if (!_active && gm.CurrentPhase == GameManager.GamePhase.RunClear && gm.Run != null && gm.Run != _doneRun)
            {
                _doneRun = gm.Run;
                Play(Endings.Resolve(gm.Run));
            }
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
            if (GameHooks.BotDriving || Application.isBatchMode) { if (_active) Stop(); return; }
            Subscribe();
            CheckPhase();                                                     // 取りこぼし用 (購読より前に変わっていた時)
            if (!_active) return;

            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _t += dt; _fadeT += dt;
            bool press = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
            if (_closing) { if (_fadeT >= FadeSec) { Stop(); return; } }
            else if (_inArt)
            {
                // 結びの絵: ArtRise 秒で明るくなり、 NameDelay 秒置いてエンディング名が浮かび、 名前が出てから ArtHold 秒、 またはクリックで閉じる (閉じる時は網目で後ろの画面へ)
                //   絵の無い結末は明るくなる間が 0 (暗い地のまま名前だけ)
                if (_qaRise < 0f) _artT += dt;
                if (press)
                {
                    GameManager.ScreenUsedSpaceFrame = Time.frameCount;      // 押した入力を後ろへ流さない
                    if (ArtRise01() < 1f) _artT = RiseSec;                  // 明るくなる途中: 明るくなりきった所へ
                    else if (_artT < NameFullAt) _artT = NameFullAt;        // 名前が出きる前: 名前を出す
                    else { _closing = true; _fadeT = 0f; }
                }
                else if (_qaRise < 0f && _artT >= NameFullAt + ArtHold) { _closing = true; _fadeT = 0f; }
            }
            else if (press && _fadeT >= FadeSec)
            {
                GameManager.ScreenUsedSpaceFrame = Time.frameCount;      // 押した入力を後ろへ流さない
                var pg = _pages[_page];
                if (_t < pg.lines.Count * LineReveal) _t = 999f;          // 浮かびきる前: 全部を出す
                else if (_page + 1 < _pages.Count) { _page++; _t = 0f; Track = _pages[_page].track; Game2DSfx.Play("ui_tab", 0.6f); }
                else { _inArt = true; _artT = 0f; Game2DSfx.Play("ui_confirm"); }      // 字を消して後ろの絵を明るくし (同じ板・同じ絵・網目なし)、 名前を出す (絵が無ければ暗い地に名前だけ)
            }
            Draw();
            ApplyFade();
        }

        /// <summary>エンディング e の頁を作って出す。</summary>
        private void Play(Endings.Ending e)
        {
            if (string.IsNullOrEmpty(e.body) || !EnsureQuad()) return;
            _pages.Clear();
            string track = e.id == Endings.EndTrueId ? Game2DBgm.EndTrue : e.id == Endings.EndGoodId ? Game2DBgm.EndGood : Game2DBgm.EndOther;
            AddPages(e, track);
            if (_pages.Count == 0) return;
            _name = e.title; _nameNo = NumberFor(e.id);
            _page = 0; _t = 0f; _fadeT = 0f; _closing = false; _inArt = false; _artT = 0f; _qaRise = -1f; _active = true; Track = _pages[0].track;
            var gm = GameManager.Instance; _playPhase = gm != null ? gm.CurrentPhase : GameManager.GamePhase.Title; _playRun = gm != null ? gm.Run : null;
            GameManager.InputBlockedByScreen = true;
            if (_tex == null) { _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DEnding" }; _pix = new KitPix(W, H); }
            LoadArt(ArtNameFor(e.id));                                        // 最初の Draw より前に読む (入りの網目から絵を敷く)
            SetBlend();
            _quad.enabled = true;
            Draw(); ApplyFade();
            Game2DSfx.Play("gui_out");
        }

        /// <summary>本文を頁に割る: 「———」の行で区切り、 段落 (空行まで) が頁に入りきらなければ次の頁へ送る。 どの頁も同じ組み方 (題は出さない・1 頁 <see cref="LinesPerPage"/> 行)。</summary>
        private void AddPages(Endings.Ending e, string track)
        {
            var cur = new List<string>(); int per = LinesPerPage;
            var para = new List<string>();
            void FlushPage() { if (cur.Count > 0) { while (cur.Count > 0 && cur[cur.Count - 1].Length == 0) cur.RemoveAt(cur.Count - 1); if (cur.Count > 0) _pages.Add(new Page { lines = cur, track = track }); } cur = new List<string>(); }
            void FlushPara()
            {
                if (para.Count == 0) return;
                if (cur.Count > 0 && cur.Count + 1 + para.Count > per) FlushPage();
                if (cur.Count > 0) cur.Add("");
                foreach (var l in para) { if (cur.Count >= per) FlushPage(); cur.Add(l); }
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

        /// <summary>網目の板に見せる物を渡す。 板に出すのはいつも _tex 1 枚 (本文も結びの絵も同じ画像に描く)・見せないドットは捨てて後ろを見せる (入りと閉じ)。</summary>
        private void SetBlend()
        {
            if (_quad == null) return;
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_MainTex", _tex); _mpb.SetVector("_Dots", new Vector4(W, H, 0, 0)); _mpb.SetFloat("_UsePrev", 0f);
            _quad.SetPropertyBlock(_mpb);
        }

        /// <summary>結びの絵を読み、 液晶の大きさ (W×H) に写して _artPx に置く (KitPix と同じ下の行からの並び)。 絵の画素は変えない: 整数倍で液晶を覆う最小の倍率で最近傍に拡大し、
        /// 中央に置いて端を切る (タイトル画面の背景と同じ見せ方・幅 256 なら 2 倍・480×270 なら 1 倍)。 余りが出れば黒。 絵が黒い地でも「読めた」扱い (_artPx の有無だけで決める・画素の明るさは見ない)。
        /// name が null・読めない時は false (_artPx = null・本文の地は Bg・最後の頁から網目で閉じる)。</summary>
        private bool LoadArt(string name)
        {
            _artPx = null; _backPx = null; _backK = -1f; _artName = name;
            if (string.IsNullOrEmpty(name)) return false;
            var a = Resources.Load<TextAsset>(ArtDir + name + ".png");
            if (a == null) { Debug.LogWarning("[Game2DEndingScreen] Resources/" + ArtDir + name + ".png.bytes が無い"); return false; }
            var t = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            bool ok = t.LoadImage(a.bytes) && t.width > 0 && t.height > 0;
            Resources.UnloadAsset(a);
            if (!ok) { Debug.LogWarning("[Game2DEndingScreen] " + name + " を読めない"); Destroy(t); return false; }
            int sw = t.width, sh = t.height; var src = t.GetPixels32();                      // 下の行から
            Destroy(t);
            int s = Mathf.Max(1, Mathf.Max(Mathf.CeilToInt((float)W / sw), Mathf.CeilToInt((float)H / sh)));
            int ox = (W - sw * s) / 2, oy = (H - sh * s) / 2;                                  // 左上からの位置 (負 = はみ出して切る)
            var dst = new Color32[W * H]; var black = new Color32(0, 0, 0, 255);
            for (int y = 0; y < H; y++)
            {
                int ry = y - oy;                                                               // 絵の上からの行 (拡大後)
                for (int x = 0; x < W; x++)
                {
                    int rx = x - ox;
                    Color32 c = black;
                    if (rx >= 0 && ry >= 0 && rx < sw * s && ry < sh * s) { c = src[(sh - 1 - ry / s) * sw + rx / s]; c.a = 255; }
                    dst[(H - 1 - y) * W + x] = c;                                              // Texture2D は下の行から
                }
            }
            _artPx = dst;
            return true;
        }

        /// <summary>結びの絵の明るくなる進み (0..1)。 確認用に止めていればその値。 絵の無い結末はいつも 1。</summary>
        private float ArtRise01()
        {
            if (!ArtReady) return 1f;
            float r = _qaRise >= 0f ? _qaRise : ArtRise <= 0f ? 1f : _artT / ArtRise;
            return Mathf.Clamp01(r);
        }

        /// <summary>結びの絵の段で、 明るくなる長さ (秒)。 絵の無い結末は 0。</summary>
        private float RiseSec => ArtReady ? Mathf.Max(0f, ArtRise) : 0f;
        /// <summary>エンディング名の段の数 (番号 + 名前・番号が無ければ名前だけ)。</summary>
        private int NameRows => string.IsNullOrEmpty(_nameNo) ? 1 : 2;
        /// <summary>結びの絵の段で、 名前が浮かび始める時刻 (_artT・秒)。</summary>
        private float NameAt => RiseSec + NameDelay;
        /// <summary>結びの絵の段で、 名前が浮かびきる時刻 (本文の行と同じく 1 段 <see cref="LineReveal"/> 秒)。 ArtHold はここから数える。</summary>
        private float NameFullAt => NameAt + NameRows * LineReveal;
        /// <summary>今出ている名前の段の数 (0 = まだ・本文の行と同じ数え方: 浮かび始めに 1 段目)。</summary>
        private int NameShown => _artT < NameAt ? 0 : Mathf.Min(NameRows, Mathf.FloorToInt((_artT - NameAt) / LineReveal) + 1);

        /// <summary>本文の地を dst に写す: 結びの絵があり係数が 0 より大きければ「絵 × 係数」(係数が変わった時だけ作り直す)、 なければ Bg。
        /// 頁をめくっても同じ _backPx を写すだけなので、 背景は頁をまたいで変わらない。</summary>
        private void FillBackdrop(Color32[] dst)
        {
            float k = Mathf.Clamp01(BackdropDim);
            if (_artPx == null || k <= 0f) { for (int i = 0; i < dst.Length; i++) dst[i] = Bg; return; }
            if (_backPx == null || _backK != k)
            {
                if (_backPx == null) _backPx = new Color32[_artPx.Length];
                for (int i = 0; i < _artPx.Length; i++) _backPx[i] = Scale(_artPx[i], k);
                _backK = k;
            }
            System.Array.Copy(_backPx, dst, dst.Length);
        }

        /// <summary>結びの絵の段の 1 コマ: 本文の地 (絵 × 係数・係数 0 なら Bg) から元の絵まで、 進み r で各画素の色を寄せる (色を掛けるだけ・ディザなし)。
        /// 進みは両端を緩めた曲線 (smoothstep)。 係数 k &gt; 0 では「絵 × (k + (1−k)·e)」なので、 r = 0 は本文の地と同じ色 (切り替わりで色が跳ねない)。</summary>
        private void FillArt(Color32[] dst, float r)
        {
            float k = Mathf.Clamp01(BackdropDim);
            float e = r * r * (3f - 2f * r);
            if (e >= 1f) { System.Array.Copy(_artPx, dst, dst.Length); return; }
            if (k > 0f) { float m = k + (1f - k) * e; for (int i = 0; i < dst.Length; i++) dst[i] = Scale(_artPx[i], m); return; }
            for (int i = 0; i < dst.Length; i++)
            {
                var a = _artPx[i];
                dst[i] = new Color32((byte)(Bg.r + (a.r - Bg.r) * e + 0.5f), (byte)(Bg.g + (a.g - Bg.g) * e + 0.5f), (byte)(Bg.b + (a.b - Bg.b) * e + 0.5f), 255);
            }
        }

        /// <summary>色を m 倍 (0..1・黒へ寄せる)。 a は 255。</summary>
        private static Color32 Scale(Color32 c, float m) => new Color32((byte)(c.r * m + 0.5f), (byte)(c.g * m + 0.5f), (byte)(c.b * m + 0.5f), 255);

        private void ApplyFade()
        {
            if (_quad == null) return;
            float f = Mathf.Clamp01(_fadeT / FadeSec); float fade = _closing ? 1f - f : f;      // 入りと閉じだけ (結びの絵の段へは網目を使わず、 _tex の中で明るくする)
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb); _mpb.SetFloat("_Fade", fade); _quad.SetPropertyBlock(_mpb);
        }

        private void Draw()
        {
            if (_pix == null || _pages.Count == 0) return;
            var p = _pix;
            if (_inArt)
            {
                // 結びの絵の段: 本文は描かず、 絵 (無ければ暗い地 Bg) の上にエンディング名だけ
                if (_artPx != null) FillArt(p.px, ArtRise01()); else for (int i = 0; i < p.px.Length; i++) p.px[i] = Bg;
                DrawName(p, NameShown);
                _tex.SetPixels32(p.px); _tex.Apply(false); return;
            }
            FillBackdrop(p.px);                                                                // 本文の地 (頁をまたいで同じ配列)
            var pg = _pages[_page];
            int shown = Mathf.Min(pg.lines.Count, Mathf.FloorToInt(_t / LineReveal) + 1);
            for (int i = 0; i < shown; i++) Text(p, pg.lines[i], Left, BodyY + i * Game2DJpFont.LineH, Ink, 1);
            bool done = shown >= pg.lines.Count;
            string foot = (_page + 1) + " / " + _pages.Count;
            Text(p, foot, Left + TextW - Game2DJpFont.Width(foot), FootY, Dim, 1);              // 頁数と「クリックで進む」も本文と同じ縁取り (絵の明るい所に重なっても読めるよう)
            if (done && (int)(Time.unscaledTime * 2.5f) % 2 == 0) Text(p, "クリックで進む", Left, FootY, Dim, 1);      // 浮かびきったら明滅
            _tex.SetPixels32(p.px); _tex.Apply(false);
        }

        /// <summary>名前の実際の倍率: <see cref="NameScale"/> (1 以上) を、 名前が縁取りごと安全域の幅 (x <see cref="SafeL"/>〜<see cref="SafeR"/>) に収まる最大へ丸めた値。</summary>
        public static int FitNameScale(string name)
        {
            int k = Mathf.Max(1, NameScale), w = Game2DJpFont.Width(name);
            int rim = Mathf.Clamp(NameRim, 0, 2);
            while (k > 1 && w * k + rim * 2 > SafeR - SafeL) k--;
            return k;
        }

        /// <summary>番号の倍率 (名前の半分・最小 1)。</summary>
        private static int NumberScale(int nameScale) => Mathf.Max(1, nameScale / 2);

        /// <summary>エンディング名を描く: 上に通し番号 (名前の半分の倍率)、 下に名前 (<see cref="NameScale"/>)。 色はどちらも白 (<see cref="NameWhite"/>)。
        /// 2 段を合わせた塊の中心を y = <see cref="NameCenterY"/> に置き (左右は中央)、 安全域に収める。 rows = 出す段の数 (本文の行と同じく上から順に浮かぶ・番号が無ければ名前だけの 1 段)。
        /// 縁取り: 名前は <see cref="NameRim"/> dot・番号は 1 dot (NameRim = 0 なら 0)・色は本文と同じ暗い色。 帯や板は敷かない。</summary>
        private void DrawName(KitPix p, int rows)
        {
            if (rows <= 0 || string.IsNullOrEmpty(_name)) return;
            int ks = FitNameScale(_name), kn = NumberScale(ks), cell = Game2DJpFont.Cell;
            bool hasNo = !string.IsNullOrEmpty(_nameNo);
            int hNo = hasNo ? cell * kn + NameGap : 0, hAll = hNo + cell * ks;
            int top = Mathf.Clamp(NameCenterY - hAll / 2, SafeT, Mathf.Max(SafeT, SafeB - hAll));
            int rim = Mathf.Clamp(NameRim, 0, 2), rimNo = rim == 0 ? 0 : 1;
            if (hasNo) Text(p, _nameNo, CenterX(_nameNo, kn), top, NameWhite, kn, rimNo);
            if (rows > (hasNo ? 1 : 0)) Text(p, _name, CenterX(_name, ks), top + hNo, NameWhite, ks, rim);
        }

        private static int CenterX(string s, int k) => Mathf.Max(SafeL, (W - Game2DJpFont.Width(s) * k) / 2);

        /// <summary>字を打つ: 先に周り rim dot (8 方向) を暗い縁取り (rimColor・省略なら <see cref="Rim"/>・rim = 0 なら打たない)、 その上に字。 scale = 1 dot を scale×scale で打つ (整数倍・補間しない)。</summary>
        private static void Text(KitPix p, string s, int x, int y, Color32 c, int scale, int rim = 1) => Text(p, s, x, y, c, scale, rim, Rim);

        private static void Text(KitPix p, string s, int x, int y, Color32 c, int scale, int rim, Color32 rimColor)
        {
            var dots = new List<Vector2Int>();
            Game2DJpFont.Draw((px, py) => dots.Add(new Vector2Int(px, py)), s, x, y);
            if (rim > 0)
                foreach (var d in dots)
                {
                    int bx = x + (d.x - x) * scale, by = y + (d.y - y) * scale;
                    p.Rect(bx - rim, by - rim, scale + rim * 2, scale + rim * 2, rimColor);
                }
            foreach (var d in dots) p.Rect(x + (d.x - x) * scale, y + (d.y - y) * scale, scale, scale, c);
        }

        private void Stop()
        {
            _active = false; _endFrame = Time.frameCount; _closing = false; _inArt = false; _qaRise = -1f; Track = null; ClosedSerial++;
            GameManager.InputBlockedByScreen = false;
            if (_quad != null) _quad.enabled = false;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>[実機の確認用・2026-11-15] エンディングの画面を直接出す (フェーズもランも変えない・セーブを止める)。
        /// id = end1 / end2 / end3 / end_true / end_good。 例: <c>UI.Game2D.Game2DEndingScreen.QaShow("end_true")</c>。
        /// 読み終わると結びの絵 (<see cref="ArtNameFor"/>) とエンディング名を見せて閉じ、 元の画面に戻る (踏破の窓は出ない)。 出している間にフェーズかランが変わるとすぐ片付く。 無い id なら false。</summary>
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
                default: return false;
            }
            if (_active) s.Stop();
            s.Play(e);
            return _active;
        }

        /// <summary>[確認用・2026-11-16] 本文の後ろの絵の係数 (<see cref="BackdropDim"/>・0..1 に丸める) を替え、 出ている頁 (または結びの絵の段) をすぐ描き直す。
        /// 例: <c>UI.Game2D.Game2DEndingScreen.QaSetBackdropDim(0.4f)</c>。</summary>
        public static void QaSetBackdropDim(float k)
        {
            BackdropDim = Mathf.Clamp01(k);
            var s = FindObjectOfType<Game2DEndingScreen>();
            if (s != null && _active) s.Draw();
        }

        /// <summary>[確認用・2026-11-16] 出ている結末の頁 i (0 始まり・範囲外は丸める) を、 行を全部出し網目の入りも済んだ状態で見せる (結びの絵の段にいれば本文へ戻る)。
        /// 先に <see cref="QaShow"/> で出しておく。 出ていなければ false。 例: <c>UI.Game2D.Game2DEndingScreen.QaGotoPage(2)</c>。</summary>
        public static bool QaGotoPage(int i)
        {
            var s = FindObjectOfType<Game2DEndingScreen>(); if (s == null || !_active || s._pages.Count == 0) return false;
            s._page = Mathf.Clamp(i, 0, s._pages.Count - 1); s._t = 999f; s._fadeT = FadeSec; s._closing = false; s._inArt = false; s._qaRise = -1f;
            Track = s._pages[s._page].track;
            s.Draw(); s.ApplyFade();
            return true;
        }

        /// <summary>[確認用・2026-11-16] 結びの絵の段へ入り、 明るさの進みを rise01 (0..1 に丸める・1 = 明るくなりきった状態) で止めて見せる (名前は出さない・自動では閉じない・クリックでは進む)。
        /// 先に <see cref="QaShow"/> で出しておく。 出ていない・絵の無い結末なら false。 例: <c>UI.Game2D.Game2DEndingScreen.QaGotoArt(0.5f)</c>。</summary>
        public static bool QaGotoArt(float rise01) => QaGotoArt(rise01, false);

        /// <summary>[確認用・2026-11-16] 結びの絵の段へ入り、 止めて見せる。 showName = true なら、 絵を明るくなりきった状態 (rise01 は無視) にしてエンディング名を出しきった所で止める
        /// (絵の無い結末でも、 暗い地に名前だけの状態を見せられる)。 false なら <see cref="QaGotoArt(float)"/> と同じ (名前なし・絵の無い結末は false)。
        /// 自動では閉じない (クリックでは進む)。 例: <c>UI.Game2D.Game2DEndingScreen.QaGotoArt(1f, true)</c>。</summary>
        public static bool QaGotoArt(float rise01, bool showName)
        {
            var s = FindObjectOfType<Game2DEndingScreen>(); if (s == null || !_active) return false;
            if (!showName && !s.ArtReady) return false;
            s._page = s._pages.Count - 1; s._t = 999f; s._fadeT = FadeSec; s._closing = false;
            s._inArt = true;
            if (showName) { s._qaRise = 1f; s._artT = s.NameFullAt; }
            else { s._qaRise = Mathf.Clamp01(rise01); s._artT = s._qaRise * ArtRise; }
            s.Draw(); s.ApplyFade();
            return true;
        }

        /// <summary>[確認用・2026-11-16] エンディング名の倍率 (<see cref="NameScale"/>・1 以上に丸める) を替え、 出ている所をすぐ描き直す。 戻り値 = 今の結末の名前に実際に使う倍率
        /// (安全域に収まらなければ収まる最大へ丸めた値・出ていなければ「英雄の帰還」で数えた値)。 例: <c>UI.Game2D.Game2DEndingScreen.QaSetNameScale(5)</c>。</summary>
        public static int QaSetNameScale(int k)
        {
            NameScale = Mathf.Max(1, k);
            var s = FindObjectOfType<Game2DEndingScreen>();
            if (s != null && _active) s.Draw();
            return FitNameScale(s != null && _active && !string.IsNullOrEmpty(s._name) ? s._name : Endings.End1.title);
        }

        /// <summary>[確認用・2026-11-16] エンディング名の縁取りの太さ (<see cref="NameRim"/>・0〜2 に丸める・0 = 縁取りなし) を替え、 出ている所をすぐ描き直す。
        /// 例: <c>UI.Game2D.Game2DEndingScreen.QaSetNameRim(1)</c>。</summary>
        public static void QaSetNameRim(int rim)
        {
            NameRim = Mathf.Clamp(rim, 0, 2);
            var s = FindObjectOfType<Game2DEndingScreen>();
            if (s != null && _active) s.Draw();
        }

        /// <summary>[確認用・2026-11-16] エンディング名 (番号と名前を合わせた塊) の上下の中心 (<see cref="NameCenterY"/>・dot・上から) を替え、 出ている所をすぐ描き直す。
        /// 安全域 (y <see cref="SafeT"/>〜<see cref="SafeB"/>) に丸める (塊がはみ出す時は描く時にさらに収まる所へずらす)。 戻り値 = 丸めた後の値。
        /// 例: <c>UI.Game2D.Game2DEndingScreen.QaSetNameCenterY(68)</c>。</summary>
        public static int QaSetNameCenterY(int y)
        {
            NameCenterY = Mathf.Clamp(y, SafeT, SafeB);
            var s = FindObjectOfType<Game2DEndingScreen>();
            if (s != null && _active) s.Draw();
            return NameCenterY;
        }

        /// <summary>[確認用・2026-11-16] 今の様子を 1 行で返す (頁/全頁数・絵の名前・係数・段・名前の段と倍率)。 例: <c>UI.Game2D.Game2DEndingScreen.QaStatus()</c>。</summary>
        public static string QaStatus()
        {
            var s = FindObjectOfType<Game2DEndingScreen>(); if (s == null) return "画面の部品が無い";
            if (!_active) return "出していない";
            string stage = s._closing ? "閉じる途中" : s._inArt ? (s.ArtRise01() < 1f ? "結びの絵 (明るくなる途中)" : "結びの絵 (見せている)") : "本文";
            string name = !s._inArt ? "未" : s.NameShown <= 0 ? "出る前" : s.NameShown < s.NameRows ? "浮かぶ途中" : "出ている";
            int ks = FitNameScale(s._name ?? "");
            return "頁 " + (s._page + 1) + "/" + s._pages.Count + " (1 頁 " + LinesPerPage + " 行) ・絵 " + (s._artName ?? "なし") + (s.ArtReady ? "" : " (読めていない)")
                + " ・係数 " + BackdropDim.ToString("0.###") + " ・段 " + stage
                + (s._inArt ? " ・明るさの進み " + s.ArtRise01().ToString("0.###") + (s._qaRise >= 0f ? " (固定)" : "") : "")
                + " ・名前 " + name + " 「" + (s._nameNo ?? "") + " / " + (s._name ?? "") + "」 位置 中心 y " + NameCenterY + " (左右は中央) ・倍率 " + ks + (ks != NameScale ? " (指定 " + NameScale + " を丸めた)" : "")
                + " ・番号の倍率 " + NumberScale(ks)
                + " ・縁取り " + Mathf.Clamp(NameRim, 0, 2) + " dot (番号 " + (Mathf.Clamp(NameRim, 0, 2) == 0 ? 0 : 1) + " dot)"
                + " ・色 白 #" + ColorUtility.ToHtmlStringRGB(NameWhite) + " (番号も同じ)";
        }
#endif
    }
}
