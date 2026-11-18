using System.Collections.Generic;
using CombatSystem;
using GameLoop;
using UnityEngine;
using UI.Game2D.Battle;

namespace UI.Game2D
{
    /// <summary>
    /// 液晶の中に描く戦闘の画面 (2026-11-16・大規模改修の第 1 段・ブランチ battle-ui-lcd)。
    /// 旧画面 (Game2DBattleHud: 液晶の外の層に浮くパネル群 + 筐体の下の HP 帯・2026-11-16 に削除・GAME.md §24) をやめ、 戦闘の表示と操作を<b>液晶の中の 1 枚の絵</b>として描く
    /// (演出 <see cref="Game2DBattleFx"/> の作図面 480×270 へ、 敵・背景・演出と同じ dot で打つ ── 走査線などの液晶の効果も同じく掛かる)。
    /// 見本は Captures/ui_layout/scene/still.png・tip.png・turn.gif (座標の正本は <see cref="BattleLcdLayout"/>)。
    ///
    /// <para><b>作者が決めた配置</b>: 板・枠・帯で囲った領域を作らず、 関係のある物のそばに置く。 狙いを結ぶ線と自機の絵は無し。
    /// 敵の次の手と激化 = 敵の頭上 (<see cref="DrawHead"/>)。 敵の HP・ダウンの目盛り・この手で削れる分 = 敵の足もとの棒 (<see cref="DrawFoot"/>)。
    /// 自分の HP・食われる分・守りで覆った分・シールド・充電の玉・振り直し = 左下。 「決める」= 右下 (字と下線だけ)。 山と捨て札の枚数 = 右上。
    /// 手札 = 下に並ぶカード (名前の帯・絵の窓・左下の出目の差し口)。 出目は振ると床に転がり、 つかんでカードへ運んで挿す。 挿したカードは少し持ち上がる。
    /// 出目をつかんでいる間は、 挿せないカードが暗くなる。 「残す」の印はカードの上。 カードに触ると全文の帯。
    /// 解決の間 (操作できない間) は手札が下へ引っ込み、 敵と HP の棒だけが残る。 敵の出目・敵の防御・ターンの数・激化の段の数字は常設しない (触った時の帯に出す)。</para>
    ///
    /// <para><b>仮置き</b> (作者が未決・情報と操作を落とさない最小の形): カードの枠 (見本と同じ単純な枠・<see cref="DrawCardFrame"/>)・カードの絵
    /// (絵が無い札は端子のマーク・<see cref="CardArt"/> が名前で引く口)・7 枚の時の並び (送りを詰めて重ねる)・2 個挿せる札 (差し口を 2 つ)・
    /// 役 (床の出目の上に字・役の出目に金の縁)・充電の支払い / 使い捨て / 常設の印 (帯の中だけ)・止まった札 (暗く + 横線 + 「停止」)・
    /// ボスの HP が大きい時 (棒は割合のまま)・出目が 4 個以上の時の床 (3 個ずつ上の段へ)・おまかせ / 道具 / 逃げる (右の列の字)・[持ち物] (左上の字)・
    /// 自分のステータス (会心・攻撃・防御・貫通 = 左下の HP に触った時の帯)・削れる分と食われる分の見込み (下の注意)。</para>
    ///
    /// <para><b>見込みの数 (仮)</b>: 本体に「この挿し方でいくら削れるか / いくら食われるか」を返す関数が無い。 ここで式を作らない (DAMAGE_CALC_REFERENCE が正本) ので、
    /// 削れる分 = 端子の見込みの攻撃値 (<see cref="Game2DBattleSim.GetWired"/> の atk・敵の防御・会心・倍率の前)、
    /// 食われる分 = 予告の攻撃値 (ブロックの前・防御の後) + 〈業火〉の烈炎、 覆った分 = 見込みのブロック + シールド (旧画面の資源バーの「守り」と同じ値) を出す。
    /// 本当の見込みは本体に関数を足してから差し替える。</para>
    ///
    /// <para>戦闘中は筐体の下の HP 帯とレーダーを常に隠す (<see cref="Game2DHud"/>)。 道具・〈有効活用〉・持ち物の窓は <see cref="BattleWindows"/>。
    /// BOT がランを動かしている間は戦闘の流れ (<see cref="Game2DBattleSim"/>) が動かないので、 この画面も出ない。</para>
    /// </summary>
    public class Game2DBattleLcd : MonoBehaviour
    {
        public static Game2DBattleLcd Instance { get; private set; }
        /// <summary>新しい画面が戦闘を受け持っているか (戦闘の流れが動いている)。</summary>
        public static bool Drawing => Instance != null && Game2DBattleSim.Active;

        /// <summary>手引きの強調 (issue #109) が範囲を読む部品。</summary>
        public enum Part { Intent, Gauge, Ports, Stat, Ctl, Hand }

        private Game2DBattleSim _sim; private Game2DBattleFx _fx; private BattleWindows _win; private Game2DScreen _screen;

        // ── 色 (見本の ui_scene_mock.py の値) ──
        private static readonly Color32 Bone = new Color32(242, 236, 216, 255), Gold = new Color32(244, 200, 92, 255), Gold2 = new Color32(255, 232, 160, 255),
            DimC = new Color32(150, 140, 128, 255), Edge = new Color32(10, 8, 10, 255), RedC = new Color32(228, 88, 64, 255), RedHi = new Color32(248, 140, 110, 255),
            BlueC = new Color32(110, 170, 236, 255), GreenC = new Color32(104, 170, 108, 255), GreenHi = new Color32(160, 214, 160, 255), Chg = new Color32(232, 190, 80, 255),
            ChgDark = new Color32(92, 70, 24, 255), BarEmpty = new Color32(38, 30, 30, 255), CardBg = new Color32(26, 22, 24, 255), ArtBg = new Color32(20, 17, 19, 255),
            SockBg = new Color32(14, 12, 13, 255), TipBg = new Color32(30, 26, 28, 255), Heavy = new Color32(0xff, 0xb3, 0x47, 255), Faint = new Color32(80, 60, 56, 255),
            NotchDone = new Color32(0x6a, 0x5a, 0x48, 255), Amber = Game2DKit.Amber;
        private static readonly Color32 CandEdge = Game2DTerminalLook.Mix(Game2DKit.Ivory, Game2DKit.Off, 0.45f), ChosenEdge = Game2DKit.Ivory,
            KeepMark = Game2DTerminalLook.Mix(Game2DKit.Amber, Game2DKit.Ivory, 0.3f), StopMark = Game2DTerminalLook.Mix(Game2DKit.Blue, Game2DKit.Ivory, 0.45f);

        // ── 状態 ──
        private Game2DSpring _spHand, _spStow, _spExtra; private bool _spInit;
        /// <summary>今のコマで描く ずれ (dot・ばねの値を 1 コマ <see cref="BattleLcdLayout.MaxStepPerFrame"/> までで追う): 手札 (下へ) / 左の列 (左へ) / 右の列 (右へ) / 上の行 (上へ) / 「決める」(右へ)。</summary>
        private int _handOff = BattleLcdLayout.HandHide, _leftOff, _rightOff, _topOff, _goOff;
        private const int LeftHide = 100, RightHide = 70, TopHide = 30;
        private Vector2 _mouse; private bool _hasMouse;
        private int _hoverCard = -1, _hoverRelic = -1; private Zone _hoverZone;
        private int _dragDie = -1, _pressDie = -1, _pressCard = -1, _dragCard = -1; private bool _moved; private Vector2 _pressDot;
        private bool _fleeArmed;
        private CombatManager.HudStats _hs; private bool _hsKnown;
        private static int _releaseFrame = -10;
        private enum Zone { None, Head, Hp, Orb, Deck }

        /// <summary>出目・カードをつかんでいる間と、 離したフレーム (の次まで) は true (敵の詳細の窓 Game2DEnemyPanel を開かない)。</summary>
        public static bool PointerBusy => Instance != null && (Instance._dragDie >= 0 || Instance._dragCard >= 0 || Instance._pressDie >= 0 || Instance._pressCard >= 0 || Time.frameCount <= _releaseFrame + 1);

        private void OnEnable() { Instance = this; }
        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            if (_relicHooked) { ItemSystem.PassiveSkills.RelicFireProbe.OnRelicFired -= OnRelicFired; _relicHooked = false; }
        }

        private void Bind()
        {
            if (_sim == null) _sim = GetComponent<Game2DBattleSim>();
            if (_fx == null && _sim != null) _fx = _sim.fx;
            if (_win == null && _sim != null) _win = new BattleWindows(_sim, null);
            if (_screen == null) _screen = FindObjectOfType<Game2DScreen>();
        }

        // =====================================================================
        //  操作 (Update)
        // =====================================================================

        private void Update()
        {
            Bind();
            float dt = Mathf.Min(0.05f, Time.unscaledDeltaTime);
            if (!_spInit) { _spHand.Set(1f); _spStow.Set(0f); _spExtra.Set(1f); _spInit = true; }
            bool live = Drawing && _sim != null && _fx != null;
            bool handOn = live && !_fx.IsIntro && (_sim.phase == Game2DBattleSim.P.PRoll || _sim.phase == Game2DBattleSim.P.Wire);
            // 解決の間 (作者の決定「手札が引っ込み、 敵と HP の棒だけが残る」): 手札・床の出目に加えて、 左の列 (充電の玉・振り直す・シールド・補正)・右の列・上の行 (秘宝・持ち物・山と捨て札) も引っ込める。
            //   「決める」は 次へ / 終わる の間も出す (大技の格納の間だけ引っ込む)。 2026-11-16・実機確認で勝った後に玉が残った
            _spHand.Go(handOn ? 0f : 1f, handOn ? 0.05f : 0f); _spStow.Go(live && _fx.GuiStowed ? 1f : 0f, 0f); _spExtra.Go(handOn ? 0f : 1f, handOn ? 0.05f : 0f);
            _spHand.Step(dt); _spStow.Step(dt); _spExtra.Step(dt);
            float ex = Mathf.Max(_spStow.Value(1f, 0f), _spExtra.Value(1f, 0f));
            _handOff = StepTo(_handOff, Mathf.RoundToInt(_spHand.Value(1f, 0f) * BattleLcdLayout.HandHide));
            _leftOff = StepTo(_leftOff, Mathf.RoundToInt(ex * LeftHide)); _rightOff = StepTo(_rightOff, Mathf.RoundToInt(ex * RightHide));
            _topOff = StepTo(_topOff, Mathf.RoundToInt(ex * TopHide)); _goOff = StepTo(_goOff, Mathf.RoundToInt(_spStow.Value(1f, 0f) * RightHide));
            if (!live) { CancelAll(); return; }
            // 自分のステータスは配線を決める間だけ読み直す (その後は本判定に取り込まれて二重に数えるので、 最後の値を出し続ける・旧画面と同じ)
            if (_sim.CanOperate && CombatManager.Instance != null) { _hs = CombatManager.Instance.PreviewHudStats(); _hsKnown = true; }
            if (!_sim.CanFlee) _fleeArmed = false;
            RefreshRelics();

            bool ready = !Stowed && !Game2DModal.Active && _screen != null;
            _hasMouse = ready && Game2DPointer.TryLcdDot(_screen, out _mouse);      // 湾曲を戻した液晶の dot (2026-11-16〜 変換は TryLcdDot の中・後掛けはしない)
            // 持ち物の画面 (見るだけ): キー I。 窓が出ている間・つかんでいる間は開かない (旧画面と同じ条件)
            if (ready && !PointerBusy && !Game2DTutorial.Blocking && Input.GetKeyDown(KeyCode.I)) OpenInventory();
            if (!_hasMouse) { _hoverCard = _hoverRelic = -1; _hoverZone = Zone.None; if (!ready) CancelAll(); return; }
            if ((_pressDie >= 0 || _pressCard >= 0) && !Input.GetMouseButton(0) && !Input.GetMouseButtonUp(0)) { CancelAll(); return; }      // 画面の外で離した
            Pointer(_mouse);
        }

        private bool Stowed => _spStow.tgt > 0.5f || _spStow.x > 0.02f;
        /// <summary>大技の格納のばねの値 (0 = 出ている / 1 = 格納・QA の口 QaBattle が読む)。</summary>
        public float StowValue => _spStow.x;
        private bool HandUp => _handOff == 0 && _spHand.tgt < 0.5f;
        /// <summary>左右・上の列が出ているか (押せるか・触った時の帯を出してよいか)。</summary>
        private bool ExtrasUp => _leftOff == 0 && _rightOff == 0 && _topOff == 0;
        private static int StepTo(int cur, int target) => cur + Mathf.Clamp(target - cur, -BattleLcdLayout.MaxStepPerFrame, BattleLcdLayout.MaxStepPerFrame);


        private void CancelAll()
        {
            if (_dragDie >= 0 || _dragCard >= 0 || _pressDie >= 0 || _pressCard >= 0) _releaseFrame = Time.frameCount;
            _dragDie = _pressDie = _pressCard = _dragCard = -1; _moved = false;
        }

        private void Pointer(Vector2 d)
        {
            var sim = _sim;
            // つかんでいる間
            if (_pressDie >= 0 || _pressCard >= 0)
            {
                if (!sim.CanOperate || !HandUp) { CancelAll(); return; }
                if (!_moved && (d - _pressDot).sqrMagnitude > 9f)
                {
                    _moved = true;
                    if (_pressDie >= 0) _dragDie = _pressDie;
                    else if (_pressCard >= 0 && sim.IsTargetCandidate(_pressCard, out _)) _dragCard = _pressCard;      // カードを動かすのは対象を選ぶ時だけ
                }
                _hoverCard = CardAt(d, _dragDie >= 0 ? -1 : _pressCard);
                if (Input.GetMouseButtonUp(0)) Release(d);
                return;
            }
            _hoverCard = HandUp ? CardAt(d, -1) : -1;
            _hoverRelic = ExtrasUp ? RelicAt(d) : -1;
            _hoverZone = In(d, HeadRect()) ? Zone.Head : In(d, OrbRect()) || In(d, RerollRect()) ? Zone.Orb : In(d, HpRect()) ? Zone.Hp : In(d, DeckRect()) ? Zone.Deck : Zone.None;
            if (Input.GetMouseButtonDown(0))
            {
                int die = HandUp ? DieAt(d) : -1;
                if (die >= 0)
                {
                    if (sim.CanOperate && !sim.IsCatLocked(die)) { _pressDie = die; _pressDot = d; _moved = false; }
                    else Game2DSfx.Play("ui_click");
                    return;
                }
                if (_hoverCard >= 0) { if (sim.CanOperate) { _pressCard = _hoverCard; _pressDot = d; _moved = false; } return; }
                if (ExtrasUp && In(d, InvRect())) { OpenInventory(); return; }
                if (ExtrasUp && sim.CanOperate && (In(d, OrbRect()) || In(d, RerollRect()))) { if (sim.CanRerollNow) sim.Reroll(); else Game2DSfx.Play("ui_click"); return; }
                if (ExtrasUp && sim.CanOperate)
                {
                    if (In(d, SideRect(0))) { sim.AutoWire(); return; }
                    if (In(d, SideRect(1))) { if (_win != null) _win.OpenItems(); return; }
                    if (In(d, SideRect(2))) { PressFlee(); return; }
                }
                if (GoVisible && In(d, GoRect())) { if (sim.ButtonEnabled) { _fleeArmed = false; sim.Button(); } return; }
            }
            else if (Input.GetMouseButtonDown(1) && sim.CanOperate && HandUp)
            {
                int die = DieAt(d);
                if (die >= 0) { sim.CycleWire(die, -1); return; }
                if (_hoverCard >= 0) { for (int i = 0; i < sim.slot.Length; i++) if (sim.slot[i] == _hoverCard) sim.Unplug(i); }      // 札を右クリック = 挿した出目を床へ戻す
            }
        }

        private void Release(Vector2 d)
        {
            var sim = _sim; int die = _pressDie, card = _pressCard, dragDie = _dragDie, dragCard = _dragCard; bool moved = _moved;
            _pressDie = _pressCard = _dragDie = _dragCard = -1; _moved = false; _releaseFrame = Time.frameCount;
            if (!moved)
            {
                // 押しただけ: 出目 = 行き先を順に変える (1〜9 キーと同じ) / 札 = 〈有効活用〉なら戻す 1 枚の窓・それ以外は「残す」の印の付け外し
                if (die >= 0)
                {
                    // 刻みの賽 (ADR-0015): Shift を押しながら押すと、 刻む印の付け外し
                    if ((Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) && sim.ToggleCarve(die)) return;
                    sim.CycleWire(die, 1);
                }
                else if (card >= 0)
                {
                    if (sim.IsRecaller(card)) { if (_win != null) _win.OpenRecall(card); }
                    else if (sim.CanKeep(card)) sim.ToggleKeep(card);
                    else Game2DSfx.Play("ui_click");
                }
                return;
            }
            int over = CardAt(d, dragCard);
            if (dragDie >= 0)
            {
                if (over >= 0)
                {
                    if (sim.slot[dragDie] == over) return;
                    bool wasRecall = sim.IsRecaller(over);
                    if (sim.PlugDie(dragDie, over)) { if (!wasRecall && sim.IsRecaller(over) && _win != null) _win.OpenRecall(over); }
                    else Game2DSfx.Play("ui_click");      // 挿せない札: 出目は元の所に残る
                }
                else if (sim.slot[dragDie] >= 0) sim.Unplug(dragDie);      // 札の外で離した = 床へ戻す
                return;
            }
            if (dragCard >= 0 && over >= 0 && !sim.SetTarget(over, dragCard)) Game2DSfx.Play("ui_click");      // 候補の札を、 対象を取る札の上へ落とす = 対象に決める
        }

        /// <summary>逃げるは 2 度押しで確定 (旧画面と同じ)。</summary>
        private void PressFlee()
        {
            if (_fleeArmed) { _fleeArmed = false; if (!_sim.Flee()) Game2DSfx.Play("ui_click"); }
            else if (_sim.CanFlee) _fleeArmed = true;
            else Game2DSfx.Play("ui_click");
        }

        private void OpenInventory()
        {
            if (_win != null) _win.OpenInventory();
        }

        // =====================================================================
        //  場所 (描くのと当たり判定で同じ関数を使う)
        // =====================================================================

        private static bool In(Vector2 d, RectInt r) => d.x >= r.x && d.x < r.x + r.width && d.y >= r.y && d.y < r.y + r.height;
        private int HandOff => _handOff;
        private int LeftOff => -_leftOff;
        private int RightOff => _rightOff;
        private int TopOff => -_topOff;

        private bool HasDie(int s) { for (int i = 0; i < _sim.slot.Length; i++) if (_sim.slot[i] == s && i != _dragDie) return true; return false; }

        /// <summary>カード s の範囲 (持ち上げ・引っ込めを含む)。</summary>
        private RectInt CardRect(int s)
        {
            BattleLcdLayout.CardPos(s, _sim.board.Count, out int x, out int arc);
            int y = BattleLcdLayout.HandY + arc + HandOff - (HasDie(s) ? BattleLcdLayout.Lift : 0);
            return new RectInt(x, y, BattleLcdLayout.CardW, BattleLcdLayout.CardH);
        }

        private static RectInt SockRect(RectInt card, int k)
            => new RectInt(card.x + BattleLcdLayout.SockX + k * BattleLcdLayout.SockStep, card.y + BattleLcdLayout.SockY, BattleLcdLayout.SockSize, BattleLcdLayout.SockSize);

        /// <summary>d の下のカード (重なっている時は右の札が手前)。 except = つかんでいる札。</summary>
        private int CardAt(Vector2 d, int except)
        {
            for (int s = _sim.board.Count - 1; s >= 0; s--) if (s != except && In(d, CardRect(s))) return s;
            return -1;
        }

        /// <summary>出目 i の今の場所 (床 か 挿した札の差し口の中)。</summary>
        private RectInt DieRect(int i)
        {
            int s = i < _sim.slot.Length ? _sim.slot[i] : -1;
            if (s >= 0 && s < _sim.board.Count)
            {
                int k = 0; for (int j = 0; j < i; j++) if (_sim.slot[j] == s) k++;
                var sr = SockRect(CardRect(s), k);
                return new RectInt(sr.x + 1, sr.y + 1, Game2DDiceRoll.Face, Game2DDiceRoll.Face);
            }
            BattleLcdLayout.FloorPos(i, out int x, out int y);
            return new RectInt(x, y, Game2DDiceRoll.Face, Game2DDiceRoll.Face);
        }

        private int DieAt(Vector2 d)
        {
            for (int i = _sim.DiceCount - 1; i >= 0; i--) if (In(d, DieRect(i))) return i;
            return -1;
        }

        private RectInt OrbRect() => new RectInt(BattleLcdLayout.OrbX - BattleLcdLayout.OrbR + LeftOff, BattleLcdLayout.OrbY - BattleLcdLayout.OrbR, BattleLcdLayout.OrbR * 2 + 1, BattleLcdLayout.OrbR * 2 + 1);
        private string RerollLabel => _sim.canReroll ? "振り直す -" + _sim.rerollCost : "振り直す";
        private RectInt RerollRect() => new RectInt(BattleLcdLayout.RerollTextX + LeftOff, BattleLcdLayout.OrbY - 7, Game2DJpFont.Width(RerollLabel) + 2, Game2DJpFont.Cell + 2);
        private static RectInt HpRect() => new RectInt(BattleLcdLayout.MBarX0 - 2, BattleLcdLayout.MNumY - 2, BattleLcdLayout.MBarX1 - BattleLcdLayout.MBarX0 + 4, BattleLcdLayout.MBarY + 7 - BattleLcdLayout.MNumY + 2);
        private RectInt DeckRect() { string s = DeckText(); int w = Game2DJpFont.Width(s); return new RectInt(BattleLcdLayout.DeckRight - w + RightOff, BattleLcdLayout.DeckY + TopOff, w, Game2DJpFont.Cell); }
        private RectInt InvRect() => new RectInt(BattleLcdLayout.InvX + LeftOff, BattleLcdLayout.InvY + TopOff, Game2DJpFont.Width(BattleHudLayout.InvLabel) + 2, Game2DJpFont.Cell + 2);
        private RectInt HeadRect() => new RectInt(BattleMotion.EX - 70, BattleMotion.FloorY + BattleLcdLayout.HeadNextDy - 2, 140, -BattleLcdLayout.HeadNextDy + BattleLcdLayout.HeadIntentDy + Game2DJpFont.Cell + 4);

        private static readonly string[] SideLabels = { "おまかせ", "道具", "逃げる" };
        private string SideLabel(int k) => k == 2 && _fleeArmed ? "本当に?" : SideLabels[k];
        private RectInt SideRect(int k)
        {
            int w = Game2DJpFont.Width(SideLabel(k)) + 2;
            return new RectInt(BattleLcdLayout.SideRight - w + RightOff, BattleLcdLayout.SideY + k * BattleLcdLayout.SideStep, w, Game2DJpFont.Cell + 2);
        }
        private bool GoVisible => _sim.phase == Game2DBattleSim.P.Wire || _sim.phase == Game2DBattleSim.P.Won || _sim.phase == Game2DBattleSim.P.Lost;
        private RectInt GoRect() => new RectInt(BattleLcdLayout.GoCx - BattleLcdLayout.GoLineW / 2 + _goOff, BattleLcdLayout.GoY - 12, BattleLcdLayout.GoLineW, BattleLcdLayout.GoLineY - BattleLcdLayout.GoY + 15);

        private static string DeckText()
        {
            var dk = CombatManager.Instance?.TerminalDeck;
            return dk == null ? "" : "山 " + dk.draw.Count + "  捨 " + dk.discard.Count + (dk.exiled.Count > 0 ? "  外 " + dk.exiled.Count : "");
        }

        /// <summary>手引きの強調 (issue #109) の範囲 (格子の dot)。 部品が無ければ null。 escalation = 「戦闘の長期化」(激化は敵の頭上)。</summary>
        public RectInt? FocusRect(Part p, bool escalation)
        {
            if (!Drawing || _sim == null) return null;
            switch (p)
            {
                case Part.Intent: return HeadRect();
                case Part.Gauge: return escalation ? HeadRect() : new RectInt(BattleLcdLayout.LeftX, BattleLcdLayout.OrbY - BattleLcdLayout.OrbR - 2, 80, BattleLcdLayout.OrbR * 2 + 5);
                case Part.Ports:
                {
                    int n = Mathf.Max(1, _sim.DiceCount); int x0 = 999, y0 = 999, x1 = 0, y1 = 0;
                    for (int i = 0; i < n; i++) { BattleLcdLayout.FloorPos(i, out int x, out int y); x0 = Mathf.Min(x0, x); y0 = Mathf.Min(y0, y); x1 = Mathf.Max(x1, x + 18); y1 = Mathf.Max(y1, y + 18); }
                    return new RectInt(x0, y0, x1 - x0, y1 - y0);
                }
                case Part.Stat: return HpRect();
                case Part.Ctl: return new RectInt(BattleLcdLayout.SideRight - 52, BattleLcdLayout.SideY, 54, BattleLcdLayout.GoLineY - BattleLcdLayout.SideY + 2);
                default: return new RectInt(BattleLcdLayout.HandX0, BattleLcdLayout.HandY - BattleLcdLayout.Lift, BattleLcdLayout.HandX1 - BattleLcdLayout.HandX0, BattleLcdLayout.SafeY1 + 2 - BattleLcdLayout.HandY + BattleLcdLayout.Lift);
            }
        }

        // =====================================================================
        //  描く (Game2DBattleFx.Frame から呼ばれる・作図面へ直接打つ)
        // =====================================================================

        private BattleCanvas _c; private float _dark = 1f;

        private Col C(Color32 c) => _dark >= 0.999f ? new Col(c.r, c.g, c.b) : new Col(c.r * _dark, c.g * _dark, c.b * _dark);
        /// <summary>網点で残す割合 (1 = 全部打つ・0 = 何も打たない)。 敵の棒を、 撃破の演出で敵が網点で抜けるのと一緒に抜く (<see cref="DrawFoot"/>)。</summary>
        private float _keep = 1f;
        private bool Kept(int x, int y) => _keep >= 1f || BattleCanvas.Bayer[(y & 3) * 4 + (x & 3)] / 16f < _keep;
        private void Put(int x, int y, Col c) { if (Kept(x, y)) _c.Put(x, y, c); }
        private void Px(int x, int y, Color32 c) => Put(x, y, C(c));
        private void Rect(int x, int y, int w, int h, Color32 c)
        {
            if (w <= 0 || h <= 0) return;
            if (_keep >= 1f) { _c.Rect(x, y, w, h, C(c)); return; }
            var cc = C(c); for (int j = 0; j < h; j++) for (int i = 0; i < w; i++) Put(x + i, y + j, cc);
        }
        private void Box(int x, int y, int w, int h, Color32 fill, Color32 edge) { Rect(x, y, w, h, edge); Rect(x + 1, y + 1, w - 2, h - 2, fill); }

        /// <summary>ドット字 (既定のフォント・Game2DJpFont)。 edge = 周り 8 方向に暗い縁取り (背景の上で読めるように)。</summary>
        private void Txt(string s, int x, int y, Color32 col, bool edge = true)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (edge) { var e = C(Edge); Game2DJpFont.Draw((px, py) => { for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++) Put(px + dx, py + dy, e); }, s, x, y); }
            var cc = C(col); Game2DJpFont.Draw((px, py) => Put(px, py, cc), s, x, y);
        }
        private void TxtC(string s, int cx, int y, Color32 col, bool edge = true) => Txt(s, cx - Game2DJpFont.Width(s) / 2, y, col, edge);
        private void TxtR(string s, int right, int y, Color32 col, bool edge = true) => Txt(s, right - Game2DJpFont.Width(s), y, col, edge);

        /// <summary>上の行から並んだ絵 (Color32[]・w×h) を打つ。 透明 (α &lt; 128) は飛ばす。 k = 明るさ。</summary>
        private void Stamp(Color32[] img, int w, int h, int x, int y, float k = 1f)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var c = img[j * w + i]; if (c.a < 128) continue;
                    if (k != 1f) c = new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), 255);
                    Px(x + i, y + j, c);
                }
        }

        private void DrawDie(int x, int y, int v, float k = 1f) => Stamp(Game2DDiceRoll.FaceImg(v), Game2DDiceRoll.Face, Game2DDiceRoll.Face, x, y, k);

        private void Frame1(int x, int y, int w, int h, Color32 c)
        {
            Rect(x, y, w, 1, c); Rect(x, y + h - 1, w, 1, c); Rect(x, y, 1, h, c); Rect(x + w - 1, y, 1, h, c);
        }

        // ── 敵の頭上 ──

        /// <summary>敵の頭上 (Game2DBattleFx.DrawMarks の後・同じ間だけ呼ばれる): 予告の右に 〈業火〉の烈炎 と 激化の段の印・上の行に前触れ。
        /// ダウン・チャージのターン (予告の印が出ない) は予告の行に字で出す。 cx・feet = 敵の足もとの中心 (体の揺れ込み)。</summary>
        public void DrawHead(BattleCanvas c, int cx, int feet)
        {
            if (!Drawing || _sim == null || _fx == null) return;
            _c = c; _dark = _fx.LcdDark; _keep = 1f; var s = _sim; var m = _fx.marks;
            int yI = feet + BattleLcdLayout.HeadIntentDy, yN = feet + BattleLcdLayout.HeadNextDy, xr;
            if (m.intent > 0)
            {
                int tw = Game2DJpFont.Width(m.atk.ToString()), w = 12 + 3 + tw; xr = cx - w / 2 + w + 3;      // Game2DBattleFx.DrawMarks と同じ幅
                if (s.burst > 0) { string b = "+" + s.burst; Txt(b, xr, yI, Heavy); xr += Game2DJpFont.Width(b) + 3; }      // 〈業火〉の烈炎の分 (軽減無視)
            }
            else
            {
                // ダウン・チャージ・攻撃しない天与 (予告の印が無いターン): 字で
                string t = s.down ? (s.downWeak ? "ダウン(ひび) 攻撃なし" : "ダウン 攻撃なし")
                         : s.charging ? (s.tenyo > 0 ? "チャージ " + TenyoHeavy.NameOf((TenyoHeavy.Gift)s.tenyo) : "チャージ 攻撃なし")
                         : s.heavyIn == 0 && s.tenyo > 0 && s.eatk == 0 ? TenyoHeavy.NameOf((TenyoHeavy.Gift)s.tenyo) + " 攻撃なし" : null;
                if (t == null) { xr = cx + 4; }
                else { int w = Game2DJpFont.Width(t); Txt(t, cx - w / 2, yI, Heavy); xr = cx - w / 2 + w + 3; }
            }
            // 激化の段 (数字は出さない・小さな四角を 5 つ・点いている数が段)
            for (int i = 0; i < BattleLcdLayout.PipMax; i++)
            {
                int px = xr + i * BattleLcdLayout.PipStep, py = yI + 4;
                Rect(px - 1, py - 1, BattleLcdLayout.PipSize + 2, BattleLcdLayout.PipSize + 2, Edge);
                Rect(px, py, BattleLcdLayout.PipSize, BattleLcdLayout.PipSize, i < s.Stage ? RedC : Faint);
            }
            // 前触れ (次のターン)・ダウン中の倍率・ひび・目盛りまで・不安定 (旧画面の予告の枠の下の行)
            var parts = new List<string>(3); bool hot = false;
            if (s.downPending > 0) { parts.Add(s.downNextWeak ? "次 ダウン(ひび)" : "次 ダウン"); hot = true; }
            else if (s.guardIn == 1 && s.heavyIn == 1) { parts.Add("次 構え・大技"); hot = true; }
            else if (s.heavyIn == 1) { parts.Add("次 大技"); hot = true; }
            else if (s.guardIn == 1) parts.Add("次 構え");
            if (s.down && !s.downWeak) { parts.Add("被ダメ×" + BossDown.TakenMul.ToString("0.0")); hot = true; }
            else if (s.crackBreakTurn >= 0) { parts.Add(s.crackBreakTurn <= s.turn ? "ひび このターンで割れる" : "ひび あと" + (s.crackBreakTurn - s.turn + 1) + "ターン"); hot = true; }
            else if (s.downNotchHp >= 0 && s.downCracks != null && s.downBroken < s.downCracks.Length && s.downCracks[s.downBroken] >= 0) { parts.Add("ひび 通常攻撃で割れる"); hot = true; }
            if (parts.Count > 0)
            {
                string t = Game2DJpFont.Clip(string.Join("  ", parts), 220);
                TxtC(t, cx, yN, hot ? Heavy : DimC);
            }
        }

        // ── 敵の足もとの棒 ──

        /// <summary>敵の HP の棒 (旧 Game2DBattleFx.DrawEnemyHp の置き換え・新しい画面の間だけ): 棒・ダウンの目盛り・この手で削れる分 (金)・右に今の HP と −削れる分。
        /// 体の揺れでは動かない (EX・FloorY に固定)。 大技の暗幕に合わせて暗くする。</summary>
        /// <param name="keep">網点で残す割合 (撃破の演出で敵の足もとが抜けるのと同じ値・Game2DBattleFx が渡す・ふだんは 1)。
        /// 2026-11-16: 敵が崩れて消えるのと一緒に、 敵の棒も網点で抜ける (勝った後は敵の棒を残さない ── 棒は敵の足もとに付いた敵の一部なので、 敵が居ない床に空の棒を残さない)。</param>
        public void DrawFoot(BattleCanvas c, float keep = 1f)
        {
            if (_sim == null || _fx == null) return;
            _c = c; _dark = _fx.LcdDark; _keep = Mathf.Clamp01(keep);
            if (_dark <= 0.02f || _keep <= 0f) { _keep = 1f; return; }
            int ehp = _fx.EnemyHp, emax = Mathf.Max(1, _fx.EnemyMax);
            int x = BattleMotion.EX + BattleLcdLayout.EBarDx, y = BattleMotion.FloorY + BattleLcdLayout.EBarDy, w = BattleLcdLayout.EBarW, h = BattleLcdLayout.EBarH;
            int inner = w - 2;
            Rect(x, y, w, h, Edge); Rect(x + 1, y + 1, inner, h - 2, BarEmpty);
            int fill = Mathf.Clamp(Mathf.CeilToInt(inner * ehp / (float)emax), ehp > 0 ? 1 : 0, inner);
            if (fill > 0) { Rect(x + 1, y + 1, fill, h - 2, RedC); Rect(x + 1, y + 1, fill, 1, RedHi); }
            // この手で削れる分 = 見込みの攻撃値 (Game2DBattleSim.GetWired の atk)。 判断の間だけ。
            //   2026-11-16 作者の決定「すべては攻撃力に統合」: 本体に見込みの関数は足さず、 敵の防御・会心・倍率の前の攻撃の値のまま出す (仮置きではない)
            int cut = _sim.CanOperate ? Mathf.Min(ehp, _sim.GetWired().atk) : 0;
            if (cut > 0 && fill > 0)
            {
                int cw = Mathf.Clamp(Mathf.RoundToInt(inner * cut / (float)emax), 1, fill);
                for (int i = 0; i < cw; i++) Rect(x + 1 + fill - cw + i, y + 1, 1, h - 2, ((i / 3) & 1) == 0 ? Gold : Gold2);
            }
            var m = _fx.marks;
            if (m.notches != null)
                for (int i = 0; i < m.notches.Length; i++)
                {
                    int nx = x + 1 + Mathf.Clamp(Mathf.RoundToInt(inner * m.notches[i]), 1, inner - 2);
                    bool cracked = i >= m.notchBroken && m.cracks != null && i < m.cracks.Length && m.cracks[i] >= 0;
                    if (cracked) { Rect(nx, y - 2, 1, 3, Heavy); Rect(nx, y + h - 1, 1, 3, Heavy); }
                    else { Rect(nx - 1, y - 2, 3, h + 4, Edge); Rect(nx, y - 1, 1, h + 2, i < m.notchBroken ? NotchDone : Gold2); }
                }
            int tx = x + w + BattleLcdLayout.ENumGap, ty = y + BattleLcdLayout.ENumDy;
            string hp = ehp.ToString(); Txt(hp, tx, ty, Bone);
            if (cut > 0) Txt("-" + cut, tx + Game2DJpFont.Width(hp) + 6, ty, Gold2);
        }

        // ── 画面の残り (手札・床の出目・左下・右下・右上・左上・説明の帯) ──

        /// <summary>手札・出目・左下・右・上・説明の帯を打つ (敵の HP の棒の後・ダメージの数字の前)。</summary>
        public void DrawUi(BattleCanvas c)
        {
            if (!Drawing || _sim == null || _fx == null || _fx.IsIntro) return;
            _c = c; _dark = _fx.LcdDark; _keep = 1f; if (_dark <= 0.02f) return;
            var s = _sim;
            DrawPlayer();
            // 引っ込んだ部品は、 ずれで液晶の外へ出る。 1 コマの動きは MaxStepPerFrame まで (残像が 2 段に見えないように)
            if (_topOff < TopHide) { DrawRelics(); DrawInvAndDeck(); }
            if (_leftOff < LeftHide) DrawLeftCtl();
            DrawRight();
            if (_handOff < BattleLcdLayout.HandHide && s.board.Count > 0) for (int k = 0; k < s.board.Count; k++) DrawCard(k);
            if (_spHand.tgt < 0.5f) DrawFloor();      // 床の出目は札と一緒には動かさない (振り始めから見せる)
            DrawDragGhost();
            DrawTip();
        }

        /// <summary>左下: HP の数字・食われる分 (赤)・覆った分 (青)・棒。 解決の間・大技の格納の間も残す。</summary>
        private void DrawPlayer()
        {
            var s = _sim; int max = Mathf.Max(1, s.playerMax), hp = Mathf.Clamp(s.playerHp, 0, max);
            int x0 = BattleLcdLayout.MBarX0, x1 = BattleLcdLayout.MBarX1, y = BattleLcdLayout.MBarY, bw = x1 - x0;
            Rect(x0 - 1, y - 1, bw + 3, 7, Edge); Rect(x0, y, bw + 1, 5, BarEmpty);
            int e = x0 + Mathf.RoundToInt(bw * hp / (float)max);
            if (e > x0) { Rect(x0, y, e - x0 + 1, 5, GreenC); Rect(x0, y, e - x0 + 1, 1, GreenHi); }
            Txt(hp.ToString(), x0, BattleLcdLayout.MNumY, Bone);
            if (!s.CanOperate) return;
            // 食われる分 (仮): 予告の攻撃値 (防御の後・ブロックの前) + 〈業火〉の烈炎。 覆った分 (仮): 見込みのブロック + シールド
            int bite = s.down || s.charging ? 0 : Mathf.Max(0, s.eatk) + Mathf.Max(0, s.burst);
            int cover = Guard();
            if (bite > 0)
            {
                int wd = Mathf.Clamp(Mathf.RoundToInt(bw * Mathf.Min(bite, hp) / (float)max), 1, Mathf.Max(1, e - x0));
                Rect(e - wd + 1, y, wd, 5, RedC);
                if (cover > 0) { int cw = Mathf.Clamp(Mathf.RoundToInt(bw * Mathf.Min(cover, Mathf.Min(bite, hp)) / (float)max), 1, wd); Rect(e - cw + 1, y - 2, cw, 9, BlueC); Rect(e - cw + 1, y - 2, cw, 1, Bone); }
                int net = Mathf.Max(0, bite - cover);
                Txt("-" + net, x0 + Game2DJpFont.Width(hp.ToString()) + 8, BattleLcdLayout.MNumY, bite > cover ? RedC : BlueC);
            }
        }

        private int Shield => ItemSystem.PassiveSkills.PassiveSkillManager.Instance?.Context?.consShield ?? 0;
        private int Guard() => _sim.GetWired().blk + Shield;

        /// <summary>左下の残り: シールド・与ダメの補正 (消耗品・〈徒労〉)・充電の玉・振り直す。 大技の格納の間は左へ。</summary>
        private void DrawLeftCtl()
        {
            var s = _sim; int ox = LeftOff;
            int sh = Shield;
            if (sh > 0) Txt("シールド " + sh, BattleLcdLayout.LeftX + ox, BattleLcdLayout.ShieldY, BlueC);
            if (_hsKnown && (_hs.consOutgoingPct > 0 || _hs.toilCutPct > 0))
            {
                string add = (_hs.consOutgoingPct > 0 ? "与ダメ+" + _hs.consOutgoingPct + "%" : "")
                           + (_hs.toilCutPct > 0 ? (_hs.consOutgoingPct > 0 ? " " : "") + EventSystem.TimedEffects.BattleBoonRule.ToilName + "−" + _hs.toilCutPct + "%" : "");
                Txt(Game2DJpFont.Clip(add, 78), BattleLcdLayout.LeftX + ox, BattleLcdLayout.ModY, Gold2);
            }
            // 充電の玉: 中の数 = 今の充電・扇 = 充電 30 で満ちる (旧画面のランプ 10 個 × 3)
            int cx = BattleLcdLayout.OrbX + ox, cy = BattleLcdLayout.OrbY, r = BattleLcdLayout.OrbR;
            float frac = Mathf.Clamp01(s.charge / 30f);
            for (int j = -r - 1; j <= r + 1; j++)
                for (int i = -r - 1; i <= r + 1; i++)
                {
                    float d = Mathf.Sqrt(i * i + j * j);
                    if (d > r + 1.2f) continue;
                    Color32 col;
                    if (d > r - 0.2f || d < r - 4.6f) col = Edge;
                    else
                    {
                        float a = Mathf.Repeat(Mathf.Atan2(i, -j) / (Mathf.PI * 2f), 1f);      // 真上から時計回り
                        col = a < frac ? Chg : ChgDark;
                    }
                    Px(cx + i, cy + j, col);
                }
            TxtC(s.charge.ToString(), cx + 1, cy - 5, Gold2);
            bool can = s.CanRerollNow;
            Txt(RerollLabel, BattleLcdLayout.RerollTextX + ox, cy - 6, can ? Bone : DimC);
        }

        /// <summary>右: 右の列の小さな操作 (仮) と「決める」(字と下線だけ)。</summary>
        private void DrawRight()
        {
            var s = _sim; int ox = RightOff;
            if (s.CanOperate && _rightOff < RightHide)
            {
                bool[] on = { s.CanOperate, _win != null && _win.AnyUsable(), s.CanFlee };
                for (int k = 0; k < 3; k++) { var r = SideRect(k); Txt(SideLabel(k), r.x + 1, r.y + 1, k == 2 && _fleeArmed ? RedC : on[k] ? Bone : DimC); }
            }
            if (!GoVisible) return;
            bool en = s.ButtonEnabled;
            bool ready = en && (s.phase != Game2DBattleSim.P.Wire || AnyPlugged());
            var col = ready ? Gold2 : en ? Bone : DimC;
            int cx = BattleLcdLayout.GoCx + _goOff;
            TxtC(s.ButtonLabel, cx, BattleLcdLayout.GoY, col);
            int lx = cx - BattleLcdLayout.GoLineW / 2;
            Rect(lx - 1, BattleLcdLayout.GoLineY - 1, BattleLcdLayout.GoLineW + 2, 3, Edge); Rect(lx, BattleLcdLayout.GoLineY, BattleLcdLayout.GoLineW, 1, col);
            if (ready)
            {
                int dy = BattleLcdLayout.GoY - 10;
                for (int j = -4; j <= 4; j++) { int hw = 4 - Mathf.Abs(j); Rect(cx - hw - 1, dy + j, hw * 2 + 3, 1, Edge); }
                for (int j = -3; j <= 3; j++) { int hw = 3 - Mathf.Abs(j); Rect(cx - hw, dy + j, hw * 2 + 1, 1, Gold2); }
            }
        }

        private bool AnyPlugged() { foreach (var v in _sim.slot) if (v >= 0) return true; return false; }

        /// <summary>右上: 山と捨て札 (と外した札) の枚数。 左上: [持ち物] (仮)。</summary>
        private void DrawInvAndDeck()
        {
            var dr = DeckRect(); Txt(DeckText(), dr.x, dr.y, DimC);
            var ir = InvRect(); bool can = !GameHooks.BotDriving;
            Txt(BattleHudLayout.InvLabel, ir.x + 1, ir.y + 1, can ? (_hasMouse && In(_mouse, ir) ? Bone : DimC) : Faint);
        }

        // ── カード ──

        private static readonly Dictionary<string, Color32[]> _art = new Dictionary<string, Color32[]>();
        private static readonly HashSet<string> _noArt = new HashSet<string>();
        /// <summary>カードの絵を札の名前で引く口 (仮・2026-11-16)。 Resources/Game2DCardArt/&lt;札の名前&gt; の絵 (窓の大きさ 62×36 で描いた物) を読む。
        /// まだ絵は 1 枚も取り込んでいない (見本の「諸刃の剣」は Captures の中だけ)。 無ければ null = 端子のマークを窓の中央に置く。</summary>
        public static Color32[] CardArt(string id)
        {
            if (string.IsNullOrEmpty(id) || _noArt.Contains(id)) return null;
            if (_art.TryGetValue(id, out var a)) return a;
            var t = Resources.Load<Texture2D>("Game2DCardArt/" + id);
            var src = t != null ? KitSrc.From(t.isReadable ? t : null) : null;
            if (src == null || src.w != BattleLcdLayout.ArtW || src.h != BattleLcdLayout.ArtH) { _noArt.Add(id); return null; }
            return _art[id] = src.px;
        }

        /// <summary>カードの枠 (<b>仮</b>: 見本の card() と同じ単純な枠)。 枠の絵が決まったらここだけを差し替える (寸法は BattleLcdLayout)。</summary>
        private void DrawCardFrame(RectInt r, Color32 edge, Color32 band, string name, Color32 nameCol)
        {
            Rect(r.x - 1, r.y - 1, r.width + 2, r.height + 2, Edge);
            Rect(r.x, r.y, r.width, r.height, edge); Rect(r.x + 1, r.y + 1, r.width - 2, r.height - 2, CardBg);
            Rect(r.x + 1, r.y + 1, r.width - 2, BattleLcdLayout.BandH - 1, band);
            TxtC(Game2DJpFont.Clip(name, r.width - 4), r.x + r.width / 2, r.y + 1, nameCol, false);
            Rect(r.x + BattleLcdLayout.ArtX, r.y + BattleLcdLayout.ArtY, BattleLcdLayout.ArtW, BattleLcdLayout.ArtH, ArtBg);
        }

        private static Color32 Half(Color32 c) => new Color32((byte)(c.r / 2), (byte)(c.g / 2), (byte)(c.b / 2), 255);

        private void DrawCard(int s)
        {
            var sim = _sim; var d = TerminalCards.Get(sim.board[s]); if (d == null) return;
            var r = CardRect(s);
            var kc = Game2DTerminalLook.KindColor(d.kind);
            bool stopped = sim.IsStopped(s);
            bool dragging = _dragDie >= 0;
            bool fits = !stopped && (!dragging || sim.slot[_dragDie] == s || sim.CanPlugDie(_dragDie, s, out _));
            bool targetDrop = _dragCard >= 0 && _dragCard != s && sim.CanSetTarget(s, _dragCard);
            bool dim = stopped || (dragging && !fits) || (_dragCard >= 0 && !targetDrop && s != _dragCard);
            bool plugged = HasDie(s);
            var o = sim.preview; bool fires = o != null && o.fired.Contains(s);
            bool chosen = false, cand = sim.CanOperate && sim.IsTargetCandidate(s, out chosen);
            Color32 edge = kc, band = Game2DTerminalLook.Mix(kc, Game2DKit.Dark, 0.55f), nameCol = Bone;
            if (dim) { edge = Half(edge); band = Half(band); nameCol = DimC; }
            if (stopped) { edge = Faint; band = Game2DTerminalLook.Mix(kc, Game2DKit.Dark, 0.85f); }
            if (cand) edge = chosen ? ChosenEdge : CandEdge;
            if (targetDrop) edge = _hoverCard == s ? Game2DTerminalLook.Mix(ChosenEdge, Game2DKit.Ivory, 0.4f) : ChosenEdge;
            if (dragging && fits && _hoverCard == s) edge = Game2DTerminalLook.Mix(kc, Game2DKit.Ivory, 0.5f);
            else if (!dragging && _dragCard < 0 && _hoverCard == s) edge = Bone;      // 触っている札
            DrawCardFrame(r, edge, band, d.id, nameCol);
            // 絵 (無ければ端子のマーク)
            var art = CardArt(d.id);
            float k = dim ? 0.5f : 1f;
            if (art != null) Stamp(art, BattleLcdLayout.ArtW, BattleLcdLayout.ArtH, r.x + BattleLcdLayout.ArtX, r.y + BattleLcdLayout.ArtY, k);
            else
            {
                var icon = Game2DTerminalIcons.Get(d.id); const int n = Game2DTerminalIcons.Size;
                int ix = r.x + BattleLcdLayout.ArtX + (BattleLcdLayout.ArtW - n) / 2, iy = r.y + BattleLcdLayout.ArtY + (BattleLcdLayout.ArtH - n) / 2;
                Color32 front = dim ? DimC : new Color32(200, 194, 180, 255), back = Half(kc);
                if (icon != null && icon.Length >= n * n)
                {
                    for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) { byte v = icon[j * n + i]; if (v != 0) Px(ix + i, iy + j, v == Game2DTerminalIcons.Back ? back : front); }
                }
                else TxtC(Game2DTerminalLook.KindShort(d.kind), r.x + r.width / 2, iy + 3, front, false);
            }
            // 差し口 (2 個挿せる札は 2 つ・仮)。 空なら出目の条件・挿すと出目
            int cap = Mathf.Max(1, d.capacity), k2 = 0;
            var dice = new List<int>(cap);
            for (int i = 0; i < sim.slot.Length; i++) if (sim.slot[i] == s && i != _dragDie) dice.Add(i);
            for (int q = 0; q < cap; q++)
            {
                var sr = SockRect(r, q);
                Color32 sc = dim ? Half(kc) : kc;
                if (dragging && fits && dice.Count <= q) sc = _hoverCard == s ? Gold2 : Amber;      // 挿せる口
                Rect(sr.x - 1, sr.y - 1, sr.width + 2, sr.height + 2, Edge);
                Box(sr.x, sr.y, sr.width, sr.height, SockBg, sc);
                if (q < dice.Count)
                {
                    int i = dice[q];
                    if (!sim.Rolling(i)) DrawDie(sr.x + 1, sr.y + 1, sim.pd[i], plugged && !fires ? 0.6f : 1f);      // 挿したが発動しない = 暗く
                    else DrawRolling(i, sr.x + 1, sr.y + 1);
                    if (sim.IsRoleDie(i)) Frame1(sr.x, sr.y, sr.width, sr.height, Game2DKit.Gold);
                    if (sim.IsCatLocked(i)) Frame1(sr.x, sr.y, sr.width, sr.height, Amber);
                    if (sim.IsCarved(i) || sim.IsCarvedFixed(i)) Frame1(sr.x, sr.y, sr.width, sr.height, BlueC);      // 刻みの賽: 刻む印 / 刻んで置いた目
                    k2++;
                }
                else if (q == 0 && !string.IsNullOrEmpty(d.condLabel)) Txt(d.condLabel, sr.x + 2, sr.y + 4, dim ? Half(kc) : kc, false);
            }
            if (stopped) Rect(r.x + 2, r.y + BattleLcdLayout.ArtY + BattleLcdLayout.ArtH / 2, r.width - 4, 1, StopMark);      // 止まっている = 横切る 1 本の線
            // 札の上の印 (仮の字): 停止 / 対象・候補 / 不発 / 残す / 残り (2 個挿せる札)
            bool kept = sim.CanOperate && sim.IsKept(s);
            string label = stopped ? "停止" : cand ? (chosen ? "対象" : "候補") : plugged && !fires ? "不発" : kept ? "残す" : cap >= 2 && sim.Remaining(s) > 0 && plugged ? "残" + sim.Remaining(s) : null;
            if (label != null) TxtR(label, r.x + BattleLcdLayout.LabelRight, r.y + BattleLcdLayout.LabelY, stopped ? StopMark : kept ? Gold2 : cand && chosen ? Bone : label == "不発" ? DimC : KeepMark);      // 絵の窓の右上 (札の中)
        }

        // ── 床の出目 ──

        private readonly Color32[] _rollBuf = new Color32[Game2DDiceRoll.TexSize * Game2DDiceRoll.TexSize];

        /// <summary>回っている出目 (Game2DDiceRoll の 1 コマ・TexSize 四方・下の行から) を、 18×18 の左上 (x, y) に中心を合わせて打つ。</summary>
        private void DrawRolling(int i, int x, int y)
        {
            var s = _sim; if (s.rollSeq == null || i >= s.rollSeq.Length) { DrawDie(x, y, s.pd[i]); return; }
            Game2DDiceRoll.Render(_rollBuf, s.rollSeq[i], s.RollTime, Game2DDiceRoll.DurOf(i), false);
            int n = Game2DDiceRoll.TexSize, ox = x + Game2DDiceRoll.Face / 2 - Game2DDiceRoll.Reach, oy = y + Game2DDiceRoll.Face / 2 - Game2DDiceRoll.Reach;
            for (int j = 0; j < n; j++)
                for (int k = 0; k < n; k++) { var c = _rollBuf[(n - 1 - j) * n + k]; if (c.a >= 128) Px(ox + k, oy + j, c); }
        }

        private void DrawFloor()
        {
            var s = _sim; int n = s.DiceCount; if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                if (i == _dragDie || (i < s.slot.Length && s.slot[i] >= 0)) continue;
                BattleLcdLayout.FloorPos(i, out int x, out int y);
                // 床の影 (網点・見本の楕円の影の代わり)
                for (int q = -1; q < Game2DDiceRoll.Face + 1; q++) if (((q + y) & 1) == 0) Px(x + q, y + Game2DDiceRoll.Face + 1, Edge);
                if (s.Rolling(i)) DrawRolling(i, x, y);
                else if (!s.rolled && s.phase != Game2DBattleSim.P.PRoll) { Box(x, y, 18, 18, Game2DKit.Ivory, Edge); TxtC("?", x + 9, y + 3, Edge, false); }
                else DrawDie(x, y, s.pd[i]);
                if (s.IsRoleDie(i)) Frame1(x - 1, y - 1, 20, 20, Game2DKit.Gold);
                if (s.IsCatLocked(i)) Frame1(x - 1, y - 1, 20, 20, Amber);
                if (s.IsCarved(i) || s.IsCarvedFixed(i)) Frame1(x - 1, y - 1, 20, 20, BlueC);      // 刻みの賽: 刻む印 / 刻んで置いた目
                if (_hoverCard < 0 && _hasMouse && In(_mouse, new RectInt(x, y, 18, 18)) && s.CanOperate) Frame1(x - 1, y - 1, 20, 20, Bone);
            }
            // 役 (仮): 床の出目の上に名前 (出目は金の縁)
            if (s.rolled && s.roles.Count > 0)
            {
                var k = s.roles[0]; int face = YachtRoles.FaceOf(k);
                string t = k == RoleKind.Straight ? "役 " + YachtRoles.NameOf(k)
                         : "役 " + YachtRoles.NameOf(k) + " (" + face + "・" + face + (YachtRoles.IsTriple(k) ? "・" + face : "") + ")";
                BattleLcdLayout.FloorPos(Mathf.Max(0, n - 1), out _, out int ty);
                Txt(t, BattleLcdLayout.FloorX, Mathf.Min(BattleLcdLayout.FloorY, ty) - 15, Gold);
            }
        }

        private void DrawDragGhost()
        {
            if (!_hasMouse) return;
            int mx = Mathf.FloorToInt(_mouse.x), my = Mathf.FloorToInt(_mouse.y);
            if (_dragDie >= 0 && _dragDie < _sim.DiceCount) DrawDie(mx - 9, my - 9, _sim.pd[_dragDie]);
            else if (_dragCard >= 0 && _dragCard < _sim.board.Count)
            {
                var d = TerminalCards.Get(_sim.board[_dragCard]); if (d == null) return;
                var kc = Game2DTerminalLook.KindColor(d.kind); int w = Game2DJpFont.Width(d.id) + 8;
                Rect(mx - w / 2 - 1, my - 8, w + 2, 16, Edge); Box(mx - w / 2, my - 7, w, 14, Game2DTerminalLook.Mix(kc, Game2DKit.Dark, 0.55f), kc);
                TxtC(d.id, mx, my - 6, Bone, false);
            }
        }

        // ── 説明の帯 ──

        /// <summary>敵の足もとの棒と、 その右の数 (今の HP・−削れる分) の範囲。 説明の帯はここに被せない。</summary>
        private static RectInt BarZone()
        {
            int x = BattleMotion.EX + BattleLcdLayout.EBarDx, y = BattleMotion.FloorY + BattleLcdLayout.EBarDy;
            int top = Mathf.Min(y - 3, y + BattleLcdLayout.ENumDy) - 1, bottom = Mathf.Max(y + BattleLcdLayout.EBarH + 3, y + BattleLcdLayout.ENumDy + Game2DJpFont.Cell + 2);
            return new RectInt(x - 2, top, BattleLcdLayout.EBarW + BattleLcdLayout.ENumGap + 64, bottom - top);
        }

        private void DrawTip()
        {
            if (_dragDie >= 0 || _dragCard >= 0 || !_hasMouse) return;
            List<string> lines = null; int head = 1; Color32 edge = DimC, headCol = Bone; RectInt anchor = default; bool above = true;
            if (_hoverCard >= 0 && _hoverCard < _sim.board.Count && HandUp)
            {
                var d = TerminalCards.Get(_sim.board[_hoverCard]); if (d == null) return;
                lines = CardTipLines(_hoverCard, d, out head); edge = headCol = Game2DTerminalLook.KindColor(d.kind); anchor = CardRect(_hoverCard);
            }
            else if (_hoverRelic >= 0 && _hoverRelic < _relics.Count && ExtrasUp)
            {
                lines = RelicTipLines(_relics[_hoverRelic], out head); edge = headCol = Chg; anchor = RelicRect(_hoverRelic); above = false;
            }
            else if (_hoverZone == Zone.Head) { lines = EnemyTipLines(); edge = RedC; headCol = Bone; anchor = HeadRect(); above = false; }
            else if (_hoverZone == Zone.Hp) { lines = HpTipLines(); edge = GreenC; headCol = Bone; anchor = HpRect(); }
            else if (_hoverZone == Zone.Orb && ExtrasUp) { lines = OrbTipLines(); edge = headCol = Chg; anchor = OrbRect(); }
            else if (_hoverZone == Zone.Deck && ExtrasUp) { lines = DeckTipLines(); edge = DimC; anchor = DeckRect(); above = false; }
            if (lines == null || lines.Count == 0) return;
            int n = Mathf.Min(BattleLcdLayout.TipLinesMax, lines.Count), lh = Game2DJpFont.LineH;
            int tw = 0; for (int i = 0; i < n; i++) tw = Mathf.Max(tw, Game2DJpFont.Width(lines[i]));
            int w = tw + 10, h = 6 + n * lh + 2;
            int x = Mathf.Clamp(anchor.x + anchor.width / 2 - w / 2, BattleLcdLayout.SafeX0, BattleLcdLayout.SafeX1 + 1 - w);
            int y = above ? anchor.y - h - 3 : anchor.y + anchor.height + 3;
            y = Mathf.Clamp(y, BattleLcdLayout.SafeY0, BattleLcdLayout.SafeY1 + 1 - h);
            // 敵の足もとの棒 (と右の数) に被るなら、 棒の上へ逃がす (2026-11-16・実機確認で行の多い帯が棒に被った)。 頭上の予告の行には被せない (体には重なってよい)
            var bar = BarZone();
            if (new RectInt(x, y, w, h).Overlaps(bar))
            {
                y = bar.y - h - 2;
                var hr = HeadRect();
                if (new RectInt(x, y, w, h).Overlaps(hr)) y = hr.yMax + 1;
            }
            Rect(x - 1, y - 1, w + 2, h + 2, Edge); Box(x, y, w, h, TipBg, edge);
            for (int i = 0; i < n; i++) Txt(lines[i], x + 5, y + 4 + i * lh, i < head ? headCol : Bone, false);
        }

        /// <summary>カードの全文 (旧画面の説明の帯と同じ中身): 名前 → 印 (種類・条件・レア・使い捨て・常設・軸) → 効果 → 止まっている / 残す / 対象 / 誓約の封じ。</summary>
        private List<string> CardTipLines(int f, TerminalDef d, out int head)
        {
            int iw = BattleLcdLayout.TipMaxW - 10; var run = GameManager.Instance?.Run; var sim = _sim;
            var l = new List<string> { d.id }; head = 1;
            l.AddRange(Game2DJpFont.Lines(Game2DTerminalLook.Marks(d, run), iw));
            var body = new List<string>(Game2DJpFont.Lines(d.description, iw));
            var extra = new List<string>();
            if (sim.IsStopped(f)) extra.AddRange(Game2DJpFont.Lines(d.permanent ? "止まっている (このターンは挿せない)" : "止まっている (このターンは挿せない・次のターンまで手札に残る)", iw));
            if (sim.IsKept(f)) extra.AddRange(Game2DJpFont.Lines("次のターンも手札に残す (押して外す)", iw));
            else if (sim.CanKeep(f) && !sim.ShowingResult && sim.Remaining(f) > 0) extra.AddRange(Game2DJpFont.Lines("押すと、 挿さなかった時に次のターンも手札に残す", iw));
            string tgt = _win != null ? _win.TargetLine(f) : null;
            if (tgt != null) extra.AddRange(Game2DJpFont.Lines(tgt, iw));
            if (sim.IsTargetCandidate(f, out _)) extra.AddRange(Game2DJpFont.Lines("つかんで、 対象を取る札の上へ落とすと対象に決まる", iw));
            for (int q = 0; q < sim.board.Count && !sim.ShowingResult; q++)
                if (q != f && sim.IsPicker(q) && sim.TargetOf(q) == f) extra.AddRange(Game2DJpFont.Lines("〈" + sim.board[q] + "〉の対象", iw));
            if (TerminalCards.AxisSealedByRun(d, run)) extra.AddRange(Game2DJpFont.Lines("誓約で封じられている (軸の資源は増えない)", iw));
            int over = l.Count + body.Count + extra.Count - BattleLcdLayout.TipLinesMax;
            if (over > 0) body.RemoveRange(Mathf.Max(0, body.Count - over), Mathf.Min(body.Count, over));      // 収まらない時は効果の末尾を削って、 状態の行を残す
            l.AddRange(body); l.AddRange(extra);
            return l;
        }

        /// <summary>敵の頭上に触った時: 常設しない数 (敵の出目・攻撃・防御・ターン・激化の段と次の段まで・目盛りまで・不安定)。</summary>
        private List<string> EnemyTipLines()
        {
            var s = _sim; var l = new List<string>();
            if (s.phase == Game2DBattleSim.P.ERoll) return l;
            var dice = new List<string>(); for (int i = 0; i < s.enemyDiceCount; i++) dice.Add(s.ed[i].ToString());
            l.Add("敵の出目 " + string.Join("・", dice) + " (計 " + s.EnemyDiceSum + ")");
            l.Add("攻撃 " + s.eatk + (s.burst > 0 ? " + 烈炎 " + s.burst : "") + "  防御 " + s.edef);
            l.Add("HP " + (_fx != null ? _fx.EnemyHp + "/" + _fx.EnemyMax : "-"));
            l.Add("ターン " + s.turn + "  激化 " + s.Stage + (s.NextStageIn >= 0 ? " (次まで " + s.NextStageIn + ")" : " (最大)"));
            if (s.downNotchHp >= 0 && _fx != null) l.Add("目盛りまで " + Mathf.Max(0, _fx.EnemyHp - s.downNotchHp));
            int inst = GameManager.Instance != null ? GameManager.Instance.InstabilityFromEscalationThisCombat : 0;
            if (inst > 0) l.Add("不安定 +" + inst);
            return l;
        }

        /// <summary>左下の HP に触った時: HP・シールド・自分のステータス (旧画面のステータスの板)・見込みの内訳。</summary>
        private List<string> HpTipLines()
        {
            var s = _sim; var l = new List<string> { "HP " + s.playerHp + "/" + s.playerMax + (Shield > 0 ? "  シールド " + Shield : "") };
            if (_hsKnown)
            {
                l.Add("会心 " + Mathf.RoundToInt(_hs.critRate * 100f) + "% " + _hs.critMul.ToString("0.0") + "倍");
                l.Add("攻撃 " + _hs.attack + "  防御 " + _hs.defense + "  貫通 " + _hs.pen);
                if (_hs.consOutgoingPct > 0) l.Add("与ダメ +" + _hs.consOutgoingPct + "% (消耗品)");
                if (_hs.toilCutPct > 0) l.Add(EventSystem.TimedEffects.BattleBoonRule.ToilName + " 与えるダメージ −" + _hs.toilCutPct + "%");
            }
            if (s.CanOperate) l.Add("受ける見込み " + (s.down || s.charging ? 0 : s.eatk + s.burst) + " / 守り " + Guard() + " (目安)");
            return l;
        }

        private List<string> OrbTipLines()
        {
            var s = _sim; var l = new List<string> { "充電 " + s.charge };
            l.Add(s.canReroll ? "振り直す: 充電 " + s.rerollCost + " を払って全部の出目を振り直す (E)" : "いまは振り直せない");
            return l;
        }

        private static List<string> DeckTipLines()
        {
            var dk = CombatManager.Instance?.TerminalDeck; var l = new List<string>();
            if (dk == null) return l;
            l.Add("山札 " + dk.draw.Count + " 枚  捨て札 " + dk.discard.Count + " 枚");
            if (dk.exiled.Count > 0) l.Add("外した札 " + dk.exiled.Count + " 枚");
            return l;
        }

        // ── 秘宝の列 (#320 の左上の列を液晶の中へ移した・仮置き) ──
        //   中身は旧画面 (2026-11-16 に削除) の秘宝の列のまま。
        //   見え方は本体 (ItemSystem.PassiveSkills.RelicLookup) が品ごとに返す: 状態 = 光っている間は縁を光らせ絵を少し明るく・数・使用済みは暗く /
        //   出来事に反応する品 = 0.5 秒 (1 秒 8 コマの 4 コマ) 縁が光る / 【規則】の品 = 光らせない。

        private readonly List<string> _relics = new List<string>();
        private readonly Dictionary<string, float> _relicFlashAt = new Dictionary<string, float>();
        private int _relicSig = -1; private bool _relicHooked;
        private static readonly Color32 RelicGlow = new Color32(0xf0, 0xb4, 0x5a, 255);

        private void OnRelicFired(string id)
        {
            if (string.IsNullOrEmpty(id) || !Drawing || !_relics.Contains(id)) return;
            if (ItemSystem.PassiveSkills.RelicLookup.ModeOf(id, Env(), out _) != ItemSystem.PassiveSkills.RelicMode.Pulse) return;
            _relicFlashAt[id] = Time.unscaledTime;
            if (!GameHooks.BotDriving) Game2DSfx.Play("ui_tab", 0.5f, 1.3f);
        }

        private ItemSystem.PassiveSkills.RelicEnv Env()
        {
            var o = _sim != null ? _sim.preview : null;
            if (o != null && _sim.CanOperate) return new ItemSystem.PassiveSkills.RelicEnv { atkDice = o.attackDice, blockDice = o.blockDice };
            return ItemSystem.PassiveSkills.RelicEnv.Unknown;
        }

        private void RefreshRelics()
        {
            if (!_relicHooked) { ItemSystem.PassiveSkills.RelicFireProbe.OnRelicFired += OnRelicFired; _relicHooked = true; }
            var own = GameManager.Instance?.Run?.ownedPassiveItems;
            int sig = 17;
            if (own != null) unchecked { foreach (var id in own) sig = sig * 31 + (id ?? "").GetHashCode(); sig = sig * 31 + own.Count; }
            if (sig == _relicSig) return;
            _relicSig = sig; _relics.Clear();
            if (own == null) return;
            var db = ItemSystem.ItemDatabase.Instance;
            foreach (var id in own)
            {
                var data = db != null ? db.GetItem(id) : null;
                if (data != null && data.category == ItemSystem.ItemCategory.Weapon) continue;      // 武器は攻撃の絵で見えるので並べない
                if (!_relics.Contains(id)) _relics.Add(id);
            }
        }

        private static int RelicSlots => (BattleLcdLayout.RelicRight - BattleLcdLayout.RelicX) / BattleLcdLayout.RelicPitch;
        private RectInt RelicRect(int i) => new RectInt(BattleLcdLayout.RelicX + i * BattleLcdLayout.RelicPitch + LeftOff, BattleLcdLayout.RelicY + TopOff, 16, 16);
        private int RelicAt(Vector2 d)
        {
            int shown = _relics.Count <= RelicSlots ? _relics.Count : RelicSlots - 1;
            for (int i = 0; i < shown; i++) if (In(d, RelicRect(i))) return i;
            return -1;
        }

        private float RelicFlash(string id)
        {
            if (!_relicFlashAt.TryGetValue(id, out var t0)) return 0f;
            int step = (int)((Time.unscaledTime - t0) * 8f);
            return step < 0 || step >= 4 ? 0f : (4 - step) / 4f;
        }

        private void DrawRelics()
        {
            if (_relics.Count == 0) return;
            int slots = RelicSlots, n = _relics.Count, shown = n <= slots ? n : slots - 1, more = n - shown;
            var env = Env(); bool anyHidden = false;
            for (int i = 0; i < n; i++)
            {
                var id = _relics[i];
                var mode = ItemSystem.PassiveSkills.RelicLookup.ModeOf(id, env, out var v);
                float glow = mode == ItemSystem.PassiveSkills.RelicMode.State ? (v.lit ? 0.75f : 0f) : mode == ItemSystem.PassiveSkills.RelicMode.Pulse ? RelicFlash(id) : 0f;
                if (i >= shown) { if (glow > 0f) anyHidden = true; continue; }
                var r = RelicRect(i);
                var icon = Game2DKit.ItemIcon(id);
                float k = mode == ItemSystem.PassiveSkills.RelicMode.State && v.spent ? 0.45f : glow > 0f ? 1f + 0.15f * glow : 1f;
                Rect(r.x - 1, r.y - 1, 18, 18, Edge);
                if (icon != null) StampSrc(icon, r.x, r.y, 16, 16, k);
                else { string nm = ItemSystem.ItemDatabase.DisplayNameOf(id) ?? id; Txt(nm.Length > 0 ? nm.Substring(0, 1) : "?", r.x + 2, r.y + 2, Bone, false); }
                if (glow > 0f && !(mode == ItemSystem.PassiveSkills.RelicMode.State && v.spent))
                    _c.Rect(r.x - 1, r.y - 1, 18, 1, C(RelicGlow), glow, true);
                if (glow > 0f && !(mode == ItemSystem.PassiveSkills.RelicMode.State && v.spent))
                {
                    _c.Rect(r.x - 1, r.y + 16, 18, 1, C(RelicGlow), glow, true); _c.Rect(r.x - 1, r.y, 1, 16, C(RelicGlow), glow, true); _c.Rect(r.x + 16, r.y, 1, 16, C(RelicGlow), glow, true);
                }
                if (mode == ItemSystem.PassiveSkills.RelicMode.State && v.count >= 0) TxtR(v.count + (v.percent ? "%" : ""), r.x + 18, r.y + 7, Gold2);
                if (_hoverRelic == i) Rect(r.x, r.y + 17, 16, 1, Bone);
            }
            if (more > 0) Txt("+" + more, RelicRect(shown).x, RelicRect(shown).y + 2, anyHidden ? Chg : DimC);
        }

        /// <summary>KitSrc (上の行から) を w×h へ打つ (同じ大きさなら写すだけ・拡大縮小しない)。</summary>
        private void StampSrc(KitSrc s, int x, int y, int w, int h, float k)
        {
            for (int j = 0; j < h; j++)
                for (int i = 0; i < w; i++)
                {
                    var c = s.px[(j * s.h / h) * s.w + (i * s.w / w)]; if (c.a < 128) continue;
                    if (k != 1f) c = new Color32((byte)Mathf.Min(255, c.r * k), (byte)Mathf.Min(255, c.g * k), (byte)Mathf.Min(255, c.b * k), 255);
                    Px(x + i, y + j, c);
                }
        }

        private static List<string> RelicTipLines(string id, out int head)
        {
            int iw = BattleLcdLayout.TipMaxW - 10;
            var data = ItemSystem.ItemDatabase.Instance?.GetItem(id);
            var l = new List<string>(Game2DJpFont.Lines(ItemSystem.ItemDatabase.DisplayNameOf(id) ?? id, iw)); head = l.Count;
            if (data != null)
            {
                bool any = false;
                if (data.passiveSkills != null) foreach (var ps in data.passiveSkills) if (!string.IsNullOrEmpty(ps.description)) { l.AddRange(Game2DJpFont.Lines(ps.description, iw)); any = true; }
                if (!any && !string.IsNullOrEmpty(data.description)) l.AddRange(Game2DJpFont.Lines(data.description, iw));
            }
            return l;
        }
    }
}
