---
title: 機能の一覧（何を検査するか）
description: DICE BOUND の全機能を、検査の単位に分けた表。高強度デバッグの準備の段・初版
status: draft
updated: 2026-11-13
base: origin/main 6ea741d
---

# 機能の一覧（何を検査するか）

この表は「どこを検査するか」の地図です。観点（どう壊しに行くか）は [viewpoints.md](viewpoints.md)、
段取り・見積もり・仕様の穴は [test-plan.md](test-plan.md) と [spec-gaps.md](spec-gaps.md) にあります。

## 読み方

- **番号**: `F-大分類-連番`。項目書（test-plan.md の書式）の「機能」の欄はこの番号で引く。
- **正本**: 仕様の節（`GAME.md §x`）・コード（ファイルと主なクラス/メソッド）・データ（ファイル）。
  このプロジェクトの決まりで、**唯一の正本は実コード**（CLAUDE.md）。仕様は設計の意図を書いた文書。
- **個数**: 実物を数えた数。数え方は末尾の「数え方」に脚注で書く（[^n]）。数えていない物は「未確認」。
- **画面**: `ロ` = ロジックだけで検査できる（Unity なしの検算台・自動実行の BOT で足りる）／`画` = 画面が要る（人が Unity の再生か製品版で見る）／`両` = 両方。
- **確かさ**: `仕` = 仕様に書いてある／`コ` = コードにしか無い／`食` = 仕様とコードが食い違う（番号は spec-gaps.md の `G-xx`）／`未` = 未決（GAME.md §23）。
- **凍結**: `動` = 今も動いている／`固` = ほぼ固まっている／`仮` = 仮置き（GAME.md に「仮置き」「未測定」とある）。
  根拠の 1 語は、直近 2 週間（2026-10-30〜11-13）の git の変更回数（`n回`）か、GAME.md の記述。
- 用語: ランの中の持ち物は**秘宝**、ラン外の装備（冠・環・符）は**遺物**（CLAUDE.md の呼び分け）。コード上の識別子は `passive*` のまま。
  「深さ」は内部の層番号（1/3/5/6/7/8）、「層」は画面に見せる番号（0〜5）。対応は `FloorPlan.DepthOf`（GAME.md §4-1）。

## 大分類の一覧

| 記号 | 大分類 | 単位の数 |
|---|---|---:|
| A | 起動・タイトル・設定・終了 | 9 |
| B | ランの構造と場面の遷移 | 14 |
| C | 航行マップ | 18 |
| D | 戦闘 | 26 |
| E | キーワードと軸 | 11 |
| F | 品（秘宝・武器・消耗品・出目パーツ） | 12 |
| G | 経済（店・報酬・休憩・物資） | 13 |
| H | イベント | 11 |
| I | メタ進行（ラン外） | 13 |
| J | 記録と保存 | 9 |
| K | 乱数と決定性 | 5 |
| L | 表示・文字・音・演出 | 12 |
| M | BOT と測定の口 | 8 |
| | 合計 | 161 |

---

## A. 起動・タイトル・設定・終了

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-A-01 | 初回起動 | 記録なし（PlayerPrefs も中断セーブも無い）で起動してタイトルまで | `UI/TitleMenuBootstrap.cs`・`MetaProgressManager.Load` | ― | 画 | コ | 固（仕様に節なし） |
| F-A-02 | タイトルの項目 | はじめる・SKILL TREE（整備パネル）・GALLERY（図鑑）・設定・終了 | `UI/TitleMenuRouter.cs` | 未確認（ボタン数は数えていない） | 画 | 仕（§23-5） | 仮（仮置き） |
| F-A-03 | 職業の選択 | 4 職の書類画面・開始武器とスターター消耗品の表示 | `UI/ClassSelect/ClassDossierData.cs`・`RunState.cs` `ClassStarter` | 職業 4[^cls] | 両 | 食（G-10） | 固（0回） |
| F-A-04 | 設定の項目 | 音量 3・液晶の効果 6（明るさ・走査線・湾曲・色収差・ビネット・ノイズ） | `UI/GameSettings.cs` `Defs` | 9 項目[^set] | 画 | コ | 固 |
| F-A-05 | 設定の保存 | 値の範囲（1〜100 / 0〜100）・閉じる時に書き出し | `GameSettings.Save` | 9 | 両 | コ | 固 |
| F-A-06 | 「手引きをもう一度」 | 見た印を全部消す | `Game2DTutorial.ResetAll`・`UI/TutorialSeen.cs` | 1 | 画 | 仕（§0-2） | 仮 |
| F-A-07 | 終了（製品版） | 閉じる時、保存できる時点なら自動で中断セーブ | `Application.wantsToQuit`・`GameManager.Save.cs` | ― | 画 | 仕（§4-5） | 動（3回） |
| F-A-08 | 開発用のキー | `G`（タイトルからラン開始）・数字キー・`Space`/`R`/`F`/`L`/`S`/`T`/`Esc` が `GameManager.Update` に残る | `GameManager.cs:499-` | 11 種のキー[^key] | 画 | 食（G-29） | 固 |
| F-A-09 | 解像度と窓 | 液晶 924×556 物理 px = 231×139 dot（§18-2）／格子 320×180（memory） | `UI/Game2D/Game2DLayout.cs` ほか | 未確認 | 画 | 食（G-57） | 仮 |

## B. ランの構造と場面の遷移

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-B-01 | 場面（GamePhase） | 全状態と遷移。`TrapTriggered` は一度も立たない | `GameManager.cs:75-97` `GamePhase`・`SetPhase` | 18 状態・`SetPhase` 呼び出し 62 か所[^phase] | 両 | 食（G-23） | 動 |
| F-B-02 | ランの開始 | 開始 HP 30・職業の武器と消耗品・開幕の秘宝・挑戦カードの初回の引き | `GameManager.StartNewRun`・`RunState.Initialize` | ― | 両 | 仕（§4-1・§11） | 動 |
| F-B-03 | 層の並び | 0 層（ファーム）→1→2→3→4→5、5 層は〈門〉の後にヴェスカ戦 | `MapSystem/FreeMove/FloorPlan.cs` | 層 6・深さ 7 種（0/1/3/5/6/7/8） | ロ | 仕（§4-1） | 固 |
| F-B-04 | 0 層（ファーム） | 一本道・敵は HP1 攻撃 0・ボスは古い宝箱 | `FreeMapGenerator.GenerateFarm` | 点 7 | 両 | 仕（§4-1） | 仮（層題が仮置き） |
| F-B-05 | 層に入る | HP 全回復・前哨基地の物資・層効果・天候・層題のバナー | `GameManager.EnterFloor`（`GameManager.Floor.cs`） | ― | 両 | 仕（§10-3・§5-5） | 動 |
| F-B-06 | 層効果 | 深さ 3 瘴気侵蝕／5 地獄門／6 深淵の洗礼 | `MapSystem/FloorModifier.cs` | 3[^mod] | ロ | 仕（§5-5） | 固 |
| F-B-07 | 〈確信〉の段 | 災厄の予兆で 1・エリート勝利で +1 | `GameLoop/ConvictionSystem.cs` | ― | ロ | 仕（§4-2） | 固 |
| F-B-08 | 6 層・7 層の解錠 | 〈決意〉で 6 層・〈灰に埋もれた基地・再訪〉(灰燼の王を倒したことがある記録) で手記を開き 7 層 | `layer6Unlocked`/`layer7Unlocked`・`GameManager.HandleFloorClear` | 2 つの門 | 両 | 仕（§4-2・§14） | 固 |
| F-B-09 | 〈門〉の 3 工程 | 血（最大 HP −25%）・秘宝 2 個・物資 4。払わない工程は欠陥 | `GameManager.Gate.cs`・`GameLoop/GateFlaws.cs` | 工程 3・欠陥 3 | 両 | 食（G-01） | 仮（未計測） |
| F-B-10 | 5 層裏ボス | シュヴァリエの出現と撃破フラグ | `defeatedSaintGeorges`・`SuppressLayer5HiddenBoss` | 1 | 両 | 仕（§4-4・§23-7） | 未（遮断の恒久化） |
| F-B-11 | ヴェスカ撃破後の一択 | `RiftChoice` で待つ・閉じる/閉じない | `GameManager.Gate.cs:176` `ChooseRiftFate`・`Game2DPhaseScreens.cs:151` | 2 択 | 画 | 食（G-28） | 固 |
| F-B-12 | 結末 | end1・end2・end3・end_true・end_good（end4 は到達不能） | `GameLoop/Endings.cs` | 6 定義・到達できる 5[^end] | 両 | 仕（§20） | 動（7回） |
| F-B-13 | 敗北（ゲームオーバー） | 結末を出さずに終える。遺物は落ちる | `GameManager` `GameOver`・`EndRun` | ― | 両 | 仕（§20・§15-5） | 固 |
| F-B-14 | 層のクリア | ボス撃破／7 層は〈門〉を処理して戻った瞬間 | `GameManager.IsBosslessFloorCleared`・`FloorClear` | ― | 両 | 仕（§4-1） | 固 |

## C. 航行マップ

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-C-01 | 層の生成 | 16.4×9.4・点 50・最小全域木＋近道・山岳 3〜5・引き直し 40 回 | `FreeMove/FreeMapGenerator.cs`・`FreeMapParams.cs` | 点 50 | ロ | 仕（§5-1） | 固（2回） |
| F-C-02 | 点の種類と重み | 戦闘 29・イベント 25・エリート 14・交換 7・店 7・秘宝 3・休憩 3。店と休憩は必ず 1 つ | `MapGenerator.FromLayout` | 種類 7 抽選＋固定 3 | ロ | 仕（§5-2） | 固 |
| F-C-03 | 危険の帯 | 左右 3 帯で出る敵とエリートを変える | `FreeMapParams.BandOf`・`EnemyData.danger` | 帯 3 | ロ | 仕（§5-2） | 仮（未測定） |
| F-C-04 | 点の状態 | 使い切り・種類の公開・戦闘名の公開・固定の点 | `MapNode`・`FreeMapSim.Scouted` | 状態 4 | 両 | 仕（§5-3） | 固 |
| F-C-05 | 移動と手番 | 隣へ 1 歩・待つ・偵察・照明・囮罠・休憩の多め回復 | `GameManager.MoveStep/WaitTurn/ReconTurn/RestHealMore`・`FreeMapSim` | 行動 7 | 両 | 食（G-02） | 動（16回） |
| F-C-06 | 物資の不足 | 不足分を HP 5/物資 1 で払う・死ぬ移動も許す | `FreeMapParams.HpPerSupply`・`ProvisionPayment.cs` | ― | ロ | 仕（§5-8・§10-1） | 固 |
| F-C-07 | 視界と索敵 | 1 歩・偵察 2 歩・照明 3 歩（3 手番） | `FreeMapSim` | 手段 4 | 両 | 仕（§5-10） | 固 |
| F-C-08 | 追跡者の状態 | 巡回→警戒→追跡→見失う・追跡の上限 8・休み 4 | `FreeMapSim.FoeTurn`・`FreeMapParams.ChaseGiveUp/ChaseRest` | 状態 4 | ロ | 仕（§5-9） | 動 |
| F-C-09 | 追跡者の性質 | 召喚者・猟犬使い・疾走者・重装（プレイヤーに明かさない） | `FoeProfile.ForFloor` | 4 | ロ | 仕（§5-9） | 仮 |
| F-C-10 | 縄張り | 7〜11 点・外へ出ない・外の自分に気づかない | `FreeMapSim.Leashed`・`FoeStepToward` | ― | 両 | 仕（§5-9） | 動（11-12 の作り替え） |
| F-C-11 | 縄張りを避ける道の保証 | 縄張りの側を削る規則 ①〜④（下限 5・3 未満なら置かない） | `TerritoryBypass.Compute`・`FreeMapSim.ConfigureTerritory` | 規則 4 段 | ロ | 仕（§5-9） | 動（11-13） |
| F-C-12 | 縄張りの宝 | 中心の隣・1 層 1 回・武器を 1 段鍛える | `FreeMapSim.TreasurePointOf` | 1 | 両 | 仕（§5-9） | 仮 |
| F-C-13 | 捕まる・専用エネミー | 性質ごとの型・攻撃 ×1.6・勝つと物資とアイテム | `GameManager.PursuerFightOf`・`BuildPursuerEnemy` | 型 4 | 両 | 食（G-04） | 仮 |
| F-C-14 | 使い魔・猟犬 | 接触で戦闘・逃げられる | `FreeMapSim` | 2 | ロ | 仕（§5-9） | 仮 |
| F-C-15 | 天候と前線 | 霧・削る砂・朱の雪・雹・崩れる地平・沈む湖面。前線 5 手番で 2.4 | `FreeMove/Weather.cs` `WeatherKind` | 6[^wx] | 両 | 仕（§5-12） | 仮 |
| F-C-16 | 道具（囮罠） | 置く・気を引く（6 歩）・足止め 2・寿命 8・開始 2 個 | `FreeMapParams.Lure*`・`Consumables.Use` | 1 品 | 両 | 仕（§5-10） | 仮 |
| F-C-17 | 逃げる | 物資と金の半分・直前の点へ・ボス不可 | `CombatManager.RequestFlee`・`FleeAllowed` | ― | 両 | 食（G-03） | 固 |
| F-C-18 | 航行アイテム | 煙玉ほか（〈前哨の旗〉だけ使う） | `GameLoop/NavItems.cs`・`Ids.cs:82-104` | 13[^nav] | 両 | 仕（§23-21） | 仮（未測定） |

## D. 戦闘

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-D-01 | ターン構造 | 0 開始→1 敵ロール→2 予告→3 自ロール→4 振り直し/逃げる→5 配線→6 解決→7 終了 | `CombatManager.Turn.cs` `ExecuteTurnMutual` | 8 段 | 両 | 仕（§6-3） | 動 |
| F-D-02 | 解決の順 | プレイヤー先制・撃破なら敵は攻撃しない | 同上 | ― | ロ | 仕（§6-3） | 固 |
| F-D-03 | 振り直し | 全部振り直す・費用は充電で 個数×回目・回目は戦闘中に戻らない | `CombatManager.RerollPhase` | ― | 両 | 仕（§6-3・DCR §1⓪） | 固 |
| F-D-04 | 端子の山札 | 開始 5 枚（攻撃 2・防御 2・充電 1）・手札 5 枚・切り直し | `CombatSystem/TerminalCards.cs`・`TerminalDeckState` | 端子 60 種（攻撃 24・防御 9・充電 5・補助 21・レア 14）[^term] | 両 | 仕（§6-4・ADR-0014） | 動（25回） |
| F-D-05 | 手札の残す印 | 挿さなかった端子に印を付けると次も残る（Pick） | `TerminalDeckState.SplitHand/KeepFromMarks` | 規則 3 形（Off/All/Pick） | 両 | 仕（§6-4） | 動（11-13） |
| F-D-06 | 〈呼び戻し〉 | 捨て札の 1 枚を次の手札へ | `TerminalDeckState.RecallPick/ApplyRecall`・`Game2DBattleHud.OpenRecall` | 1 | 両 | 仕（§6-4） | 動（11-13） |
| F-D-07 | 挿す条件 | 3 以下・5 以上・6 以上・偶数・奇数・同じ目・同じ目 2 個 | `TerminalCond` | 7＋なし[^cond] | 両 | コ | 動 |
| F-D-08 | 使い捨て・常設 | 使い捨ては戦闘から外れる (次の戦闘ではまた山札に入る・〈応急箱〉も同じ・2026-11-14〜)・〈主幹〉は場に残る | `TerminalDef.disposable`・`TerminalDeckState.FinishTurn` | ― | ロ | 仕（§6-4） | 動 |
| F-D-09 | 撃破のボーナスの端子 | 撃破の経路を問わず入る | `TerminalCards.Resolve` | 4 種 | ロ | 仕（§6-4） | 動（#117） |
| F-D-10 | 端子の報酬・店・休憩で抜く | 通常 2 択・エリート 3 択・ボスはレア 3 択・下限 `MinDeckSize` 3 | `GameManager.OfferTerminalReward`・`GameManager.Terminals.cs` | ― | 両 | 仕（§6-4） | 仮 |
| F-D-11 | 役 | 目 1〜9 × {2 個そろい, ゾロ目}・面の Tier で成立 | `CombatSystem/YachtRoles.cs`・`YachtRoleEffects.cs` | 18 | 両 | 仕（§6-13） | 固（0回）・数値は仮 |
| F-D-12 | 与ダメージの式 | atkBase → 臨界 → ProcessDamage → 勝ち側の補正 → 敵 HP | DCR §9・`CombatManager.Damage.cs` | ― | ロ | 仕（DCR） | 動 |
| F-D-13 | 被ダメージの式 | 敵攻撃 = round(段階倍率 × (基礎 + 出目)) → 防御 → ブロック → 補正 | `Escalation.EnemyAttackValue`・DCR §9 | ― | ロ | 仕（§6-6） | 動 |
| F-D-14 | 予告と実際の一致 | 予告の数字＝実際の被ダメージの元 | `Escalation.GetMultiplier` | ― | 両 | 仕（§6-6・§6-9） | 固 |
| F-D-15 | 防御と貫通 | 逓減 `Rmax·D/(D+K)`（0.6/10/初期 2）・貫通は固定で引く | `CombatSystem/DefenseFormula.cs` | 式 1 | ロ | 仕（§6-14） | 固（#39 で確定） |
| F-D-16 | 装甲持ちの敵 | 高防御・割れる・積み上がる・構え | `EnemyPassiveSkillEffects.cs`・`guardPeriod` | 型 4・敵 9 | 両 | 仕（§6-14） | 仮 |
| F-D-17 | 会心 | 率を % で足して 0〜1 に収める・確定/封印フラグ | `CombatContext.ResolveCritRate` | フラグ 2 | ロ | 仕（§6-10・DCR §2-B） | 固 |
| F-D-18 | 段階エスカレーション | T5/10/15/20・std/rush/gentle/spike・狂暴化 T50 | `CombatSystem/Escalation.cs` | 型 4・段 5 | ロ | 仕（§6-8） | 動（6回） |
| F-D-19 | 収支トリガー | (与ダメ＋固定)−被ダメ の符号で勝ち/負け/引き分けの発火 | DCR §9.3 | 3 | ロ | 仕（§6-7） | 固 |
| F-D-20 | ボスのダウン | 目盛りで次のターンにダウン・攻撃なし・被ダメ ×1.5・大技取り消し・周期のやり直し | `CombatSystem/BossDown.cs`・`BossParams.DownNotches` | 目盛りを持つ敵 10（ボス 6＋ヴェスカ 4 段） | 両 | 仕（§6-15） | 動（11-13・未測定） |
| F-D-21 | 大技の周期 | `heavyPeriod`・溜め・深さ 3 のボスにも | `BossParams.Apply`・`enemies.json` | ― | 両 | 仕（§13-1） | 仮 |
| F-D-22 | 2 体戦 | 通常マスの組（エリート・ボスには無い） | `GameLoop/EncounterPresets.cs` | 組 14[^enc] | 両 | 仕（§5-4・§13-6） | 固 |
| F-D-23 | 敵とその能力 | 雑魚 24・精鋭 21・ボス 12（ファーム含む）・敵の能力 49 種 | `Resources/enemies.json`・`EnemyPassiveSkillEffects.cs` | 敵 57・能力 49[^enemy] | 両 | 食（G-09） | 動（8回） |
| F-D-24 | 視点の入れ替え | 敵の能力は player*/enemy* を入れ替えて走る | `PassiveSkillManager.FireEnemyTrigger` | 共有フィールド 10 | ロ | 仕（§6-12・DCR §0） | 固 |
| F-D-25 | ヴェスカ 4 段 | 遺物学者（18 品の抽選）・連続実験・停滞する時間・解析演算・裂け目・天与の才・大出血 | `Effects/VescaEffects.cs` | 段 4・抽選の品 18・固有 6 | 両 | 食（G-12） | 動（8回） |
| F-D-26 | 戦闘の終わり | 勝ち（報酬・端子の報酬・ドロップ）・負け・逃げた | `CombatManager.Finish.cs`・`GameManager.Combat.cs` | 3 | 両 | 仕 | 動 |

## E. キーワードと軸

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-E-01 | 毒 | 最大 HP の 0.05%/スタック・出すたび半減・上限 99 | `StatusRegistry.Defs["poison"]`・`CombatContext.cs:872-880` | ― | ロ | 仕（§7-1） | 動 |
| F-E-02 | 出血 | 毎ターン 1＋出血/10 減る・上限なし | `CombatContext`（出血） | ― | ロ | 仕（§7-2） | 動 |
| F-E-03 | 充電 | 出目の合計で得る・上限 30・過充電 7 以上 | `CombatManager.Turn.cs:17`・`CombatContext.cs:1374` | ― | ロ | 仕（§7-3） | 固 |
| F-E-04 | 臨界 | メーター 50 で爆発 +30・品 1 つ以上で有効 | `CombatContext`（rinkai）・`AllPassiveSkillEffects.cs:584` | ― | ロ | 仕（§7-4） | 動 |
| F-E-05 | シールド | 毎ターン半減（T1 は無し）・半減→付与の順 | `CombatContext.BeginNewTurn` Phase C | ― | ロ | 仕（DCR ③-D） | 固 |
| F-E-06 | キーワードの出来事 | 爆発・充電を得る/使う・過充電・シールド・毒・出血の出来事。1 連鎖で同じ品は 1 回 | `ItemSystem/.../Keywords.cs` `Kw`・`RaiseKeyword` | 出来事の種類 未確認 | ロ | 仕（§8-6） | 動 |
| F-E-07 | 【規則】の品 | 帳簿外の爆薬・雷壷・脈なしの鋼心臓 ほか | `ReworkItemEffects.cs` | 9（§8-6 の列挙） | ロ | 仕（§8-6） | 仮 |
| F-E-08 | 軸と偏重 | 秘宝の `axis`・偏重 6 つ（心眼・血晶・瘴気・炉温・電流・堅牢） | `items.json` `axis`・`AxisRules.cs` | 軸 7（金を含む）・軸なし 24 品 | ロ | 食（G-08） | 動 |
| F-E-09 | 誓約の封じ | 他の軸の入口を封じる・山札の端子にも及ぶ | `AxisItemEffects.cs` | 誓約 6 | ロ | 仕（§8-7） | 仮 |
| F-E-10 | 入り口の偏重の条件 | 最も大きい偏重/上位 2/絶対数 | `EntryEffect.Table` | 13 品に条件 | ロ | 未（§23-26） | 仮 |
| F-E-11 | 発火の時点 | OnPostRoll / OnRoll / OnWired / OnCriticalCheck など | `PassiveSkillTrigger` | 種類 未確認 | ロ | 仕（§8-4） | 固 |

## F. 品（秘宝・武器・消耗品・出目パーツ）

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-F-01 | 秘宝（全品） | 品そのものが 1 つの常時効果 | `items.json`（category Passive）・各 `*Effects.cs` | 167 品（BRONZE 47・SILVER 44・GOLD 42・LEGENDARY 34）[^items] | 両 | 食（G-07・G-09） | 動（16回） |
| F-F-02 | 秘宝の重複禁止 | 同じ品はラン中 1 個きり（手放しても再取得不可） | `PassiveAddHelper.AddPassiveItem`・`seenPassiveItemIds` | ― | ロ | 仕（§9-3） | 固 |
| F-F-03 | ステータスで書いた秘宝 | `stats`・`per`・`perEvery`・`perMax`・`on` | `StatModifierEffect`・DCR §2-C | 9 品[^items] | ロ | 仕（§9-2b） | 固 |
| F-F-04 | 剣の舞セット | 4 枚集めて〈ブレイドダンス〉（0G の非売品） | `GameLoop/SwordDanceSet.cs` | 4＋1 | 両 | 仕（§8-2） | 固 |
| F-F-05 | 武器 | 家系 4 × 段 2〜4 ＋〈竜閃〉。1 本だけ持つ | `items.json`（Weapon）・`Loadout.Gain` | 13 品[^items] | 両 | 食（G-10） | 動 |
| F-F-06 | 武器の段の効果 | 家系の専用ラダー（共有 skills 表） | `WeaponFamilyEffects.cs`・`WeaponProgression.Compute` | skills 30 段[^items] | ロ | 仕（§9-4b） | 仮（#43） |
| F-F-07 | 武器の "+" と強化 | +0/+1・T4+ が終点・入れ替えで +0 | `weaponPlus`・`GameManager.Rest.cs` | 段 2 × 3 | 両 | 仕（§9-4） | 固 |
| F-F-08 | 武器の入れ替えの窓 | 2 本目を得た時の窓・店は下取り | `RunState.pendingWeaponIds`・`ShopManager.WeaponTradeIn` | ― | 画 | 仕（§9-4） | 動（11-11） |
| F-F-09 | 消耗品 | 回復・護符・油 各 4 段・囮罠・古い手紙 | `items.json`（Consumable）・`GameLoop/Consumables.cs` | 18 品（うち職業 4）[^items] | 両 | 食（G-14） | 固 |
| F-F-10 | 職業スターター | 瞬間研磨剤・不抜の聖紋・痛覚遮断剤・仕込み刃（店に出ない） | `ClassStarter`・`EventOnlyItemFilter` | 4 | 両 | 仕（§11） | 固 |
| F-F-11 | 出目パーツ | 目 1〜9 × Tier 1〜4・同じ目は 1 枚・素の面 {2,3,4,5} | `GameLoop/DiceFaceParts.cs` | 36 | 両 | 仕（§21-2） | 固（0回） |
| F-F-12 | 旧 id の読み替え | 名前を変えた品の旧 id を新 id へ | `ItemDatabase.cs:331` `LegacyIdMap`・`TerminalCards.cs:379` | 品 171 組・端子 未確認[^legacy] | ロ | 食（G-31） | 固 |

## G. 経済（店・報酬・休憩・物資）

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-G-01 | 店の棚 | 秘宝 5・消耗品 3（1 枠目は回復）・出目パーツ 2・武器 1・素材 1・端子の棚 3 | `ItemSystem/Shop/ShopManager.cs` | 枠 15 | 両 | 仕（§16-1・§6-4） | 動（15回） |
| F-G-02 | 値段 | 基準価格 × 抽選幅 × レア度 × 物価（1＋3×(層/5)²）・5 層 +40% | `ShopManager.ProgressPriceMultiplier`・`RarityPriceMul` | ― | ロ | 食（G-07） | 動 |
| F-G-03 | 特売・割引 | 商才の段で枠・15/30/50/99% | `ShopManager`・`MetaPanel` | 段 4 | ロ | 仕（§15-1） | 固 |
| F-G-04 | 売却 | 店でだけ・レア度ごとの売値 | `ShopManager` | 5 段 | 両 | 仕（§16-4） | 固 |
| F-G-05 | 強盗 | 強奪 r10・1 ラン 1 回・物資を払い怪しい商人戦・以後は割増 | `ShopManager.RobberySurcharge`・`shopRobberyDone` | 1 | 両 | 食（G-06） | 固 |
| F-G-06 | 物資の売り場・囮罠 | 物資 1 = 2G・囮罠 8G | `ShopManager.TryBuyProvisionPack/TryBuyDecoy` | 2 | 両 | 食（G-05） | 未（UI 未実装と §16-1） |
| F-G-07 | 物資 | 上限 10・1 歩 1・前哨基地 +4・ボス +3 | `GameLoop/ProvisionSystem.cs`・`ProvisionRules.cs` | ― | 両 | 食（G-02〜G-06） | 固（11-09 に撤去済み） |
| F-G-08 | 戦闘の報酬 | 金 10G（エリート ×2）・素材・ドロップ | `GameLoop/CombatRewards.cs`・`FloorManager.cs` | ― | ロ | 仕（§17 値段表） | 動 |
| F-G-09 | 休憩 | 回復 30%・多めに 60%（1 手番）・鍛える・面を外す・端子を抜く | `GameManager.Rest.cs`・`DiceFaceParts.RestRemoveFace` | 選択 5 | 両 | 仕（§5-8・§21-2） | 動 |
| F-G-10 | 交換マス | いちばん段の低い秘宝 → 上の段 | `GameManager.TradeLowestPassive` | 1 | 両 | 仕（§5-2・§17） | 固 |
| F-G-11 | 秘宝の点（宝箱） | 秘宝 30%・残りは消耗品・金庫 r10 で金 | `GameManager.Rewards.cs` | 1 | 両 | 仕（§13-7） | 固 |
| F-G-12 | 強化素材 | 1 ランに約 56 入り使い道 16 | `GameManager.Rest.cs:87` | ― | ロ | 未（§23-21） | 仮 |
| F-G-13 | 貸金庫 | 層を突破するたび所持金 ×1.5（200G まで） | `MetaProgression/VaultBank.cs` | 1 | ロ | 仕（§15-1） | 固 |

## H. イベント

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-H-01 | イベント（全件） | 名前・出現条件・フレーバー・選択肢 | `Resources/Events/event_list.txt`・`event_list_observatory.txt` | 70 件（本体 67・観測所 3）・選択肢 171（159＋12）・抽選に出る 62[^ev] | 両 | 仕（§17） | 動（14回） |
| F-H-02 | 出現条件 | 層の範囲（深さで読む）・フラグ・秘宝・一度のみ・優先・稀・召喚専用 | `EventSystem/EventParser.cs:113-148`・`EventDatabase.IsAvailable` | 条件の種類 7 | ロ | 食（G-27） | 固 |
| F-H-03 | 抽選 | 優先プールの確率・重み（稀 0.33）・乱数 2 本 | `EventDatabase.Pick`（`EventDatabase.cs:68-138`） | ― | ロ | 食（G-26） | 動 |
| F-H-04 | 出やすさの重み | 重み[追跡]・重み[地図]・重み[素材] | `EventSystem/EventWeights.cs` | 札 3・付けたイベント 14 | ロ | 仕（§17） | 仮 |
| F-H-05 | 効果の種類 | HP・金・物資・素材・バフ・品・フラグ・戦闘・ターン+N・航行の効果 ほか | `EventEffectExecutor.cs`・`EventEffectType` | 46 種（§17 の列挙・廃止 1 を含む） | ロ | 仕（§17） | 動（16回） |
| F-H-06 | 確率分岐と分岐ごとの文 | `N%でA、N%でB`・`||` で文を分ける | `EventBranchText.cs` | 確率分岐の選択肢 12・分岐ごとの文 11[^ev] | 両 | 仕（§17） | 動 |
| F-H-07 | 選べない選択肢 | (a) 払えない (b) 得が何も起きない・全部暗い時の安全弁 | `EventChoice.UnavailableReason`・`EventEffectRoles` | 規則 2＋安全弁 | 両 | 仕（§17） | 動（11-13） |
| F-H-08 | 航行の効果とターン+N | 地図へ戻った時に 航行の効果 → ターン の順 | `GameManager.PassEventTurns`・`FreeMapSim.PassTurnsExternal` | 航行の効果 9 | 両 | 仕（§17） | 動 |
| F-H-09 | 近道+N | 次の移動 N 回は物資もターンも使わない（雹も払わない） | `FreeMapSim.GrantFreeMoves/MoveTo` | 1 | 両 | 仕（§17） | 動（11-13） |
| F-H-10 | 確信の連鎖 | 預言者の石板 → 啓示 → 災厄の予兆 | `EventDatabase.GetPriorityChance` | 3 段 | 両 | コ | 固 |
| F-H-11 | 観測所 | 召喚専用 3 件・写しを取る・会った回数 | `GameLoop/ObservatoryState.cs`・`ObservatoryTakeCopy` | 3 件 | 両 | コ | 固 |

## I. メタ進行（ラン外）

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-I-01 | 整備パネルの予算 | n 個目のポイント = 8n トークン・上限 24・リスペック無料 | `MetaProgression/MetaPanel.cs`・`MetaProgressManager.TryPurchasePoint` | 上限 24 | 両 | 仕（§15-1） | 固 |
| F-I-02 | 数値系トラック | 外殻・出力・剛胆・金庫・強奪・商才・宝物庫・装甲（各 10 段） | `MetaPanelKind.cs` | 8 本 × 10 段 | 両 | 仕（§15-1） | 動（7回） |
| F-I-03 | 宣言系トラック | 種火 5（充電・臨界・毒・出血・シールド）・端子調律 2（各 3 段） | 同上 | 7 本 × 3 段 | 両 | 仕（§15-1） | 仮（暫定） |
| F-I-04 | 枠から外したトラック | 兵站・精密・会心の種火（古い記録の段は丸める） | `MetaProgressState.ClampRanksToMax` | 3 | ロ | 食（G-51） | 固 |
| F-I-05 | トークンの獲得 | 層到達など（0 層は対象外） | `MetaProgression/MetaTokenEarner.cs` | 未確認 | ロ | コ | 固 |
| F-I-06 | 遺物の構成 | 冠・環・符 × 6 セット・★1〜4・強化 +0〜+4・サブ最大 3 個 2 段 | `MetaProgression/Relics/RelicCatalog.cs` | 部位 3 × セット 6 = 18 種の主ステ・接辞 17[^relic] | 両 | 仕（§15-5） | 仮（バランス未調整） |
| F-I-07 | 遺物の入手・分解・強化 | ランの終わりに 1〜3 個・セットの指名・分解 1 個 = 強化 1 段 | `RelicRoller.cs`・`Game2DRelicPanel.cs` | ― | 両 | 食（G-30） | 仮 |
| F-I-08 | 遺物のセット効果 | 2 個 / 3 個 | `RelicApplicator.cs` | 6 × 2 = 12 | ロ | 仕（§15-5） | 仮 |
| F-I-09 | 挑戦の難易度 | 0〜10・選べるのは記録 +1 まで・引く回数 = 難易度 | `MetaProgression/ChallengeCards.cs`・`Game2DChallengePanel` | 11 段 | 両 | 仕（§23-25） | 動（13回） |
| F-I-10 | 挑戦カード | 表がデバフ・裏がバフ・2 枚から 1 枚・同じ種類は 1 度 | `ChallengeCards.cs` `K(...)` | 種類 40（デバフ 27・バフ 13）・Tier つき 96 枚・配らない 2[^card] | 両 | コ（中身は仕様に書かない方針） | 動 |
| F-I-11 | 大罪（挑戦カード IV） | 嫉妬・暴食・色欲・強欲・傲慢・憤怒・怠惰 | `ChallengeCards.cs`・`PermanentDebuffIds.cs` | 7 | 両 | 食（G-41） | 仮 |
| F-I-12 | 実績 | 解除・隠し | `MetaProgression/Achievements/AchievementCatalog.cs` | 18[^ach] | 両 | コ | 固（2回） |
| F-I-13 | 図鑑（GALLERY） | 読み物・層・敵・品の 4 分類・解禁 6 段 | `Codex/WorldVignettes.cs`・`VignetteUnlockState.cs`・`Game2DCodexPanel` | 読み物 10・層 8・敵 57・品 198[^codex] | 両 | 仕（§20） | 動（5回） |

## J. 記録と保存

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-J-01 | 中断セーブの時点 | 航行のマップで何も待っていない時だけ | `GameManager.Save.cs:16-30` `CanSuspendNow` | 断る理由 6 | 両 | 仕（§4-5） | 動（3回） |
| F-J-02 | 中断セーブの中身 | RunState 全部・盤面・FreeMapSim・乱数・戦闘の通し番号・近道の残り | `GameLoop/RunSave.cs` | 写す物 9 区分（§4-5） | ロ | 仕（§4-5） | 動 |
| F-J-03 | 中断セーブの書き込み | 一時ファイル → 消す → 移す | `RunSave.cs:179-182` | ― | ロ | 食（G-24） | 動 |
| F-J-04 | 再開の窓 | 続きから・新しく始める（確かめて消す）・やめる。読んだら消す | `Game2DResumePanel` | 3 択 | 画 | 仕（§4-5） | 仮 |
| F-J-05 | 版と指紋 | `RunSave.Version` = 1・RunState の名前と型の指紋・メタの指紋 | `RunSave.cs:37,115,505` | 3 | ロ | 仕（§4-5） | 動 |
| F-J-06 | メタ進行の保存 | PlayerPrefs `MetaProgressState_v1`・壊れたら新しい状態 | `MetaProgressManager.cs:15,62-96` | 1 鍵 | ロ | 食（G-25） | 固 |
| F-J-07 | そのほかの PlayerPrefs | 設定 9・手引きの印・観測所 2・BGM | `GameSettings.cs`・`TutorialSeen.cs`・`ObservatoryState.cs`・`TitleBgm.cs` | 7 ファイルが使う[^prefs] | ロ | コ | 固 |
| F-J-08 | 戦記（台帳） | 1 行 1 出来事のコード | `GameLoop/RunChronicle.cs` | コード 22[^chr] | ロ | 食（G-48） | 固 |
| F-J-09 | ダイジェスト | 選抜の規則・最大 20 行・数値を出さない | `GameLoop/RunDigest.cs` `MaxBeats` | 規則 5 | 両 | 仕（§20-1） | 固 |

## K. 乱数と決定性

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-K-01 | GameRng | `hash(塩, キー, 連番)`・キー別カウンタ | `GameLoop/GameRng.cs` | 文字列キー 30 種（直書きのもの）[^rng] | ロ | 仕（§4-5） | 固 |
| F-K-02 | シードの入力と表示 | `TryParseSeed`・`FormatSeed`・`SetMasterSeed` | `GameRng.cs` | ― | 両 | コ | 固 |
| F-K-03 | 戦闘の通し番号 | `_combatSeq` はランごとに戻す | `CombatManager._combatSeq`・`GameManager.StartNewRun` | ― | ロ | 仕（§23-20） | 固 |
| F-K-04 | GameRng を使わない乱数 | 遺物の抽選（`System.Random`）・画面の演出 | `RelicRoller.cs:25-28`・`Game2DRelicPanel.cs:17`・`Game2DBattleFx.cs`・`Game2DRadar.cs` | 4 ファイル | ロ | 食（G-30） | 仮 |
| F-K-05 | BOT と人の一致 | 同じ操作列なら同じ結果・〈猫の手〉は専用キー | `GameManager.MoveStep` ほか・`challenge.cat` | ― | ロ | 仕（§5-8・§23-25） | 動 |

## L. 表示・文字・音・演出

| 番号 | 単位 | 中身 | 正本 | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|
| F-L-01 | フォントの指定 | 指定は 1 か所・TMP は `GameFont.Apply`・液晶は `Game2DJpFont` | `UI/GameFont.cs` | 1 | 画 | 仕（CLAUDE.md） | 固（2回） |
| F-L-02 | 液晶のドット字にある字 | DenkiChip＋拡張フォント | `Tools/fontext/glyphs.txt`・`Tools/glyph_check.py` | 未確認（字数） | ロ | 仕 | 固 |
| F-L-03 | 画面に出る文字列 | 品・敵・イベント・端子・カード・手引き・結末・図鑑・UI の文 | items.json・enemies.json・event_list・各 .cs | 概算 約 1,500 本[^str] | 両 | ― | 動 |
| F-L-04 | 手引き | 契機で 1 度だけ出す・見出しが印の鍵 | `docs/handbook.md`・`Game2DTutorial.cs`・`Game2DTutorialText.cs` | 節 32・表に載る 31[^tut] | 画 | 食（G-21・G-22） | 動（21回） |
| F-L-05 | 層題のバナー | 第一〜第五層・終層 | `GameLoop/LayerTitles.cs`・`Game2DLayerBanner` | 8 | 画 | 仕（§5-6） | 仮 |
| F-L-06 | 戦闘の画面 | 予告・HP の棒の目盛り・手札・残す印・猫の印・敵の詳細の窓・DEF | `Game2DBattleHud.cs`・`Game2DEnemyPanel.cs` | 未確認 | 画 | 仕（§6-4・§6-15） | 動 |
| F-L-07 | 地図の画面 | 点・道・縄張りの赤・宝の印・状態の帯・近道 N 回・ETA | `Game2DMapView*.cs`・`Game2DNavExtras.cs` | 未確認 | 画 | 仕（§5-9・§17） | 動 |
| F-L-08 | 場面の画面 | 店・休憩・イベント・宝・交換・門・結果・一択 | `Game2DPhaseScreens.cs` | 未確認 | 画 | 仕（仮置き） | 動 |
| F-L-09 | 液晶の効果 | 走査線・湾曲・色収差・ビネット・ノイズ | `UI/Lcd/`・`Game2DSettingsApply.cs` | 5 | 画 | 仕（memory 11-02） | 固 |
| F-L-10 | 効果音 | 名前で鳴らす WAV（`Resources/Game2DSfx/`） | `UI/Game2D/Game2DSfx.cs` | 未確認（WAV は git 管理外） | 画 | 食（G-33） | 仮 |
| F-L-11 | BGM | タイトルの曲 | `UI/TitleBgm.cs` | 未確認 | 画 | コ | 仮 |
| F-L-12 | 戦闘の演出 | 毒・出血・会心の FX シート | `UI/Game2D/Battle/Game2DBattleFx.cs`・`Resources/Game2DFx` | FX 3 | 画 | 仕（memory: CombatManager 未接続） | 仮 |

## M. BOT と測定の口

| 番号 | 単位 | 中身 | 正本 | 製品に残るか | 個数 | 画面 | 確かさ | 凍結 |
|---|---|---|---|---|---|---|---|---|
| F-M-01 | GameHooks | BOT が判断を差す口・`BotDriving`・`MetaPersistenceDisabled` | `GameLoop/GameHooks.cs` | 残る（本体の一部） | 未確認 | ロ | 仕（§2・CLAUDE.md） | 動 |
| F-M-02 | BossParams.Overrides | ボスの数値を外から差す口（既定 null） | `CombatSystem/BossParams.cs` | 残る | 1 | ロ | 仕（§13-3） | 固 |
| F-M-03 | 〈猫の手〉の評価 | AutoTest の Super の評価を本体が呼ぶ（人が遊ぶ時も） | `GameHooks.CatPawPick`・`AutoTest.CatPawAdvisor` | 残る（札の効果） | 1 | 両 | 仕（§23-25） | 動 |
| F-M-04 | AutoRunner | ヘッドレスの自動プレイ・partial 群 | `AutoTest/AutoRunner*.cs` | 残る（Assets/Scripts 内・ビルドに入る見込み・未確認） | 52 ファイル | ロ | 仕（§21） | 動（416回） |
| F-M-05 | 測定の口（フィールド） | `handCarry`・`bossDown`・`pursuerLeavesTerritory`・`territoryKeepBypass` ほか | `AutoRunner` | 残る | 未確認 | ロ | 仕（各節） | 動 |
| F-M-06 | エディタのメニュー | AutoRun・並列・検証 | `Assets/Editor/*.cs` | 残らない（Editor） | `MenuItem` 212 か所 | ロ | コ | 動 |
| F-M-07 | エディタのテスト | GameRng の決定性・回帰キャッシュ・ワーカーの目録 | `Assets/Editor/Tests/*.cs` | 残らない | `[Test]` 14 | ロ | コ | 固 |
| F-M-08 | 開発用の表示 | `GameDebugHUD` | `GameLoop/GameDebugHUD.cs` | 残る（シーン次第・未確認） | 1 | 画 | コ | 固 |

---

## 抜けやすい所（明示の確認）

| 抜けやすい所 | この表の番号 | 状態 |
|---|---|---|
| 中断セーブと復帰 | F-J-01〜05・F-A-07 | 実装済み。戦闘中・8 層は対象外 |
| ラン跨ぎの記録 | F-I-01〜13・F-J-06〜07 | メタ・遺物・実績・図鑑は PlayerPrefs 1 鍵、観測所・設定・手引きの印は別の鍵 |
| 乱数とシード | F-K-01〜05 | GameRng。遺物は別の乱数 |
| 場面の遷移 | F-B-01 | 18 状態。`TrapTriggered` は使われない |
| 起動/終了・設定 | F-A-01〜09 | 製品版の自動中断セーブはエディタで再現できない |
| フォントと文字 | F-L-01〜04 | 検算台あり（glyph_check） |
| 演出と音 | F-L-09〜12 | 戦闘 FX は CombatManager 未接続（memory）・WAV は git 管理外 |
| BOT/測定の口 | F-M-01〜08 | 製品に残る口は GameHooks・BossParams.Overrides・猫の手・AutoRunner（Assets/Scripts 内） |

## 数え方

[^cls]: `Assets/Scripts/GameLoop/RunState.cs` の `enum ClassType`（4 値）。
[^set]: `Assets/Scripts/UI/GameSettings.cs` の `Defs` 配列の `new Def` を目で数えた（9 行）。
[^key]: `grep -o "KeyCode\.[A-Za-z0-9]*" Assets/Scripts/GameLoop/GameManager.cs | sort | uniq -c`（Alpha1/2/3・Escape・F・G・L・R・S・Space・T の 11 種）。
[^phase]: `enum GamePhase` を `GameManager.cs:75-97` で数えた（18 値）。遷移は `grep -rn "SetPhase(GamePhase\.\|CurrentPhase = GamePhase\." --include=*.cs Assets/Scripts` が 62 行。`TrapTriggered` を立てる行は 0。
[^mod]: GAME.md §5-5 の表と `FloorModifier.cs`（深さ 3/5/6）。コードの値は未照合。
[^end]: `Assets/Scripts/GameLoop/Endings.cs` の `id = "end1"`〜`"end4"` と `EndTrueId`/`EndGoodId`。
[^wx]: `Assets/Scripts/MapSystem/FreeMove/Weather.cs` の `enum WeatherKind`（None と欠番 2 を除く 6）。
[^nav]: `Assets/Scripts/GameLoop/Ids.cs:85-104` の `NavItems` 集合（13）。
[^term]: `Assets/Scripts/CombatSystem/TerminalCards.cs:285-400` の `Build()` 内の `D(TerminalIds.…, 種類, …)` を種類別に数えた（`grep -o "D(TerminalIds\.[A-Za-z]*, [ABCS]"`）。`Rare(D(` が 14。GAME.md の「ITT 並列 (端子 57 種)」は基本 3 種を除いた数で、60 − 3 = 57 と一致。
[^cond]: `TerminalCards.cs:21-31` の `enum TerminalCond`。
[^enc]: `Assets/Scripts/GameLoop/EncounterPresets.cs` の `new EncounterPreset` の数（14・通常抽選の組を含む・2 体戦の組の数としては未確認）。
[^enemy]: `Assets/Resources/enemies.json` を Python で読み、`id` が `boss` で始まる 12、`elite: true` が 21、残り 24（ファームの箱・怪しい商人・偽の商人を含む）。能力は各敵の `passiveSkills` の異なる id の数（49）。
[^items]: `Assets/Data/InventorySystem/items.json` を Python で読んだ。`category` 別に Passive 167・Consumable 18・Weapon 13（計 198）。秘宝のレア度は BRONZE 47・SILVER 44・GOLD 42・LEGENDARY 34。`stats` を持つ品 9。最上位の `skills` 表 30。
[^legacy]: `ItemDatabase.cs` の `LegacyIdMap = new …` から `};` までの `{ "旧", "新" }` を正規表現で数えた（171 組）。端子側は書式が違い数えていない。
[^ev]: 2 つの `.txt` を Python で読み、`#` 行と空行を除き、`:` で 4 つに割れる行をイベント、4 つ目を `/` で割った数を選択肢とした。`N%で` を含む選択肢 12、`||` を含む 11、`@@` を含む 159＋12。条件に `召喚専用` を含む行が 5（67 − 5 = 62）。区切り文字の例外は考慮していないので ±数件の誤差がありうる。
[^relic]: `RelicCatalog.cs:13-38` の `RelicSlot`（3）・`RelicSet`（6）・`RelicStat`（17：サブにも出る 8・主ステ専用 9）。
[^card]: `ChallengeCards.cs:66-140` の `K(...)` 行を数え（40）、2 番目の引数 `true`（バフ）が 13。`T(a,b,c,d)` の null でない値を Tier 別に数えた（I 20・II 20・III 25・IV 31 = 96）。`pending: true` が 2。
[^ach]: `AchievementCatalog.cs` の `D("` の数（18）。
[^codex]: `WorldVignettes.cs` の `new Vignette` の数（10）。層は `LayerTitles` の 8 行（GAME.md §5-6）、敵と品は enemies.json・items.json の件数。図鑑に載る範囲そのものは未確認。
[^prefs]: `grep -rln "PlayerPrefs\." --include=*.cs Assets/Scripts`（AutoRunner.Ascension・ObservatoryState・MetaBuffApplicator・MetaProgressManager・GameSettings・TitleBgm・TutorialSeen）。
[^chr]: `grep -c '= "[A-Z][0-9]";' Assets/Scripts/GameLoop/RunChronicle.cs`（22）。
[^rng]: `grep -rhoE 'GameRng\.(Value|Range|Int|Next|Chance|Pick|Shuffle)[A-Za-z]*\("[^"]+"'` の異なる鍵（30）。変数で鍵を渡す呼び出しは数えていない。
[^tut]: handbook の `## ` 見出し 34 から「目次」「See Also」を除く 32。`Game2DTutorial.cs` の `TutorialTopics` の定数 32、`Table`（110〜143 行）の `new Topic` 31（`BossDown` が無い）。
[^str]: 品 198 × 3（名前・説明・フレーバー）＋ 敵 57 × 2 ＋ イベント 70（フレーバー）＋ 選択肢 171 × 3 ＋ 端子 60 ＋ カード 96 ＋ 手引き 32 ＋ 結末 6 ＋ 読み物 10 ＝ 1,488。UI の .cs の文字列は数えていない（見立てで数百）。
