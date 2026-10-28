using GameLoop;
using MapSystem;
using MapSystem.FreeMove;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 航行の操作 (map-prototype.html の pointer / wheel / key イベント)。
    ///
    /// <list type="bullet">
    /// <item>点にカーソルを乗せる = 行き先の見込み (半径 7 dot = HTML の 14 画素)。 GOLD の入れ物・ボタンの上、 ドラッグ中は出さない。</item>
    /// <item>クリック = 隣の点へ 1 歩。 ドラッグした後のクリックは無効 / ボタン・GOLD の入れ物の上は、 そちらが先 (<see cref="Game2DNavPanel.Consumes"/>)。</item>
    /// <item>ホイール = X1 / X2 / X3 を 1 段ずつ (上 = 寄る・下 = 引く・140ms に 1 回)。 Z キー = X1→X2→X3 を回す。</item>
    /// <item>どの倍率でも (X1 でも) ドラッグ (5 画素以上動かすと始まる) か WASD (押している間・6 距離/秒) で視点を動かせる (視点は自分への追従を止める)。 C キーで追従に戻る。</item>
    /// </list>
    /// </summary>
    public partial class Game2DMapView
    {
        [Tooltip("操作パネル (ボタン・GOLD の入れ物の上のクリックを地図が奪わないため)")] public Game2DNavPanel panel;

        private int _hover = -1, _hoverSfx = -1;
        private const float HoverRadius = 13f;              // dot (HTML は 14 画素 = 7 dot。 点どうしが離れているので、 約 2 倍に広げた)
        private const float PanSpeed = 6f;                  // 距離 / 秒
        private bool _camManual;                            // 視点を自分で動かしている
        private bool _dragOn; private Vector2 _dragLast;
        private float _wheelAt = -1f;
        private bool _dragMoved;
        private bool _downInMap;

        /// <summary>直前のクリックの前に、 ドラッグで視点を動かしたか (次に押すまで残る)。</summary>
        public bool DragMoved => _dragMoved;

        /// <summary>カーソルを乗せた隣の点へ動く時、 物資のバーが減る割合 (行き先の見込み・点滅用)。 無ければ 0。</summary>
        public float SupplyPreview { get; private set; }
        /// <summary>見込みの物資の数字 (例 "-10")。 無ければ null。</summary>
        public string SupplyPreviewText { get; private set; }

        private string[] _idByIndex;
        private int _idFloor = -1;

        /// <summary>画面の位置 (GUI の dot) → 液晶の絵 (480×270 のバッファ) の dot。 液晶は樽型に湾曲している (Game2D/LcdScreen の <c>_Curve</c>) ので、
        /// 見えている位置と絵の位置がずれる。 シェーダと同じ式 「元 = 中心 × (1 + k·r²) / (1 + 2k)」 で、 見えている点が絵のどこを映しているかを求める
        /// (点の表示と当たり判定を合わせる)。 GUI (パネル・入れ物) は湾曲しないので、 こちらは地図の点にだけ使う。</summary>
        private Vector2 ScreenToMapDot(Vector2 d)
        {
            float k = 0f;
            if (lcdQuad != null) { var mr = lcdQuad.GetComponent<MeshRenderer>(); if (mr != null && mr.sharedMaterial != null && mr.sharedMaterial.HasProperty("_Curve")) k = mr.sharedMaterial.GetFloat("_Curve"); }
            if (k <= 0f) return d;
            // 画面の uv (上 = 1): c = uv × 2 − 1
            float cx = d.x / W * 2f - 1f, cy = (1f - d.y / H) * 2f - 1f;
            float r2 = cx * cx + cy * cy, f = (1f + k * r2) / (1f + 2f * k);
            float sx = cx * f, sy = cy * f;                              // 絵の uv (−1..1)
            return new Vector2((sx * 0.5f + 0.5f) * W, (1f - (sy * 0.5f + 0.5f)) * H);
        }

        private void ZoomTo(int z)
        {
            z = Mathf.Clamp(z, 1, 3);
            if (z == _zoom) return;
            _zoom = z; Game2DSfx.Play("ui_tab", 0.8f, 0.9f + 0.1f * z);
            if (_zoom == 1) _camManual = false;
        }

        private void HandleInput(FreeMapSim sim, float dt)
        {
            SupplyPreview = 0; SupplyPreviewText = null;
            if (Game2DModal.Active) return;      // 画面 (ショップなど) が出ている間は、 地図を操作しない
            var gm = GameManager.Instance;

            // キー: Z = 拡大を回す / C = 追従に戻る / WASD = 視点を動かす (拡大中)
            if (Input.GetKeyDown(KeyCode.Z)) ZoomTo(_zoom >= 3 ? 1 : _zoom + 1);
            if (Input.GetKeyDown(KeyCode.C)) _camManual = false;
            float vx = (Input.GetKey(KeyCode.D) ? 1 : 0) - (Input.GetKey(KeyCode.A) ? 1 : 0), vy = (Input.GetKey(KeyCode.S) ? 1 : 0) - (Input.GetKey(KeyCode.W) ? 1 : 0);
            if (vx != 0 || vy != 0)
            {
                _camManual = true;
                _camPos = new Vector2(Mathf.Clamp(_camPos.x + vx * PanSpeed * dt, 0, (float)FreeMapParams.Width), Mathf.Clamp(_camPos.y + vy * PanSpeed * dt, 0, (float)FreeMapParams.Height));
            }

            float ppd = screen != null ? screen.PixelsPerGuiDot : 2f;
            bool inside = Game2DPointer.TryDot(screen, out var d);

            // ホイール: 1 段ずつ (140ms に 1 回)
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0f && inside && Time.unscaledTime - _wheelAt >= 0.14f) { _wheelAt = Time.unscaledTime; ZoomTo(_zoom + (wheel > 0 ? 1 : -1)); }

            bool consumes = inside && panel != null && panel.Consumes(d.x, d.y);
            var dm = ScreenToMapDot(d);                                  // 地図の点の判定は、 湾曲を戻した位置で行う

            // ドラッグ (拡大中・ボタンの上でないところから)
            var mp = (Vector2)Input.mousePosition;
            if (Input.GetMouseButtonDown(0))
            {
                _dragMoved = false; _downInMap = inside && !consumes;
                if (_downInMap) { _dragOn = true; _dragLast = mp; }
            }
            if (_dragOn && Input.GetMouseButton(0))
            {
                var delta = mp - _dragLast; delta.y = -delta.y;           // 画面の下向きを正に
                if (_dragMoved || delta.magnitude >= 5f)
                {
                    _camManual = true; _dragMoved = true;
                    float k = 1f / (ppd * _ls);
                    _camPos = new Vector2(_camPos.x - delta.x * k, _camPos.y - delta.y * k);
                    _dragLast = mp;
                }
            }

            // ホバー
            _hover = -1;
            if (inside && !consumes && !_dragOn)
            {
                var lay = sim.Layout; float best = HoverRadius;
                for (int i = 0; i < lay.Count; i++)
                {
                    if (sim.IsSunk(i)) continue;
                    float dist = Vector2.Distance(Sp(lay.pos[i]), dm);
                    if (dist < best) { best = dist; _hover = i; }
                }
            }
            int hvSfx = (_hover >= 0 && _hover != sim.Me && sim.CanMoveTo(_hover)) ? _hover : -1;
            if (hvSfx != _hoverSfx) { _hoverSfx = hvSfx; if (hvSfx >= 0) Game2DSfx.Play("node_hover"); }
            if (_hover >= 0 && _hover != sim.Me && sim.CanMoveTo(_hover))
            {
                var run = gm != null ? gm.Run : null;
                if (run != null && run.provisionCap > 0)
                {
                    int pay = Mathf.Min(FreeMapParams.MoveSupply, run.provision);
                    SupplyPreview = pay / (float)run.provisionCap; SupplyPreviewText = "-" + pay;
                }
            }

            // クリック (離した時): ドラッグした後・ボタン・GOLD の入れ物の上は無視
            if (Input.GetMouseButtonUp(0))
            {
                bool wasDrag = _dragOn && _dragMoved; _dragOn = false;
                if (wasDrag || !_downInMap || !inside || consumes) { _downInMap = false; return; }
                _downInMap = false;
                if (sim.Dead || sim.InFight || gm == null) return;
                int hit = -1; float bestD = HoverRadius; var lay2 = sim.Layout;
                for (int i = 0; i < lay2.Count; i++)
                {
                    if (sim.IsSunk(i)) continue;
                    float dist = Vector2.Distance(Sp(lay2.pos[i]), dm);
                    if (dist < bestD) { bestD = dist; hit = i; }
                }
                if (hit < 0 || hit == sim.Me) return;                      // 空振り / 今いる点: 何もしない
                string id = IdOf(hit);
                if (id != null) gm.MoveStep(id);                           // 隣でなければ「行けない」がログに出る
            }
        }

        private string IdOf(int index)
        {
            var map = MapManager.Instance != null ? MapManager.Instance.CurrentMap : null;
            if (map == null) return null;
            if (_idByIndex == null || _idFloor != _viewFloor || _idByIndex.Length != _viewNodes)
            {
                _idByIndex = new string[_viewNodes]; _idFloor = _viewFloor;
                foreach (var n in map.GetAllNodes()) if (n.index >= 0 && n.index < _idByIndex.Length) _idByIndex[n.index] = n.id;
            }
            return index >= 0 && index < _idByIndex.Length ? _idByIndex[index] : null;
        }
    }
}
