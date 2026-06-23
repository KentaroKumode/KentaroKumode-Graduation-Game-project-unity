using System;
using System.Collections.Generic;
using UnityEngine;

namespace UI
{
    /// <summary>
    /// タイトル画面のマウス疑似パララックス。 各レイヤーをマウス位置に応じて位置オフセットさせ、 奥行きを演出する。
    ///
    /// 前提: イラストが奥/中/手前のレイヤー(別 GameObject)に分かれていること（一枚絵では深度が出ない）。
    /// 設計: localScale は使わず **localPosition のオフセットのみ**（PPU=32 ピクセル要素のスケール禁止に準拠）。
    /// 対応: ワールド SpriteRenderer / UI(RectTransform) どちらの Transform でも可。
    ///
    /// 使い方:
    ///   1) タイトルの親(空 GameObject 等)にこのコンポーネントを付ける。
    ///   2) layers に 奥(背景)→手前 の順でレイヤー Transform を登録。
    ///   3) strength を「奥=小さい / 手前=大きい」に設定（例 背景0.05 / 中0.15 / 手前0.35）。
    ///   4) タイトル文字やボタンは invert=true で僅かに逆方向に動かすと “浮き” が出る。
    /// </summary>
    public class TitleParallax : MonoBehaviour
    {
        [Serializable]
        public class Layer
        {
            [Tooltip("動かす対象（ワールド Sprite でも UI でも可）")]
            public Transform target;
            [Tooltip("マウス端での最大オフセット量。 奥ほど小さく・手前ほど大きく。 単位はワールド/UIローカル。")]
            public Vector2 strength = new Vector2(0.15f, 0.10f);
            [Tooltip("true で逆方向（タイトル文字/ボタンを僅かに逆に動かすと立体感が増す）")]
            public bool invert = false;

            [NonSerialized] public Vector3 initialLocalPos;
            [NonSerialized] public Vector3 velocity;
        }

        [Header("レイヤー（奥→手前の順で登録）")]
        public List<Layer> layers = new List<Layer>();

        [Header("挙動")]
        [Tooltip("追従の滑らかさ（小さいほど機敏／大きいほどゆったり）")]
        [Range(0.01f, 0.5f)] public float smoothTime = 0.12f;
        [Tooltip("マウス入力の最大振れ幅（縦横とも -1〜1 に正規化した値へ乗る全体係数）")]
        public float globalScale = 1f;
        [Tooltip("ポーズ中(timeScale=0)でも動かす")]
        public bool useUnscaledTime = true;
        [Tooltip("ウィンドウ外/未入力時は中央へ戻す")]
        public bool recenterWhenNoInput = true;

        [Header("ピクセルスナップ（任意・ワールド Sprite 用）")]
        [Tooltip("0=スナップしない。 >0 でその PPU グリッドへ丸める（PPU=32 推奨）。 ※カクつくことがあるので通常はOFF推奨")]
        public int pixelsPerUnit = 0;

        private void Awake()
        {
            foreach (var l in layers)
                if (l != null && l.target != null)
                    l.initialLocalPos = l.target.localPosition;
        }

        private void OnDisable()
        {
            // 無効化時は初期位置へ戻す（再有効化で中央から始まるように）
            foreach (var l in layers)
                if (l != null && l.target != null)
                {
                    l.target.localPosition = l.initialLocalPos;
                    l.velocity = Vector3.zero;
                }
        }

        private void Update()
        {
            Vector2 m = ReadMouseNormalized(); // -1〜1（中央=0）
            float dt = useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            if (dt <= 0f) return;

            foreach (var l in layers)
            {
                if (l == null || l.target == null) continue;
                float sign = l.invert ? -1f : 1f;
                Vector3 offset = new Vector3(
                    m.x * l.strength.x * globalScale * sign,
                    m.y * l.strength.y * globalScale * sign,
                    0f);
                Vector3 goal = l.initialLocalPos + offset;

                Vector3 next = Vector3.SmoothDamp(l.target.localPosition, goal, ref l.velocity, smoothTime, Mathf.Infinity, dt);
                if (pixelsPerUnit > 0)
                {
                    next.x = Mathf.Round(next.x * pixelsPerUnit) / pixelsPerUnit;
                    next.y = Mathf.Round(next.y * pixelsPerUnit) / pixelsPerUnit;
                }
                l.target.localPosition = next;
            }
        }

        /// <summary>マウス位置を画面中心基準で -1〜1 に正規化。 入力なし/画面外は (0,0)。</summary>
        private Vector2 ReadMouseNormalized()
        {
            Vector3 mp = Input.mousePosition;
            if (recenterWhenNoInput &&
                (mp.x < 0 || mp.y < 0 || mp.x > Screen.width || mp.y > Screen.height))
                return Vector2.zero;

            float nx = (Screen.width  > 0) ? (mp.x / Screen.width  * 2f - 1f) : 0f;
            float ny = (Screen.height > 0) ? (mp.y / Screen.height * 2f - 1f) : 0f;
            return new Vector2(Mathf.Clamp(nx, -1f, 1f), Mathf.Clamp(ny, -1f, 1f));
        }
    }
}
