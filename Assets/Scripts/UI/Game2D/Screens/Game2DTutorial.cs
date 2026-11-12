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
        public const string FacePart = "出目パーツ";
        public const string Role = "役";
        public const string Crit = "会心";
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
        public const string LayerBoss = "層とボス";
        /// <summary>目盛りのあるボスとの初めての戦闘 (2026-11-13・ボスのダウン)。</summary>
        public const string BossDown = "ボスのダウン";
        public const string MetaPanel = "整備パネル";
        public const string Challenge = "挑戦";
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
    /// 字は <see cref="Game2DJpFont"/> だけ。 大きさで状態を表さない (入りは網目と幕の濃さで出す)。</para>
    /// <para><b>強調</b> (issue #109): 出している間は画面全体を幕で暗くし、 節ごとに決めた部品 (<see cref="FocusOf"/>) の範囲だけ幕を抜いて枠で囲む。
    /// 範囲は部品の RectTransform から毎フレーム求める (座標はここに書かない)。 板は範囲に重ならない所 (上下 → 左右) に置き、 部品の無い節は中央。</para>
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
        /// <item>ランと帰還 / 層の歩き方 / 前哨基地 / 出目パーツ / 役 / キーワード / 毒 / 出血 / 充電 / 臨界 / 物資 / 持ち物 ── この部品が状態を見て頼む (<see cref="Watch"/>)</item>
        /// <item>巡回するもの ── 魔石が初めて反応した時 (<see cref="Game2DRadar.SetStone"/>)</item>
        /// <item>戦闘の形式 / 層とボス / 端子の山札 / ダイスを振る / 敵の予告 / 端子に挿す / 端子の種類 / 解決の順番 / 戦闘の長期化 / 会心 / 確定会心 / 逃げる ── 戦闘の流れ (<see cref="Game2DBattleSim"/>)</item>
        /// <item>端子を増やす / 装備と強化 (休憩・武器の入れ替え) / ショップ ── 場面の窓 (<see cref="Game2DPhaseScreens"/>)</item>
        /// <item>出自 ── 出自 (職業) の選択画面が出そろった時 (<see cref="UI.ClassSelect.ClassSelectView"/>)</item>
        /// <item>整備パネル ── 整備パネルの窓を開いた時 (<see cref="Game2DMetaPanel.Open"/>)</item>
        /// <item>挑戦 ── 第三層 (内部の深さ <see cref="GameLoop.RunState.normalClearFloor"/>) を初めてクリアした後のタイトル (<see cref="Watch"/>)。
        /// 挑戦の難易度 1 は最初から選べる (<see cref="Game2DChallengePanel"/> の「記録 +1 まで」) が、 作者の指示で第三層のクリアまで見せない</item>
        /// </list>
        /// </summary>
        private static readonly Topic[] Table =
        {
            new Topic(TutorialTopics.RunAndReturn, Scene.Title),
            new Topic(TutorialTopics.WalkLayer, Scene.Map),
            new Topic(TutorialTopics.Patrol, Scene.Map),
            new Topic(TutorialTopics.Outpost, Scene.Map),
            new Topic(TutorialTopics.CombatForm, Scene.Battle),
            new Topic(TutorialTopics.RollDice, Scene.Battle),
            new Topic(TutorialTopics.Telegraph, Scene.Battle),
            new Topic(TutorialTopics.Plug, Scene.Battle),
            new Topic(TutorialTopics.Deck, Scene.Battle),
            new Topic(TutorialTopics.Kinds, Scene.Battle),
            new Topic(TutorialTopics.GrowDeck, Scene.Any),
            new Topic(TutorialTopics.Order, Scene.Battle),
            new Topic(TutorialTopics.Long, Scene.Battle),
            new Topic(TutorialTopics.FacePart, Scene.Any),
            new Topic(TutorialTopics.Role, Scene.Battle),
            new Topic(TutorialTopics.Crit, Scene.Battle),
            new Topic(TutorialTopics.ForceCrit, Scene.Battle),
            new Topic(TutorialTopics.Keyword, Scene.Any),
            new Topic(TutorialTopics.Poison, Scene.Battle),
            new Topic(TutorialTopics.Bleed, Scene.Battle),
            new Topic(TutorialTopics.Charge, Scene.Battle),
            new Topic(TutorialTopics.Rinkai, Scene.Battle),
            new Topic(TutorialTopics.Provision, Scene.Map),
            new Topic(TutorialTopics.Flee, Scene.Battle),
            new Topic(TutorialTopics.Inventory, Scene.Any),
            new Topic(TutorialTopics.Equip, Scene.Any),
            new Topic(TutorialTopics.Shop, Scene.Any),
            new Topic(TutorialTopics.Origin, Scene.Any),
            new Topic(TutorialTopics.LayerBoss, Scene.Battle),
            new Topic(TutorialTopics.MetaPanel, Scene.Any),
            new Topic(TutorialTopics.Challenge, Scene.Title),
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
        // 2 枚重ね (issue #109): この部品の RawImage = 画面全体を暗くする幕 (強調の範囲だけ穴と枠)、 子の「Window」 = 文面の板
        private RawImage _dimImg; private RectTransform _dimRt; private Texture2D _dimTex;
        private RawImage _img; private RectTransform _rt; private Texture2D _tex;
        private RectInt? _focus; private int _stable; private string _probe; private RectInt? _probeRect; private float _probeT;
        private Game2DBattleHud _hud; private Game2DRadar _radar;
        private int _winW = W;
        private Game2DTutorialText.Entry _entry;
        private readonly List<Page> _pages = new List<Page>();
        private int _page, _w, _h, _px, _py, _hoverBtn = -1, _lastOwned = -1;
        private float _t; private bool _dirty;
        private readonly List<RectInt> _btnR = new List<RectInt>();
        private readonly List<string> _btnA = new List<string>();
        private bool _locked, _prevLock;

        private const int W = 408, Pad = 12, TopY = 7, TitleH = 14, BtnH = 22, MaxLines = 10, Hang = 10;
        private const float InDur = 0.18f, DimIn = 0.3f;
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        private struct Line { public string s; public bool[] em; public int y; }
        private sealed class Page { public readonly List<Line> lines = new List<Line>(); public int h; }

        private void Awake()
        {
            _inst = this;
            // 幕: 格子いっぱい (左上が原点)
            _dimRt = (RectTransform)transform;
            _dimRt.anchorMin = _dimRt.anchorMax = _dimRt.pivot = new Vector2(0, 1);
            _dimRt.anchoredPosition = Vector2.zero; _dimRt.sizeDelta = new Vector2(Game2DLayout.Width, Game2DLayout.Height);
            _dimImg = gameObject.AddComponent<RawImage>(); _dimImg.raycastTarget = false; _dimImg.enabled = false;
            // 板: 幕の子 (幕より手前に描く)
            var win = new GameObject("Window", typeof(RectTransform)); win.layer = 5;
            _rt = (RectTransform)win.transform; _rt.SetParent(transform, false);
            _rt.anchorMin = _rt.anchorMax = _rt.pivot = new Vector2(0, 1); _rt.sizeDelta = new Vector2(1, 1);
            _img = win.AddComponent<RawImage>(); _img.raycastTarget = false; _img.enabled = false;
#if UNITY_EDITOR
            foreach (var tp in Table) if (Game2DTutorialText.Find(tp.title) == null)
                Debug.LogWarning("[Game2DTutorial] 手引きの文に「" + tp.title + "」が無い (handbook の見出しを変えた? python Tools/sync_handbook.py を回して TutorialTopics を合わせる)");
#endif
        }

        private void OnDestroy()
        {
            if (_inst == this) _inst = null;
            if (_tex != null) Destroy(_tex);
            if (_dimTex != null) Destroy(_dimTex);
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
            if (_cur != null) { HandleInput(); }
            if (_cur != null) { Track(); Draw(); }
            SyncInput();
        }

        // ── 状態を見て頼む節 ──

        private void Watch(GameManager gm)
        {
            var ph = gm.CurrentPhase;
            if (ph == GameManager.GamePhase.Title)
            {
                Request(TutorialTopics.RunAndReturn);
                if (ClearedThirdLayerEver()) Request(TutorialTopics.Challenge);      // 「挑戦」: 第三層をクリアした記録がある (前の起動のクリアも拾う)
            }
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
            if (run != null) WatchRun(run, ph);
            if (Game2DBattleSim.Active)
            {
                var ctx = ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context;
                if (ctx != null)
                {
                    if (ctx.EnemyPoison > 0) Request(TutorialTopics.Poison);
                    if (ctx.enemyBleedStacks > 0) Request(TutorialTopics.Bleed);
                    if (ctx.GetCharge() > 0) Request(TutorialTopics.Charge);
                    if (ctx.rinkaiMeter > 0) Request(TutorialTopics.Rinkai);
                    if (ctx.firedRolesThisTurn.Count > 0) Request(TutorialTopics.Role);      // 「役」: 初めて役が成立した (CombatManager.ResolveRoles が積む・次のターンの頭で空になる)
                }
            }
        }

        // ラン中の増減を見る節 (物資・持ち物) の前の値。 ランが替わったら (新しいラン・続きから) 取り直す。
        private GameLoop.RunState _watchRun; private int _lastProv, _lastItems;

        /// <summary>ランの状態を見て頼む節: 前哨基地 / 出目パーツ / 物資 / 持ち物。</summary>
        private void WatchRun(GameLoop.RunState run, GameManager.GamePhase ph)
        {
            int items = (run.ownedPassiveItems != null ? run.ownedPassiveItems.Count : 0) + (run.ownedConsumables != null ? run.ownedConsumables.Count : 0);
            if (run != _watchRun) { _watchRun = run; _lastProv = run.provision; _lastItems = items; }
            // 「前哨基地」: 回復と補給のある層 (ファーム層の次から・GameManager.EnterFloor) の前哨基地に立っている
            if (ph == GameManager.GamePhase.MapNavigation && !MapSystem.FreeMove.FloorPlan.IsFarm(run.Depth)
                && MapSystem.MapManager.Instance != null && MapSystem.MapManager.Instance.CurrentNode != null
                && MapSystem.MapManager.Instance.CurrentNode.type == MapSystem.TileType.Outpost)
                Request(TutorialTopics.Outpost);
            // 「挑戦」: このランで第三層のボスを倒した (〈決意〉で先へ進むランは、 エンドの記録が付かないのでここで頼む)。 出すのはタイトルに戻ってから (Scene.Title)
            if (run.Depth > run.normalClearFloor || (run.Depth == run.normalClearFloor && run.bossDefeatedThisFloor))
                Request(TutorialTopics.Challenge);
            // 「物資」: 地図の上で物資が減った (歩いた・照明を灯した)
            if (run.provision != _lastProv)
            {
                if (run.provision < _lastProv && ph == GameManager.GamePhase.MapNavigation) Request(TutorialTopics.Provision);
                _lastProv = run.provision;
            }
            // 「持ち物」: ランの途中で品 (パッシブ・消耗品) が増えた ── 開始時に配られた分は数えない
            if (items != _lastItems)
            {
                if (items > _lastItems) Request(TutorialTopics.Inventory);
                _lastItems = items;
            }
            // 「出目パーツ」: 素の面 (DiceFaceParts.StartingParts) に無い面を持っている = 出目パーツを手にした
            if (run.diceFaceParts != null && !TutorialSeen.Has(TutorialTopics.FacePart))
                foreach (var p in run.diceFaceParts)
                    if (p.tier != GameLoop.DiceFaceParts.Tier.T1 || System.Array.IndexOf(GameLoop.DiceFaceParts.StartingFaces, p.face) < 0)
                    { Request(TutorialTopics.FacePart); break; }
        }

        /// <summary>第三層をクリアした記録がメタ進行にあるか: エンドの記録 (<see cref="Codex.VignetteUnlockState.OnRunClear"/>) か、 挑戦の踏破の記録。</summary>
        private static bool ClearedThirdLayerEver()
        {
            var st = MetaProgression.MetaProgressManager.Instance?.State;
            return st != null && ((st.endingClearKeys != null && st.endingClearKeys.Count > 0) || st.challengeBest > 0);
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
            if (!FocusReady(pick)) return;                                 // 強調する部品がばねで入ってくる途中なら、 止まるまで待つ
            _queue.Remove(pick);
            Show(pick);
        }

        private void Show(string title)
        {
            _entry = Game2DTutorialText.Find(title); if (_entry == null) return;
            _cur = title; _page = 0; _t = 0f; _hoverBtn = -1; _dirty = true;
            _focus = FocusRect(title);
            Layout();
            _dimImg.enabled = true;
            transform.SetAsLastSibling();
            Game2DSfx.Play("ui_open", 0.8f);
        }

        /// <summary>下げる (印は付けない)。</summary>
        private void Hide() { _cur = null; _entry = null; _img.enabled = false; _dimImg.enabled = false; _closeFrame = Time.frameCount; }

        // ── 強調 (issue #109) ──
        //   節ごとに「見てほしい部品」を決め、 その RectTransform の四隅から 480×270 の格子での範囲を求める (座標は部品の側が持つ)。
        //   画面全体を幕で暗くし、 範囲の内側だけ幕を抜いて枠で囲む。 文面の板は範囲に重ならない所へ置く。 部品が無い節は幕だけ (板は中央)。

        /// <summary>強調する部品の種類。</summary>
        private enum Focus { None, NavTop, Radar, Intent, BattleTop, Ports, Stat, Ctl, Hand }

        /// <summary>節 → 強調する部品。 ここに無い節は強調しない (幕だけ)。</summary>
        private static Focus FocusOf(string title)
        {
            switch (title)
            {
                case TutorialTopics.WalkLayer: return Focus.NavTop;        // 手番・待つ の帯
                case TutorialTopics.Patrol: return Focus.Radar;            // 魔石
                case TutorialTopics.Telegraph: return Focus.Intent;        // 予告の枠
                case TutorialTopics.Plug: return Focus.Ports;              // 出目と差し込み口
                case TutorialTopics.Deck:
                case TutorialTopics.Kinds: return Focus.Hand;              // 手札の端子
                case TutorialTopics.Long:
                case TutorialTopics.Charge: return Focus.BattleTop;        // 充電・エスカレーション・敵 HP の帯
                case TutorialTopics.Crit:
                case TutorialTopics.ForceCrit: return Focus.Stat;          // 会心率
                case TutorialTopics.Flee: return Focus.Ctl;                // 逃げる のボタン
                default: return Focus.None;
            }
        }

        private static bool HasFocus(string title) => FocusOf(title) != Focus.None;

        /// <summary>節の強調に使う部品の RectTransform。 強調しない節・部品がまだ無い時は null。</summary>
        private RectTransform FocusSource(string title)
        {
            var f = FocusOf(title);
            if (f == Focus.None) return null;
            if (f == Focus.NavTop) return Game2DNavExtras.TopBar;
            if (f == Focus.Radar) { if (_radar == null) _radar = FindObjectOfType<Game2DRadar>(); return _radar != null ? (RectTransform)_radar.transform : null; }
            if (_hud == null) _hud = FindObjectOfType<Game2DBattleHud>();
            if (_hud == null) return null;
            switch (f)
            {
                case Focus.Intent: return _hud.PartRect(Game2DBattleHud.Part.Intent);
                case Focus.BattleTop: return _hud.PartRect(Game2DBattleHud.Part.Top);
                case Focus.Ports: return _hud.PartRect(Game2DBattleHud.Part.Ports);
                case Focus.Stat: return _hud.PartRect(Game2DBattleHud.Part.Stat);
                case Focus.Ctl: return _hud.PartRect(Game2DBattleHud.Part.Ctl);
                default: return _hud.PartRect(Game2DBattleHud.Part.Hand);
            }
        }

        private const int FocusPad = 2;

        /// <summary>部品の範囲 (格子の dot・左上が原点・余白 <see cref="FocusPad"/> 込み・画面の内側に切る)。 部品が無い・隠れている・ほとんど画面の外なら null。</summary>
        private RectInt? FocusRect(string title)
        {
            var src = FocusSource(title);
            if (src == null || !src.gameObject.activeInHierarchy) return null;
            var grid = transform.parent as RectTransform; if (grid == null) return null;
            var c = new Vector3[4]; src.GetWorldCorners(c);
            float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
            foreach (var w in c)
            {
                var l = grid.InverseTransformPoint(w);                                // 格子の原点 = 左上 (pivot 0,1)・y は上向き
                x0 = Mathf.Min(x0, l.x); x1 = Mathf.Max(x1, l.x); y0 = Mathf.Min(y0, -l.y); y1 = Mathf.Max(y1, -l.y);
            }
            int ix0 = Mathf.FloorToInt(x0) - FocusPad, iy0 = Mathf.FloorToInt(y0) - FocusPad, ix1 = Mathf.CeilToInt(x1) + FocusPad, iy1 = Mathf.CeilToInt(y1) + FocusPad;
            float full = (ix1 - ix0) * (float)(iy1 - iy0);
            ix0 = Mathf.Max(0, ix0); iy0 = Mathf.Max(0, iy0); ix1 = Mathf.Min(Game2DLayout.Width, ix1); iy1 = Mathf.Min(Game2DLayout.Height, iy1);
            if (ix1 - ix0 < 4 || iy1 - iy0 < 4 || (ix1 - ix0) * (float)(iy1 - iy0) < full * 0.8f) return null;      // 8 割が画面の外 = まだ入ってきていない
            return new RectInt(ix0, iy0, ix1 - ix0, iy1 - iy0);
        }

        /// <summary>強調する部品が 3 フレーム続けて同じ所にある (ばねが止まった) か。 1 秒待っても止まらなければ出す。 部品の無い節はすぐ true。</summary>
        private bool FocusReady(string title)
        {
            if (!HasFocus(title)) return true;
            var r = FocusRect(title);
            if (_probe != title) { _probe = title; _probeRect = r; _stable = 0; _probeT = 0f; return false; }
            _probeT += Time.unscaledDeltaTime;
            if (r.HasValue && _probeRect.HasValue && r.Value.Equals(_probeRect.Value)) _stable++; else { _stable = 0; _probeRect = r; }
            if (_stable >= 3 || _probeT > 1f) { _probe = null; return true; }
            return false;
        }

        /// <summary>出している間、 部品の範囲を読み直す (ばね・画面の出入りで動く)。 変わったら板を置き直し、 幕を描き直す。</summary>
        private void Track()
        {
            var r = FocusRect(_cur);
            if (r.HasValue == _focus.HasValue && (!r.HasValue || r.Value.Equals(_focus.Value))) return;
            _focus = r; Layout(); _dirty = true;
        }

        /// <summary>板の大きさと位置を決める。 範囲の上か下に入ればそこ (空きの広い側から)・入らなければ左右の空き (幅を詰めて折り返し直す)・
        /// どこにも入らなければ空きの広い側に寄せる。 範囲が無ければ中央。</summary>
        private void Layout()
        {
            const int Gap = 6, MinSideW = 220, Edge = 2;
            int SW = Game2DLayout.Width, SH = Game2DLayout.Height;
            _winW = W; Paginate();
            _px = (SW - _w) / 2; _py = (SH - _h) / 2;
            if (_focus.HasValue)
            {
                var f = _focus.Value;
                int above = f.y - Gap - Edge, below = SH - f.yMax - Gap - Edge;
                bool placed = false;
                foreach (bool down in below >= above ? new[] { true, false } : new[] { false, true })
                {
                    int room = down ? below : above;
                    if (room < _h) continue;
                    _py = down ? f.yMax + Gap + (room - _h) / 2 : Edge + (room - _h) / 2;
                    placed = true; break;
                }
                if (!placed)
                {
                    int left = f.x - Gap - Edge, right = SW - f.xMax - Gap - Edge;
                    int room = Mathf.Max(left, right);
                    if (room >= MinSideW)
                    {
                        _winW = Mathf.Min(W, room); Paginate();
                        _px = right >= left ? f.xMax + Gap + (room - _w) / 2 : Edge + (room - _w) / 2;
                        _py = Mathf.Clamp(f.y + (f.height - _h) / 2, Edge, SH - Edge - _h);
                    }
                    else _py = below >= above ? SH - Edge - _h : Edge;          // どこにも入らない: 空きの広い側の端へ (少し重なる)
                }
            }
            if (_tex == null || _tex.width != _w || _tex.height != _h)
            {
                if (_tex != null) Destroy(_tex);
                _tex = new Texture2D(_w, _h, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DTutorial" };
            }
            _img.texture = _tex; _img.enabled = true;
            _rt.sizeDelta = new Vector2(_w, _h); _rt.anchoredPosition = new Vector2(_px, -_py);
            _page = Mathf.Clamp(_page, 0, _pages.Count - 1);
            DrawDim();
        }

        /// <summary>幕の色 (暗さ) と枠の色。 枠は控えめな象牙色 (原色にしない)、 外側に 1 dot の暗い縁を付けて地図の上でも読めるように。</summary>
        private static readonly Color32 DimCol = new Color32(0x05, 0x04, 0x03, 168), FrameCol = Game2DKit.Ivory, FrameShadow = new Color32(0x05, 0x03, 0x02, 230);

        private void DrawDim()
        {
            int SW = Game2DLayout.Width, SH = Game2DLayout.Height;
            if (_dimTex == null)
            {
                _dimTex = new Texture2D(SW, SH, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, name = "Game2DTutorialDim" };
                _dimImg.texture = _dimTex;
            }
            var p = new KitPix(SW, SH);
            p.Rect(0, 0, SW, SH, DimCol);
            if (_focus.HasValue)
            {
                var f = _focus.Value;
                p.Rect(f.x - 1, f.y - 1, f.width + 2, f.height + 2, FrameShadow);   // 外の暗い縁
                p.Rect(f.x, f.y, f.width, f.height, FrameCol);                      // 枠
                p.Rect(f.x + 1, f.y + 1, f.width - 2, f.height - 2, default);       // 内側は抜く (暗くしない)
            }
            p.Apply(_dimTex);
        }

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
        //   段落 (\n 区切り・handbook の空行の区切り) 1 つが 1 ページ (issue #110: 1 ページで覚えることを 1 つに)。 折り返して MaxLines 行を超える段落だけ途中で次のページへ送る。
        //   板の高さは全ページで一番高いページに合わせる (ページ送りで板の大きさが変わらない)。

        private int BodyW => _winW - Pad * 2 - Hang;

        private void Paginate()
        {
            _pages.Clear();
            int LH = Game2DJpFont.LineH, cap = MaxLines * LH;
            var cur = new Page(); int y = 0;
            foreach (var para in _entry.body.Split('\n'))
            {
                var lines = WrapEm(para, BodyW);
                if (lines.Count == 0) continue;
                if (cur.lines.Count > 0) { _pages.Add(cur); cur = new Page(); y = 0; }      // 段落ごとに改ページ
                foreach (var ln in lines)
                {
                    if (y + LH > cap && cur.lines.Count > 0) { _pages.Add(cur); cur = new Page(); y = 0; }
                    var l = ln; l.y = y; cur.lines.Add(l); y += LH; cur.h = y;
                }
            }
            if (cur.lines.Count > 0 || _pages.Count == 0) _pages.Add(cur);
            int maxH = LH; foreach (var p in _pages) maxH = Mathf.Max(maxH, p.h);
            _w = _winW; _h = Mathf.Min(Game2DLayout.Height - 4, BodyY + maxH + 8 + BtnH + Pad);
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
            bool entering = _t < DimIn; _t += dt;
            _dimImg.color = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(_t / DimIn)));      // 幕はゆっくり降りる (板は網目で入る)
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
