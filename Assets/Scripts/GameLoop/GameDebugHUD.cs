using System.Collections.Generic;
using UnityEngine;
using CombatSystem;
using EventSystem;
using InventorySystem;
using InventorySystem.Shop;
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
        private string eventText = "";

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

            if (EventEncounter.Instance != null)
            {
                EventEncounter.Instance.OnEventStarted += OnEventStarted;
                EventEncounter.Instance.OnEventResolved += OnEventResolved;
            }

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

            if (EventEncounter.Instance != null)
            {
                EventEncounter.Instance.OnEventStarted -= OnEventStarted;
                EventEncounter.Instance.OnEventResolved -= OnEventResolved;
            }

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
                eventText = "";
            }
            else
            {
                moveText = "";
            }

            if (phase != GameManager.GamePhase.EventEncounter)
                eventText = "";
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

        private void OnEventStarted(EventDefinition ev)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>=== {ev.name} ===</b>");
            sb.AppendLine(ev.flavor);
            sb.AppendLine();
            for (int i = 0; i < ev.choices.Count; i++)
                sb.AppendLine($"  {i + 1}. {ev.choices[i].text}");
            eventText = sb.ToString();
        }

        private void OnEventResolved(EventChoice choice, EventEffectExecutor.ExecutionResult result)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>→ {choice.text}</b>");
            if (result != null)
            {
                foreach (var line in result.log)
                    sb.AppendLine($"  ・{line}");
            }
            sb.AppendLine();
            sb.AppendLine(choice.postFlavor);
            sb.AppendLine();
            sb.AppendLine("[Space] マップに戻る");
            eventText = sb.ToString();
            UpdateStatus();
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

            if (!string.IsNullOrEmpty(eventText))
            {
                GUILayout.Space(5);
                GUILayout.Label(eventText, style);
            }

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

            string karma = run.karma > 0 ? $"  <color=#ff6666>カルマ: {run.karma}</color>" : "";

            return $"Floor: {run.currentFloor}/{run.maxFloor}  " +
                   $"HP: {run.playerHP}/{run.playerMaxHP}{hunger}  " +
                   $"コイン: {run.coins}{karma}";
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
                case TileType.Exchange:    return "換";
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
                    if (GameManager.Instance != null && GameManager.Instance.HasPendingRewardChoice)
                    {
                        var (rcA, rcB) = GameManager.Instance.CurrentRewardChoice;
                        var idb = InventorySystem.ItemDatabase.Instance;
                        string nA = idb?.GetItem(rcA)?.displayName ?? rcA;
                        string nB = idb?.GetItem(rcB)?.displayName ?? rcB;
                        helpText = $"報酬を選択 (同Tier):\n[1] {nA}\n[2] {nB}";
                    }
                    else
                    {
                        helpText = "[Space] 次へ";
                    }
                    break;
                case GameManager.GamePhase.RestStop:
                    helpText = "[Space] HP回復  [U] 強化（未実装）";
                    break;
                case GameManager.GamePhase.ShopVisit:
                    helpText = BuildShopHelpText();
                    break;
                case GameManager.GamePhase.EventEncounter:
                    helpText = "[1-9] 選択肢を選ぶ → [Space] 完了";
                    break;
                case GameManager.GamePhase.TreasureOpen:
                    var ts = GameManager.Instance?.LastTreasureSummary;
                    helpText = string.IsNullOrEmpty(ts)
                        ? "[Space] 宝箱を確認"
                        : $"{ts}\n[Space] 受け取って次へ";
                    break;
                case GameManager.GamePhase.TrapTriggered:
                    helpText = "[Space] 罠効果確認（未実装）";
                    break;
                case GameManager.GamePhase.ExchangeTile:
                    helpText = (GameManager.Instance != null && GameManager.Instance.CanExchangeTile)
                        ? "交換マス: [1] 最低Tierパッシブを渡し上位をrandom入手 / [Space] 通過"
                        : "交換マス: 渡せるパッシブが無い [Space] 通過";
                    break;
                case GameManager.GamePhase.SinRitual:
                    helpText = "[1] 血の儀 [2] 貪欲の儀 [3] 遺品の儀 → [Space] 完了\n  各キーで支払 (Y/Nではなく押下=捧げる、未押下=拒む)";
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

        // === ショップ表示 ===

        private string BuildShopHelpText()
        {
            var gm = GameManager.Instance;
            var sm = ShopManager.Instance;
            if (gm == null || sm?.Current == null)
                return "[Esc] 退店";

            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"<b>=== ショップ（{(gm.ShopSellMode ? "売却" : "購入")}モード）===</b>");
            sb.AppendLine($"所持金: {gm.Run.coins}G  /  [T]モード切替  [Esc]退店");

            if (!gm.ShopSellMode)
            {
                // 購入モード: 6スロット
                var inv = sm.Current;
                for (int i = 0; i < inv.slots.Count; i++)
                {
                    var slot = inv.slots[i];
                    string label = ResolveItemLabel(slot.itemId, slot.kind);
                    string price = slot.kind == ShopSlotKind.WeaponMaterial
                        ? $"{inv.CurrentMaterialPrice}G (在庫∞)"
                        : (slot.sold ? "[売切]" : $"{slot.price}G");
                    sb.AppendLine($"  [{i + 1}] {KindLabel(slot.kind)}: {label}  {price}");
                }
            }
            else
            {
                // 売却モード: 種別をサイクル + 1-9 で売却
                sb.AppendLine($"対象: {SellSourceLabel(gm.ShopSellSource)}  [S] 切替");
                IList<string> list = SellList(gm);
                if (list == null || list.Count == 0)
                {
                    sb.AppendLine("  （所持なし）");
                }
                else
                {
                    int show = Mathf.Min(list.Count, 9);
                    for (int i = 0; i < show; i++)
                    {
                        string id = list[i];
                        var data = ItemDatabase.Instance?.GetItem(id);
                        string name = data != null ? data.displayName : id;
                        int approx = ApproxSellPrice(data);
                        sb.AppendLine($"  [{i + 1}] {name} (≈{approx}G)");
                    }
                }
            }
            return sb.ToString();
        }

        private IList<string> SellList(GameManager gm)
        {
            switch (gm.ShopSellSource)
            {
                case ShopManager.SellSource.Passive:        return gm.Run.ownedPassiveItems;
                case ShopManager.SellSource.Consumable:     return gm.Run.ownedConsumables;
                case ShopManager.SellSource.WeaponMaterial: return null;
                default: return null;
            }
        }

        private string ResolveItemLabel(string id, ShopSlotKind kind)
        {
            if (kind == ShopSlotKind.WeaponMaterial) return "武器強化素材（マグナイト）";
            if (string.IsNullOrEmpty(id)) return "（空）";
            var data = ItemDatabase.Instance?.GetItem(id);
            return data != null ? $"{data.displayName} [{data.rarity}]" : id;
        }

        private string KindLabel(ShopSlotKind k)
        {
            switch (k)
            {
                case ShopSlotKind.Passive:        return "パッシブ";
                case ShopSlotKind.Consumable:     return "消費";
                case ShopSlotKind.Weapon:         return "武器";
                case ShopSlotKind.Dice:           return "ダイス";
                case ShopSlotKind.WeaponMaterial: return "強化素材";
                default: return "?";
            }
        }

        private string SellSourceLabel(ShopManager.SellSource s)
        {
            switch (s)
            {
                case ShopManager.SellSource.Passive:        return "パッシブ";
                case ShopManager.SellSource.Consumable:     return "消費";
                case ShopManager.SellSource.WeaponMaterial: return "強化素材";
                default: return "?";
            }
        }

        private int ApproxSellPrice(CompleteItemData data)
        {
            if (data?.sellPrice == null) return 5;
            return (data.sellPrice.min + data.sellPrice.max) / 2;
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
                case GameManager.GamePhase.SinRitual:      return "祭壇の儀";
                case GameManager.GamePhase.FloorClear:     return "フロアクリア";
                case GameManager.GamePhase.RunClear:       return "ランクリア！";
                case GameManager.GamePhase.GameOver:       return "ゲームオーバー";
                default: return phase.ToString();
            }
        }
    }
}
