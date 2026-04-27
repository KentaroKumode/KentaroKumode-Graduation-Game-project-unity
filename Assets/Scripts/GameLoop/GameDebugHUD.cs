using System.Collections.Generic;
using UnityEngine;
using CombatSystem;
using MapSystem;

namespace GameLoop
{
    /// <summary>
    /// マップベースゲームループの状態をIMGUIで表示するデバッグHUD。
    /// マップ・空腹度・移動先・戦闘情報をテキスト表示。
    /// 本番UIに差し替え可能 — このコンポーネントを外すだけで消える。
    /// </summary>
    public class GameDebugHUD : MonoBehaviour
    {
        [Header("表示設定")]
        [SerializeField] private bool showHUD = true;
        [SerializeField] private int fontSize = 14;
        [SerializeField] private bool showMap = true;

        private string phaseText = "";
        private string statusText = "";
        private string mapText = "";
        private string moveText = "";
        private string battleText = "";
        private string resultText = "";
        private string helpText = "";

        void Start()
        {
            var gm = GameManager.Instance;
            if (gm == null)
            {
                Debug.LogWarning("[GameDebugHUD] GameManager が見つかりません");
                return;
            }

            gm.OnPhaseChanged += OnPhaseChanged;
            gm.OnRunStarted += OnRunStarted;
            gm.OnEnemyEncountered += OnEnemyEncountered;
            gm.OnBattleEnded += OnBattleEnded;
            gm.OnRewardGranted += OnRewardGranted;
            gm.OnFloorAdvanced += OnFloorAdvanced;
            gm.OnRunCleared += OnRunCleared;
            gm.OnGameOver += OnGameOver;
            gm.OnStarvationDamage += OnStarvation;
            gm.OnTileActivated += OnTileActivated;
            gm.OnFloorModifierApplied += OnFloorModifier;

            if (CombatManager.Instance != null)
                CombatManager.Instance.OnTurnEnd += OnTurnEnd;

            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnMapGenerated += OnMapGenerated;
                MapManager.Instance.OnMysteryResolved += OnMysteryResolved;
            }

            UpdateHelpText(GameManager.GamePhase.Title);
        }

        void OnDestroy()
        {
            var gm = GameManager.Instance;
            if (gm != null)
            {
                gm.OnPhaseChanged -= OnPhaseChanged;
                gm.OnRunStarted -= OnRunStarted;
                gm.OnEnemyEncountered -= OnEnemyEncountered;
                gm.OnBattleEnded -= OnBattleEnded;
                gm.OnRewardGranted -= OnRewardGranted;
                gm.OnFloorAdvanced -= OnFloorAdvanced;
                gm.OnRunCleared -= OnRunCleared;
                gm.OnGameOver -= OnGameOver;
                gm.OnStarvationDamage -= OnStarvation;
                gm.OnTileActivated -= OnTileActivated;
                gm.OnFloorModifierApplied -= OnFloorModifier;
            }

            if (CombatManager.Instance != null)
                CombatManager.Instance.OnTurnEnd -= OnTurnEnd;

            if (MapManager.Instance != null)
            {
                MapManager.Instance.OnMapGenerated -= OnMapGenerated;
                MapManager.Instance.OnMysteryResolved -= OnMysteryResolved;
            }
        }

        // === イベントハンドラ ===

        private void OnPhaseChanged(GameManager.GamePhase phase)
        {
            phaseText = $"フェーズ: {PhaseToJapanese(phase)}";
            UpdateHelpText(phase);

            if (phase == GameManager.GamePhase.MapNavigation)
            {
                UpdateMoveOptions();
                if (showMap) mapText = BuildMapDisplay();
            }
            else
            {
                moveText = "";
            }
        }

        private void OnRunStarted(RunState run)
        {
            statusText = FormatStatus(run);
            battleText = "";
            resultText = "";
            mapText = "";
        }

        private void OnMapGenerated(FloorMap map)
        {
            if (showMap)
                mapText = BuildMapDisplay();
            UpdateStatus();
        }

        private void OnFloorModifier(FloorModifier mod)
        {
            if (mod != null)
                resultText = $"層効果: {mod.displayName} — {mod.description}";
        }

        private void OnEnemyEncountered(EnemyData enemy)
        {
            battleText = $"対戦: {enemy.displayName}  HP:{enemy.maxHP}  {enemy.DiceNotation}  威圧:{enemy.threat}";
        }

        private void OnTurnEnd(TurnResult turn)
        {
            var cm = CombatManager.Instance;
            string winText = turn.isDraw ? "引分" : (turn.playerWon ? "勝利" : "敗北");
            string critText = turn.isCritical ? " ★CRIT" : "";
            battleText = $"Turn {turn.turnNumber}: {winText}{critText}  " +
                $"P[{string.Join(",", turn.playerDice)}]={turn.playerDiceTotal} vs " +
                $"E[{string.Join(",", turn.enemyDice)}]={turn.enemyDiceTotal}  " +
                $"DMG:{turn.totalDamage}\n" +
                $"HP: P {cm.PlayerHP}/{cm.PlayerMaxHP} | E {cm.EnemyHP}/{cm.EnemyMaxHP}";
        }

        private void OnBattleEnded(CombatResult result)
        {
            string w = result.playerWon ? "★ 勝利 ★" : "✗ 敗北 ✗";
            resultText = $"{result.enemyDisplayName} — {w} ({result.totalTurns}T) 残HP:{result.playerHPRemaining}";
            UpdateStatus();
            if (showMap) mapText = BuildMapDisplay();
        }

        private void OnRewardGranted(int coins)
        {
            resultText += $"\n報酬: +{coins}コイン";
            UpdateStatus();
        }

        private void OnFloorAdvanced(int newFloor)
        {
            resultText = $"フロア{newFloor}へ！";
            UpdateStatus();
        }

        private void OnRunCleared(RunState run)
        {
            resultText = $"★★★ ランクリア！ ★★★\n戦闘:{run.totalBattles} ターン:{run.totalTurns} コイン:{run.coins}";
        }

        private void OnGameOver(RunState run)
        {
            resultText = $"GAME OVER\n到達フロア:{run.currentFloor} 戦闘:{run.totalBattles} 勝利:{run.totalWins}";
        }

        private void OnStarvation(int damage)
        {
            resultText = $"⚠ 空腹ダメージ: {damage}";
            UpdateStatus();
        }

        private void OnTileActivated(TileType type)
        {
            battleText = $"マス: {GameManager.TileToJapanese(type)}";
            resultText = "";
        }

        private void OnMysteryResolved(MapNode node, TileType resolved)
        {
            resultText = $"？→ {GameManager.TileToJapanese(resolved)}";
        }

        // === IMGUI描画 ===

        void OnGUI()
        {
            if (!showHUD) return;

            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = fontSize,
                richText = true
            };
            var boxStyle = new GUIStyle(GUI.skin.box) { fontSize = fontSize };

            // メインHUD（左上）
            GUILayout.BeginArea(new Rect(10, 10, 620, 500));
            GUILayout.BeginVertical(boxStyle);

            GUILayout.Label("<b>=== Game Debug HUD ===</b>", style);
            GUILayout.Label(phaseText, style);

            if (!string.IsNullOrEmpty(statusText))
                GUILayout.Label(statusText, style);

            if (!string.IsNullOrEmpty(battleText))
                GUILayout.Label(battleText, style);

            if (!string.IsNullOrEmpty(resultText))
                GUILayout.Label(resultText, style);

            if (!string.IsNullOrEmpty(moveText))
                GUILayout.Label(moveText, style);

            GUILayout.Space(5);
            GUILayout.Label(helpText, style);

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // マップ表示（右上）
            if (showMap && !string.IsNullOrEmpty(mapText))
            {
                var mapStyle = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize - 1,
                    richText = true
                };
                var mapBoxStyle = new GUIStyle(GUI.skin.box) { fontSize = fontSize - 1 };

                GUILayout.BeginArea(new Rect(640, 10, 400, 500));
                GUILayout.BeginVertical(mapBoxStyle);
                GUILayout.Label(mapText, mapStyle);
                GUILayout.EndVertical();
                GUILayout.EndArea();
            }
        }

        // === ヘルパー ===

        private void UpdateStatus()
        {
            var gm = GameManager.Instance;
            if (gm?.Run == null) return;
            statusText = FormatStatus(gm.Run);
        }

        private string FormatStatus(RunState run)
        {
            string hunger = "";
            var mm = MapManager.Instance;
            if (mm?.Hunger != null)
                hunger = $"  空腹: {mm.Hunger.Current}/{mm.Hunger.Max}";

            return $"Floor: {run.currentFloor}/{run.maxFloor}  " +
                   $"HP: {run.playerHP}/{run.playerMaxHP}{hunger}  " +
                   $"コイン: {run.coins}";
        }

        private void UpdateMoveOptions()
        {
            var mm = MapManager.Instance;
            if (mm == null) { moveText = ""; return; }

            var (forward, lateral) = mm.GetCategorizedMoves();
            var all = new List<MapNode>();
            all.AddRange(forward);
            all.AddRange(lateral);

            if (all.Count == 0) { moveText = "移動先なし"; return; }

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("移動先:");
            for (int i = 0; i < all.Count; i++)
            {
                var n = all[i];
                string dir = n.row > mm.CurrentNode.row ? "↑" : "→";
                string tName = GameManager.TileToJapanese(n.EffectiveType);
                string lane = n.lane >= 0 ? $"L{n.lane}" : "";
                sb.AppendLine($"  {i + 1}. {dir} [{tName}] (行{n.row} {lane})");
            }
            moveText = sb.ToString();
        }

        private string BuildMapDisplay()
        {
            var mm = MapManager.Instance;
            if (mm?.CurrentMap == null) return "";

            var map = mm.CurrentMap;
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>=== Floor {map.floor} Map ===</b>");

            for (int row = 0; row <= map.MaxRow; row++)
            {
                var nodes = map.GetNodesAtRow(row);
                if (nodes.Count == 0) continue;

                // 収束ノード（前哨基地/ボス）
                if (nodes.Count == 1 && nodes[0].lane == -1)
                {
                    var n = nodes[0];
                    string cursor = (n == mm.CurrentNode) ? " <b>◀</b>" : "";
                    string visit = n.visited ? "✓" : " ";
                    sb.AppendLine($"     [{visit}{TileAbbrev(n.EffectiveType)}{cursor}]");
                }
                else
                {
                    sb.Append($"R{row,2}: ");
                    for (int lane = 0; lane < map.laneCount; lane++)
                    {
                        var n = nodes.Find(x => x.lane == lane);
                        if (n != null)
                        {
                            string cursor = (n == mm.CurrentNode) ? "◀" : " ";
                            string visit = n.visited ? "✓" : " ";
                            // 横接続表示
                            bool hasLateral = (lane < map.laneCount - 1) &&
                                n.connections.Exists(c => c == $"r{row}_l{lane + 1}");
                            string conn = hasLateral ? "=" : " ";
                            sb.Append($"[{visit}{TileAbbrev(n.EffectiveType)}{cursor}]{conn}");
                        }
                        else
                        {
                            sb.Append("       ");
                        }
                    }
                    sb.AppendLine();
                }
            }

            return sb.ToString();
        }

        private string TileAbbrev(TileType t)
        {
            switch (t)
            {
                case TileType.Outpost:     return "前";
                case TileType.Battle:      return "戦";
                case TileType.EliteBattle: return "激";
                case TileType.Rest:        return "休";
                case TileType.Treasure:    return "秘";
                case TileType.Shop:        return "買";
                case TileType.Event:       return "イ";
                case TileType.Mystery:     return "？";
                case TileType.Trap:        return "罠";
                case TileType.Boss:        return "王";
                default:                   return "？";
            }
        }

        private void UpdateHelpText(GameManager.GamePhase phase)
        {
            switch (phase)
            {
                case GameManager.GamePhase.Title:
                    helpText = "[G] ラン開始";
                    break;
                case GameManager.GamePhase.MapNavigation:
                    helpText = "[1-9] 移動先選択";
                    break;
                case GameManager.GamePhase.Combat:
                    helpText = "[Space] 1ターン  [F] 全自動";
                    break;
                case GameManager.GamePhase.BattleResult:
                    helpText = "[Space] 結果確認";
                    break;
                case GameManager.GamePhase.Reward:
                    helpText = "[Space] 次へ";
                    break;
                case GameManager.GamePhase.RestStop:
                    helpText = "[Space] HP回復  [U] 強化（未実装）";
                    break;
                case GameManager.GamePhase.ShopVisit:
                    helpText = "[Space] ショップを出る（未実装）";
                    break;
                case GameManager.GamePhase.EventEncounter:
                    helpText = "[Space] イベント完了（未実装）";
                    break;
                case GameManager.GamePhase.TreasureOpen:
                    helpText = "[Space] 秘宝を受け取る（未実装）";
                    break;
                case GameManager.GamePhase.TrapTriggered:
                    helpText = "[Space] 罠効果確認（未実装）";
                    break;
                case GameManager.GamePhase.FloorClear:
                    helpText = "[Space] 次フロアへ";
                    break;
                case GameManager.GamePhase.RunClear:
                case GameManager.GamePhase.GameOver:
                    helpText = "[Space] タイトルに戻る";
                    break;
                default:
                    helpText = "";
                    break;
            }
        }

        private string PhaseToJapanese(GameManager.GamePhase phase)
        {
            switch (phase)
            {
                case GameManager.GamePhase.Title:          return "タイトル";
                case GameManager.GamePhase.RunStart:       return "ラン開始";
                case GameManager.GamePhase.FloorIntro:     return "フロア紹介";
                case GameManager.GamePhase.MapNavigation:  return "マップ探索";
                case GameManager.GamePhase.Combat:         return "戦闘中";
                case GameManager.GamePhase.BattleResult:   return "戦闘結果";
                case GameManager.GamePhase.Reward:         return "報酬";
                case GameManager.GamePhase.RestStop:       return "休憩";
                case GameManager.GamePhase.ShopVisit:      return "ショップ";
                case GameManager.GamePhase.EventEncounter: return "イベント";
                case GameManager.GamePhase.TreasureOpen:   return "秘宝";
                case GameManager.GamePhase.TrapTriggered:  return "罠";
                case GameManager.GamePhase.FloorClear:     return "フロアクリア";
                case GameManager.GamePhase.RunClear:       return "ランクリア！";
                case GameManager.GamePhase.GameOver:       return "ゲームオーバー";
                default: return phase.ToString();
            }
        }
    }
}
