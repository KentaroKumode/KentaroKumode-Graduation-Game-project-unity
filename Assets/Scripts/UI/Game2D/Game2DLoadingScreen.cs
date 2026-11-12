using GameLoop;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// ロード画面「次の層を測量する」(<b>仮置き</b>・2026-11-01・見本は案 D)。 ランを始めた時 (職業選択の受理の後) と、
    /// 層を降りて次の航行に入る時に出す。 <b>クリックか Space で進む</b> (時間では閉じない)。
    ///
    /// <para><b>入り・出のトランジション</b> (ぱっと切り替えると止まって見えたため): 液晶のドット単位の網目 (4×4 Bayer) で置き換える。
    /// 入り = 直前の画面 (液晶の RT を GPU の中だけで写し取ったもの) → ロード画面。 出 = ロード画面が抜けて、 後ろの本物の地図が現れ、
    /// 計器盤は戦闘から戻る時と同じばねで滑り込む (<see cref="Game2DHud"/>)。 網目の合成は Game2D/LoadingBlend。</para>
    ///
    /// <para>左の枠 = 次の層の<b>本物の</b>点と道を、 走査線がひと回りするあいだに浮かべていく。 地図の画面が見せているもの
    /// (点の位置と道) だけを描き、 点の種類は出さない ── 出すのは現在地 (橙) と出口 (赤) だけなので、 地図より先に知れる情報は無い。
    /// 右の余白 = 遊び方のひとこと (<see cref="Tips"/> から毎回 1 つ)。 文字は日本語のドット字。</para>
    ///
    /// <para><b>液晶の中身として描く</b>: 地図・戦闘の演出と同じく、 液晶を写すカメラの前の板 (地図の板の複製を少し手前に置く) に流す。
    /// だから液晶の効果 (格子・にじみ・残像・湾曲・横線ノイズ) が地図とまったく同じに掛かる。 GUI の層に描くと計器盤向けの効果
    /// (斜めの映り込み・下の陰・寒色) だけが掛かって別物に見えた (2026-11-01)。 出している間は計器盤 (GUI) を隠す。</para>
    ///
    /// <para>既定は画面のフレームごとに描き直す (<see cref="fps"/> = 0)。 出している間は地図と GameManager のキー入力を止める
    /// (<see cref="GameManager.InputBlockedByScreen"/>)。 BOT がランを動かしている間は出さない。</para>
    /// </summary>
    public class Game2DLoadingScreen : MonoBehaviour
    {
        public const int W = Game2DLayout.Width, H = Game2DLayout.Height;
        [Tooltip("入りのトランジション (直前の画面 → ロード画面) の長さ (秒)")] public float enterSeconds = 0.35f;
        [Tooltip("出のトランジション (ロード画面 → 地図) の長さ (秒)")] public float exitSeconds = 0.35f;
        [Tooltip("走査線がひと回りする長さ (秒)。 そのあとは測り終えた地図を見せてクリックを待つ")] public float sweepSeconds = 3.5f;
        [Tooltip("コマの更新回数 (1 秒あたり)。 0 = 画面のフレームごと (既定)。 走査線は長いので、 30 コマでも 1 コマ 6° (先端で約 28 dot) 跳んでカクついた (2026-11-01)")]
        public float fps = 0f;

        // 配置 (dot): 左 = 地図の枠 / 右 = ヒントの欄。 液晶の湾曲で端が歪んで切れるので、 左右 28・上 28・下 24 の内側に収める (上は液晶の写りが上へずれていて、 16 でもはみ出た)
        //   (2026-11-01: 右の欄を 464 まで伸ばしていたら、 行末の字が右端で見切れた)
        private const int HeadY = 28;
        private static readonly RectInt MapBox = new RectInt(28, 52, 248, 194), TipBox = new RectInt(292, 52, 160, 194);

        private static readonly Color32 Bg = new Color32(0x07, 0x10, 0x15, 255), Grid = new Color32(0x16, 0x23, 0x2a, 255),
            Ink = new Color32(0xd8, 0xec, 0xf2, 255), Dim = new Color32(0x4d, 0x6a, 0x76, 255),
            Amber = new Color32(0xf0, 0xb0, 0x50, 255), Red = new Color32(0xe0, 0x60, 0x50, 255);

        /// <summary>遊び方のひとこと。 実装で確かめた規則だけ・数字はコードの定数から (変えたらここも追う)。</summary>
        private static string[] Tips => new[]
        {
            // 書かないもの: ボスの仕掛け・個別のアイテム効果・確率・追跡者の性質 (GAME.md §5-9 で明かさない)。
            // 仮置き UI に入口の無い操作 (逃げる・待つ・多めに休む・店の物資) もまだ書かない。
            // ── 戦闘 ──
            "配線は次のターンに持ち越される。変えたい時だけ組み替えればよい。",
            "防御の端子を挿した出目のぶんだけ、敵の攻撃を減らせる。予告の数字と見比べて置こう。",
            "攻撃の端子を1枚も挿さないターンは、こちらから攻撃しない。",
            "こちらの攻撃が先に当たる。相手を倒し切れるなら、防御を捨てて総攻撃を仕掛けよう。",
            "敵の攻撃は、こちらが振る前に予告される。よく見て計画を立てよう。",
            "敵の攻撃は、ターンが進むと段階的に強くなる。長引くほど苦しい。",
            "充電の端子を挿した出目の合計だけ充電が溜まる。上限は" + CombatSystem.CombatManager.ChargeMax + "。",
            "充電を払うと、配線の前にダイスを全部振り直せる。費用は「ダイスの数×その戦闘で何回目か」。",
            "防御は被ダメージを一定割合低下させる。積めば積むほど効率は徐々に低下する。",
            "貫通ステータスは、敵の防御力を差し引く。高防御を持つ敵により有効だ。",
            "素の面には役が無い。レアリティの上がった面を装着したダイスは、2つそろい・3つそろいで役が発動する。",
            // ── キーワード ──
            "毒はターンの始めに、1スタックあたり敵の最大HPの" + (ItemSystem.PassiveSkills.StatusRegistry.Defs["poison"].dotMaxHpPer10kPerStack / 100f).ToString("0.##") + "%を削る。削るたびにスタックは半分に減る。",
            "出血はターンごとに1ずつ減っていくが、スタック数に制限はない。",
            "臨界のメーターはATKの出目で溜まり、満ちると次の攻撃に大きく上乗せされる。",
            // ── 航行 ──
            "移動は道でつながった隣の点へ1歩ずつ。1歩ごとに物資を使う。",
            "着いた点は必ず起動する。道順を選ぶことは、どのマスを踏むかを選ぶこと。",
            "一度使った点は、戻っても何も起きない。",
            "偵察は1ターンを使い、マップの" + FreeMapParams.ReconRange + "歩先まで見る。追跡者も動くので、注意が必要だ。",
            "照明は物資" + FreeMapParams.FlareSupply + "で、" + FreeMapParams.FlareTurns + "ターンのあいだ" + FreeMapParams.FlareRange + "歩先まで見える。ただし、追跡者にも見つかりやすい。",
            "店と休憩は、どの層にも必ず1つずつある。よく探そう。",
            "天候は前線として西側から進んでくる。効果が起きるのは前線の内側だけ。",
            // ── 物資 ──
            "物資が減ると段階が下がり、不利な状態が重なっていく。",
            "物資が0の間は、歩くたびにHPが減る。",
            "HPを減らして戦闘を終えると、少し物資を落としてしまう。なるべく被弾を少なく片付けよう。",
            // ── 追跡者 ──
            "2層からは、層を巡回する追跡者が現れる。",
            "自分が1歩動くたびに、追跡者も動く。",
            "追跡者は、道で" + FreeMapParams.DetectRange + "歩以内に入るとこちらに気づく。種類によってはもっと遠くから気づく。",
            "追跡者は、しばらく追っても捕まえられなければ諦めて巡回に戻る。",
            "歩いた点には痕跡が残る。追跡者は痕跡をたどってくる。",
            FreeMapParams.QuietLose + "ターン見つからなければ、追跡者は見失って巡回に戻る。",
            "レーダーが、追跡者の近さと方角を知らせる。",
            "囮罠はターンを使わずに置ける。近くの追跡者の気を引き、踏ませれば" + FreeMapParams.LureHoldTurns + "ターン止められる。",
            "捕まると強敵との1戦になる。勝てばリターンは大きいが、常に危険な戦闘になる。",
            "追跡者は種類毎に特性が違う。動き方をよく見よう。",
            // ── 休憩・店・交換 ──
            "休憩では、HPを回復するか、素材で武器を1段強化するかを選べる。危なくなったら回復を優先しよう。",
            "強化素材は店でいつでも買える。一か所で大量に買い込むと徐々に値上がりする。",
            "交換マスでは、パッシブ1つをランダムな上位のレアリティのものと取り替える。運試しをしてみよう。",
            "パッシブアイテムを持てる数に上限は無い。大量のアイテムこそが勝利へのカギだ。",
        };

        /// <summary>出している間 (と、 飛ばしたフレーム) は true。 地図の操作がこれを見る。</summary>
        public static bool Blocking => _instanceActive || _endFrame == Time.frameCount;
        private static bool _instanceActive; private static int _endFrame = -1;

        private MeshRenderer _quad; private Texture2D _tex; private Color32[] _out, _ground;
        private RenderTexture _snap; private MaterialPropertyBlock _mpb;
        private enum Stage { Off, Enter, Show, Exit }
        private Stage _stage = Stage.Off; private float _stageT;
        private bool _active; private float _t; private int _drawnStep = -1;
        private int _lastFloor = int.MinValue; private RunState _lastRun;
        private string _title, _tip;

        // 測る地図 (Play の時に写す): 点の画面座標・走査線から見た角度・道
        private Vector2[] _pts; private float[] _ang; private bool[] _skip; private (int a, int b)[] _roads;
        private int _me = -1, _goal = -1; private Vector2 _center;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _instanceActive = false; _endFrame = -1; }

        /// <summary>シーンに無ければ付ける (地図の部品の隣)。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DLoadingScreen>() != null) return;
            var mv = FindObjectOfType<Game2DMapView>(); if (mv == null) return;
            mv.gameObject.AddComponent<Game2DLoadingScreen>();
        }

        /// <summary>液晶に写す板: 地図の板を複製し、 同じ位置の少し手前 (カメラ寄り) に置く。 出していない間は消しておく。</summary>
        private bool EnsureQuad()
        {
            if (_quad != null) return true;
            var mv = FindObjectOfType<Game2DMapView>();
            if (mv == null || mv.plotQuad == null) return false;
            var src = mv.plotQuad;
            var go = new GameObject("LoadingPlotQuad") { layer = src.gameObject.layer };
            go.transform.SetParent(src.transform.parent, false);
            go.transform.localPosition = src.transform.localPosition + new Vector3(0, 0, -0.1f);
            go.transform.localRotation = src.transform.localRotation; go.transform.localScale = src.transform.localScale;
            go.AddComponent<MeshFilter>().sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            _quad = go.AddComponent<MeshRenderer>(); _quad.enabled = false;
            var sh = Shader.Find("Game2D/LoadingBlend");
            _quad.sharedMaterial = sh != null ? new Material(sh) { name = "Game2DLoadingBlend" } : src.sharedMaterial;      // 無ければ網目なしで出す
            return true;
        }

        private void OnDestroy()
        {
            if (_tex != null) Destroy(_tex);
            if (_snap != null) { _snap.Release(); Destroy(_snap); }
            if (_quad != null) { if (_quad.sharedMaterial != null && _quad.sharedMaterial.name == "Game2DLoadingBlend") Destroy(_quad.sharedMaterial); Destroy(_quad.gameObject); }
            _instanceActive = false; GameManager.InputBlockedByScreen = false;
        }

        private GameManager _subscribed;

        private void OnDisable() { if (_subscribed != null) _subscribed.OnPhaseChanged -= OnPhaseChanged; _subscribed = null; }

        /// <summary>フェーズが変わった<b>その瞬間</b>に始める (次のフレームの Update では遅い)。 受理の処理はフレームの途中で走るので、
        /// 次のフレームまで待つと液晶にはもう地図が描かれていて、 直前の画面 (職業選択) を写し取れない。</summary>
        private void OnPhaseChanged(GameManager.GamePhase _) => CheckStart();

        private void CheckStart()
        {
            var gm = GameManager.Instance;
            if (gm == null || GameHooks.BotDriving || Application.isBatchMode) return;
            // 航行に入った瞬間を拾う: 新しいラン、 または層が変わった (キー入力・画面のどちらの経路でも)
            var run = gm.Run;
            if (gm.CurrentPhase == GameManager.GamePhase.MapNavigation && run != null && (run != _lastRun || run.currentFloor != _lastFloor) && gm.FreeMap != null)
            {
                _lastRun = run; _lastFloor = run.currentFloor;
                Play(gm.FreeMap, "第 " + run.currentFloor + " 層");
            }
            if (gm.CurrentPhase == GameManager.GamePhase.Title) { _lastRun = null; _lastFloor = int.MinValue; }
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null) return;
            if (_subscribed != gm) { if (_subscribed != null) _subscribed.OnPhaseChanged -= OnPhaseChanged; gm.OnPhaseChanged += OnPhaseChanged; _subscribed = gm; }
            if (GameHooks.BotDriving || Application.isBatchMode) { if (_active) Stop(); return; }
            CheckStart();                                                     // 取りこぼし用 (購読より前に変わっていた時)
            if (!_active) return;

            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            _t += dt; _stageT += dt;
            bool press = Input.GetMouseButtonDown(0) || Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return);
            switch (_stage)
            {
                case Stage.Enter:
                    if (_stageT >= enterSeconds) { _stage = Stage.Show; _stageT = 0f; }
                    break;
                case Stage.Show:
                    // クリックで進む (時間では閉じない)。 **測量が最後まで終わるまでは受けない** ── 受理の押し込みやダブルクリックの余りで
                    // 途中から抜けてしまわないように (2026-11-01)。 押した瞬間の入力は地図へ流さない
                    if (press && _t >= sweepSeconds) { GameManager.ScreenUsedSpaceFrame = Time.frameCount; _stage = Stage.Exit; _stageT = 0f; Game2DSfx.Play("ui_confirm"); }
                    break;
                case Stage.Exit:
                    if (_stageT >= exitSeconds) { Stop(); return; }
                    break;
            }
            Draw();
            ApplyFade();
        }

        /// <summary>網目の割合を板へ入れる。 入り = 直前の画面からロード画面へ / 出 = ロード画面が抜けて後ろの地図へ。</summary>
        private void ApplyFade()
        {
            if (_quad == null) return;
            float fade = 1f; bool usePrev = false;
            if (_stage == Stage.Enter) { fade = Mathf.Clamp01(_stageT / Mathf.Max(0.01f, enterSeconds)); usePrev = _snap != null; }
            else if (_stage == Stage.Exit) fade = 1f - Mathf.Clamp01(_stageT / Mathf.Max(0.01f, exitSeconds));
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb);
            _mpb.SetFloat("_Fade", fade); _mpb.SetFloat("_UsePrev", usePrev ? 1f : 0f);
            _quad.SetPropertyBlock(_mpb);
        }

        private void Play(FreeMapSim sim, string title)
        {
            if (sim == null || !EnsureQuad()) return;                         // 航行の地図を持たない層 (ボス戦だけの層) は出さない
            CopyMap(sim);
            var tips = Tips; _tip = tips[Random.Range(0, tips.Length)];       // 演出なので GameRng は使わない (ランの乱数を動かさない)
            _title = title; _t = 0f; _drawnStep = -1; _active = true; _instanceActive = true;
            _stage = Stage.Enter; _stageT = 0f;
            GameManager.InputBlockedByScreen = true;
            if (_tex == null)
            {
                _tex = new Texture2D(W, H, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLoading" };
                _out = new Color32[W * H];
            }
            if (_ground == null) _ground = new Color32[W * H];
            Game2DMapView.Ground(_ground, sim.Floor);
            SnapshotLcd();
            if (_mpb == null) _mpb = new MaterialPropertyBlock();
            _quad.GetPropertyBlock(_mpb);
            _mpb.SetTexture("_MainTex", _tex);
            if (_snap != null) _mpb.SetTexture("_PrevTex", _snap);
            _mpb.SetVector("_Dots", new Vector4(W, H, 0, 0));
            _quad.SetPropertyBlock(_mpb);
            _quad.enabled = true;
            Draw();
            ApplyFade();
            Game2DSfx.Play("gui_out");
        }

        /// <summary>直前の画面を写し取る: 液晶の RT には、 まだ前のフレーム (職業選択・層の終わりの画面など) が入っている。
        /// GPU の中だけで写す (CPU へ読み出さない = 止まらない)。</summary>
        private void SnapshotLcd()
        {
            var lcd = FindObjectOfType<UI.Lcd.LcdScreen>();
            var src = lcd != null ? lcd.Texture : null;
            if (src == null) return;
            if (_snap == null || _snap.width != src.width || _snap.height != src.height)
            {
                if (_snap != null) { _snap.Release(); Destroy(_snap); }
                _snap = new RenderTexture(src.width, src.height, 0, RenderTextureFormat.ARGB32) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DLoadingPrev" };
                _snap.Create();
            }
            Graphics.Blit(src, _snap);
        }

        private void Stop()
        {
            _active = false; _instanceActive = false; _endFrame = Time.frameCount; _stage = Stage.Off;
            GameManager.InputBlockedByScreen = false;
            if (_quad != null) _quad.enabled = false;
        }

        /// <summary>地図を枠へ収める (縦横比を保つ・余白 10 dot)。 沈んだ点は描かない。</summary>
        private void CopyMap(FreeMapSim sim)
        {
            var lay = sim.Layout; int n = lay.Count;
            double minX = double.MaxValue, maxX = double.MinValue, minY = double.MaxValue, maxY = double.MinValue;
            for (int i = 0; i < n; i++) { var p = lay.pos[i]; minX = System.Math.Min(minX, p.x); maxX = System.Math.Max(maxX, p.x); minY = System.Math.Min(minY, p.y); maxY = System.Math.Max(maxY, p.y); }
            const int pad = 10;
            float sx = (MapBox.width - 2 * pad) / (float)System.Math.Max(1e-6, maxX - minX), sy = (MapBox.height - 2 * pad) / (float)System.Math.Max(1e-6, maxY - minY);
            float s = Mathf.Min(sx, sy);
            float ox = MapBox.x + (MapBox.width - (float)(maxX - minX) * s) / 2f, oy = MapBox.y + (MapBox.height - (float)(maxY - minY) * s) / 2f;
            _pts = new Vector2[n]; _ang = new float[n]; _skip = new bool[n];
            _center = new Vector2(MapBox.x + MapBox.width / 2f, MapBox.y + MapBox.height / 2f);
            for (int i = 0; i < n; i++)
            {
                _pts[i] = new Vector2(Mathf.Round(ox + (float)(lay.pos[i].x - minX) * s), Mathf.Round(oy + (float)(lay.pos[i].y - minY) * s));
                var d = _pts[i] - _center; _ang[i] = (Mathf.Atan2(d.y, d.x) + Mathf.PI * 2.5f) % (Mathf.PI * 2f);      // 真上から時計回り
                _skip[i] = sim.IsSunk(i);
            }
            _roads = new System.Collections.Generic.List<(int a, int b)>(sim.Roads()).ToArray();
            _me = sim.Me; _goal = lay.goal;
        }

        // ── 描画 (既定は画面のフレームごと・fps > 0 ならその回数だけ) ──

        private void Draw()
        {
            float tt = _t;
            if (fps > 0f)
            {
                int step = (int)(_t * fps);
                if (step == _drawnStep) return;
                _drawnStep = step; tt = step / fps;                           // コマに揃えた時刻
            }
            float sweep = Mathf.Min(1f, tt / sweepSeconds) * Mathf.PI * 2f; bool done = tt >= sweepSeconds;

            if (_ground != null) System.Array.Copy(_ground, _out, _out.Length);      // 地図と同じ地 (中央ほど明るい網点・層の色)
            else for (int i = 0; i < _out.Length; i++) _out[i] = Bg;

            // 地図の枠: 格子の点と、 四隅のかぎ
            for (int y = MapBox.y + 8; y < MapBox.yMax; y += 16) for (int x = MapBox.x + 8; x < MapBox.xMax; x += 16) Set(x, y, Grid);
            Corners(MapBox, 8, Dim);

            // 走査線 (と、 その後ろの残り 2 本)
            if (!done)
                for (int k = 2; k >= 0; k--)
                {
                    float a = sweep - k * 0.14f - Mathf.PI / 2f;
                    ClipLine(_center, _center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 400f, k == 0 ? Ink : Dim);
                }

            // 道: 両端が浮かんだら引く (測っている間は 3 点描いて 1 点空ける破線・測り終えたら実線)。
            //   測っている間も測り終えた後と同じ明るさ (2026-11-01: 地の格子と同じ暗い色の 1 点おきでは、 道がほとんど見えなかった)
            int seen = 0;
            for (int i = 0; i < _pts.Length; i++) if (!_skip[i] && Seen(i, sweep, done)) seen++;
            foreach (var (a, b) in _roads)
                if (Seen(a, sweep, done) && Seen(b, sweep, done)) Seg(_pts[a], _pts[b], Dim, done ? 0 : 4);

            // 点: 3×3。 現在地は橙・出口は赤 (種類は出さない)
            for (int i = 0; i < _pts.Length; i++)
            {
                if (_skip[i] || !Seen(i, sweep, done)) continue;
                var p = _pts[i]; bool me = i == _me, goal = i == _goal;
                Box((int)p.x - (me || goal ? 2 : 1), (int)p.y - (me || goal ? 2 : 1), me || goal ? 5 : 3, me || goal ? 5 : 3, me ? Amber : goal ? Red : Ink);
            }
            // 測り終えたら現在地の輪を明滅
            if (done && _me >= 0 && (int)(tt * 4f) % 2 == 0) Ring(_pts[_me], 7, Amber);      // 明滅は 1 秒に 2 回 (コマ数に依らない)

            // 上の帯: 題と測った点の数
            string head = _title + (done ? " 測量完了" : " 測量中" + new string('・', 1 + (int)(tt * 3f) % 3));
            Jp(head, MapBox.x, HeadY, done ? Amber : Ink);
            int total = 0; foreach (var s in _skip) if (!s) total++;
            string count = "PLOT " + seen.ToString("00") + "/" + total.ToString("00");
            Jp(count, MapBox.xMax - Game2DJpFont.Width(count), HeadY, Dim);

            // 右の余白: ヒント (行数は「クリックで進む」の上までに収まる分だけ)
            Jp("ヒント", TipBox.x, TipBox.y + 4, Amber);
            for (int x = TipBox.x; x < TipBox.xMax; x += 2) Set(x, TipBox.y + 24, Dim);
            int tipLines = (TipBox.height - 32 - Game2DJpFont.Cell - 4) / Game2DJpFont.LineH;
            JpWrap(_tip, TipBox.x, TipBox.y + 32, TipBox.width, Ink, tipLines);
            // 進む合図: 測り終えたら明滅 (クリックを待っている)
            if (!done || (int)(tt * 2.5f) % 2 == 0) Jp("クリックで進む", TipBox.x, TipBox.yMax - Game2DJpFont.Cell, done ? Ink : Dim);

            _tex.SetPixels32(_out); _tex.Apply(false);
        }

        private bool Seen(int i, float sweep, bool done) => done || _ang[i] <= sweep;

        // ── 図形 ──

        private void Set(int x, int y, Color32 c) { if (x >= 0 && y >= 0 && x < W && y < H) _out[(H - 1 - y) * W + x] = c; }
        private void Box(int x, int y, int w, int h, Color32 c) { for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Set(x + i, y + j, c); }

        private void Corners(RectInt r, int len, Color32 c)
        {
            for (int k = 0; k < len; k++)
            {
                Set(r.x + k, r.y, c); Set(r.x, r.y + k, c); Set(r.xMax - 1 - k, r.y, c); Set(r.xMax - 1, r.y + k, c);
                Set(r.x + k, r.yMax - 1, c); Set(r.x, r.yMax - 1 - k, c); Set(r.xMax - 1 - k, r.yMax - 1, c); Set(r.xMax - 1, r.yMax - 1 - k, c);
            }
        }

        /// <summary>線分 (dash = 0 で実線、 n で n 点ごとに 1 点空ける)。</summary>
        private void Seg(Vector2 a, Vector2 b, Color32 c, int dash)
        {
            int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            for (int k = 0; k <= n; k++)
            {
                if (dash > 0 && k % dash == dash - 1) continue;
                float t = n == 0 ? 0 : k / (float)n;
                Set(Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t)), c);
            }
        }

        /// <summary>地図の枠の中だけに引く線 (走査線)。</summary>
        private void ClipLine(Vector2 a, Vector2 b, Color32 c)
        {
            int n = Mathf.CeilToInt(Mathf.Max(Mathf.Abs(b.x - a.x), Mathf.Abs(b.y - a.y)));
            for (int k = 0; k <= n; k++)
            {
                float t = k / (float)n; int x = Mathf.RoundToInt(Mathf.Lerp(a.x, b.x, t)), y = Mathf.RoundToInt(Mathf.Lerp(a.y, b.y, t));
                if (!MapBox.Contains(new Vector2Int(x, y))) break;
                Set(x, y, c);
            }
        }

        private void Ring(Vector2 c, int r, Color32 col)
        {
            for (int k = 0; k < 24; k++) { float a = k / 24f * Mathf.PI * 2f; Set(Mathf.RoundToInt(c.x + Mathf.Cos(a) * r), Mathf.RoundToInt(c.y + Mathf.Sin(a) * r), col); }
        }

        // 日本語のドット字を打つ (KitPix に描いてから写す)。 毎フレーム描くので、 文字の絵は (文字列・色) ごとに作り置く
        private readonly System.Collections.Generic.Dictionary<string, KitPix> _textCache = new System.Collections.Generic.Dictionary<string, KitPix>();

        private void Jp(string s, int x, int y, Color32 c)
        {
            string key = s + "|" + c.r + "," + c.g + "," + c.b;
            if (!_textCache.TryGetValue(key, out var kp))
            {
                kp = new KitPix(Mathf.Max(1, Game2DJpFont.Width(s)), Game2DJpFont.Cell); Game2DJpFont.Draw(kp, s, 0, 0, c);
                if (_textCache.Count > 256) _textCache.Clear();
                _textCache[key] = kp;
            }
            Blit(kp, x, y);
        }

        private void JpWrap(string s, int x, int y, int maxW, Color32 c, int maxLines)
        {
            string key = "wrap|" + maxW + "|" + maxLines + "|" + s;
            if (!_textCache.TryGetValue(key, out var kp))
            {
                kp = new KitPix(maxW, maxLines * Game2DJpFont.LineH); Game2DJpFont.Wrap(kp, s, 0, 0, maxW, c, maxLines);
                _textCache[key] = kp;
            }
            Blit(kp, x, y);
        }

        private void Blit(KitPix kp, int x, int y)
        {
            for (int j = 0; j < kp.h; j++) for (int i = 0; i < kp.w; i++) { var c = kp.px[(kp.h - 1 - j) * kp.w + i]; if (c.a > 0) Set(x + i, y + j, c); }
        }
    }
}
