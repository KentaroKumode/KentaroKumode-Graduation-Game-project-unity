using UnityEngine;

namespace MapSystem.Visual
{
    /// <summary>
    /// MapDecorationPlacer が生成する装飾オブジェクトに付与されるマーカー。
    /// 片付けアニメ時の参照用にローカル位置を保持する。
    /// </summary>
    public class MapDecoration : MonoBehaviour
    {
        public Vector3 originalLocalPosition;
        public Quaternion originalLocalRotation;
    }
}
