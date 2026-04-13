using System.Collections.Generic;
using UnityEngine;
using InventorySystem;
using InventorySystem.PassiveSkills;

namespace CombatSystem
{
    /// <summary>
    /// 戦闘テスト用コンポーネント
    /// ランダムな敵・ランダムな武器で戦闘を開始する
    /// 
    /// 操作:
    /// - F1: ランダム戦闘開始（敵・武器ともにランダム）
    /// - F2: 次のターンを実行
    /// - F3: 全ターン自動実行
    /// - F4: 戦闘強制終了
    /// </summary>
    public class CombatTestController : MonoBehaviour
    {
        [Header("プレイヤー基本設定")]
        [SerializeField] private int playerMaxHP = 30;
        [Header("フォールバック（武器未装備時）")]
        [SerializeField] private int playerDiceCount = 2;
        [SerializeField] private int playerDiceMax = 6;
        [SerializeField] private int playerCriticalNumerator = 1;

        [Header("デバッグ表示")]
        [SerializeField] private bool showGUI = false;

        private CombatResult? lastResult;
        private TurnResult? lastTurn;
        private string statusText = "待機中...";
        private string weaponText = "";
        private string enemyText = "";
        private CompleteItemData lastEquippedWeapon;

        void Start()
        {
            var cm = CombatManager.Instance;
            cm.OnCombatStart += OnCombatStartHandler;
            cm.OnTurnEnd += OnTurnEndHandler;
            cm.OnCombatEnd += OnCombatEndHandler;
        }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F1))
            {
                StartRandomCombat();
            }
            else if (Input.GetKeyDown(KeyCode.F2))
            {
                if (CombatManager.Instance.IsCombatActive)
                {
                    CombatManager.Instance.ExecuteTurn();
                }
                else
                {
                    Debug.Log("[CombatTest] 戦闘中ではありません");
                }
            }
            else if (Input.GetKeyDown(KeyCode.F3))
            {
                if (CombatManager.Instance.IsCombatActive)
                {
                    CombatManager.Instance.ExecuteFullCombat();
                }
                else
                {
                    Debug.Log("[CombatTest] 戦闘中ではありません");
                }
            }
            else if (Input.GetKeyDown(KeyCode.F4))
            {
                if (CombatManager.Instance.IsCombatActive)
                {
                    Debug.Log("[CombatTest] 戦闘を強制終了（未実装）");
                }
            }
        }

        /// <summary>
        /// 外部から戦闘を開始するメソッド（イベント連携用）
        /// </summary>
        public void StartCombatWithEnemy(string enemyId)
        {
            StartCombatWithEnemyData(EnemyDatabase.Get(enemyId));
        }

        /// <summary>
        /// ランダムな敵・ランダムな武器で戦闘開始
        /// </summary>
        private void StartRandomCombat()
        {
            if (CombatManager.Instance.IsCombatActive)
            {
                Debug.Log("[CombatTest] 既に戦闘中です");
                return;
            }

            // ランダム敵を選択
            var enemy = EnemyDatabase.GetRandom();
            if (enemy == null)
            {
                Debug.LogError("[CombatTest] 敵データが見つかりません");
                return;
            }

            StartCombatWithEnemyData(enemy);
        }

        private void StartCombatWithEnemyData(EnemyData enemy)
        {
            if (enemy == null) return;
            if (CombatManager.Instance.IsCombatActive)
            {
                Debug.Log("[CombatTest] 既に戦闘中です");
                return;
            }

            lastResult = null;
            lastTurn = null;

            // ランダム武器を選択してパッシブスキルを登録
            EquipRandomWeapon();

            // 武器のステータスを使用（未装備時はフォールバック値）
            int diceCount = lastEquippedWeapon != null && lastEquippedWeapon.hasWeaponStats
                ? lastEquippedWeapon.weaponDice.count : playerDiceCount;
            int diceMax = lastEquippedWeapon != null && lastEquippedWeapon.hasWeaponStats
                ? lastEquippedWeapon.weaponDice.maxValue : playerDiceMax;
            int critRate = lastEquippedWeapon != null && lastEquippedWeapon.criticalRate > 0
                ? lastEquippedWeapon.criticalRate : playerCriticalNumerator;

            if (lastEquippedWeapon == null || !lastEquippedWeapon.hasWeaponStats)
            {
                Debug.LogWarning($"[CombatTest] 武器未装備のためフォールバック値を使用: {diceCount}d{diceMax} Crit:{critRate}/9");
            }

            // 装備ダイスの面を取得
            int[] equippedDiceFaces = null;
            var equipHandler = FindObjectOfType<ItemEquipHandler>();
            if (equipHandler != null)
            {
                var equippedDice = equipHandler.GetCurrentEquipment(ItemCategory.Dice);
                if (equippedDice != null && equippedDice.diceFaces != null)
                {
                    equippedDiceFaces = equippedDice.diceFaces;
                }
            }

            CombatManager.Instance.StartCombat(
                enemy, playerMaxHP, diceCount, diceMax, critRate, equippedDiceFaces);
        }

        /// <summary>
        /// Weapon カテゴリからランダムに1つ選び、パッシブスキルを登録
        /// </summary>
        private void EquipRandomWeapon()
        {
            var psm = PassiveSkillManager.Instance;

            // ItemDatabase から武器一覧を取得
            var db = ItemDatabase.Instance;
            if (db == null)
            {
                Debug.LogWarning("[CombatTest] ItemDatabase が見つかりません。スキルなしで戦闘します");
                return;
            }

            var weapons = db.GetItemsByCategory(ItemCategory.Weapon);
            if (weapons == null || weapons.Count == 0)
            {
                Debug.LogWarning("[CombatTest] 武器データがありません。スキルなしで戦闘します");
                return;
            }

            // ランダムに1本選択
            var weapon = weapons[Random.Range(0, weapons.Count)];
            lastEquippedWeapon = weapon;

            // パッシブスキルを登録
            psm.RefreshActiveSkills(new List<CompleteItemData> { weapon });

            // スキル名一覧をログ出力
            string skillList = "";
            if (weapon.passiveSkills != null)
            {
                foreach (var ps in weapon.passiveSkills)
                    skillList += $"  [{ps.internalName}] {ps.skillName}\n";
            }
            string diceInfo = weapon.hasWeaponStats ? $" {weapon.weaponDice.count}d{weapon.weaponDice.maxValue}" : "";
            string critInfo = weapon.criticalRate > 0 ? $" Crit:{weapon.criticalRate}/9" : "";
            weaponText = $"{weapon.displayName}{diceInfo}{critInfo} (スキル{weapon.passiveSkills?.Count ?? 0}個)";
            Debug.Log($"[CombatTest] ランダム武器: {weapon.displayName}{diceInfo}{critInfo}\n{skillList}");
        }

        private void OnCombatStartHandler(string id)
        {
            var cm = CombatManager.Instance;
            var enemy = EnemyDatabase.Get(id);
            enemyText = $"{enemy?.displayName ?? id} (HP:{enemy?.maxHP} {enemy?.DiceNotation} Crit:{enemy?.criticalNumerator}/9)";
            statusText = $"戦闘開始: {enemy?.displayName ?? id}";
            Debug.Log($"[CombatTest] ====== 戦闘開始 ======\n" +
                      $"対戦相手: {enemyText}\n" +
                      $"プレイヤーHP: {cm.PlayerHP}/{cm.PlayerMaxHP} | 敵HP: {cm.EnemyHP}/{cm.EnemyMaxHP}");
        }

        private void OnTurnEndHandler(TurnResult turn)
        {
            lastTurn = turn;
            var cm = CombatManager.Instance;
            string critText = turn.isCritical ? " ★CRIT★" : "";
            string winText = turn.isDraw ? "引き分け" : (turn.playerWon ? "勝利" : "敗北");
            statusText = $"Turn {turn.turnNumber}: {winText}{critText} " +
                         $"(P:{turn.playerDiceTotal} vs E:{turn.enemyDiceTotal}) " +
                         $"DMG:{turn.totalDamage} | 残HP P:{cm.PlayerHP}/{cm.PlayerMaxHP} E:{cm.EnemyHP}/{cm.EnemyMaxHP}";
            Debug.Log($"[CombatTest] Turn {turn.turnNumber}: {winText}{critText} " +
                      $"(P:{turn.playerDiceTotal} vs E:{turn.enemyDiceTotal}) DMG:{turn.totalDamage}\n" +
                      $"  残HP → プレイヤー: {cm.PlayerHP}/{cm.PlayerMaxHP} | 敵: {cm.EnemyHP}/{cm.EnemyMaxHP}");

            // 発動したスキルをログ出力
            var psm = PassiveSkillManager.Instance;
            var triggeredPlayer = psm.GetTriggeredPlayerSkills();
            var triggeredEnemy = psm.GetTriggeredEnemySkills();
            if (triggeredPlayer.Count > 0)
                Debug.Log($"  [発動スキル(味方)] {string.Join(", ", triggeredPlayer)}");
            if (triggeredEnemy.Count > 0)
                Debug.Log($"  [発動スキル(敵)] {string.Join(", ", triggeredEnemy)}");
        }

        private void OnCombatEndHandler(CombatResult result)
        {
            lastResult = result;
            string resultText = result.playerWon ? "★ 勝利 ★" : "✗ 敗北 ✗";
            statusText = $"戦闘終了: {result.enemyDisplayName} - {resultText} " +
                         $"({result.totalTurns}ターン) 最終HP P:{result.playerHPRemaining} E:{result.enemyHPRemaining}";
            Debug.Log($"[CombatTest] ====== 戦闘終了 ======\n" +
                      $"{result.enemyDisplayName} - {resultText}\n" +
                      $"{result.totalTurns}ターン\n" +
                      $"最終HP → プレイヤー: {result.playerHPRemaining} | 敵: {result.enemyHPRemaining}");

            // 戦闘終了時に次の武器をランダム装備（次戦に備える）
            EquipRandomWeapon();
            Debug.Log($"[CombatTest] 次戦用の武器を装備: {weaponText}");
        }

        void OnGUI()
        {
            return; // テストUI一時無効化
            if (!showGUI) return;

            GUILayout.BeginArea(new Rect(10, 10, 520, 800));

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label);
            titleStyle.fontSize = 16;
            titleStyle.fontStyle = FontStyle.Bold;
            GUILayout.Label("=== Combat Test ===", titleStyle);

            GUILayout.Label($"Status: {statusText}");
            GUILayout.Label("F1: ランダム戦闘 | F2: 1ターン | F3: 全自動 | F4: 終了");

            if (!string.IsNullOrEmpty(weaponText))
                GUILayout.Label($"武器: {weaponText}");
            if (!string.IsNullOrEmpty(enemyText))
                GUILayout.Label($"敵: {enemyText}");

            // 残HP表示
            if (CombatManager.Instance != null && CombatManager.Instance.IsCombatActive)
            {
                var cm = CombatManager.Instance;
                GUILayout.Label($"プレイヤーHP: {cm.PlayerHP} / {cm.PlayerMaxHP}   |   敵HP: {cm.EnemyHP} / {cm.EnemyMaxHP}");
            }
            else if (lastResult.HasValue)
            {
                var r = lastResult.Value;
                GUILayout.Label($"最終HP → プレイヤー: {r.playerHPRemaining}   |   敵: {r.enemyHPRemaining}");
            }

            if (lastTurn.HasValue)
            {
                var t = lastTurn.Value;
                GUILayout.Label($"最終ターン: {t.turnNumber} | " +
                    $"Player[{string.Join(",", t.playerDice)}]={t.playerDiceTotal} " +
                    $"Enemy[{string.Join(",", t.enemyDice)}]={t.enemyDiceTotal}");
            }

            if (lastResult.HasValue)
            {
                var r = lastResult.Value;
                string rText = r.playerWon ? "★勝利★" : "✗敗北✗";
                GUILayout.Label($"結果: {rText} ({r.totalTurns}ターン)");
            }

            // スキル表示セクション
            DrawSkillSection();

            GUILayout.EndArea();
        }

        /// <summary>
        /// パッシブスキルの状態を表示
        /// 白色: 所持中（未発動）、黄色: 今ターン発動中
        /// </summary>
        private void DrawSkillSection()
        {
            var psm = PassiveSkillManager.Instance;
            if (psm == null) return;

            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label);
            sectionStyle.fontSize = 13;
            sectionStyle.fontStyle = FontStyle.Bold;

            GUIStyle skillNormalStyle = new GUIStyle(GUI.skin.label);
            skillNormalStyle.normal.textColor = Color.white;

            GUIStyle skillTriggeredStyle = new GUIStyle(GUI.skin.label);
            skillTriggeredStyle.normal.textColor = Color.yellow;
            skillTriggeredStyle.fontStyle = FontStyle.Bold;

            const int skillsPerRow = 3;
            const float skillWidth = 165f;

            // --- プレイヤースキル ---
            var playerSkills = psm.GetActiveSkillNames();
            if (playerSkills.Count > 0)
            {
                GUILayout.Space(5);
                sectionStyle.normal.textColor = Color.cyan;
                GUILayout.Label("▼ プレイヤースキル", sectionStyle);

                for (int i = 0; i < playerSkills.Count; i++)
                {
                    if (i % skillsPerRow == 0) GUILayout.BeginHorizontal();

                    var skillId = playerSkills[i];
                    bool triggered = psm.IsPlayerSkillTriggered(skillId);
                    GUIStyle style = triggered ? skillTriggeredStyle : skillNormalStyle;
                    string prefix = triggered ? "★" : "・";
                    string displayName = psm.GetSkillDisplayName(skillId);
                    GUILayout.Label($"{prefix}{displayName}", style, GUILayout.Width(skillWidth));

                    if (i % skillsPerRow == skillsPerRow - 1 || i == playerSkills.Count - 1)
                        GUILayout.EndHorizontal();
                }
            }

            // --- 敵スキル ---
            var enemySkills = psm.GetEnemySkillNames();
            if (enemySkills.Count > 0)
            {
                GUILayout.Space(3);
                sectionStyle.normal.textColor = new Color(1f, 0.5f, 0.5f);
                GUILayout.Label("▼ 敵スキル", sectionStyle);

                for (int i = 0; i < enemySkills.Count; i++)
                {
                    if (i % skillsPerRow == 0) GUILayout.BeginHorizontal();

                    var skillId = enemySkills[i];
                    bool triggered = psm.IsEnemySkillTriggered(skillId);
                    GUIStyle style = triggered ? skillTriggeredStyle : skillNormalStyle;
                    string prefix = triggered ? "★" : "・";
                    string displayName = psm.GetSkillDisplayName(skillId);
                    GUILayout.Label($"{prefix}{displayName}", style, GUILayout.Width(skillWidth));

                    if (i % skillsPerRow == skillsPerRow - 1 || i == enemySkills.Count - 1)
                        GUILayout.EndHorizontal();
                }
            }
        }

        void OnDestroy()
        {
            var cm = CombatManager.Instance;
            if (cm != null)
            {
                cm.OnCombatStart -= OnCombatStartHandler;
                cm.OnTurnEnd -= OnTurnEndHandler;
                cm.OnCombatEnd -= OnCombatEndHandler;
            }
        }
    }
}
