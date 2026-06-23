---
title: 契約システム（旅団契約）
description: 前哨基地で結ぶ12旅団との契約・維持・敵対/協力関係の正本
status: design-locked
related: [map-run, combat, items, lore-endings]
---

# 契約システム（旅団契約）

## Purpose

ランの戦略軸となる「旅団との契約」。 前哨基地で確率提示され、 維持費を払い続ける限り効果が発動する。 敵対関係 6 ペア・協力関係 6 ペアが組まれており、 契約のピックそのものが次のピックを制約する。

## 共通ルール

| 項目 | 値 |
|---|---|
| 配置 | 前哨基地 (層突入時) |
| 抽選 | 既契約者以外から、 各契約 30% 独立抽選 (0個提示〜全契約者数の任意数) |
| 維持費 | L1=3G / L2=6G / L3=9G (L3 維持後も 9G) |
| 育成経路 | L1→L2→L3 最低 3 層必須 (一足飛び不可) |
| 解除条件 | ゴールド不足 (任意/強制) / 戦闘終了時 HP≤20% (**L3 免除**) / 敵対契約取得時 |
| 解除後 | 同層では再提示なし、 次層から復活可 |
| L3 強制解除時のゴールド | **没収** |
| 同時契約 | ゴールド許す限り可、 維持費不足時は **プレイヤー任意選択 UI** |
| ボス戦持ち込み | 可 |

## 旅団リスト (12 件)

各効果欄は L1 / L2 / L3 の順で表記。 各旅団の **出自 (所属組織・地理)** は [lore-endings.md §1-G](lore-endings.md) の「12 旅団の出自」表を正本とする (ここでは再記述しない)。

### 1. 傭兵団 (Mercenaries)
- **効果**: ターン終了時、 **敵に対し** 敵 maxHP の **3% / 6% / 9%** 軽減不能ダメージ (上限 40)
- 軸: DoT

### 2. 補給キャラバン (Supply Caravan)
- **効果**: 層内で **任意発動**できるものが積み上がる:
  - L1: ショップを開く
  - L2: ショップ + 強化
  - L3: ショップ + 強化 + 休息 (休息は**マップ画面のみ**)
- 各機能は層に 1 回。 層クリアでリセット
- 軸: システム解放

### 3. 商業連合隊 (Merchants' League)
- **効果**: **層終了時** に **+5G / +10G / +20G** ボーナス
- **ペナルティ**: その層内のいずれかの戦闘終了時に HP≤50% になった場合、 **その層の収入は 0** (L3 は免除)
- 判定は **医術官回復後の HP** で行う
- 軸: リスク連動収入

### 4. 宣教師 (Missionaries)
- **効果**: 戦闘以外での希望減少を **-1 / -2 / -3** (0 にはならない)
- 軸: 希望保護

### 5. 騎士 (Knights)
- **効果**: **プレイヤーが受ける** ダメージ -1 / -2 / -3 (**最低 1 通す**)
- 最終ダメージ算出後段で減算
- 軸: 防御

### 6. 暗殺教団 (Assassins)
- **効果**: 戦闘開始時、 **エネミー (敵側) に対して** 敵 HP の **25% / 50% / 75%** 軽減不能ダメージを与える
- **対象は通常戦闘マスの敵のみ** (エリート/ボス除外、 中ボスは存在しない)
- このダメージは **会心扱いではない** ─ 狩猟旅団の脆弱 (armed) は消費しない
- 軸: 雑魚処理

### 7. 旅する錬金術師 (Wandering Alchemist)
- **効果**: 戦闘終了時に **10% / 20% / 30%** で **パッシブアイテムを錬金**
- レアリティ重み (正本: [items.md](items.md)):
  - L1: BRONZE 100%
  - L2: BRONZE 70% / SILVER 30%
  - L3: BRONZE 50% / SILVER 35% / GOLD 15%
- 取得済みアイテムはプールから除外。 インベントリ満タンなら **諦める** (上書き選択肢なし)
- 軸: ドロップ

### 8. 放浪医術官 (Wandering Doctor)
- **効果**: 戦闘終了時、 **(maxHP - currentHP) の 10% / 20% / 30% を回復**
- 端数は切り上げ。 maxHP を超えない
- 軸: 回復

### 9. 捨て子のサーカス団 (Orphan Circus)
- **効果**: なし (契約効果は持たない)
- **windfall**: 3-4 層イベントで「子供たちを受け入れてくれるキャラバン」 に引き渡すと膨大な報酬。 サーカス団 Lv 1/2/3 で報酬段階増
- 軸: ロア windfall

### 10. 影武者一座 (Body Doubles)
- **効果**: HP 0 になる **任意のダメージ** で `HP = ceil(maxHP × 0.10)` で復活
- **ラン全体で L1=1 回 / L2=2 回 / L3=3 回** ── リチャージ無し (ラン中の総回数)
- 復活直後の HP が 20% 未満でも、 **戦闘継続中に 20% 超えるまで回復できれば** 戦闘終了時の HP20% 解除はかからない
- 軸: 緊急保険

### 11. 狩猟旅団 (Hunters)
- **効果**: 戦闘開始時、 **敵単体に脆弱 (armed) を付与**
- **脆弱の挙動**:
  - 自プレイヤーが会心攻撃 (`isCritical=true`) を当てた瞬間、 **全段のダメージに ×(1 + 0.15 / 0.30 / 0.45)** 倍率がかかる
  - 倍率適用は **防御後の最終ダメージにさらに乗算** (案A)
  - 倍率適用後、 脆弱は **consumed** に遷移
  - 自プレイヤーが **ロール勝利 かつ 非会心ダメージ** を与えると **armed** に再点火
  - 戦闘終了で剥がれる
- 暗殺教団のダメは会心扱いではないので、 armed のまま消費しない
- 敵は単体しか存在しないので付与対象は常に 1 体
- 軸: 状態異常 (新規 「脆弱」 を実装)

### 12. 戦術家 (Tacticians)
- **効果**: 戦闘中、 自分のロール結果を **1 / 2 / 3 回** 振り直し可能 (戦闘ごとリセット)
- **振り直しは強制採用** (振った結果を見て元に戻す選択肢はない)
- 軸: ロール救済

## 敵対関係 (6 ペア)

**ルール**: 既存契約と敵対関係にある旅団が提示された場合、 契約しようとすると **双方の警告セリフが同時表示** され、 確定で既存契約が **強制解除** (L3 なら維持費9G没収)。

| 敵対 | 解除セリフ |
|---|---|
| 騎士 ⟷ 影武者一座 | 「悪党の身代わりをして何のつもりだ?」 / 「命賭けの仕事中に騎士様の説教なんざ御免だね」 |
| 宣教師 ⟷ 暗殺教団 | 「殺人を教義とする宗教など! 主はお認めになっておらん!」 / 「お前らの主とやらはどうして我々に罰を与えないのだ?」 |
| 狩猟旅団 ⟷ サーカス団 | 「子供は煩い、 臭いで獣が逃げるだろう」 / 「なんだアイツら? 邪険にしやがって」 |
| 傭兵団 ⟷ 戦術家 | 「だから! お前らは机上の空論なんだよ! 現場で何人死んだ!」 / 「戦術通りにミスなく動けば誰も死なない!」 |
| 商業連合隊 ⟷ 錬金術師 | 「石を金塊に!? 商売あがったりだ!」 / 「いちいち煩いなぁ...」 |
| 補給キャラバン ⟷ 医術官 | 「お前が浪費する医療物資のせいでみんな餓死するぞ!」 / 「足が壊死して苦しみながら死ぬのとどっちがマシでしょうね?」 |

## 協力関係 (6 ペア、 個別効果)

**ルール**: 両方契約中 (レベル不問) のとき常時発動。 UI は 「協力中」 マーク。

| 協力 | 効果 |
|---|---|
| 騎士 + 宣教師 | 騎士軽減 **+1** ＋ 宣教師の希望減少緩和 **-1** 追加 |
| 暗殺教団 + 戦術家 | 暗殺教団がエリートにも発動 (倍率 **10% / 20% / 30%**) |
| サーカス団 + 商業連合隊 | 層終了時 **+ サーカス団 Lv × 2G** ボーナス (商業連合隊の HP≤50% ペナルティで **連動して消える**) |
| 錬金術師 + 補給キャラバン | 錬金レアリティ表 **+1 段**: L1→L2 相当 / L2→L3 相当 / L3→L3 + **LEGENDARY 5%** 追加 |
| 影武者一座 + 傭兵団 | 戦闘ターン終了時 **+1G** (傭兵団 DoT と同タイミング) |
| 狩猟旅団 + 医術官 | 戦闘終了時、 与ダメ累計の **5% / 10% / 15%** を HP 回復 (maxHP 超えず) |

## 発動順序

### 戦闘開始時
1. **暗殺教団** のダメージ (会心扱いではない)
2. **狩猟旅団** の脆弱付与
3. **戦術家** の振り直し回数チャージ

### ターン終了時
1. **傭兵団** DoT
2. **影武者+傭兵団 協力** の +1G

### 戦闘終了時
1. **商業連合隊** HP≤50% ペナルティ判定 (**医術官回復後** の HP で判定)
2. **医術官** 回復
3. **狩猟旅団+医術官 協力** の与ダメ%回復
4. **錬金術師** ロール

(順序2/1の前後は要確認 ── 上記順だと医術官回復が判定より先 = ペナルティが緩い。 「医術官回復後の HP で判定」 と仕様で確定したのでこの順)

## 効果の方向 (重要・misunderstanding 防止)

- **「敵に対し / エネミー対象」**: 傭兵団、 暗殺教団、 狩猟旅団 → 与えるダメージ・付与する状態異常
- **「プレイヤーが受ける」**: 騎士、 医術官、 影武者一座 → プレイヤー側の被ダメ・回復・復活
- **「経済」**: 商業連合隊、 サーカス団 (引渡し報酬)、 補給キャラバン、 錬金術師、 宣教師 → ゴールド・希望・パッシブドロップ
- 暗殺教団は **エネミー HP の %** を **エネミー側に** 与える契約。 プレイヤー被ダメではない

## 数値正本

- 数値・倍率の最終正本はコード実装 (`Assets/Scripts/GameLoop/Contracts/`)
- フレーバー・解除セリフの最終正本は `Assets/Data/Contracts/contracts.json`
- 本ドキュメントは設計凍結時点のスナップショット、 数値ズレが生じたらコードを正本として書き換える

## 関連実装メモ

- 「脆弱」 は新規状態異常として実装。 [combat.md](combat.md) / [DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md) に追記予定
- EnemyData に種別 enum (Normal/Elite/Boss) を追加 (暗殺教団用)
- 「リピーター」 は削除済み (実装フェーズで Registry/Effects/Context/docs/items.json 全消し)
- サーカス団引渡しイベントは [events.md](events.md) に追加

---

## 実装状況 (2026-06-19 着手)

### 完了済み

#### Phase 1: 下準備
- **リピーター削除**: [PassiveSkillRegistry.cs:201](../../Assets/Scripts/InventorySystem/PassiveSkills/PassiveSkillRegistry.cs)、 [AllPassiveSkillEffects.cs](../../Assets/Scripts/InventorySystem/PassiveSkills/Effects/AllPassiveSkillEffects.cs) の Repeater クラス、 [CombatContext.cs](../../Assets/Scripts/InventorySystem/PassiveSkills/CombatContext.cs) の `retriggerOnCrit` フィールド、 [DAMAGE_CALC_REFERENCE.md](../../Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md) の関連記述 ── 全て削除済
- **EnemyKind enum**: [EnemyKind.cs](../../Assets/Scripts/CombatSystem/EnemyKind.cs) ── Normal/Elite/Boss + `TileType.ToEnemyKind()` 拡張メソッド。 [CombatManager.cs](../../Assets/Scripts/CombatSystem/CombatManager.cs) で OnBattleStart 前に `ctx.currentEnemyKind` を MapManager から導出
- **脆弱状態異常**: [VulnerabilityStatus.cs](../../Assets/Scripts/CombatSystem/VulnerabilityStatus.cs) ── armed/consumed サイクル管理 (Apply / ConsumeOnCrit / RearmOnNonCritWin)。 CombatContext に `enemyVulnerabilityMultiplier` / `enemyVulnerabilityArmed` フィールド。 [CombatManager.ApplyWinDamageModifiers](../../Assets/Scripts/CombatSystem/CombatManager.cs) の Λ「微妙な手応え」 直後 (最低保証の前) で適用ロジック挿入済

#### Phase 2: 契約システム核 ([Assets/Scripts/GameLoop/Contracts/](../../Assets/Scripts/GameLoop/Contracts/))
- **ContractKind.cs**: 12 旅団の enum
- **ContractRelations.cs**: 敵対 6 + 協力 6 ペアテーブル、 双方向辞書ビルダー
- **ContractDefinition.cs**: 静的定義クラス、 `ContractCost.For(level)` で維持費取得 (3/6/9G)
- **ContractDatabase.cs**: 12 旅団の名前・軸ラベル・フレーバー・効果説明 (L1-L3)・解除セリフを保持
- **ContractInstance.cs**: 現在発効中の契約 1 件分の runtime state (kind/level/影武者残数/補給キャラバン使用フラグ等)
- **IContractEffect.cs**: hook インターフェース + `ContractEffectBase` (no-op デフォルト基底クラス) + `ContractBattleResult`
- **ContractManager.cs**: シングルトン。 `SignNew` (敵対契約の強制解除込み)、 `Cancel`、 `CheckHpReleaseRule` (HP20%・L3 免除)、 hook ディスパッチ (FireOnBattleStart / OnTurnEnd / OnBattleEnd / OnLayerStart / OnLayerEnd / OnRollWin)、 維持費徴収
- **RunState.cs**: `activeContracts` List + `circusHandedOver` フラグ追加、 ResetForNewRun でクリア済

#### Phase 3: 12 効果実装 ([Assets/Scripts/GameLoop/Contracts/Effects/AllContractEffects.cs](../../Assets/Scripts/GameLoop/Contracts/Effects/AllContractEffects.cs))
全 12 効果を 1 ファイルに集約済。 `ContractEffectBase` を継承して必要 hook のみオーバーライド。
- 発動順は ContractManager 側で BattleStartOrder (暗殺→狩猟→戦術家) と BattleEndOrder (医術官→商業連合→錬金) の明示制御
- AlchemistEffect は PassiveAddHelper.AddPassiveItem 経由でインベ満タンチェックも兼ねる
- BodyDoubles の復活と Missionaries の希望減少は **直接 hook では無く** ContractManager.TryReviveOnLethal/GetHopeLossReduction を外部から呼ぶ形 (CombatManager / HopeSystem 内で接続済)

##### 各効果の実装詳細

| # | クラス | 主要 hook | 実装内容 |
|---|---|---|---|
| 1 | `MercenariesEffect` | OnTurnEnd | `dmg = min(50, ceil(enemyMaxHP × {0.04,0.08,0.12}))` を `ctx.enemyCurrentHP` から減算。 影武者協力中なら `run.coins += 1` |
| 2 | `SupplyCaravanEffect` | (hook 無し) | 静的 helper `CanUseShop/CanUseEnhance/CanUseRest` のみ。 ContractManager 側に `ConsumeSupplyShop/Enhance/Rest` 提供。 Lv2 で強化解放、 Lv3 で休息解放 |
| 3 | `MerchantsLeagueEffect` | OnBattleEnd, OnLayerEnd | OnBattleEnd: Lv≤2 のとき HP/maxHP ≤ 0.50 で `merchantsLeagueLayerLossFlag=1`。 OnLayerEnd: フラグ無しで `run.coins += {5,10,20}`。 サーカス団協力中なら `+ サーカス Lv × 2G`、 ペナルティ時は連動消滅 |
| 4 | `MissionariesEffect` | (hook 無し) | `ContractManager.GetHopeLossReduction(run)` で `level (+ 騎士協力中で +1)` を返却。 HopeSystem.Reduce が `IsCombatActive==false` のときに減算 |
| 5 | `KnightsEffect` | OnBattleStart | `ctx.playerFlatDamageReduction += level (+ 宣教師協力で +1)`。 既存の被ダメ軽減チェーンに加算合成、 最低 1 通すは `winMinDamage` で別途保証 |
| 6 | `AssassinsEffect` | OnBattleStart | `currentEnemyKind == Normal` で `dmg = ceil(enemyCurrentHP × {0.33,0.66,0.99})`。 戦術家協力中は `Elite` も対象、 倍率 `{0.15,0.30,0.45}` (協力時専用テーブル)。 軽減不能は `enemyCurrentHP` 直接減算で表現 |
| 7 | `AlchemistEffect` | OnBattleEnd | `playerWon` のみ。 確率 `{0.10,0.20,0.30}`。 重み: L1=B100%/L2=B70+S30/L3=B50+S35+G15。 補給キャラバン協力中は effectiveLevel +1 (L3+協力 → L3 + LEGENDARY 5%)。 `PassiveAddHelper.AddPassiveItem` が null 返却で「インベ満タンで諦め」、 候補 0 件 (取得済み除外で空) でも諦め |
| 8 | `WanderingDoctorEffect` | OnBattleEnd | `heal = ceil((maxHP - currentHP) × {0.10,0.20,0.30})`。 狩猟旅団協力中は `+ ceil(totalDamageDealt × {0.05,0.10,0.15})` を追加。 `run.playerHP` 更新後、 `result.finalPlayerHp` を同期 (後段の商業連合判定が回復後HPを見るため) |
| 9 | `OrphanCircusEffect` | (hook 無し) | 効果なし。 引渡しイベント側で `run.activeContracts` から検出して報酬段階を Lv で決める |
| 10 | `BodyDoublesEffect` | (hook 無し) | `ContractManager.TryReviveOnLethal(run, ref playerHp, playerMaxHp)` で `bodyDoublesRemainingRevives > 0` のとき `playerHp = max(1, ceil(maxHp × 0.10))` で復活、 残数 -1。 ラン全体カウンタで戦闘終了時にリセットされない |
| 11 | `HuntersEffect` | OnBattleStart | `VulnerabilityStatus.Apply(ctx, level)` 呼び出し ── ctx に `enemyVulnerabilityMultiplier = {0.15,0.30,0.45}` をセットし `armed=true`。 戦闘中の消費/再点火は CombatManager.ApplyWinDamageModifiers の `ConsumeOnCrit` / `RearmOnNonCritWin` で処理 |
| 12 | `TacticiansEffect` | OnBattleStart | `state.tacticiansRerollsRemainingThisCombat = level`。 ロール画面から `ContractManager.TryConsumeReroll(run)` で 1 回ずつ消費 (true/false を返す)。 戦闘ごとに再チャージ |

##### ContractManager の外部 helper API (CombatManager / UI が呼ぶ)

| Method | 目的 | 呼び出し元 |
|---|---|---|
| `TryReviveOnLethal(run, ref hp, maxHp)` | 影武者復活判定 | `CombatManager` 致死確定直後 |
| `GetHopeLossReduction(run)` | 宣教師の戦闘外希望減少オフセット | `HopeSystem.Reduce` |
| `TryConsumeReroll(run)` | 戦術家振り直しを1回消費 | ロール UI (未実装) |
| `GetRerollsRemaining(run)` | 残振り直し回数表示 | ロール UI (未実装) |
| `CanUseSupply{Shop,Enhance,Rest}(run)` | 補給キャラバン UI 可否判定 | 前哨基地 / マップ UI (未実装) |
| `ConsumeSupply{Shop,Enhance,Rest}(run)` | 補給キャラバン使用済みフラグセット | 同上 |
| `IsAllianceActive(run, kind)` | 協力ペアが両方アクティブか | 各 IContractEffect が自身の協力判定で呼ぶ |
| `RegisterAllEffects()` | 12 効果を一括登録 | `ContractBootstrap.Initialize` |

##### CombatContext の新規フィールド (契約システム由来)

| フィールド | 型 | 役割 |
|---|---|---|
| `currentEnemyKind` | `CombatSystem.EnemyKind` | 暗殺教団の対象判定 (StartCombat 時に MapManager から導出) |
| `enemyVulnerabilityMultiplier` | `float` | 狩猟旅団の倍率 (0=契約なし、 0.15/0.30/0.45) |
| `enemyVulnerabilityArmed` | `bool` | 脆弱の armed/consumed 状態 |

##### RunState の新規フィールド

| フィールド | 型 | 役割 |
|---|---|---|
| `activeContracts` | `List<ContractInstance>` | 発効中の契約 |
| `circusHandedOver` | `bool` | サーカス団引渡しイベント消化済みフラグ |
| `contractsExpiredThisLayer` | `List<ContractKind>` | 同層失効プール (再提示防止)。 OnLayerEnd でクリア |

#### Phase 4: ライフサイクル連携 (完了済)
- [ContractBootstrap.cs](../../Assets/Scripts/GameLoop/Contracts/ContractBootstrap.cs): `RuntimeInitializeOnLoadMethod` で 12 効果を登録
- [CombatManager.cs](../../Assets/Scripts/CombatSystem/CombatManager.cs):
  - StartCombatInternal で `FireOnBattleStart`
  - ターン末 OnTurnEnd 処理直後で `FireOnTurnEnd`
  - 戦闘終了時 `FireOnBattleEnd` + `CheckHpReleaseRule`
  - 致死判定の瞬間に `TryReviveOnLethal` (combatLethalThisTurn 確定時)
- [HopeSystem.Reduce](../../Assets/Scripts/GameLoop/HopeSystem.cs): `IsCombatActive == false` のとき `GetHopeLossReduction` で宣教師オフセット適用
- [GameManager.EnterFloor](../../Assets/Scripts/GameLoop/GameManager.cs): 層遷移時に `FireOnLayerEnd` → `FireOnLayerStart`
- [ContractOfferRoller.cs](../../Assets/Scripts/GameLoop/Contracts/ContractOfferRoller.cs): 抽選ロジック (30% 各独立) + Sign/Extend API
- RunState に `contractsExpiredThisLayer` プール追加 (同層解除契約の再提示防止)

##### CombatManager のフック挿入位置 (実コード基準)

| フック | 挿入位置 | 用途 |
|---|---|---|
| `FireOnBattleStart` | `StartCombatInternal` の `psm.FireTrigger(OnBattleStart)` 直後 | 暗殺ダメ / 脆弱付与 / 戦術家チャージ |
| `TryReviveOnLethal` | `combatLethalThisTurn = playerHP <= 0` 確定直後 | HP=0 になるあらゆるダメ (毒等含む) で発動 |
| `FireOnTurnEnd` | `psm.FireTrigger(OnTurnEnd)` 直後 | 傭兵団 DoT / 影武者協力 +1G |
| `FireOnBattleEnd` | `GetLastCombatResult()` 取得後、 `OnCombatEnd?.Invoke` の前 | 医術官→商業連合→錬金 の順 |
| `CheckHpReleaseRule` | FireOnBattleEnd 直後 | HP20% で L1/L2 契約剥がれ (L3 免除) |

##### 戦闘終了時の HP 同期

医術官が `run.playerHP` を更新するため、 CombatManager 側の `playerHP` を `run.playerHP` と同期させてから HP20% 判定する (回復後の HP で判定する仕様)。

#### Phase 5: AutoRunner 統合 (完了済)
- [ContractAiPicker.cs](../../Assets/Scripts/AutoTest/ContractAiPicker.cs): 契約評価 AI
  - **BaseValueL1 テーブル**: 旅団ごとの主観評価 (騎士=6 / 影武者=8 / 暗殺=4 等)、 レベルで線形スケール
  - **AllianceBonus = 1.30**: alliance 既契約と一致なら value ×1.30
  - **RivalryPenalty {L1=-1, L2=-3, L3=-6}**: 取得すると既存契約を強制解除する場合の減算
  - **AcceptThreshold = 1.5**: value ≥ cost × 1.5 で採用
  - **floor バイアス**: 経済系は floor≤3 で ×1.20、 戦闘系は floor≥4 で ×1.20
  - **API**: `PickOffers / PickExtension / PickShortfallReleases`
- [AutoRunner.cs](../../Assets/Scripts/AutoTest/AutoRunner.cs):
  - `OnTileActivated(TileType.Outpost)` で `HandleOutpostContracts()` 起動
  - フロー: 維持費徴収 → 不足時 AI 切り捨て → 延長候補 1 件 → 新規抽選 + AI 採用判定
  - RunRec に `contractsSigned/Extended/Forced/HpReleased/ShortfallReleased/MaintenancePaid/OutpostsVisited/FinalActive` 追加
  - サマリに `BuildContractBlock()` 追加 ── 旅団別の新規/延長/最終発効率テーブル + 解除内訳
- ContractManager に `Stat_HpReleaseCount` カウンタ追加 (AutoRunner がランごとリセット)

#### Phase 6: サーカス団引渡しイベント (完了済)
- [event_list.txt](../../Assets/Resources/Events/event_list.txt) に **「別れのキャラバン」** を追加
  - 条件: `フラグ:[サーカス団同行], 3-4層, 一度のみ, 優先`
  - 選択肢: 「子らを託す」 (引渡し効果発動) / 「連れて歩く」 (なし)
- [EventEffectType.cs](../../Assets/Scripts/EventSystem/EventEffectType.cs) に `CircusHandover` enum 追加
- [EventParser.cs](../../Assets/Scripts/EventSystem/EventParser.cs): 「サーカス引渡し」 キーワードを CircusHandover に解釈
- [EventEffectExecutor.cs](../../Assets/Scripts/EventSystem/EventEffectExecutor.cs): `ExecuteCircusHandover` で Lv 連動報酬
  - **Lv1**: +25G / 希望+10
  - **Lv2**: +50G / 希望+20
  - **Lv3**: +90G / 希望+30
  - 効果発動と同時にサーカス団契約を `Cancel`、 `run.circusHandedOver = true`
- [ContractManager.cs](../../Assets/Scripts/GameLoop/Contracts/ContractManager.cs): `SyncFlagFor(run, OrphanCircus)` で 「サーカス団同行」 フラグを自動同期
  - `SignNew` / `Cancel` / `CheckHpReleaseRule` の各解除/契約タイミングで呼ばれる
  - 現状サーカスのみ。 将来他契約もイベント条件に使いたい場合は同関数を拡張
- [EventChoiceScorer.cs](../../Assets/Scripts/AutoTest/EventChoiceScorer.cs): AutoRunner が「子らを託す」 を優先選択するよう `CircusHandover` スコアを追加 (`gold×0.5 + hope×0.3 + 6f`)

#### Phase 7: UI ([Assets/Scripts/UI/Contracts/](../../Assets/Scripts/UI/Contracts/) を新設予定)
runtime API は揃っているので UI は薄いラッパー:
- **ContractOfferScreen**: 前哨基地の契約提示画面 (30% 抽選結果を並べる)
- **ContractRivalryWarningDialog**: 敵対契約取得時の確認ダイアログ (双方のセリフ表示)
- **ContractBuffPanel**: 既存バフ欄に「契約: X (Lv)」「協力中」 マークを追加
- **ContractShortfallPicker**: 維持費不足時、 解除する契約をプレイヤーが選ぶダイアログ

#### Phase 6: サーカス団引渡しイベント
- [events.md](events.md) と [Assets/Resources/events.json](../../Assets/Resources/events.json) に新規イベント追加
- 3-4 層でランダム出現、 サーカス団契約中なら 「子供たちを引き渡す」 選択肢が有効
- 引渡しで `run.circusHandedOver = true` + Lv×K G + メタ的な希望ボーナス

#### Phase 7: テスト / バランス
- [AutoRunner.cs](../../Assets/Scripts/AutoTest/AutoRunner.cs) に契約取捨選択ロジック追加 (敵対避け・コスト/効果比評価)
- バランス検証用に AutoRunLog の集計に契約使用率を追加

### 実装上のノート

#### CombatContext の新規フィールド (脆弱用)
```csharp
public CombatSystem.EnemyKind currentEnemyKind = CombatSystem.EnemyKind.Normal;
public float enemyVulnerabilityMultiplier = 0f;  // 0=契約なし、 >0=狩猟旅団 active
public bool enemyVulnerabilityArmed = false;
```
リセットは [CombatContext.cs:380](../../Assets/Scripts/InventorySystem/PassiveSkills/CombatContext.cs) の戦闘開始時初期化に含まれる。

#### RunState の新規フィールド
```csharp
public List<GameLoop.Contracts.ContractInstance> activeContracts = new List<...>();
public bool circusHandedOver = false;
```

#### 「リピーター」 削除の正当化
docs/specs/contracts.md 凍結時点で正本実装は無効化 (2026-05-31)。 残骸 (Registry/Effects/Context/docs) は契約システム実装と同タイミングで全消し。

#### 維持費徴収のフロー
1. プレイヤーが前哨基地マスを踏む
2. `CollectMaintenanceOrFlagShortfall(run)` 呼ぶ
3. 戻り値が空 (徴収完了) → そのまま提示画面へ
4. 戻り値が非空 (不足) → `ContractShortfallPicker` 起動 → ユーザー解除選択 → `ResolveShortfall(run, picks)` → 提示画面へ
