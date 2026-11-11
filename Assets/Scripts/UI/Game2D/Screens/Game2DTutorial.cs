using System.Collections.Generic;
using GameLoop;
using UI.Game2D.Battle;
using UnityEngine;
using UnityEngine.UI;

namespace UI.Game2D
{
    /// <summary>
    /// 手引きの節の見出し (= 見た印の鍵)。 <b>handbook.md の「## 見出し」と同じ字</b>。 見出しを変えたらここも変える
    /// (エディタでは、 生成した文に無い見出しを起動時に警告する)。
    /// </summary>
    public static class TutorialTopics
    {
        public const string RunAndReturn = "ランと帰還";
        public const string WalkLayer = "層の歩き方";
        public const string Patrol = "巡回するもの";
        public const string Outpost = "前哨基地";
        public const string CombatForm = "戦闘の形式";
        public const string RollDice = "ダイスを振る";
        public const string Telegraph = "敵の予告";
        public const string Plug = "端子に挿す";
        public const string Deck = "端子の山札";
        public const string Kinds = "端子の種類";
        public const string GrowDeck = "端子を増やす";
        public const string Order = "解決の順番";
        public const string Long = "戦闘の長期化";
        public const string Crit = "会心";
        public const string CritCurve = "会心率の伸び";
        public const string ForceCrit = "確定会心";
        public const string Keyword = "キーワード";
        public const string Poison = "毒";
        public const string Bleed = "出血";
        public const string Charge = "充電";
        public const string Rinkai = "臨界";
        public const string Provision = "物資";
        public const string Flee = "逃げる";
        public const string Inventory = "持ち物";
        public const string Equip = "装備と強化";
        public const string Shop = "ショップ";
        public const string Origin = "出自";
        public const string Contract = "旅団契約";
        public const string LayerBoss = "層とボス";
        public const string Rift = "時間の狭間";
        public const string MetaPanel = "整備パネル";
    }

    /// <summary>手引きの文。 <b>文の正本は docs/handbook.md</b> ── 中身 (<see cref="All"/>) は Tools/sync_handbook.py が作る Game2DTutorialText.cs にある。
    /// handbook を直したら <c>python Tools/sync_handbook.py</c> を回す (<c>--check</c> で食い違いだけ見る)。 画面に出す字は
    /// <c>python Tools/sync_handbook.py --plain 出力先</c> → <c>python Tools/glyph_check.py 出力先</c> でドット字にあるかを確かめる。</summary>
    public static partial class Game2DTutorialText
    {
        public sealed class Entry
        {
            public readonly string title, trigger, body;
            public Entry(string title, string trigger, string body) { this.title = title; this.trigger = trigger; this.body = body; }
        }

        public static Entry Find(string title)
        {
            foreach (var e in All) if (e.title == title) return e;
            return null;
        }
    }

    /// <summary>
    /// 手引き (issue #86・<b>仮置き</b>・2026-11-12)。 handbook の各節を、 その節の「出す契機」の場面に初めて来た時に 1 度だけ、 小さな板で出す。
    /// 読んで閉じたら見た印 (<see cref="TutorialSeen"/>・PlayerPrefs) を付け、 ランをまたいで二度と出さない。 設定の画面の「手引きをもう一度」で全部消せる。
    ///
    /// <para><b>出し方</b>: 場面の側が <see cref="Request"/>(見出し) を呼ぶ (呼ぶ所の一覧は <see cref="Table"/> の注記)。 見た節・出さない節は何もしない。
    /// 頼まれた節は待ち行列に入り、 その節の場面 (<see cref="Scene"/>) が整ったら handbook の並び順で 1 つずつ出す。 戦闘の節は戦闘が終わったら捨てる
    /// (印は付かないので、 次の戦闘でまた頼まれる)。 長い節はページ送り (段落の切れ目で送る)。 太字 (**…**) は金の字で描く。</para>
    /// <para><b>出している間</b>は、 窓 (<see cref="Game2DModal.Active"/> が true を返す)・タイトルのボタン (<see cref="SpriteButton.InputLocked"/>)・
    /// GameManager のキー (<see cref="GameManager.InputBlockedByScreen"/>) を止める ── 戦闘も地図も進まない。 閉じたフレームまで止め続ける
    /// (同じ Space / Esc が後ろの画面へ流れないように)。 操作: Space / Enter / → / クリックで次へ・← で前へ・Esc でその節を閉じる。</para>
    /// <para><b>BOT がランを動かしている間</b> (<see cref="GameHooks.BotDriving"/>・バッチ実行) は頼まれても積まず、 出していれば印を付けずに下げる。
    /// 板は 480×270 の格子の中央。 大きさ・位置で状態を表さない (入りは網目で出す)。 字は <see cref="Game2DJpFont"/> だけ。</para>
    /// </summary>
    public class Game2DTutorial : MonoBehaviour
    {
        /// <summary>節を出してよい場面。 Title = タイトル (窓が出ていない時) / Map = 地図 (窓・層の帯が出ていない時) /
        /// Battle = 2D の戦闘の間 (窓の上にも出す) / Any = どこでも (場面の画面の上にも出す)。</summary>
        public enum Scene { Title, Map, Battle, Any }

        private struct Topic
        {
            public string title; public Scene scene;
            /// <summary>null = 出す。 字が入っていれば出さない理由 (handbook の文が今のコードと食い違う・場面が無い)。 handbook を直したら null に戻す。</summary>
            public string held;
            public Topic(string t, Scene s, string held = null) { title = t; scene = s; this.held = held; }
        }

        /// <summary>
        /// 契機の表 (並び = handbook の並び = 同時に頼まれた時に出す順)。 頼む所:
        /// <list type="bullet">
        /// <item>ランと帰還 / 層の歩き方 / キーワード / 出血 / 充電 / 臨界 ── この部品が状態を見て頼む (<see cref="Watch"/>)</item>
        /// <item>巡回するもの ── 魔石が初めて反応した時 (<see cref="Game2DRadar.SetStone"/>)</item>
        /// <item>戦闘の形式 / 層とボス / 端子の山札 / 敵の予告 / 端子に挿す / 端子の種類 / 解決の順番 / 戦闘の長期化 / 会心 / 確定会心 / 逃げる ── 戦闘の流れ (<see cref="Game2DBattleSim"/>)</item>
        /// <item>端子を増やす ── 戦闘の後の端子の報酬の窓 (<see cref="Game2DPhaseScreens"/>)</item>
        /// <item>整備パネル ── 整備パネルの窓を開いた時 (<see cref="Game2DMetaPanel.Open"/>)</item>
        /// </list>
        /// </summary>
        private static readonly Topic[] Table =
        {
            new Topic(TutorialTopics.RunAndReturn, Scene.Title),
            new Topic(TutorialTopics.WalkLayer, Scene.Map),
            new Topic(TutorialTopics.Patrol, Scene.Map),
            new Topic(TutorialTopics.Outpost, Scene.Map, "旅団との契約 (2026-08-11 に削除) を挙げている。 前哨基地は立ち寄る画面が無い (層に入ると自動で回復・強化)"),
            new Topic(TutorialTopics.CombatForm, Scene.Battle),
            new Topic(TutorialTopics.RollDice, Scene.Battle, "振り直しは物資ではなく充電を払う (CombatManager.Mechanics の rerollCost)。 「目の内容はダイスが決める」は出目パーツ化 (2026-08-17) で古い"),
            new Topic(TutorialTopics.Telegraph, Scene.Battle),
            new Topic(TutorialTopics.Plug, Scene.Battle),
            new Topic(TutorialTopics.Deck, Scene.Battle),
            new Topic(TutorialTopics.Kinds, Scene.Battle),
            new Topic(TutorialTopics.GrowDeck, Scene.Any),
            new Topic(TutorialTopics.Order, Scene.Battle),
            new Topic(TutorialTopics.Long, Scene.Battle),
            new Topic(TutorialTopics.Crit, Scene.Battle),
            new Topic(TutorialTopics.CritCurve, Scene.Battle, "会心率の逓減は 2026-09-15 に撤去 (DAMAGE_CALC_REFERENCE §2-B)。 「100% には届かない」も今は違う"),
            new Topic(TutorialTopics.ForceCrit, Scene.Battle),
            new Topic(TutorialTopics.Keyword, Scene.Any),
            new Topic(TutorialTopics.Poison, Scene.Battle, "「自然には減らない・戦闘が終わるまで残る」が今の毒 (ダメージを出すたびに残りが半減) と逆"),
            new Topic(TutorialTopics.Bleed, Scene.Battle),
            new Topic(TutorialTopics.Charge, Scene.Battle),
            new Topic(TutorialTopics.Rinkai, Scene.Battle),
            new Topic(TutorialTopics.Provision, Scene.Map, "物資の段階ごとの不調は無い (ProvisionRules.TierOf は 足りる / 尽きた の 2 つだけ)"),
            new Topic(TutorialTopics.Flee, Scene.Battle),
            new Topic(TutorialTopics.Inventory, Scene.Any, "持ち物の格子・広さの上限は無い"),
            new Topic(TutorialTopics.Equip, Scene.Any, "「ダイス」の品は廃止 (出目パーツ・2026-08-17)"),
            new Topic(TutorialTopics.Shop, Scene.Any, "「ダイス」の品・持ち物の領域の拡張を挙げている (どちらも今は無い)"),
            new Topic(TutorialTopics.Origin, Scene.Any, "契機の場面 (出自の選択画面) が Game2D に無い"),
            new Topic(TutorialTopics.Contract, Scene.Any, "旅団契約は 2026-08-11 にシステムごと削除"),
            new Topic(TutorialTopics.LayerBoss, Scene.Battle),
            new Topic(TutorialTopics.Rift, Scene.Any, "時間の狭間はコードに無い"),
            new Topic(TutorialTopics.MetaPanel, Scene.Any),
        };

        // ── 待ち行列 (部品が無くても頼める・場面をまたいで残る) ──
        private static readonly List<string> _queue = new List<string>();
        private static string _cur;
        private static int _closeFrame = -10;
        private static Game2DTutorial _inst;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { _queue.Clear(); _cur = null; _closeFrame = -10; _inst = null; }

        /// <summary>出している (か、 閉じたフレーム) ── この間は後ろの画面の入力を止める。</summary>
        public static bool Blocking => _cur != null || Time.frameCount <= _closeFrame;

        /// <summary>節を頼む。 見た節・出さない節・BOT の間は何もしない。 何度呼んでもよい (毎フレームでも)。</summary>
        public static void Request(string title)
        {
            if (Application.isBatchMode || GameHooks.BotDriving || string.IsNullOrEmpty(title)) return;
            if (title == _cur || TutorialSeen.Has(title) || _queue.Contains(title)) return;
            int i = IndexOf(title); if (i < 0 || Table[i].held != null) return;
            if (Game2DTutorialText.Find(title) == null) return;
            _queue.Add(title);
        }

        /// <summary>見た印が付いているか (「端子に挿す」を読んだ後で「逃げる」を出す ── のように順を作る)。</summary>
        public static bool Seen(string title) => TutorialSeen.Has(title);

        /// <summary>全部の見た印を消す (設定の画面)。 待ち行列も空にする。</summary>
        public static void ResetAll() { TutorialSeen.Clear(); _queue.Clear(); }

        private static int IndexOf(string title) { for (int i = 0; i < Table.Length; i++) if (Table[i].title == title) return i; return -1; }

        /// <summary>シーンに無ければ付ける (シーンは git 管理外)。 窓 (Game2DModal) と同じ格子に、 窓より手前へ置く。</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            if (FindObjectOfType<Game2DTutorial>() != null) return;
            var m = FindObjectOfType<Game2DModal>(); if (m == null || m.transform.parent == null) return;
            var go = new GameObject("Tutorial", typeof(RectTransform)); go.layer = 5;
            var rt = (RectTransform)go.transform; rt.SetParent(m.transform.parent, false);
            rt.SetAsLastSibling();                                         // 窓より後の兄弟 = 窓より手前に描く
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0, 1); rt.sizeDelta = new Vector2(1, 1);
            go.AddComponent<Game2DTutorial>().modal = m;
        }

        public Game2DModal modal;
        private RawImage _img; private RectTransform _rt; private Texture2D _tex;
        private Game2DTutorialText.Entry _entry;
        private readonly List<Page> _pages = new List<Page>();
        private int _page, _w, _h, _px, _py, _hoverBtn = -1, _lastOwned = -1;
        private float _t; private bool _dirty;
        private readonly List<RectInt> _btnR = new List<RectInt>();
        private readonly List<string> _btnA = new List<string>();
        private bool _locked, _prevLock;

        private const int W = 408, Pad = 12, TopY = 7, TitleH = 14, BtnH = 22, MaxLines = 10, ParaGap = 5, Hang = 10;
        private const float InDur = 0.18f;
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        private struct Line { public string s; public bool[] em; public int y; }
        private sealed class Page { public readonly List<Line> lines = new List<Line>(); public int h; }

        private void Awake()
        {
            _inst = this;
            _rt = (RectTransform)transform;
            _img = gameObject.AddComponent<RawImage>(); _img.raycastTarget = false; _img.enabled = false;
#if UNITY_EDITOR
            foreach (var tp in Table) if (Game2DTutorialText.Find(tp.title) == null)
                Debug.LogWarning("[Game2DTutorial] 手引きの文に「" + tp.title + "」が無い (handbook の見出しを変えた? python Tools/sync_handbook.py を回して TutorialTopics を合わせる)");
#endif
        }

        private void OnDestroy()
        {
            if (_inst == this) _inst = null;
            if (_tex != null) Destroy(_tex);
            ReleaseInput();
            _cur = null;
        }

        private void Update()
        {
            var gm = GameManager.Instance;
            if (gm == null) { if (_cur != null) Hide(); SyncInput(); return; }
            if (Application.isBatchMode || GameHooks.BotDriving)
            {
                _queue.Clear();
                if (_cur != null) Hide();          // 印は付けない (人が遊ぶ時にまた出る)
                SyncInput(); return;
            }
            Watch(gm);
            if (_cur != null && !SceneOk(Table[IndexOf(_cur)].scene, gm, true)) Hide();      // 場面が消えた (ロード画面が始まった・戦闘が閉じた): 下げて頼み直しを待つ
            if (_cur == null) TryStart(gm);
            if (_cur != null) { HandleInput(); Draw(); }
            SyncInput();
        }

        // ── 状態を見て頼む節 ──

        private void Watch(GameManager gm)
        {
            var ph = gm.CurrentPhase;
            if (ph == GameManager.GamePhase.Title) Request(TutorialTopics.RunAndReturn);
            else if (ph == GameManager.GamePhase.MapNavigation) Request(TutorialTopics.WalkLayer);
            var run = gm.Run;
            int owned = run?.ownedPassiveItems != null ? run.ownedPassiveItems.Count : -1;
            if (owned != _lastOwned)
            {
                _lastOwned = owned;
                if (owned > 0 && !TutorialSeen.Has(TutorialTopics.Keyword))
                    foreach (var axis in ItemSystem.PassiveSkills.AxisRules.Ranked)
                        if (ItemSystem.PassiveSkills.AxisRules.Count(run, axis) > 0) { Request(TutorialTopics.Keyword); break; }
            }
            if (Game2DBattleSim.Active)
            {
                var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                if (ctx != null)
                {
                    if (ctx.enemyBleedStacks > 0) Request(TutorialTopics.Bleed);
                    if (ctx.GetCharge() > 0) Request(TutorialTopics.Charge);
                    if (ctx.rinkaiMeter > 0) Request(TutorialTopics.Rinkai);
                }
            }
        }

        private static bool SceneOk(Scene s, GameManager gm, bool showing)
        {
            if (Game2DLoadingScreen.Blocking || Game2DBoot.Waiting) return false;
            switch (s)
            {
                case Scene.Title: return gm.CurrentPhase == GameManager.GamePhase.Title && (showing || !Game2DModal.WindowActive);
                case Scene.Map: return gm.CurrentPhase == GameManager.GamePhase.MapNavigation && (showing || (!Game2DModal.WindowActive && !Game2DLayerBanner.Busy));
                case Scene.Battle: return Game2DBattleSim.Active;
                default: return true;
            }
        }

        /// <summary>待ち行列から、 場面の整った節を handbook の順で 1 つ出す。 戦闘の外に残った戦闘の節は捨てる。</summary>
        private void TryStart(GameManager gm)
        {
            if (_queue.Count == 0) return;
            string pick = null; int best = int.MaxValue;
            for (int i = _queue.Count - 1; i >= 0; i--)
            {
                string q = _queue[i]; int k = IndexOf(q);
                if (k < 0 || TutorialSeen.Has(q) || (Table[k].scene == Scene.Battle && !Game2DBattleSim.Active)) { _queue.RemoveAt(i); continue; }
                if (k < best && SceneOk(Table[k].scene, gm, false)) { best = k; pick = q; }
            }
            if (pick == null) return;
            _queue.Remove(pick);
            Show(pick);
        }

        private void Show(string title)
        {
            _entry = Game2DTutorialText.Find(title); if (_entry == null) return;
            _cur = title; _page = 0; _t = 0f; _hoverBtn = -1; _dirty = true;
            Paginate();
            if (_tex == null || _tex.width != _w || _tex.height != _h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DTutorial" };
            }
            _img.texture = _tex; _img.enabled = true;
            _px = (Game2DLayout.Width - _w) / 2; _py = (Game2DLayout.Height - _h) / 2;
            _rt.sizeDelta = new Vector2(_w, _h); _rt.anchoredPosition = new Vector2(_px, -_py);
            _rt.SetAsLastSibling();
            Game2DSfx.Play("ui_open", 0.8f);
        }

        /// <summary>下げる (印は付けない)。</summary>
        private void Hide() { _cur = null; _entry = null; _img.enabled = false; _closeFrame = Time.frameCount; }

        /// <summary>読み終えた: 印を付けて閉じ、 続けて出せる節があれば出す。</summary>
        private void Finish()
        {
            TutorialSeen.Mark(_cur);
            Game2DSfx.Play("ui_close");
            Hide();
            var gm = GameManager.Instance; if (gm != null) TryStart(gm);
        }

        // ── 入力 ──

        private void HandleInput()
        {
            bool space = Input.GetKeyDown(KeyCode.Space);
            if (space) GameManager.ScreenUsedSpaceFrame = Time.frameCount;
            bool next = space || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.RightArrow);
            bool prev = Input.GetKeyDown(KeyCode.LeftArrow) || Input.GetKeyDown(KeyCode.Backspace);
            if (Input.GetKeyDown(KeyCode.Escape)) { Finish(); return; }
            int hv = -1;
            if (modal != null && Game2DPointer.TryDot(modal.screen, out var d))
                for (int i = 0; i < _btnR.Count; i++) { var r = _btnR[i]; if (d.x >= _px + r.x && d.x < _px + r.x + r.width && d.y >= _py + r.y && d.y < _py + r.y + r.height) { hv = i; break; } }
            if (hv != _hoverBtn) { if (hv >= 0) Game2DSfx.Play("ui_hover", 0.7f); _hoverBtn = hv; _dirty = true; }
            if (hv >= 0 && Input.GetMouseButtonDown(0)) { if (_btnA[hv] == "prev") prev = true; else next = true; }
            if (prev && _page > 0) { _page--; _dirty = true; Game2DSfx.Play("ui_click"); }
            else if (next)
            {
                if (_page < _pages.Count - 1) { _page++; _dirty = true; Game2DSfx.Play("ui_click"); }
                else Finish();
            }
        }

        /// <summary>出している間 (と閉じたフレーム) は、 タイトルのボタンと GameManager のキーを止める。</summary>
        private void SyncInput()
        {
            bool block = Blocking;
            if (block && !_locked)
            {
                _prevLock = SpriteButton.InputLocked; SpriteButton.InputLocked = true;
                GameManager.InputBlockedByScreen = true; _locked = true;
            }
            else if (!block && _locked) ReleaseInput();
        }

        private void ReleaseInput()
        {
            if (!_locked) return;
            SpriteButton.InputLocked = _prevLock;
            if (!Game2DLoadingScreen.Blocking) GameManager.InputBlockedByScreen = false;      // ロード画面が立てている時は触らない
            _locked = false;
        }

        // ── ページ割り ──
        //   段落 (\n 区切り) ごとに折り返し、 1 ページ MaxLines 行に収める。 段落が入り切らなければ次のページへ送る (1 段落がページより長い時だけ途中で切る)。
        //   板の高さは全ページで一番高いページに合わせる (ページ送りで板の大きさが変わらない)。

        private int BodyW => W - Pad * 2 - Hang;

        private void Paginate()
        {
            _pages.Clear();
            int LH = Game2DJpFont.LineH, cap = MaxLines * LH;
            var cur = new Page(); int y = 0;
            foreach (var para in _entry.body.Split('\n'))
            {
                var lines = WrapEm(para, BodyW);
                if (lines.Count == 0) continue;
                int need = lines.Count * LH + (cur.lines.Count > 0 ? ParaGap : 0);
                if (cur.lines.Count > 0 && y + need > cap) { _pages.Add(cur); cur = new Page(); y = 0; }
                if (cur.lines.Count > 0) y += ParaGap;
                foreach (var ln in lines)
                {
                    if (y + LH > cap && cur.lines.Count > 0) { _pages.Add(cur); cur = new Page(); y = 0; }
                    var l = ln; l.y = y; cur.lines.Add(l); y += LH; cur.h = y;
                }
            }
            if (cur.lines.Count > 0 || _pages.Count == 0) _pages.Add(cur);
            int maxH = LH; foreach (var p in _pages) maxH = Mathf.Max(maxH, p.h);
            _w = W; _h = Mathf.Min(Game2DLayout.Height - 4, BodyY + maxH + 8 + BtnH + Pad);
        }

        private const int BodyY = TopY + TitleH + 7;

        /// <summary>行頭に来ると読みにくい字 (前の行の終わりにぶら下げる)。</summary>
        private const string NoHead = "、。，．・：；？！）」』】〉》ー…ぁぃぅぇぉっゃゅょァィゥェォッャュョ";

        /// <summary>**…** を強調として読み、 幅 maxW で折り返す。 行頭の句読点は前の行へぶら下げる (Hang ドットまで)。</summary>
        private static List<Line> WrapEm(string s, int maxW)
        {
            var chars = new List<char>(); var em = new List<bool>(); bool on = false;
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] == '*' && i + 1 < s.Length && s[i + 1] == '*') { on = !on; i++; continue; }
                chars.Add(s[i]); em.Add(on);
            }
            var outL = new List<Line>();
            var sb = new System.Text.StringBuilder(); var eb = new List<bool>(); int w = 0;
            for (int i = 0; i < chars.Count; i++)
            {
                char ch = chars[i]; int a = Game2DJpFont.Advance(ch);
                if (w + a > maxW && sb.Length > 0 && !(NoHead.IndexOf(ch) >= 0 && w + a <= maxW + Hang))
                {
                    outL.Add(new Line { s = sb.ToString(), em = eb.ToArray() }); sb.Clear(); eb.Clear(); w = 0;
                    if (ch == ' ' || ch == '　') continue;
                }
                sb.Append(ch); eb.Add(em[i]); w += a;
            }
            if (sb.Length > 0) outL.Add(new Line { s = sb.ToString(), em = eb.ToArray() });
            return outL;
        }

        // ── 絵 ──

        private void Draw()
        {
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            bool entering = _t < InDur; _t += dt;
            if (!_dirty && !entering) return;
            _dirty = false;
            var p = new KitPix(_w, _h);
            p.NineSlice(Game2DKit.Frame, 12, Game2DKit.Well);
            int x0 = Pad, iw = _w - Pad * 2, LH = Game2DJpFont.LineH;
            // 題 (左) と ページ (右)
            Game2DJpFont.Draw(p, _entry.title, x0, TopY, Game2DKit.Ivory);
            string pg = "手引き" + (_pages.Count > 1 ? "  " + (_page + 1) + "/" + _pages.Count : "");
            Game2DJpFont.Draw(p, pg, x0 + iw - Game2DJpFont.Width(pg), TopY, Game2DKit.Dim);
            p.Rect(x0, TopY + TitleH + 2, iw, 1, Game2DKit.Faint);
            // 本文 (強調は金)
            var page = _pages[Mathf.Clamp(_page, 0, _pages.Count - 1)];
            foreach (var ln in page.lines)
            {
                int cx = x0 + 2, cy = BodyY + ln.y;
                for (int i = 0; i < ln.s.Length; i++)
                    cx += Game2DJpFont.Draw(p, ln.s[i].ToString(), cx, cy, ln.em[i] ? Game2DKit.Gold : Game2DKit.Ink);
            }
            // ボタン (右寄せ): [前へ] [次へ / 閉じる]
            _btnR.Clear(); _btnA.Clear();
            bool last = _page >= _pages.Count - 1;
            var labels = new List<string>(); var acts = new List<string>();
            if (_page > 0) { labels.Add("前へ"); acts.Add("prev"); }
            labels.Add(last ? "閉じる" : "次へ"); acts.Add("next");
            int by = _h - Pad - BtnH, bx = _w - Pad;
            var rects = new RectInt[labels.Count];
            for (int i = labels.Count - 1; i >= 0; i--) { int bw = Mathf.Max(52, Game2DJpFont.Width(labels[i]) + 20); bx -= bw; rects[i] = new RectInt(bx, by, bw, BtnH); bx -= 5; }
            for (int i = 0; i < labels.Count; i++)
            {
                var r = rects[i]; _btnR.Add(r); _btnA.Add(acts[i]);
                p.Sprite(acts[i] == "next" ? Game2DKit.Go : Game2DKit.Btn, r.x, r.y, r.width, r.height);
                if (i == _hoverBtn) p.Rect(r.x, r.y + r.height - 1, r.width, 1, Game2DKit.Amber);
                Game2DJpFont.Draw(p, labels[i], r.x + (r.width - Game2DJpFont.Width(labels[i])) / 2, r.y + (BtnH - Game2DJpFont.Cell + 1) / 2, Game2DKit.Ink);
            }
            // 足元の操作の案内 (ボタンの左)
            const string hint = "Space で次へ・Esc で閉じる";
            if (Game2DJpFont.Width(hint) < bx - x0) Game2DJpFont.Draw(p, hint, x0 + 2, by + (BtnH - Game2DJpFont.Cell + 1) / 2, Game2DKit.Dim);
            // 入り: 網目 (4×4 Bayer) で出る
            if (_t < InDur)
            {
                float lv = Mathf.Clamp01(_t / InDur) * 16f;
                for (int y = 0; y < _h; y++)
                    for (int x = 0; x < _w; x++)
                        if (lv <= Bayer[(y & 3) * 4 + (x & 3)]) p.px[(_h - 1 - y) * _w + x] = default;
            }
            p.Apply(_tex);
        }
    }
}
