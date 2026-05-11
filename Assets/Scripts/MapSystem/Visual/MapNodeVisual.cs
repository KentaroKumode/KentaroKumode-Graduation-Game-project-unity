using UnityEngine;
using GameLoop;

namespace MapSystem.Visual
{
    public enum NodeVisualState
    {
        Default,    // 未訪問・到達不可
        Reachable,  // 現在ノードから到達可能
        Current,    // 現在位置
        Visited,    // 訪問済み
    }

    /// <summary>
    /// マップノード1個の表示制御。
    /// SpriteRenderer ベースでタイルアイコン表示＋状態に応じた色/スケール変化。
    /// </summary>
    public class MapNodeVisual : MonoBehaviour
    {
        private MapNode node;
        private SpriteRenderer sr;
        private TileIconAtlas atlas;
        private NodeVisualState currentState;

        /// <summary>MapVisualizer から渡されるノード色パレット</summary>
        public struct ColorPalette
        {
            public Color Default;
            public Color Visited;
            public Color Reachable;
            public Color Current;
            public Color BossDeep;
            public Color BossBright;
        }

        // === 色設定 (Inspector で MapVisualizer 経由で調整可能) ===
        private ColorPalette palette;

        // 色未設定時のフォールバック
        private static readonly ColorPalette DefaultPalette = new ColorPalette
        {
            Default    = new Color(0.4f, 0.4f, 0.4f, 0.7f),
            Visited    = new Color(0.7f, 0.65f, 0.5f, 0.9f),
            Reachable  = new Color(1f, 1f, 1f, 1f),
            Current    = new Color(1f, 0.85f, 0.3f, 1f),
            BossDeep   = new Color(0.18f, 0.02f, 0.02f, 1f),
            BossBright = new Color(0.65f, 0.05f, 0.05f, 1f),
        };

        private float pulseTimer;
        private Color tileBaseColor; // タイル種別の基色（明滅などの計算基準）
        private bool isBoss;

        public string NodeId => node?.id;
        public NodeVisualState CurrentState => currentState;

        public void Initialize(MapNode node, TileIconAtlas atlas = null, ColorPalette? colorPalette = null)
        {
            this.node = node;
            this.atlas = atlas;
            this.palette = colorPalette ?? DefaultPalette;
            sr = GetComponent<SpriteRenderer>();
            if (sr == null) sr = gameObject.AddComponent<SpriteRenderer>();

            // 親の寝かせ回転 (X=-90°) によって UV Y が反転して見える問題を補正
            sr.flipY = true;

            // 先にスプライトを設定 (SetTileType がスプライトと tileBaseColor を準備)
            SetTileType(node.EffectiveType);

            // ピクセルアート用: フィルタを Point に強制
            if (sr.sprite != null && sr.sprite.texture != null)
                sr.sprite.texture.filterMode = FilterMode.Point;

            // クリック判定用コライダー (3D物理 — 親が寝かせ回転しても追従)
            // この時点で sr.sprite は確実に設定されているため bounds が正しく取れる
            if (GetComponent<Collider>() == null)
            {
                var col = gameObject.AddComponent<BoxCollider>();
                if (sr.sprite != null)
                {
                    var sb = sr.sprite.bounds.size;
                    // 局所XY平面のスプライトに合わせる。Z(局所)は薄いボックス
                    col.size = new Vector3(sb.x, sb.y, 0.1f);
                }
                else
                {
                    // フォールバック: 1x1x0.1 の標準サイズ
                    col.size = new Vector3(1f, 1f, 0.1f);
                }
            }

            SetState(NodeVisualState.Default);
        }

        /// <summary>MapClickHandler から呼ばれる。Reachable 時のみ移動を通知する。</summary>
        public void OnClicked()
        {
            if (currentState != NodeVisualState.Reachable) return;
            if (node == null) return;

            // 通常: GameManager 経由 (戦闘・報酬等のフェーズ制御を伴う)
            // フォールバック: GameManager がない (テストシーン) なら MapManager に直接通知
            if (GameManager.Instance != null)
                GameManager.Instance.MoveToNode(node.id);
            else if (MapManager.Instance != null)
                MapManager.Instance.MoveTo(node.id, 30); // テスト用 maxHP 固定
        }

        /// <summary>タイルタイプに応じたアイコン/色を設定</summary>
        public void SetTileType(TileType type)
        {
            if (sr == null) return;

            // アトラス優先 → フォールバック色
            Sprite sprite = null;
            if (atlas != null)
                sprite = atlas.GetSprite(type);

            if (sprite != null)
            {
                sr.sprite = sprite;
                if (sprite.texture != null)
                    sprite.texture.filterMode = FilterMode.Point;
                tileBaseColor = Color.white; // スプライト色はそのまま
            }
            else
            {
                // アトラス未設定時: 1×1白テクスチャ+色でカラーブロック表示
                sr.sprite = GetOrCreateFallbackSprite();
                tileBaseColor = GetTileColor(type);
            }

            // ボスだけ特別扱い: アイコンを血の赤で染める
            isBoss = (type == TileType.Boss);
            if (isBoss)
                tileBaseColor = palette.BossDeep;

            // 状態色を再適用（明滅などの基準色を更新したため）
            SetState(currentState);
        }

        // 1×1白スプライト（全ノード共有）
        private static Sprite fallbackSprite;
        private static Sprite GetOrCreateFallbackSprite()
        {
            if (fallbackSprite != null) return fallbackSprite;
            var tex = new Texture2D(1, 1) { filterMode = FilterMode.Point };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            fallbackSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            fallbackSprite.name = "FallbackTileSprite";
            return fallbackSprite;
        }

        public void SetState(NodeVisualState state)
        {
            currentState = state;
            if (sr == null) return;
            ApplyStateColor(0f);
        }

        void Update()
        {
            // ボスは状態に関係なく、まがまがしい遅い脈動を続ける
            if (isBoss)
            {
                pulseTimer += Time.deltaTime * 1.2f;
                float pulse = 0.5f + Mathf.Sin(pulseTimer) * 0.35f
                                   + Mathf.Sin(pulseTimer * 2.7f) * 0.10f;
                pulse = Mathf.Clamp01(pulse);
                Color c = Color.Lerp(palette.BossDeep, palette.BossBright, pulse);
                if (currentState == NodeVisualState.Reachable) c *= 1.15f;
                else if (currentState == NodeVisualState.Current) c *= 1.3f;
                sr.color = c;
                return;
            }

            // 状態に応じた色アニメーション（スケールはピクセル密度を保つため変更しない）
            if (currentState == NodeVisualState.Reachable || currentState == NodeVisualState.Current)
            {
                pulseTimer += Time.deltaTime * (currentState == NodeVisualState.Current ? 2.5f : 3f);
                ApplyStateColor(Mathf.Sin(pulseTimer));
            }
        }

        /// <summary>
        /// 状態とパルス位相 (-1..1) から最終色を計算する。スケールは触らない。
        /// </summary>
        private void ApplyStateColor(float pulsePhase)
        {
            // 各状態を単色置き換えで適用 (タイル基色とブレンドしない)
            // Reachable / Current は明滅のため明度を脈動させる
            switch (currentState)
            {
                case NodeVisualState.Default:
                    sr.color = palette.Default;
                    break;
                case NodeVisualState.Visited:
                    sr.color = palette.Visited;
                    break;
                case NodeVisualState.Reachable:
                    {
                        float b = 0.85f + pulsePhase * 0.15f; // 0.7..1.0
                        sr.color = ScaleRgb(palette.Reachable, b);
                    }
                    break;
                case NodeVisualState.Current:
                    {
                        float b = 0.80f + pulsePhase * 0.20f; // 0.6..1.0
                        sr.color = ScaleRgb(palette.Current, b);
                    }
                    break;
            }
        }

        // === ヘルパー ===

        private static Color GetTileColor(TileType type)
        {
            switch (type)
            {
                case TileType.Outpost:     return new Color(0.2f, 0.6f, 0.9f);
                case TileType.Battle:      return new Color(0.9f, 0.3f, 0.3f);
                case TileType.EliteBattle: return new Color(0.8f, 0.1f, 0.1f);
                case TileType.Rest:        return new Color(0.3f, 0.8f, 0.4f);
                case TileType.Treasure:    return new Color(1f, 0.85f, 0.2f);
                case TileType.Shop:        return new Color(0.6f, 0.4f, 0.9f);
                case TileType.Event:       return new Color(0.9f, 0.7f, 0.2f);
                case TileType.Mystery:     return new Color(0.7f, 0.7f, 0.7f);
                case TileType.Trap:        return new Color(0.6f, 0.2f, 0.5f);
                case TileType.Boss:        return new Color(1f, 0f, 0f);
                default:                   return Color.white;
            }
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return Color.Lerp(a, b, t);
        }

        /// <summary>RGB だけ係数倍して明度を変える (アルファは不変)</summary>
        private static Color ScaleRgb(Color c, float k)
        {
            return new Color(c.r * k, c.g * k, c.b * k, c.a);
        }
    }
}
