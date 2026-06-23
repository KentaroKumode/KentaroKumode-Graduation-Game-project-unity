using System.Collections;
using UnityEngine;
using CombatSystem;
using CombatSystem.DiceLED;

namespace Battle.Visual
{
    /// <summary>
    /// バトル演出の指揮役。 既存の <see cref="CombatManager"/> / <see cref="DiceLEDManager"/>
    /// のイベントを購読し、 <see cref="BattleVisualRig"/> の駒・FX を駆動する。
    ///
    /// シーケンス:
    ///   OnCombatStart        → リグ表示・駒を待機姿勢
    ///   OnRollingStart       → 双方の攻撃 FX を中央へ→鍔迫り合い
    ///   OnTurnEnd            → 直近の TurnResult をキャッシュ(まだ可視化しない)
    ///   OnRollingComplete    → キャッシュ結果に従い 勝/負/分 の決着演出を再生
    ///   OnCombatEnd          → リグ非表示
    ///
    /// 駒・FX はすべて LCD コンテンツ・カメラ視野内に同居(LcdContent レイヤ)。
    /// </summary>
    [DisallowMultipleComponent]
    public class BattleVisualDirector : MonoBehaviour
    {
        [Header("配線")]
        [Tooltip("演出のステージ。 未指定時は同 GO もしくは子から探す。")]
        public BattleVisualRig rig;

        [Header("タイミング")]
        [Tooltip("鍔迫り合いの最低再生時間(秒)。 ダイス演出と概ね同期させる。")]
        public float clashDurationMin = 0.8f;
        [Tooltip("着弾/破砕シーケンスの長さ(秒)")]
        public float resolveDuration = 0.55f;
        [Tooltip("敗北時、敵カウンター攻撃の再生時間(秒)")]
        public float counterDuration = 0.5f;

        [Header("駒モーション")]
        [Tooltip("駒が中央に踏み出す距離(world)")]
        public float pieceStepForward = 0.6f;
        [Tooltip("被弾時の駒のよろけ量(world)")]
        public float pieceStaggerBack = 0.5f;

        [Header("武器エフェクト")]
        [Tooltip("剣エフェクトの Tier 1〜4 スプライト(sword_effect の sliced 4枚)。 indexは武器の tier に対応(1基底)")]
        public Sprite[] swordSprites;
        [Tooltip("敵エフェクトのデフォルトスプライト(プレイヤー側と同じシリーズを流用)")]
        public Sprite enemyDefaultSprite;

        private PlaceholderClashFX _playerFx;
        private PlaceholderClashFX _enemyFx;
        private TurnResult? _pendingResult;
        private bool _clashing;
        private bool _wired;
        private Coroutine _resolveCoroutine;

        private void Awake()
        {
            if (rig == null) rig = GetComponent<BattleVisualRig>();
            if (rig == null) rig = GetComponentInChildren<BattleVisualRig>(true);
        }

        private void OnEnable() => Wire(true);
        private void OnDisable() => Wire(false);

        private void Wire(bool on)
        {
            var cm = CombatManager.Instance;
            var dm = DiceLEDManager.Instance;
            if (on && _wired) return;
            if (!on && !_wired) return;
            if (cm != null)
            {
                if (on) { cm.OnCombatStart += HandleCombatStart; cm.OnTurnEnd += HandleTurnEnd; cm.OnCombatEnd += HandleCombatEnd; }
                else    { cm.OnCombatStart -= HandleCombatStart; cm.OnTurnEnd -= HandleTurnEnd; cm.OnCombatEnd -= HandleCombatEnd; }
            }
            if (dm != null)
            {
                if (on) { dm.OnRollingStart += HandleRollingStart; dm.OnRollingComplete += HandleRollingComplete; }
                else    { dm.OnRollingStart -= HandleRollingStart; dm.OnRollingComplete -= HandleRollingComplete; }
            }
            _wired = on;
        }

        private void Start()
        {
            // CombatManager/DiceLEDManager のシングルトンが Awake 後に確定するケースに備えた再配線
            if (!_wired) Wire(true);
        }

        // ---- 戦闘ライフサイクル ----

        private void HandleCombatStart(string enemyId)
        {
            if (rig == null) return;
            rig.SetVisible(true);
            rig.ResetPieces();
            _pendingResult = null;
            _clashing = false;
        }

        private void HandleCombatEnd(CombatResult result)
        {
            if (_resolveCoroutine != null) { StopCoroutine(_resolveCoroutine); _resolveCoroutine = null; }
            CleanupFx();
            if (rig != null) rig.SetVisible(false);
        }

        // ---- ターン ----

        private void HandleRollingStart()
        {
            if (rig == null) return;
            if (_resolveCoroutine != null) { StopCoroutine(_resolveCoroutine); _resolveCoroutine = null; }
            CleanupFx();
            rig.ResetPieces();
            _pendingResult = null;
            _clashing = true;
            StartCoroutine(PlayClash());
        }

        private void HandleTurnEnd(TurnResult result)
        {
            _pendingResult = result;
        }

        private void HandleRollingComplete()
        {
            _clashing = false;
            if (!_pendingResult.HasValue) return;
            var r = _pendingResult.Value;
            _pendingResult = null;
            if (_resolveCoroutine != null) StopCoroutine(_resolveCoroutine);
            _resolveCoroutine = StartCoroutine(PlayResolve(r));
        }

        // ---- 演出 ----

        private IEnumerator PlayClash()
        {
            string weaponId = "";
            string enemyId = "";
            var gm = GameLoop.GameManager.Instance;
            if (gm != null && gm.Run != null) weaponId = gm.Run.equippedWeaponId ?? "";
            var cm = CombatManager.Instance;
            if (cm != null && cm.CurrentEnemy != null) enemyId = cm.CurrentEnemy.id;

            var pFx = BattleVisualFXRegistry.ResolvePlayer(weaponId);
            var eFx = BattleVisualFXRegistry.ResolveEnemy(enemyId);

            var pSprite = PickSwordSprite(weaponId);
            var eSprite = enemyDefaultSprite != null ? enemyDefaultSprite : pSprite;

            _playerFx = PlaceholderClashFX.Spawn(rig.transform, pSprite, pFx.color, pFx.size, rig.ContentLayer, "PlayerFX");
            _enemyFx  = PlaceholderClashFX.Spawn(rig.transform, eSprite, eFx.color, eFx.size, rig.ContentLayer, "EnemyFX");

            // 駒を半歩前進
            StartCoroutine(StepPiece(rig.PlayerPivot, new Vector3(pieceStepForward, 0f, 0f), 0.2f));
            StartCoroutine(StepPiece(rig.EnemyPivot,  new Vector3(-pieceStepForward, 0f, 0f), 0.2f));

            var pFromV = rig.PlayerPivot.position + Vector3.up * 0.6f;
            var eFromV = rig.EnemyPivot.position  + Vector3.up * 0.6f;
            var center = rig.ClashCenter.position;

            // 両者を中央へ → 中央で揺れ
            var pCo = StartCoroutine(_playerFx.PlayClash(pFromV, center, clashDurationMin));
            var eCo = StartCoroutine(_enemyFx.PlayClash(eFromV, center, clashDurationMin));
            yield return pCo;
            yield return eCo;
            // OnRollingComplete を待つ間も中央で揺れ続けるのは PlayClash の後半が担当(終端まで再生済み)。
            // 余韻として位置をそのまま保持。
        }

        private IEnumerator PlayResolve(TurnResult r)
        {
            if (rig == null) yield break;
            var center = rig.ClashCenter.position;
            var playerHead = rig.PlayerPivot.position + Vector3.up * 0.6f;
            var enemyHead  = rig.EnemyPivot.position  + Vector3.up * 0.6f;

            if (r.playerWon)
            {
                // 自 FX が敵へ着弾、敵 FX は破砕
                if (_enemyFx != null) StartCoroutine(_enemyFx.PlayBreak(resolveDuration));
                if (_playerFx != null) yield return _playerFx.PlayLand(enemyHead, resolveDuration);
                StartCoroutine(StaggerPiece(rig.EnemyPivot, new Vector3(pieceStaggerBack, 0f, 0f), 0.35f));
            }
            else if (r.isDraw)
            {
                // 双方を押し合いつつ自陣に戻す
                if (_playerFx != null) StartCoroutine(_playerFx.PlayBreak(resolveDuration));
                if (_enemyFx != null)  yield return _enemyFx.PlayBreak(resolveDuration);
                StartCoroutine(StepPiece(rig.PlayerPivot, Vector3.zero, 0.25f));
                StartCoroutine(StepPiece(rig.EnemyPivot,  Vector3.zero, 0.25f));
            }
            else
            {
                // プレイヤー敗北: 自 FX 破砕 → 敵 FX が改めてプレイヤーへ着弾
                if (_playerFx != null) yield return _playerFx.PlayBreak(resolveDuration);
                if (_enemyFx != null)  yield return _enemyFx.PlayLand(playerHead, counterDuration);
                StartCoroutine(StaggerPiece(rig.PlayerPivot, new Vector3(-pieceStaggerBack, 0f, 0f), 0.4f));
            }

            _playerFx = null;
            _enemyFx = null;
            _resolveCoroutine = null;
        }

        private IEnumerator StepPiece(Transform piece, Vector3 delta, float dur)
        {
            if (piece == null) yield break;
            Vector3 from = piece.localPosition;
            Vector3 to   = from + delta;
            float t = 0f;
            while (t < dur)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / dur);
                piece.localPosition = Vector3.Lerp(from, to, k);
                yield return null;
            }
            piece.localPosition = to;
        }

        private IEnumerator StaggerPiece(Transform piece, Vector3 backDelta, float dur)
        {
            if (piece == null) yield break;
            Vector3 from = piece.localPosition;
            Vector3 mid  = from + backDelta;
            float half = dur * 0.5f;
            float t = 0f;
            while (t < half) { t += Time.deltaTime; piece.localPosition = Vector3.Lerp(from, mid, t / half); yield return null; }
            t = 0f;
            while (t < half) { t += Time.deltaTime; piece.localPosition = Vector3.Lerp(mid, from, t / half); yield return null; }
            piece.localPosition = from;
        }

        private Sprite PickSwordSprite(string weaponId)
        {
            if (swordSprites == null || swordSprites.Length == 0) return null;
            int tier = ParseWeaponTier(weaponId);
            int idx = Mathf.Clamp(tier - 1, 0, swordSprites.Length - 1);
            return swordSprites[idx];
        }

        private static int ParseWeaponTier(string id)
        {
            if (string.IsNullOrEmpty(id)) return 1;
            // id 例: "sword_t1", "sword_t3" 等。 末尾の "_t<digit>" を抽出。
            int i = id.LastIndexOf("_t");
            if (i < 0 || i + 2 >= id.Length) return 1;
            int n = 0;
            for (int k = i + 2; k < id.Length && char.IsDigit(id[k]); k++) n = n * 10 + (id[k] - '0');
            return n >= 1 ? n : 1;
        }

        private void CleanupFx()
        {
            if (_playerFx != null) Destroy(_playerFx.gameObject);
            if (_enemyFx != null)  Destroy(_enemyFx.gameObject);
            _playerFx = null;
            _enemyFx = null;
        }
    }
}
