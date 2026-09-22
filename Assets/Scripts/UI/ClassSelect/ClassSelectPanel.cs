using System.Collections;
using UnityEngine;

namespace UI.ClassSelect
{
    /// <summary>
    /// 職業選択 UI の汎用スライドパネル (Papers, Please 風の書類出し入れ)。
    /// shown/hidden のローカル座標間を easing 付きで往復する。 位置移動のみで
    /// localScale は一切触らない (PPU=32 規約準拠)。
    ///
    /// - SlideIn  : hidden → shown。 overshoot > 0 なら着地時に行き過ぎて戻る (紙が落ちて弾む感触)
    /// - SlideOut : shown → hidden。 引っ込みは速め・ease-in
    /// 時間は unscaledTime 基準 (ポーズ/タイトルでも動く)。
    /// </summary>
    public class ClassSelectPanel : MonoBehaviour
    {
        [Header("位置 (ローカル)")]
        public Vector3 shownLocalPos;
        public Vector3 hiddenLocalPos;

        [Header("アニメ")]
        [Tooltip("表示スライドの所要秒")]
        public float slideInDuration = 0.25f;
        [Tooltip("退場スライドの所要秒")]
        public float slideOutDuration = 0.15f;
        [Tooltip("SlideIn 着地オーバーシュート量 (ワールド単位)。 0=なし。 shown 位置を通り過ぎてから戻る (バネ挙動)")]
        public float overshoot = 0f;
        [Tooltip("SlideOut 着地オーバーシュート量 (ワールド単位)。 0=なし。 hidden 位置を通り過ぎてから戻る (バネ挙動)")]
        public float outOvershoot = 0f;

        public bool IsShown { get; private set; }
        public bool IsAnimating { get; private set; }

        private Coroutine _anim;

        /// <summary>即座に隠し位置へスナップ (初期化用)。</summary>
        public void SnapHidden()
        {
            StopAnim();
            transform.localPosition = hiddenLocalPos;
            IsShown = false;
        }

        /// <summary>即座に表示位置へスナップ。</summary>
        public void SnapShown()
        {
            StopAnim();
            transform.localPosition = shownLocalPos;
            IsShown = true;
        }

        public void SlideIn(System.Action onDone = null)
        {
            StopAnim();
            _anim = StartCoroutine(CoSlide(shownLocalPos, slideInDuration, easeOut: true, overshoot, () =>
            {
                IsShown = true;
                onDone?.Invoke();
            }));
        }

        public void SlideOut(System.Action onDone = null)
        {
            StopAnim();
            // SlideOut もオーバーシュートさせてバネ的な着地に (outOvershoot > 0 のとき)。
            // ease を減速型 (easeOut=true) にして「押し込んで少し戻る」 感触。
            _anim = StartCoroutine(CoSlide(hiddenLocalPos, slideOutDuration, easeOut: true, outOvershoot, () =>
            {
                IsShown = false;
                onDone?.Invoke();
            }));
        }

        private void StopAnim()
        {
            if (_anim != null) { StopCoroutine(_anim); _anim = null; }
            IsAnimating = false;
        }

        private IEnumerator CoSlide(Vector3 target, float duration, bool easeOut, float overshootAmount, System.Action onDone)
        {
            IsAnimating = true;
            Vector3 from = transform.localPosition;
            duration = Mathf.Max(0.01f, duration);

            // 3 段バウンド: from → target → bounce (target - dir * amount = from 側/外側へ跳ね返り) → target。
            // 壁にぶつかって反発する物理挙動。 amount = 跳ね返り量 (dot 単位で view から指定)。
            bool doOvershoot = overshootAmount > 0.0001f && (target - from).sqrMagnitude > 0.000001f;
            if (doOvershoot)
            {
                Vector3 dir = (target - from).normalized;
                Vector3 bounce = target - dir * overshootAmount; // target を過ぎて from 方向へ
                float t1 = duration * 0.55f; // from → target
                float t2 = duration * 0.20f; // target → bounce (跳ね返り、 加速)
                float t3 = duration * 0.25f; // bounce → target (減速して着地)
                yield return CoMove(from, target, t1, easeOut);
                yield return CoMove(target, bounce, t2, easeOut: false); // 加速で跳ね返り
                yield return CoMove(bounce, target, t3, easeOut: true);  // 減速で戻る
            }
            else
            {
                yield return CoMove(from, target, duration, easeOut);
            }

            transform.localPosition = target;
            IsAnimating = false;
            _anim = null;
            onDone?.Invoke();
        }

        private IEnumerator CoMove(Vector3 a, Vector3 b, float dur, bool easeOut)
        {
            float t = 0f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.Clamp01(t / dur);
                // ease-out-cubic / ease-in-cubic
                float e = easeOut ? 1f - Mathf.Pow(1f - u, 3f) : u * u * u;
                transform.localPosition = Vector3.LerpUnclamped(a, b, e);
                yield return null;
            }
            transform.localPosition = b;
        }
    }
}
