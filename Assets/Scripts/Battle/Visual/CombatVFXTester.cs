using System.Collections;
using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Battle.Visual
{
    /// <summary>
    /// Eric VFX Studio の斬撃/ヒット VFX を別シーンで試打するためのコントローラ。
    ///
    /// 想定操作:
    ///   Q / E    斬撃プレハブを切替
    ///   A / D    ヒットプレハブを切替
    ///   Space    プレイヤー側から敵側へ斬撃 + 遅延ヒット
    ///   Tab      敵側からプレイヤー側へ (逆向き)
    ///   R        現在 instantiated 済みの VFX を全消去
    ///
    /// 配置:
    ///   1. 任意 GO にアタッチ
    ///   2. Inspector の右クリック → "Auto Populate (Built-In)" でプレハブ自動収集
    ///   3. playerAnchor / enemyAnchor 未設定なら Start 時に左右へ自動生成
    /// </summary>
    [DisallowMultipleComponent]
    public class CombatVFXTester : MonoBehaviour
    {
        [Header("登場人物アンカー (未設定なら自動生成)")]
        public Transform playerAnchor;
        public Transform enemyAnchor;
        [Tooltip("自動生成時のプレイヤー〜敵間距離。")]
        public float separation = 4f;

        [Header("VFXプレハブ (Inspector のコンテキストメニューで一括収集可)")]
        public List<GameObject> slashPrefabs = new List<GameObject>();
        public List<GameObject> hitPrefabs = new List<GameObject>();
        [Tooltip("鍔迫り合い中央で常駐させる火花/衝撃 VFX。 AutoPopulate でパス内 spark/impact/clash を拾う。")]
        public List<GameObject> clashPrefabs = new List<GameObject>();

        [Header("プレハブ毎の追加回転オフセット (LookRotation の後にかける)")]
        [Tooltip("slashPrefabs と同じ index で対応。 Magic Slash 系はオーサリング向き違いで Y=-90 が必要。")]
        public List<Vector3> slashRotationOffsets = new List<Vector3>();
        [Tooltip("hitPrefabs と同じ index で対応。 通常は 0 で問題なし。")]
        public List<Vector3> hitRotationOffsets = new List<Vector3>();

        [Header("Ink (アルファブレンド・黒系) 判定")]
        [Tooltip("slashPrefabs と同じ index で対応。 true なら ink レイヤーに配置 (素のアルファブレンドで第二 RT に焼く)。 AutoPopulate でパス内 ink を自動検出。")]
        public List<bool> slashIsInk = new List<bool>();
        [Tooltip("hitPrefabs と同じ index で対応。 true なら ink レイヤー。")]
        public List<bool> hitIsInk = new List<bool>();

        [Header("入力")]
        public KeyCode firePlayerToEnemy = KeyCode.Space;
        public KeyCode fireEnemyToPlayer = KeyCode.Tab;
        public KeyCode clashKey = KeyCode.C;
        public KeyCode slashPrev = KeyCode.Q;
        public KeyCode slashNext = KeyCode.E;
        public KeyCode hitPrev = KeyCode.A;
        public KeyCode hitNext = KeyCode.D;
        public KeyCode clearKey = KeyCode.R;

        [Header("発火タイミング")]
        [Tooltip("斬撃発射からヒット表示までの遅延 (秒)")]
        public float hitDelay = 0.15f;
        [Tooltip("自動破棄を有効化する")]
        public bool autoCleanup = true;
        [Tooltip("自動破棄までの猶予 (秒)")]
        public float autoCleanupAfter = 3f;
        [Tooltip("攻撃側 anchor から斬撃を出すオフセット (敵方向)")]
        public float slashSpawnOffset = 0.5f;

        [Header("鍔迫り合い (Clash)")]
        [Tooltip("クラッシュ開始までの待機時間 (秒)。 0 で即時")]
        public float clashStartDelay = 0.1f;
        [Tooltip("各 anchor から発生点までの接近時間 (秒)。 0 でテレポート")]
        public float clashApproachTime = 0.18f;
        [Tooltip("各斬撃発生点 → 接触点までの距離。 中心放射の弧の半径に合わせて調整 (大きいほど離れる)")]
        public float clashOriginGap = 1.2f;
        [Tooltip("中央で押し合っている持続秒数")]
        public float clashHoldTime = 0.9f;
        [Tooltip("押し合い中に発生点が前後にブレる幅 (接近←→離反)")]
        public float clashPushJitter = 0.08f;
        [Tooltip("接触点が左右にズレる幅")]
        public float clashSideJitter = 0.04f;
        [Tooltip("鍔迫り合い中の simulationSpeed 倍率 (1=通常)")]
        [Range(0.05f, 1f)] public float clashSimulationSpeed = 0.35f;
        [Tooltip("hold 中に追加で連射する間隔 (秒)。 ループしないプレハブの再点火用。 0で無効")]
        public float clashReignitionInterval = 0.18f;
        [Tooltip("決着時、 勝者方向へ抜ける移動量")]
        public float clashBreakthrough = 1.4f;
        [Tooltip("決着までの時間 (breakthrough 期間)")]
        public float clashBreakTime = 0.22f;

        [Header("HUD")]
        public bool drawHud = true;

        private int slashIndex;
        private int hitIndex;
        private readonly List<GameObject> _spawned = new List<GameObject>();

        private void Start()
        {
            EnsureAnchors();
        }

        private void EnsureAnchors()
        {
            if (playerAnchor == null)
            {
                var go = new GameObject("PlayerAnchor");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(-separation * 0.5f, 0f, 0f);
                playerAnchor = go.transform;
            }
            if (enemyAnchor == null)
            {
                var go = new GameObject("EnemyAnchor");
                go.transform.SetParent(transform, false);
                go.transform.localPosition = new Vector3(separation * 0.5f, 0f, 0f);
                enemyAnchor = go.transform;
            }
        }

        private void Update()
        {
            int sCount = Mathf.Max(1, slashPrefabs.Count);
            int hCount = Mathf.Max(1, hitPrefabs.Count);

            if (Input.GetKeyDown(slashPrev)) slashIndex = (slashIndex - 1 + sCount) % sCount;
            if (Input.GetKeyDown(slashNext)) slashIndex = (slashIndex + 1) % sCount;
            if (Input.GetKeyDown(hitPrev))   hitIndex   = (hitIndex - 1 + hCount) % hCount;
            if (Input.GetKeyDown(hitNext))   hitIndex   = (hitIndex + 1) % hCount;

            // 1〜9 で直接ジャンプ
            for (int i = 0; i < 9; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i) && i < slashPrefabs.Count)
                    slashIndex = i;
            }

            if (Input.GetKeyDown(firePlayerToEnemy)) FirePlayerSlash();
            if (Input.GetKeyDown(fireEnemyToPlayer)) FireEnemySlash();
            if (Input.GetKeyDown(clashKey))          FireClash();
            if (Input.GetKeyDown(clearKey))          ClearSpawned();
        }

        public void FirePlayerSlash()
        {
            SpawnSlash(playerAnchor, enemyAnchor);
            StartCoroutine(SpawnHitAfter(enemyAnchor.position, hitDelay));
        }

        public void FireEnemySlash()
        {
            SpawnSlash(enemyAnchor, playerAnchor);
            StartCoroutine(SpawnHitAfter(playerAnchor.position, hitDelay));
        }

        private void SpawnSlash(Transform from, Transform to)
        {
            SpawnSlashCore(from, to, manageLifetime: true);
        }

        private GameObject SpawnSlashCore(Transform from, Transform to, bool manageLifetime)
        {
            if (slashPrefabs.Count == 0)
            {
                Debug.LogWarning("[CombatVFXTester] 斬撃プレハブが未登録です。 'Auto Populate (Built-In)' を実行してください。");
                return null;
            }
            int idx = Mathf.Clamp(slashIndex, 0, slashPrefabs.Count - 1);
            var prefab = slashPrefabs[idx];
            if (prefab == null) return null;

            var dir = (to.position - from.position);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            dir.Normalize();

            var pos = from.position + dir * slashSpawnOffset;
            var offsetEuler = (idx < slashRotationOffsets.Count) ? slashRotationOffsets[idx] : Vector3.zero;
            var rot = Quaternion.LookRotation(dir) * Quaternion.Euler(offsetEuler);
            var go = Instantiate(prefab, pos, rot);
            go.name = $"{prefab.name} (spawn)";
            AssignLayerForCurrentSlash(go);
            if (manageLifetime) TrackSpawn(go);
            else _spawned.Add(go);
            return go;
        }

        // ============================================================
        //  鍔迫り合い (Clash)
        // ============================================================

        public void FireClash()
        {
            if (playerAnchor == null || enemyAnchor == null) return;
            if (slashPrefabs.Count == 0)
            {
                Debug.LogWarning("[CombatVFXTester] 斬撃プレハブが未登録です。");
                return;
            }
            StartCoroutine(ClashRoutine());
        }

        /// <summary>
        /// 中心放射型 (発生点中心の円形切り払い) パーティクル前提の鍔迫り合い。
        /// 移動はさせず、 両者を中央接触点に重ねて生成し、 互いに反対方向を向けて
        /// 双方の弧が互いを切り払う形で噛み合うようにする。
        /// </summary>
        private IEnumerator ClashRoutine()
        {
            int idx = Mathf.Clamp(slashIndex, 0, slashPrefabs.Count - 1);
            var prefab = slashPrefabs[idx];
            if (prefab == null) yield break;
            var offsetEuler = (idx < slashRotationOffsets.Count) ? slashRotationOffsets[idx] : Vector3.zero;

            var mid = (playerAnchor.position + enemyAnchor.position) * 0.5f;
            var pDir = (enemyAnchor.position - playerAnchor.position).normalized;   // プレイヤー → 敵
            var eDir = -pDir;
            var side = Vector3.Cross(pDir, Vector3.up).normalized;

            // 中心放射の弧が外側で噛み合うよう、 発生点を中央から離す
            var pBase = mid - pDir * clashOriginGap;   // プレイヤー側 (敵に向かって弧を切る)
            var eBase = mid + pDir * clashOriginGap;   // 敵側 (プレイヤーに向かって弧を切る)

            var pRot = Quaternion.LookRotation(pDir) * Quaternion.Euler(offsetEuler);
            var eRot = Quaternion.LookRotation(eDir) * Quaternion.Euler(offsetEuler);

            // 開始待機 (構え時間)
            if (clashStartDelay > 0f) yield return new WaitForSeconds(clashStartDelay);

            // anchor から発生点 (pBase/eBase) へ接近
            var pSpawn = playerAnchor.position + pDir * slashSpawnOffset;
            var eSpawn = enemyAnchor.position  + eDir * slashSpawnOffset;
            var pSlash = SpawnAtCore(prefab, pSpawn, pRot, $"{prefab.name} (clash-P)");
            var eSlash = SpawnAtCore(prefab, eSpawn, eRot, $"{prefab.name} (clash-E)");
            // 接近中はそれなりに動きが見えるよう速度を抑えめにしない
            float approach = 0f;
            while (approach < clashApproachTime)
            {
                approach += Time.deltaTime;
                float k = Mathf.Clamp01(approach / Mathf.Max(0.001f, clashApproachTime));
                if (pSlash != null) pSlash.transform.position = Vector3.Lerp(pSpawn, pBase, k);
                if (eSlash != null) eSlash.transform.position = Vector3.Lerp(eSpawn, eBase, k);
                yield return null;
            }
            if (pSlash != null) pSlash.transform.position = pBase;
            if (eSlash != null) eSlash.transform.position = eBase;

            SetSimulationSpeed(pSlash, clashSimulationSpeed);
            SetSimulationSpeed(eSlash, clashSimulationSpeed);

            // 火花/衝撃は外縁が噛み合う中央へ
            GameObject clashFx = SpawnClashFx(mid);

            float held = 0f;
            float sinceReignite = 0f;
            int winnerSign = Random.value < 0.5f ? -1 : 1; // -1: player / +1: enemy
            while (held < clashHoldTime)
            {
                float dt = Time.deltaTime;
                held += dt;
                sinceReignite += dt;

                // 押し合い: 両者が中央方向に詰めたり離れたりする (cos で同位相 → 同時に押し込む = 接触強度up)
                float push = Mathf.Sin(held * 14f) * clashPushJitter;
                float sideShift = Mathf.Sin(held * 22f) * clashSideJitter;

                if (pSlash != null) pSlash.transform.position = pBase + pDir * push + side * sideShift;
                if (eSlash != null) eSlash.transform.position = eBase - pDir * push + side * sideShift;
                if (clashFx != null) clashFx.transform.position = mid + side * sideShift;

                // 再点火: ループしないプレハブの円形切り払いを連射で繋ぐ
                if (clashReignitionInterval > 0f && sinceReignite >= clashReignitionInterval)
                {
                    sinceReignite = 0f;
                    bool fromPlayer = Random.value < 0.5f;
                    var sparkPos = (fromPlayer ? pBase : eBase) + side * sideShift;
                    var sparkRot = fromPlayer ? pRot : eRot;
                    var spark = SpawnAtCore(prefab, sparkPos, sparkRot, $"{prefab.name} (clash-pulse)");
                    SetSimulationSpeed(spark, clashSimulationSpeed * 1.5f);
                    Destroy(spark, 0.6f);
                }
                yield return null;
            }

            // 決着: 勝者は通常速度で通過、 敗者は消す
            Vector3 breakDir = pDir * winnerSign;
            GameObject winner = winnerSign < 0 ? pSlash : eSlash;
            GameObject loser  = winnerSign < 0 ? eSlash : pSlash;
            Vector3 winnerStart = winnerSign < 0 ? pBase : eBase;
            StopParticles(loser);
            if (loser != null)
            {
                _spawned.Remove(loser);
                Destroy(loser); // 敗北側は即座に消す (フェードや残光無し)
                loser = null;
            }
            SetSimulationSpeed(winner, 1f);

            // フィニッシュ斬撃を中央から勝者方向へ放射
            var finishRot = Quaternion.LookRotation(breakDir) * Quaternion.Euler(offsetEuler);
            var finish = SpawnAtCore(prefab, mid, finishRot, $"{prefab.name} (clash-finish)");

            float bt = 0f;
            while (bt < clashBreakTime)
            {
                bt += Time.deltaTime;
                float k = Mathf.Clamp01(bt / Mathf.Max(0.001f, clashBreakTime));
                if (winner != null) winner.transform.position = winnerStart + breakDir * (clashBreakthrough * k);
                if (finish != null) finish.transform.position = mid + breakDir * (clashBreakthrough * k * 0.6f);
                yield return null;
            }

            if (clashFx != null) Destroy(clashFx);
            // 勝者側のみ残光を許す
            if (winner  != null) { _spawned.Remove(winner);  Destroy(winner,  0.4f); }
            if (finish  != null) { _spawned.Remove(finish);  Destroy(finish,  0.4f); }
        }

        private GameObject SpawnAtCore(GameObject prefab, Vector3 pos, Quaternion rot, string label)
        {
            var go = Instantiate(prefab, pos, rot);
            go.name = label;
            AssignLayerForCurrentSlash(go);
            _spawned.Add(go);
            return go;
        }

        private void AssignLayerForCurrentSlash(GameObject go)
        {
            // per-renderer 自動振り分け (1 プレハブ内の additive / alpha-blend 混在に対応)
            VFXPixelCompositor.AssignSmartLayerRecursive(go);
        }

        private void AssignLayerForHit(int idx, GameObject go)
        {
            VFXPixelCompositor.AssignSmartLayerRecursive(go);
        }

        private static void SetSimulationSpeed(GameObject go, float speed)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;
                main.simulationSpeed = speed;
            }
        }

        private GameObject SpawnClashFx(Vector3 pos)
        {
            GameObject prefab = null;
            if (clashPrefabs.Count > 0) prefab = clashPrefabs[Random.Range(0, clashPrefabs.Count)];
            else if (hitPrefabs.Count > 0) prefab = hitPrefabs[Mathf.Clamp(hitIndex, 0, hitPrefabs.Count - 1)];
            if (prefab == null) return null;
            var go = Instantiate(prefab, pos, Quaternion.identity);
            go.name = $"{prefab.name} (clash)";
            VFXPixelCompositor.AssignSmartLayerRecursive(go); // 火花も per-renderer 自動判定
            _spawned.Add(go);
            return go;
        }

        private static void StopParticles(GameObject go)
        {
            if (go == null) return;
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        private IEnumerator SpawnHitAfter(Vector3 pos, float delay)
        {
            if (hitPrefabs.Count == 0) yield break;
            if (delay > 0f) yield return new WaitForSeconds(delay);
            int idx = Mathf.Clamp(hitIndex, 0, hitPrefabs.Count - 1);
            var prefab = hitPrefabs[idx];
            if (prefab == null) yield break;
            var offsetEuler = (idx < hitRotationOffsets.Count) ? hitRotationOffsets[idx] : Vector3.zero;
            var go = Instantiate(prefab, pos, Quaternion.Euler(offsetEuler));
            go.name = $"{prefab.name} (hit)";
            AssignLayerForHit(idx, go);
            TrackSpawn(go);
        }

        private void TrackSpawn(GameObject go)
        {
            _spawned.Add(go);
            if (autoCleanup) StartCoroutine(CleanupAfter(go, autoCleanupAfter));
        }

        private IEnumerator CleanupAfter(GameObject go, float t)
        {
            yield return new WaitForSeconds(t);
            if (go != null)
            {
                _spawned.Remove(go);
                Destroy(go);
            }
        }

        public void ClearSpawned()
        {
            for (int i = _spawned.Count - 1; i >= 0; i--)
            {
                if (_spawned[i] != null) Destroy(_spawned[i]);
            }
            _spawned.Clear();
        }

        private void OnDrawGizmos()
        {
            if (playerAnchor != null && enemyAnchor != null)
            {
                Gizmos.color = new Color(0.4f, 0.8f, 1f, 0.9f);
                Gizmos.DrawSphere(playerAnchor.position, 0.15f);
                Gizmos.color = new Color(1f, 0.4f, 0.4f, 0.9f);
                Gizmos.DrawSphere(enemyAnchor.position, 0.15f);
                Gizmos.color = new Color(1f, 1f, 1f, 0.4f);
                Gizmos.DrawLine(playerAnchor.position, enemyAnchor.position);
            }
        }

        private void OnGUI()
        {
            if (!drawHud) return;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 14 };
            style.normal.textColor = Color.white;

            string slashName = (slashPrefabs.Count > 0 && slashIndex < slashPrefabs.Count && slashPrefabs[slashIndex] != null)
                ? slashPrefabs[slashIndex].name : "(none)";
            string hitName = (hitPrefabs.Count > 0 && hitIndex < hitPrefabs.Count && hitPrefabs[hitIndex] != null)
                ? hitPrefabs[hitIndex].name : "(none)";

            float y = 10f;
            bool slashInk = (slashIndex < slashIsInk.Count) && slashIsInk[slashIndex];
            GUI.Label(new Rect(10, y, 900, 22), $"[Q/E] 斬撃 ({slashIndex + 1}/{slashPrefabs.Count}): {slashName}{(slashInk ? " [INK]" : "")}", style); y += 20f;
            GUI.Label(new Rect(10, y, 900, 22), $"[A/D] ヒット ({hitIndex + 1}/{hitPrefabs.Count}): {hitName}", style); y += 20f;
            GUI.Label(new Rect(10, y, 900, 22), $"[Space] プレイヤー→敵    [Tab] 敵→プレイヤー    [C] 鍔迫り合い    [R] 全消去    [1-9] 斬撃直接選択", style); y += 20f;
            GUI.Label(new Rect(10, y, 900, 22), $"clashPrefabs: {clashPrefabs.Count} (未登録なら hit を流用)", style);
        }

#if UNITY_EDITOR
        // ============================================================
        //  Editor: プレハブ自動収集
        // ============================================================

        private const string VfxRootPath = "Assets/Eric VFX Studio";

        [ContextMenu("Auto Populate (Built-In)")]
        public void AutoPopulateBuiltIn() => AutoPopulate(builtIn: true);

        [ContextMenu("Auto Populate (URP)")]
        public void AutoPopulateUrp() => AutoPopulate(builtIn: false);

        private void AutoPopulate(bool builtIn)
        {
            slashPrefabs.Clear();
            hitPrefabs.Clear();
            clashPrefabs.Clear();
            slashRotationOffsets.Clear();
            hitRotationOffsets.Clear();
            slashIsInk.Clear();
            hitIsInk.Clear();

            string token = builtIn ? "/Built-In/" : "/URP/";
            var slashCollected = new List<(GameObject go, string path)>();
            var hitCollected   = new List<(GameObject go, string path)>();
            var clashCollected = new List<(GameObject go, string path)>();

            var guids = AssetDatabase.FindAssets("t:Prefab", new[] { VfxRootPath });
            foreach (var g in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(g);
                if (!path.Contains(token)) continue;
                var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (go == null) continue;

                string lower = path.ToLowerInvariant();
                bool isClash = lower.Contains("spark") || lower.Contains("impact") || lower.Contains("clash");
                if (isClash)               clashCollected.Add((go, path));
                else if (lower.Contains("hit")) hitCollected.Add((go, path));
                else                       slashCollected.Add((go, path));
            }
            slashCollected.Sort((a, b) => string.Compare(a.go.name, b.go.name, System.StringComparison.Ordinal));
            hitCollected.Sort((a, b) => string.Compare(a.go.name, b.go.name, System.StringComparison.Ordinal));
            clashCollected.Sort((a, b) => string.Compare(a.go.name, b.go.name, System.StringComparison.Ordinal));

            foreach (var (go, path) in slashCollected)
            {
                slashPrefabs.Add(go);
                slashRotationOffsets.Add(DefaultOffsetFor(path));
                slashIsInk.Add(path.ToLowerInvariant().Contains("ink"));
            }
            foreach (var (go, path) in hitCollected)
            {
                hitPrefabs.Add(go);
                hitRotationOffsets.Add(Vector3.zero);
                hitIsInk.Add(path.ToLowerInvariant().Contains("ink"));
            }
            foreach (var (go, _) in clashCollected)
            {
                clashPrefabs.Add(go);
            }

            EditorUtility.SetDirty(this);
            Debug.Log($"[CombatVFXTester] {(builtIn ? "Built-In" : "URP")} 収集: slash={slashPrefabs.Count}, hit={hitPrefabs.Count}, clash={clashPrefabs.Count}");
        }

        /// <summary>
        /// アセットパスに応じた既定の回転オフセット。
        /// Magic Slash 系は元の forward が +X 方向にオーサリングされているため、
        /// LookRotation で +Z に向けると 90deg ずれて見える。 -90deg Y で補正する。
        /// </summary>
        private static Vector3 DefaultOffsetFor(string path)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("magic slash")) return new Vector3(0f, -90f, 0f);
            return Vector3.zero;
        }
#endif
    }
}
