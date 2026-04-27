using System.Collections.Generic;
using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// マップのパス1本の表示制御。
    /// 内部はベジェ曲線上に並べたドットスプライト群（ピクセルパーフェクト用）。
    /// 状態に応じて全ドットの色を一括変更する。
    /// </summary>
    public class MapLineVisual : MonoBehaviour
    {
        public string FromId { get; private set; }
        public string ToId { get; private set; }
        public bool IsLateral { get; private set; }

        private readonly List<SpriteRenderer> dots = new List<SpriteRenderer>();

        public void InitializeDots(string fromId, string toId, IEnumerable<SpriteRenderer> dotSprites, bool isLateral)
        {
            FromId = fromId;
            ToId = toId;
            IsLateral = isLateral;

            dots.Clear();
            if (dotSprites != null)
                dots.AddRange(dotSprites);
        }

        public void SetColor(Color color)
        {
            foreach (var d in dots)
                if (d != null) d.color = color;
        }
    }
}
