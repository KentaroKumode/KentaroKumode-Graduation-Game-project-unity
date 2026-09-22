# 引き継ぎ手順書 — ゲーム完成までのロードマップ（Opus 4.7 向け）

作成: 2026-07-15。読者: このプロジェクトを引き継ぐ AI アシスタント（Opus 4.7）。
**まず本書 → CLAUDE.md → docs/GAME.md → ADR-0008/0009 の順で読むこと。**

> ⚠ **2026-07-27 注記**: 本書は 2026-07-15 時点のスナップショット。
> 以降に 整備パネル v6・会心リバランス・挑戦デバフ draft・docs 統合が入っており、
> **現況は [GAME.md](GAME.md)（特に §23 未決事項）が正本**。本書はロードマップの記録として残置。

---

## 0. 現状の俯瞰（2026-07-15 時点）

Unity 2022.3.22f1 / Built-in RP のローグライク。「机上の架空電子ボードゲーム機」が筐体で、
LCD（RT 転写・dot 厳守）にゲーム画面、盤上に LED ダイス・物理コイン・インベントリグリッドが実在する。

| 領域 | 状態 |
|---|---|
| コアループ（マップ→戦闘→ショップ→ボス、5層+Λ層） | **実装済・通しプレイ可** |
| 現行戦闘（ロール勝負ベース） | 実装済だが **ADR-0009 で全面刷新が決定済み（Proposed）** |
| 契約12旅団 / 職業4種 / メタ進行 / イベント / 異常現象 | 実装済・バランス収束済（旧戦闘基準） |
| 出自選択画面（Papers Please 風登録票） | **実装完了**（ページめくり・スタンプ・出自テキスト・詳細タブ） |
| ADR-0008 疑似3D 描画規約 | Accepted。W1〜W10 の実装タスクが未着手 |
| ADR-0009 相互攻撃モデル | **設計確定・AutoRunner 検証待ち → 検証通過で Accepted** |
| 計器盤（HP/空腹/希望/ターン/充電/エスカレーション） | **未実装**（現状 GameDebugHUD の IMGUI のみ） |
| バトル演出（RT→LCD 鍔迫り合い・3D敵着弾） | 方針あり・未実装 |
| 永劫回帰エンド（E5） | 部分実装・残作業あり（凍結中） |
| ビルド | 未ビルド（エディタ内のみ） |

**プロジェクトの重心は「ADR-0009 戦闘刷新の検証→実装」に移っている。** これが全バランス・
全演出・全 UI の前提になるため、他タスクより先に潰す。

## 1. 絶対規約（違反禁止・CLAUDE.md の要約 + セッション蓄積分）

1. **唯一の正本は実コード。** ダメージ計算は `Assets/Scripts/CombatSystem/DAMAGE_CALC_REFERENCE.md`
   （式を変えたら同時更新）。アイテムは `items.json`。大罪は `PermanentDebuffIds.cs`。
2. **ID 直書き禁止** — `Assets/Scripts/GameLoop/Ids.cs`（ItemIds/BossIds）経由。ボス判定は `BossIds.IsBoss(id)`。
3. **localScale で状態表現しない**（両描画ゾーン共通）。色/明度/差し替えスプライトで。
4. **CombatContext 新フィールド**は perTurn/decay/persistent の 3 区分を XML コメントに明記し、
   BeginNewTurn の該当 Phase にリセットを追加。一時値は辞書へ。
5. `BALANCE_CHANGELOG_*` / `BALANCE_TIER_LIST_*` は BOT 生成 — 改変禁止。
6. **画中画の原則**（ADR-0008）: LCD の外（筐体/ダイス/充電/計器盤）に lore は一切関与しない。
   盤上機構のフレーバーはメカ語彙のみ。進捗資料にもロア/ストーリーを書かない。
7. **再提案禁止（ユーザーが明示棄却済み）**: 勝負判定ベースの配線 / チップソケット複数化 /
   整流器 / 職業固有端子 / ロール勝負の存続 / ダイス側パッシブ。棄却理由は ADR-0009 Context 参照。
8. ボス自動チューナー(L3)は隠し倍率禁止・実数値を直接調整。
9. ダイス「個数」（振る本数）と「合計値」（出目の和）を常に区別して書く。
10. LCD 素材は PPU=8・Point フィルタ。インポート時 PPU=8100 になる事故が頻発した — 要確認。

## 2. 作業フローの約束

- スクリプト変更後は Unity MCP `read_console` でコンパイルエラー確認（既知の無害エラー:
  "CoinPrefab is not assigned!"）。MCP 切断時は `refresh_unity` リトライ、ダメならユーザーに再接続依頼。
- 仕様変更は「コード + DAMAGE_CALC_REFERENCE.md（該当時）+ docs/GAME.md 該当節」を同時更新。
- 大きな設計判断は ADR 新設 or 既存 ADR 追記。棄却案も理由付きで記録する（このプロジェクトの流儀）。
- バランス計測は AutoRunner（BOT）。職業はランダム均等サンプル。

---

## 3. 完成までの全手順（フェーズ順・依存関係順）

### Phase 1 — ADR-0009 検証（**完了 2026-07-15**）

1. ~~ヘッドレスシミュレータ~~ → [tools/adr0009_sim.py](../tools/adr0009_sim.py) で実施、
   Verification 5 条件合格（結果は ADR-0009「Verification 結果」節と
   `AutoRunLogs/adr0009_sim/sim_report.txt`）。
2. 推奨初期値確定: 傾斜 mid `[1,1.3,1.6,1.95,2.3]` / 充電 N=3・最大10・初期5 /
   敵基礎攻撃値≈threat×1.0・敵ダイス寄与β≈0.3・敵HP×0.5目安。
   特殊端子 11 種の数値と価格は **未確定**（C# 実装後の AutoRunner 実測で当てる）。
3. ADR-0009 = Accepted / ADR-0005/0006 = superseded 更新済み。
   **残**: C# 実装（W7）後に会心・実パッシブ込みの AutoRunner 実測で最終確認。

### Phase 2 — 盤面基盤（ADR-0008 W1〜W6、Phase 1 と並行可）

4. **W1-g**: Play モードで LcdScreen/DiceLED/Grid/CoinSystem/worldCamera の実座標を計測し、
   机上配置図を確定（ADR-0008 W1 詳細 a〜f のチェックリストに従う）。
5. **W2〜W4**: 端子スプライト実体化（5 枠固定・蓋絵）/ 配線パターン描画 / 状態ハイライト。
6. **W5**: BoardPointer 新設（**専用レイヤーを最初に切る** — LcdPointer の RaycastAll 問題再発防止）。
7. **W6**: LED ダイスの選択対応（Collider + 点滅 + DiceLEDManager API）。
8. 全体タスク #2: LCD 側 sortingOrder 帯域表の定数化。#3: 素材制作規約の文書化。

### Phase 3 — 戦闘接続（W7 = ADR-0009 実装。Phase 1 Accepted 後）

9. ターン構造 8 段（ADR-0009 のコードブロックが正）を CombatManager に実装。
   解決順「プレイヤー先制・撃破時は敵攻撃なし」は体感難度の急所 — 変更時 AutoRunner 再計測必須。
10. 相互攻撃パイプライン: ロール勝負の完全撤去、収支トリガーへの再定義
    （OnRollWin→収支プラス / OnRollLose→収支マイナス）、diceDifference 依存十数個の廃止/読み替え。
11. enemies.json に敵基礎攻撃値 + エスカレーションプロファイル（急/緩/スパイク型）を新設。
12. 充電経済（柱5）: 充電端子・ターン開始消費・停電（全停止・デバフ免税・戦闘中トリガーのみ課税）。
13. 特殊端子 11 種の実装 + ショップ販売（接続制限 N・レアリティ連動）。
14. ダイス固有パッシブ 7 種をパッシブアイテムへ転生（ADR-0009 転生表どおり）。
15. **DAMAGE_CALC_REFERENCE.md §1 を全面改訂**（実装と同時・規約どおり）。
    docs/GAME.md §6 も追従。ボス固有機構（覚者7形態・シュヴァリエ等）は個別移植判断。

#### W7 実装メモ（2026-07-15 着手時の棚卸し）

- **完了済み (step 1)**: [EnemyData.cs](../Assets/Scripts/CombatSystem/EnemyData.cs) に
  `baseAttack` / `attackDiceWeight` / `escalationProfile`（JSON 未指定時は threat 由来の既定値）、
  [Escalation.cs](../Assets/Scripts/CombatSystem/Escalation.cs) 新設
  （閾値 T5/10/15/20・std/rush/gentle/spike 曲線・`EnemyAttackValue()` = ADR-0009 柱2/4 の正本）。
  **未コンパイル確認**（Unity 未接続）── エディタ起動後に必ず read_console で確認すること。
  enemies.json 自体は未改変（旧パイプライン稼働中のため。数値の実書き込みは切替後）。
- **step 2 の方針**: `ExecuteTurn()` は旧機構（スタンス ADR-0005/0006・サドンデス・異常現象・
  刻印）が深く絡むため直接改造しない。**新メソッド `ExecuteTurnMutual()` を別建てし、
  切替フラグで選択**（AutoRunner が新旧比較できる形）。旧法は W7 完了まで既定のまま。
- **step 3 の依存棚卸し（grep 実測 2026-07-15）**:
  - `OnRollWin/OnRollLose/OnRollDraw` トリガー: **79 箇所 / 8 ファイル**
    （AllPassiveSkillEffects 31・EnemyPassiveSkillEffects 33・CombatManager 4・
    Contracts 4・Sigils 1・PassiveSkillManager 3・PassiveSkillTrigger 3）
  - `playerWonRoll/playerLostRoll`: **51 箇所 / 5 ファイル**（PassiveSkillManager 15・敵効果 20 が主）
  - `diceDifference`: 読み取りは CombatContext のプロパティ + AllPassiveSkillEffects
    （断罪の天秤 等）+ PassiveSkillManager の effDiff。
  - 再定義方針: 収支（与ダメ−被ダメ）は**解決 (step6) 後に確定**するため、トリガー発火位置が
    旧法の「ロール直後」から「解決後」へ移る ── 効果側の前提（ダメージ計算前に発火して
    ctx に積む）が崩れる箇所を 1 件ずつ判定すること。ここが W7 最大の手術点。

### Phase 4 — 計器盤（W1-b。Phase 2 の配置図確定後、Phase 3 と並行可）

16. HP（バー+数字）/ 空腹・希望（バー）/ ターン（数字）の物理計器を盤上に新設
    （DiceLED / DiceMonitorDisplay の描画資産流用）。
17. 充電ゲージ（10 目盛り・消費予定分点滅）+ エスカレーションゲージ（ターン数字+閾値ランプ）。
18. GameDebugHUD は開発用に温存。

### Phase 5 — 演出（Phase 3 後）

19. **W8**: 転送演出（光走り 0.15〜0.2s・スキップ可・同配線連続ターンは省略）。
20. LCD 側新規画面: 敵インテント表示・収支プレビュー・配線確定パネル（勝敗表示→収支表示へ置換）。
21. **W9**: 3D 敵着弾演出（帰属決定→実装）。バトル演出方針
    （RT→LCD 転送・向かい合い・武器固有エフェクト）は memory/project_battle_visual_direction 参照。

### Phase 6 — バランス再収束（Phase 3 完了後・必須）

22. BOT フルスイープ: 契約 12 旅団 / 職業 4 種 / 特殊端子 / エスカレーション傾斜 / 電力経済。
    既存バランスは旧戦闘基準なので全面再収束になる。ボスは L3 チューナー（実数値直接）。
23. Λ層の再調整（memory/project_lambda_layer — 未再調整のまま）。

### Phase 7 — コンテンツ仕上げ（凍結解除後）

24. 永劫回帰エンド残作業: 覚者戦選択肢 / 無上正等覚の戦闘機構 / E5 専用 codex / コンパイル確認。
25. lore-endings.md 凍結解除時の 2 行差分（class-system.md に TODO 記載済み:
    祖父母急逝→1921 年頃 / §1-E 組替）。
26. codex/永劫回帰の手術案（断片形式化）は**提案止まり・ユーザー判断待ち**。勝手に着手しない。

### Phase 8 — 小粒残件（隙間で消化）

27. 騎士タブアイコン（槍）と実武器（盾）の不一致 — 槍実装 or アイコン差し替え（ユーザーに選択を仰ぐ）。
28. 肖像のドシエ写真化アセット適用（変換ツールは tools/dossier_photo.html 完成済み・素材はユーザー作成）。
29. kit_gui フォルダの AssetPostprocessor（PPU=8 自動化）— 提案済み・ユーザー未回答。
30. LCD テキストが指定色より薄い件（Scanline ×1.12 が原因・**ユーザー保留中** — 指示があるまで触らない）。
31. Codex/SkillTree の描画経路棚卸し（ADR-0008 残タスク #1）。

### Phase 9 — ビルド・リリース準備

32. 初回ビルド（未ビルド）。ビルド固有問題（RuntimeInitializeOnLoad・RT・シェーダ）の洗い出し。
33. 全職業×全エンディングの通しプレイ検証（AutoRunner + 手動）。
34. docs/GAME.md §23 の未決事項を順次解消。

## 4. 優先順位の要約

| 優先 | 内容 | 理由 |
|---|---|---|
| P0 | Phase 1（AutoRunner 検証→ADR-0009 Accepted） | 全バランス・UI・演出のゲート |
| P1 | Phase 2（W1 配置図→配線 UI 基盤） | P0 と並行可・戦闘接続の前提 |
| P2 | Phase 3（戦闘接続）→ Phase 4（計器盤） | ゲームの新しい心臓 |
| P3 | Phase 5（演出）→ Phase 6（再収束） | 心臓が動いてから |
| P4 | Phase 7〜9 | コンテンツ仕上げ→ビルド |

## 5. 判断に迷ったら

- 描画の帰属: 「読む情報」= LCD / 「触る装置」= ワールド（ADR-0008）。
- 設計の分岐: ADR-0009 の「棄却・改廃の記録」を先に読む — 大半の案は一度議論済み。
- それでも不明なら**ユーザーに聞く**。特にゲームデザインの新規提案・棄却済み案の復活・
  凍結中タスク（codex/E5/lore-endings）の着手は必ず事前確認。
