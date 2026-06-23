using System;
using System.Collections;
using UnityEngine;

namespace CombatSystem.DiceLED
{
    /// <summary>
    /// 全サイコロ LED の統合管理クラス。
    /// 
    /// <para><b>構成:</b></para>
    /// プレイヤー 5 個 + 敵 5 個 = 計 10 個のサイコロ（90 LED）
    /// 
    /// <para><b>パフォーマンス対策:</b></para>
    /// <list type="bullet">
    ///   <item>全 90 LED が <b>1 つのマテリアル</b> を共有 → ドローコール最小化</item>
    ///   <item>GPU Instancing 有効 → 同メッシュ LED は 1 ドローコールにバッチ</item>
    ///   <item>MaterialPropertyBlock → マテリアルコピー不要（メモリ節約）</item>
    ///   <item>ローリング中のみ更新、静止時は更新ゼロ</item>
    ///   <item>更新レート制御（rollingInterval）で GPU 負荷平滑化</item>
    /// </list>
    /// 
    /// <para><b>使い方（CombatManager から）:</b></para>
    /// <code>
    /// var mgr = DiceLEDManager.Instance;
    /// mgr.SetActiveDiceCount(playerDiceCount, enemyDiceCount);
    /// mgr.PlayRollingAnimation(playerDice, enemyDice, playerDiceMax, enemyDiceMax);
    /// </code>
    /// </summary>
    public class DiceLEDManager : MonoBehaviour
    {
        // =================================================================
        //  シングルトン
        // =================================================================

        private static DiceLEDManager instance;

        public static DiceLEDManager Instance
        {
            get
            {
                if (instance == null)
                    instance = FindObjectOfType<DiceLEDManager>();
                return instance;
            }
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(this);
                return;
            }
            instance = this;
        }

        // =================================================================
        //  Inspector
        // =================================================================

        [Header("=== サイコロ参照 ===")]
        [Tooltip("プレイヤー側のサイコロ（最大 5 個）")]
        [SerializeField] private SingleDiceLED[] playerDice = new SingleDiceLED[5];

        [Tooltip("敵側のサイコロ（最大 5 個）")]
        [SerializeField] private SingleDiceLED[] enemyDice = new SingleDiceLED[5];

        [Header("=== モニター ===")]
        [Tooltip("プレイヤーの合計値モニター")]
        [SerializeField] private DiceMonitorDisplay playerMonitor;

        [Tooltip("敵の合計値モニター")]
        [SerializeField] private DiceMonitorDisplay enemyMonitor;

        [Header("=== LED カラー ===")]
        [Tooltip("プレイヤーの LED 発光色")]
        [SerializeField] private Color playerLEDColor = new Color(0.2f, 0.8f, 1f);   // 水色

        [Tooltip("敵の LED 発光色")]
        [SerializeField] private Color enemyLEDColor = new Color(1f, 0.3f, 0.2f);     // 赤

        [Tooltip("消灯時の色（暗灰色にすると LED の存在感が出る）")]
        [SerializeField] private Color offColor = new Color(0.05f, 0.05f, 0.05f);

        [Tooltip("Emission 強度（HDR 倍率）。Bloom と併用で光り感アップ")]
        [SerializeField, Range(1f, 20f)] private float emissionIntensity = 5f;

        [Header("=== ローリングアニメーション ===")]
        [Tooltip("ローリング中の LED 更新間隔（秒）。小さいほど激しく明滅")]
        [SerializeField, Range(0.02f, 0.2f)] private float rollingInterval = 0.06f;

        [Tooltip("ローリングの総時間（秒）")]
        [SerializeField, Range(0.5f, 5f)] private float rollingDuration = 1.5f;

        [Tooltip("全最大値が出たときのローリング時間倍率（期待感を煽る）")]
        [SerializeField, Range(1f, 5f)] private float maxValueRollingMultiplier = 2f;

        [Tooltip("各サイコロが確定するまでの段階的遅延（秒）")]
        [SerializeField, Range(0f, 0.3f)] private float settleStagger = 0.12f;

        [Tooltip("確定時のフラッシュ回数（0 で無効）")]
        [SerializeField, Range(0, 5)] private int settleFlashCount = 2;

        [Header("=== 全最大値演出 (MAX!) ===")]
        [Tooltip("全ダイス最大値時の点滅回数")]
        [SerializeField, Range(2, 10)] private int maxFlashCount = 5;

        [Tooltip("点滅の速度（秒）")]
        [SerializeField, Range(0.03f, 0.2f)] private float maxFlashInterval = 0.06f;

        [Tooltip("最大値演出時の発光色（HDR）")]
        [SerializeField] private Color maxFlashColor = new Color(1f, 0.95f, 0.3f); // 金色

        [Tooltip("最大値演出時の発光強度倍率")]
        [SerializeField, Range(1f, 5f)] private float maxFlashIntensityMultiplier = 2f;

        // =================================================================
        //  状態
        // =================================================================

        private bool isRolling;
        private Coroutine rollingCoroutine;

        /// <summary>ローリング中かどうか</summary>
        public bool IsRolling => isRolling;

        // =================================================================
        //  イベント
        // =================================================================

        /// <summary>ローリングアニメーション開始時に発火（バトル演出の鍔迫り合い開始用）</summary>
        public event Action OnRollingStart;

        /// <summary>ローリングアニメーション完了時に発火</summary>
        public event Action OnRollingComplete;

        /// <summary>全ダイスが最大値だったときに発火（引数: isPlayer）</summary>
        public event Action<bool> OnAllMax;

        // =================================================================
        //  初期化
        // =================================================================

        void Start()
        {
            ApplyColors();
            InitializeMonitors();
            TurnOffAll();
        }

        /// <summary>全サイコロに色設定を反映</summary>
        private void ApplyColors()
        {
            foreach (var die in playerDice)
                if (die != null)
                    die.SetColors(playerLEDColor, offColor, emissionIntensity);

            foreach (var die in enemyDice)
                if (die != null)
                    die.SetColors(enemyLEDColor, offColor, emissionIntensity);
        }

        /// <summary>モニターを初期化し、ダイスカラーに合わせる</summary>
        private void InitializeMonitors()
        {
            if (playerMonitor != null)
            {
                playerMonitor.SetGlowColor(playerLEDColor);
                if (!playerMonitor.IsInitialized) playerMonitor.Initialize();
            }
            if (enemyMonitor != null)
            {
                enemyMonitor.SetGlowColor(enemyLEDColor);
                if (!enemyMonitor.IsInitialized) enemyMonitor.Initialize();
            }
        }

        /// <summary>モニターに合計値を表示</summary>
        private void UpdateMonitor(DiceMonitorDisplay monitor, int total)
        {
            if (monitor != null && monitor.IsInitialized)
                monitor.DisplayNumber(total);
        }

        /// <summary>モニターをクリア</summary>
        private void ClearMonitors()
        {
            if (playerMonitor != null && playerMonitor.IsInitialized)
                playerMonitor.ClearDisplay();
            if (enemyMonitor != null && enemyMonitor.IsInitialized)
                enemyMonitor.ClearDisplay();
        }

        // =================================================================
        //  公開 API
        // =================================================================

        /// <summary>全 LED 消灯 + モニタークリア</summary>
        public void TurnOffAll()
        {
            StopRolling();

            foreach (var die in playerDice)
                if (die != null) { die.TurnOff(); die.ApplyVisuals(); }

            foreach (var die in enemyDice)
                if (die != null) { die.TurnOff(); die.ApplyVisuals(); }

            ClearMonitors();
        }

        /// <summary>
        /// 使用するサイコロ数を設定し、余剰サイコロを消灯。
        /// 戦闘開始時に呼ぶ。
        /// </summary>
        public void SetActiveDiceCount(int playerCount, int enemyCount)
        {
            for (int i = 0; i < playerDice.Length; i++)
            {
                if (playerDice[i] != null && i >= playerCount)
                {
                    playerDice[i].TurnOff();
                    playerDice[i].ApplyVisuals();
                }
            }

            for (int i = 0; i < enemyDice.Length; i++)
            {
                if (enemyDice[i] != null && i >= enemyCount)
                {
                    enemyDice[i].TurnOff();
                    enemyDice[i].ApplyVisuals();
                }
            }
        }

        /// <summary>
        /// 出目を即座に表示（アニメーション無し）。
        /// デバッグや即時表示用。
        /// </summary>
        public void ShowResultImmediate(int[] playerValues, int[] enemyValues)
        {
            StopRolling();
            SetDiceValues(playerDice, playerValues);
            SetDiceValues(enemyDice, enemyValues);
        }

        /// <summary>
        /// ローリングアニメーション → 段階的確定 → フラッシュ演出
        /// </summary>
        /// <param name="playerValues">プレイヤーの最終出目配列</param>
        /// <param name="enemyValues">敵の最終出目配列</param>
        /// <param name="playerDiceMax">プレイヤーのダイス最大値（ローリング表示用）</param>
        /// <param name="enemyDiceMax">敵のダイス最大値（ローリング表示用）</param>
        public void PlayRollingAnimation(
            int[] playerValues, int[] enemyValues,
            int playerDiceMax, int enemyDiceMax)
        {
            StopRolling();
            rollingCoroutine = StartCoroutine(
                RollingSequence(playerValues, enemyValues,
                                playerDiceMax, enemyDiceMax));
        }

        /// <summary>
        /// 指定した色で Emission を再設定する（演出カスタマイズ用）
        /// </summary>
        public void SetPlayerColor(Color color)
        {
            playerLEDColor = color;
            foreach (var die in playerDice)
                if (die != null)
                    die.SetColors(playerLEDColor, offColor, emissionIntensity);
        }

        public void SetEnemyColor(Color color)
        {
            enemyLEDColor = color;
            foreach (var die in enemyDice)
                if (die != null)
                    die.SetColors(enemyLEDColor, offColor, emissionIntensity);
        }

        // =================================================================
        //  ローリングアニメーション
        // =================================================================

        private void StopRolling()
        {
            if (rollingCoroutine != null)
            {
                StopCoroutine(rollingCoroutine);
                rollingCoroutine = null;
            }
            isRolling = false;
        }

        /// <summary>
        /// ローリング → スタガー確定 → フラッシュ の一連シーケンス
        /// </summary>
        private IEnumerator RollingSequence(
            int[] playerValues, int[] enemyValues,
            int playerDiceMax, int enemyDiceMax)
        {
            isRolling = true;
            OnRollingStart?.Invoke();

            int playerCount = playerValues?.Length ?? 0;
            int enemyCount  = enemyValues?.Length  ?? 0;

            // 全最大値の側だけローリング時間を延長（期待感演出）
            bool playerAllMaxEarly = IsAllMax(playerValues, playerDiceMax);
            bool enemyAllMaxEarly  = IsAllMax(enemyValues, enemyDiceMax);
            float playerDuration = playerAllMaxEarly
                ? rollingDuration * maxValueRollingMultiplier : rollingDuration;
            float enemyDuration = enemyAllMaxEarly
                ? rollingDuration * maxValueRollingMultiplier : rollingDuration;
            float maxDuration = Mathf.Max(playerDuration, enemyDuration);

            // ----- フェーズ 1: 高速ローリング -----
            // 時間経過とともに更新間隔が広がる（スローダウン演出）
            // 非MAX側が先に止まり、MAX側だけ回り続ける
            float elapsed      = 0f;
            float baseInterval = rollingInterval;
            bool playerStopped = false;
            bool enemyStopped  = false;

            while (elapsed < maxDuration)
            {
                float progress = elapsed / maxDuration;
                float currentInterval = Mathf.Lerp(
                    baseInterval, baseInterval * 3.5f, progress * progress);

                // プレイヤー側: まだ回転中なら更新、時間超過で確定表示
                if (!playerStopped && elapsed >= playerDuration)
                {
                    playerStopped = true;
                    SetDiceValues(playerDice, playerValues);
                    UpdateMonitor(playerMonitor, SumValues(playerValues));
                }
                else if (!playerStopped)
                {
                    int pRandomTotal = 0;
                    for (int i = 0; i < playerCount && i < playerDice.Length; i++)
                    {
                        if (playerDice[i] != null)
                        {
                            playerDice[i].SetRandomValue(playerDiceMax);
                            playerDice[i].ApplyVisuals();
                            pRandomTotal += playerDice[i].CurrentValue;
                        }
                    }
                    UpdateMonitor(playerMonitor, pRandomTotal);
                }

                // 敵側: まだ回転中なら更新、時間超過で確定表示
                if (!enemyStopped && elapsed >= enemyDuration)
                {
                    enemyStopped = true;
                    SetDiceValues(enemyDice, enemyValues);
                    UpdateMonitor(enemyMonitor, SumValues(enemyValues));
                }
                else if (!enemyStopped)
                {
                    int eRandomTotal = 0;
                    for (int i = 0; i < enemyCount && i < enemyDice.Length; i++)
                    {
                        if (enemyDice[i] != null)
                        {
                            enemyDice[i].SetRandomValue(enemyDiceMax);
                            enemyDice[i].ApplyVisuals();
                            eRandomTotal += enemyDice[i].CurrentValue;
                        }
                    }
                    UpdateMonitor(enemyMonitor, eRandomTotal);
                }

                yield return new WaitForSeconds(currentInterval);
                elapsed += currentInterval;
            }

            // ----- フェーズ 2: 段階的確定（スタガー）-----
            // プレイヤーのサイコロを 1 個ずつ確定
            int pRunningTotal = 0;
            for (int i = 0; i < playerCount && i < playerDice.Length; i++)
            {
                if (playerDice[i] != null)
                {
                    playerDice[i].SetValue(playerValues[i]);
                    playerDice[i].ApplyVisuals();
                    pRunningTotal += playerValues[i];
                    UpdateMonitor(playerMonitor, pRunningTotal);
                }
                if (settleStagger > 0f)
                    yield return new WaitForSeconds(settleStagger);
            }

            // 敵のサイコロを 1 個ずつ確定
            int eRunningTotal = 0;
            for (int i = 0; i < enemyCount && i < enemyDice.Length; i++)
            {
                if (enemyDice[i] != null)
                {
                    enemyDice[i].SetValue(enemyValues[i]);
                    enemyDice[i].ApplyVisuals();
                    eRunningTotal += enemyValues[i];
                    UpdateMonitor(enemyMonitor, eRunningTotal);
                }
                if (settleStagger > 0f)
                    yield return new WaitForSeconds(settleStagger);
            }

            // ----- フェーズ 3: 確定フラッシュ -----
            if (settleFlashCount > 0)
            {
                yield return StartCoroutine(
                    FlashSequence(playerValues, enemyValues,
                                  playerCount, enemyCount));
            }

            // ----- フェーズ 4: 全最大値判定 & 特殊演出 -----
            bool playerAllMax = IsAllMax(playerValues, playerDiceMax);
            bool enemyAllMax  = IsAllMax(enemyValues, enemyDiceMax);

            if (playerAllMax)
            {
                yield return StartCoroutine(
                    MaxCelebrationSequence(playerDice, playerValues, playerCount,
                                           playerLEDColor));
                OnAllMax?.Invoke(true);
            }

            if (enemyAllMax)
            {
                yield return StartCoroutine(
                    MaxCelebrationSequence(enemyDice, enemyValues, enemyCount,
                                           enemyLEDColor));
                OnAllMax?.Invoke(false);
            }

            isRolling = false;
            OnRollingComplete?.Invoke();
        }

        /// <summary>
        /// 全消灯 ↔ 結果表示 を繰り返すフラッシュ演出
        /// </summary>
        private IEnumerator FlashSequence(
            int[] playerValues, int[] enemyValues,
            int playerCount, int enemyCount)
        {
            const float flashInterval = 0.08f;

            for (int flash = 0; flash < settleFlashCount; flash++)
            {
                // 消灯
                SetAllActiveValue(playerDice, playerCount, 0);
                SetAllActiveValue(enemyDice, enemyCount, 0);
                yield return new WaitForSeconds(flashInterval);

                // 点灯（結果表示）
                SetDiceValues(playerDice, playerValues);
                SetDiceValues(enemyDice, enemyValues);
                yield return new WaitForSeconds(flashInterval);
            }
        }

        // =================================================================
        //  全最大値演出
        // =================================================================

        /// <summary>全ダイスが最大値か判定</summary>
        private bool IsAllMax(int[] values, int diceMax)
        {
            if (values == null || values.Length == 0) return false;
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] != diceMax) return false;
            }
            return true;
        }

        /// <summary>
        /// 全最大値時の特殊演出:
        ///   1. 出目パターンのまま 金色点灯 ↔ 消灯 の高速点滅
        ///   2. ウェーブで1個ずつ元の色に戻す
        ///   3. 瞬間金色ブーストで締め
        /// </summary>
        private IEnumerator MaxCelebrationSequence(
            SingleDiceLED[] dice, int[] values, int count, Color originalColor)
        {
            float boostedIntensity = emissionIntensity * maxFlashIntensityMultiplier;

            // --- フェーズ A: 出目パターンで金色点滅 ---
            for (int flash = 0; flash < maxFlashCount; flash++)
            {
                float t = (float)flash / maxFlashCount;
                float interval = Mathf.Lerp(maxFlashInterval,
                                            maxFlashInterval * 2.5f, t);

                // 金色で出目を表示
                SetColorForDice(dice, count, maxFlashColor, offColor,
                                boostedIntensity);
                SetDiceValues(dice, values);
                yield return new WaitForSeconds(interval);

                // 消灯
                SetAllActiveValue(dice, count, 0);
                yield return new WaitForSeconds(interval * 0.6f);
            }

            // --- フェーズ B: ウェーブ点灯（1個ずつ順に最大値に戻す）---
            SetColorForDice(dice, count, originalColor, offColor,
                            emissionIntensity);
            for (int i = 0; i < count && i < dice.Length; i++)
            {
                if (dice[i] != null)
                {
                    dice[i].SetValue(values[i]);
                    dice[i].ApplyVisuals();
                }
                yield return new WaitForSeconds(0.08f);
            }

            // --- フェーズ C: 最後に全体パッと光らせて完了 ---
            yield return new WaitForSeconds(0.15f);

            // 瞬間金色ブースト
            SetColorForDice(dice, count, maxFlashColor, offColor,
                            boostedIntensity);
            SetDiceValues(dice, values);
            yield return new WaitForSeconds(0.12f);

            // 元の色に戻す
            SetColorForDice(dice, count, originalColor, offColor,
                            emissionIntensity);
            SetDiceValues(dice, values);
        }

        /// <summary>指定ダイス群の発光色を一括変更</summary>
        private void SetColorForDice(
            SingleDiceLED[] dice, int count,
            Color onColor, Color off, float intensity)
        {
            for (int i = 0; i < count && i < dice.Length; i++)
            {
                if (dice[i] != null)
                    dice[i].SetColors(onColor, off, intensity);
            }
        }

        // =================================================================
        //  ヘルパー
        // =================================================================

        /// <summary>出目配列の合計を計算</summary>
        private static int SumValues(int[] values)
        {
            if (values == null) return 0;
            int sum = 0;
            for (int i = 0; i < values.Length; i++) sum += values[i];
            return sum;
        }

        /// <summary>出目配列に従ってサイコロを設定</summary>
        private void SetDiceValues(SingleDiceLED[] dice, int[] values)
        {
            if (values == null) return;
            for (int i = 0; i < values.Length && i < dice.Length; i++)
            {
                if (dice[i] != null)
                {
                    dice[i].SetValue(values[i]);
                    dice[i].ApplyVisuals();
                }
            }
        }

        /// <summary>指定数のサイコロを同じ値に設定</summary>
        private void SetAllActiveValue(SingleDiceLED[] dice, int count, int value)
        {
            for (int i = 0; i < count && i < dice.Length; i++)
            {
                if (dice[i] != null)
                {
                    dice[i].SetValue(value);
                    dice[i].ApplyVisuals();
                }
            }
        }

        // =================================================================
        //  エディタ用: DICE_N 自動検索
        // =================================================================

        /// <summary>サイコロの命名プレフィックス</summary>
        private const string DICE_PREFIX = "DICE_";

        /// <summary>
        /// シーン内から "DICE_1"～"DICE_10" を自動検索して
        /// playerDice[0..4] / enemyDice[0..4] に割り当てる。
        /// 
        /// 命名規則:
        ///   DICE_1 ～ DICE_5  → プレイヤー側
        ///   DICE_6 ～ DICE_10 → 敵側
        /// 
        /// 各 DICE_N は子に LED_0～LED_8 を持ち、
        /// SingleDiceLED コンポーネントが付いている必要がある。
        /// 
        /// 右クリック → "Auto-Assign All Dice (DICE_1～DICE_10)" で実行。
        /// </summary>
        [ContextMenu("Auto-Assign All Dice (DICE_1～DICE_10)")]
        public void AutoAssignAllDice()
        {
            // シーン内の全 SingleDiceLED を取得
            var allDice = FindObjectsOfType<SingleDiceLED>(true);
            var sorted = new System.Collections.Generic.List<SingleDiceLED>();

            foreach (var d in allDice)
            {
                if (d.gameObject.name.StartsWith(DICE_PREFIX,
                        System.StringComparison.OrdinalIgnoreCase))
                    sorted.Add(d);
            }

            // DICE_1, DICE_2, ... の番号でソート
            sorted.Sort((a, b) =>
            {
                int numA = ExtractDiceNumber(a.gameObject.name);
                int numB = ExtractDiceNumber(b.gameObject.name);
                return numA.CompareTo(numB);
            });

            // DICE_1～5 → player, DICE_6～10 → enemy
            playerDice = new SingleDiceLED[5];
            enemyDice  = new SingleDiceLED[5];

            int assignedPlayer = 0;
            int assignedEnemy  = 0;

            foreach (var d in sorted)
            {
                int num = ExtractDiceNumber(d.gameObject.name);
                if (num >= 1 && num <= 5 && assignedPlayer < 5)
                {
                    playerDice[num - 1] = d;
                    assignedPlayer++;
                }
                else if (num >= 6 && num <= 10 && assignedEnemy < 5)
                {
                    enemyDice[num - 6] = d;
                    assignedEnemy++;
                }
            }

            Debug.Log($"[DiceLEDManager] DICE_* 検出: {sorted.Count} 個");
            Debug.Log($"  Player (DICE_1～5): {assignedPlayer} 個割り当て");
            Debug.Log($"  Enemy  (DICE_6～10): {assignedEnemy} 個割り当て");

            for (int i = 0; i < 5; i++)
            {
                string pName = playerDice[i] != null ? playerDice[i].gameObject.name : "(なし)";
                string eName = enemyDice[i]  != null ? enemyDice[i].gameObject.name  : "(なし)";
                Debug.Log($"  [{i}] P={pName}  E={eName}");
            }
        }

        /// <summary>
        /// 全 DICE_N の子 LED も一括で座標ベースで自動割り当てする。
        /// DICE の検索 → 各サイコロの子 Renderer を座標ソートでマッピング。
        /// 
        /// 右クリック → "Auto-Assign All (Dice + LEDs)" で実行。
        /// </summary>
        [ContextMenu("Auto-Assign All (Dice + LEDs)")]
        public void AutoAssignAllDiceAndLEDs()
        {
            AutoAssignAllDice();

            int count = 0;
            foreach (var d in playerDice)
            {
                if (d != null) { d.AutoAssignRenderers(); count++; }
            }
            foreach (var d in enemyDice)
            {
                if (d != null) { d.AutoAssignRenderers(); count++; }
            }

            Debug.Log($"[DiceLEDManager] {count} 個のサイコロの LED を座標ベースで自動割り当てしました");
        }

        /// <summary>"DICE_3" → 3</summary>
        private static int ExtractDiceNumber(string name)
        {
            if (name.Length <= DICE_PREFIX.Length) return 999;
            string numPart = name.Substring(DICE_PREFIX.Length);
            return int.TryParse(numPart, out int n) ? n : 999;
        }

        // =================================================================
        //  エディタ検証用
        // =================================================================

#if UNITY_EDITOR
        [ContextMenu("Test: Show All Value 5")]
        private void TestShowAll5()
        {
            ApplyColors();
            foreach (var die in playerDice)
                if (die != null) { die.SetValue(5); die.ApplyVisuals(); }
            foreach (var die in enemyDice)
                if (die != null) { die.SetValue(5); die.ApplyVisuals(); }
        }

        [ContextMenu("Test: Turn Off All")]
        private void TestTurnOff()
        {
            foreach (var die in playerDice)
                if (die != null) { die.TurnOff(); die.ApplyVisuals(); }
            foreach (var die in enemyDice)
                if (die != null) { die.TurnOff(); die.ApplyVisuals(); }
        }
#endif

        void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }
    }
}
