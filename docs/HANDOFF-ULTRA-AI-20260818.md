# Ultra AI 引継ぎ書（2026-08-18）

## 0. 目的と現在の結論

この文書は、次の担当者が推測なしで Ultra AI を再開できるよう、実装済み範囲、実走結果、停止理由、再起動手順、完全版までの残作業を固定する。

現在の Ultra は **RunStartOnly** である。standalone の production Player を複数起動して凍結方策を比較し、採用方策を選んでから本10,000ランを開始する。ショップ・イベント・経路・戦闘ターンごとに再思考する完全版 Ultra は未実装。

~~現時点では本10,000ランはまだ開始していない。~~

> **2026-08-18 更新。** §2 のブロッカーを修正（§2-9）し、portfolio 4候補×1000ラン を完走、
> selector が `super-baseline-v1` を採用、**本10,000ラン を開始した**（§11）。
> あわせて §8「candidate識別力」の異常の原因が判明し解消した ── 同じ mechanical profile
> 不一致が原因で、**前回の portfolio は 4 候補が同一挙動だった**（§8 参照）。

---

## 1. 最新の実走状況

最新セッション:

```text
AutoRunLogs/ultra_planning/20260817215206-133d0d79904744748a23d3092203aa53
```

| 候補 | 進捗 | 状態 |
|---|---:|---|
| `super-baseline-v1` | 1000/1000 | response出力済み |
| `optimal-v1` | 1000/1000 | response出力済み |
| `super-pure-v1` | 960/1000 | 親側停止により中断 |
| `super-high-variance-v1` | 820/1000 | 親側停止により中断 |

完走した2候補はいずれも valid 999/1000、7層クリア140/1000。共通ordinal 0だけが同じsynthetic seedで`Deadlock`となった。full-clear相違は0件、deterministic digestは988/1000件で一致。

### 適用済み修正

1. Unityが`Library`配下へのPlayer buildを拒否するため、出力先をプロジェクト直下の`UltraProductionWorkerBuild/`へ変更。
2. `UltraProductionRunRecord.scenarioSeedHex`設定漏れを修正。
3. portfolio evidenceのinvalid処理を変更。
   - 全候補で同じordinalがinvalidなら比較から共通除外。
   - 候補ごとにvalid/invalidが違えばhard reject。
   - 共通invalidが`max(5, runCount/20)`を超えてもhard reject。
   - McNemarの差分率は共通valid runだけを分母にする。

注意: 3番の変更後はまだUnity再コンパイル・再実走していない。

---

## 2. 確定ブロッカー → **2026-08-18 修正済み**

> **状態: 解消。** 下記の設計どおり実装し、compile 0 error / Ultra EditModeテスト 54件 全passまで確認済み。
> 実装差分は §2-9 にまとめた。**未実施は「実走」だけ**（§6 の手順へ進んでよい）。

**worker試走と本10Kのmechanical profileが一致していない。修正前に再起動しないこと。**

直近EditorPrefsでは本ラン側が相互攻撃ON、出目パーツ陳列抑止ON、軽減無視の盾肩代わりON。一方workerはこれらを明示せずAutoRunner既定値を使用した。

Ultra v1の正式条件はEditorPrefsから切り離して次へ固定する。

```text
useMutualAttackPipeline = true
suppressFacePartOffers = false
shieldAbsorbsUnmitigable = true
enableAllDebuffs = false
sweepAllMetaAxes = false
challengeSpec = ""
```

`suppressFacePartOffers=false`は計測用の遮断を外し、製品アイテムを含めるため。将来別条件に変える場合もEditorPrefs継承ではなくprofile versionを更新する。

### 修正対象

1. `Assets/Scripts/AutoTest/Ultra/UltraAutoRunProfile.cs`
   - 上記fieldをprofileへ追加。
   - `CreateStandard50WithRelic10000`で固定。
   - `ApplyTo`でAutoRunnerへ代入。
   - `Matches`で全値を検査。
2. `Assets/Scripts/AutoTest/Ultra/UltraPolicyPortfolio.cs`
   - `UltraPortfolioProfileSpec`へ同じmechanical fieldを追加。
   - `RequestProperties` whitelistを更新。
   - `CanonicalProfile`、`ComputeProfileFingerprint`、`TryValidateCanonicalProfile`を更新。
3. `Assets/Scripts/AutoTest/Ultra/UltraProductionWorkerBootstrap.cs`
   - request profileから同じ値をworker AutoRunnerへ代入。
   - 既定値やEditorPrefsへ依存しない。
4. `Assets/Editor/AutoRunMenu.cs`
   - Ultra専用menuで残留SessionStateをclear/上書き。

worker側に必要な代入例:

```csharp
_runner.useMutualAttackPipeline = profile.useMutualAttackPipeline;
_runner.suppressFacePartOffers = profile.suppressFacePartOffers;
_runner.shieldAbsorbsUnmitigable = profile.shieldAbsorbsUnmitigable;
_runner.enableAllDebuffs = profile.enableAllDebuffs;
_runner.sweepAllMetaAxes = false;
_runner.challengeSpec = profile.challengeSpec ?? "";
```

専用profileでは、存在する既存fieldを確認したうえで次の別モードも明示OFFにする。

```text
simBoss5Sweep / lambdaFarmSweep / relicSweep / relicAxisSweep
challengeAxisSweep / challengeCategorySweep / personaSweep
ascension / skillComparison / tierCalibration
```

今回のSessionStateはcleanだったため直近実走には混入していないが、再現性確保に必要。

### 2-9. 実装結果（2026-08-18）

| 対象 | 変更 |
|---|---|
| `UltraAutoRunProfile` | mechanical field 6 個を追加。`ApplyTo` で代入し、競合バッチモード 10 個を明示 OFF。`IsCanonical` / `Matches` の両方で検査 |
| `UltraPortfolioProfileSpec` | 同じ 6 個を追加。**`CanonicalProfile()` は `UltraAutoRunProfile` から導出する** ── 条件を 2 箇所に手書きすることが今回の不一致の原因なので、書き写しをやめた |
| `ComputeProfileFingerprint` | **v1 → v2**。mechanical field を hash に含めた |
| `RequestProperties` | 新 field 6 個を whitelist へ追加 |
| `TryValidateCanonicalProfile` | `profile_mechanics_not_canonical` を追加 |
| `UltraProductionWorkerBootstrap` | request の profile から代入。既定値・EditorPrefs へ依存しない。競合モードも明示 OFF |
| `AutoRunMenu.RunUltra50WithRelic` | `ClearBatchModeSessionState()` を追加（37 キー） |
| `UltraAutoRunIntegrationTests` | 検査を追加（下記） |

**fingerprint を v2 へ上げたので、v1 で作られた request / response / evidence は全て無効になる。**
これは意図どおり ── §1 の完走 2 候補は「条件が検証されていない状態」で採ったデータなので、
採用判断には使えない。**portfolio はセッションごと最初からやり直す。**

#### 引継ぎ書の field 名の誤り（実コードで確認）

§2 に列挙されていた 3 つは、その名前のフィールドが存在しない。実在するものへ読み替えた。

| 引継ぎ書の記載 | 実在するフィールド |
|---|---|
| `relicSweep` | `relicPresetSweep`（+ `relicSweepBaselineOnly`） |
| `skillComparison` | `wiringSkillCompare` |
| `tierCalibration` | 単独フィールド無し。`learnTier`（既に `ApplyTo` で false） |

`ascension` は `ascensionMode`。他（`simBoss5Sweep` / `lambdaFarmSweep` / `relicAxisSweep` /
`challengeAxisSweep` / `challengeCategorySweep` / `personaSweep`）は記載どおり存在した。

#### EditorPrefs を書き換えなかった理由

`Launch()` は EditorPrefs を runner へ読み込んだ**後**に `ApplyTo` を呼ぶ
（`useMutualAttackPipeline` の代入が `AutoRunMenu.cs:1349`、`ApplyTo` が `:1367`）。
したがって profile が最後に勝ち、`Matches` が再検査する。
EditorPrefs 側を書き換えると**Ultra 以外の後続バッチの設定まで黙って変わる**ので触っていない。

#### 追加した検査

| テスト | 何を防ぐか |
|---|---|
| `PortfolioProfile_MatchesTheProductionRunProfileFieldForField` | **今回の不一致そのもの。** worker と本ランの条件が再び分岐したら落ちる |
| `ProfileFingerprint_ChangesWhenAMechanicalRuleChanges` | v1 の穴。ルールが違うのに fingerprint が一致する状態を防ぐ |
| `CanonicalProfile_RejectsMechanicalDrift` (5 ケース) | 結果が変わるルールが検証を素通りするのを防ぐ |
| `CanonicalProfile_AppliesAndRevalidatesRuntimeRunnerState` (拡張) | runner を汚してから `ApplyTo` を呼び、**継承ではなく上書き**していることを示す |

実行結果: `EditMode / AutoTest.EditorTests.Ultra` **54 passed / 0 failed**（0.67 秒）。

---

## 3. 実装済みの動作方式

```text
専用AutoRunMenu
  -> production worker Playerをbuild
  -> Play Mode開始
  -> 共通synthetic seed 1000本を作成
  -> 4 workerを別processで並列起動
  -> 各workerがproduction AutoRunnerを1000ラン実行
  -> 親がmanifest / seed順 / digest / artifactを検証
  -> 親のtrusted selectorがMcNemar判定
  -> 採用方策を凍結
  -> Editor本体でStandard / 50pt / 理論値遺物 / 10000ラン
```

controllerはdamage、passive、shop、event、enemy、rewardを計算しない。worker内のproduction `GameManager` / `CombatManager` / `AutoRunner`が通常どおり動く。現在の試走に架空のtransition modelはない。

### 候補方策

| index | policy ID | 戦闘AI | 6F+店 | 6Fボス | Lambda |
|---:|---|---|---|---|---:|
| 0 | `super-baseline-v1` | Super | all-in | Optimal hybrid | 6 |
| 1 | `optimal-v1` | Optimal | OFF | OFF | 6 |
| 2 | `super-pure-v1` | Super | all-in | Super | 6 |
| 3 | `super-high-variance-v1` | Super | all-in | Optimal hybrid | -3 |

候補順はprotocolの一部。baselineはindex 0。challengerが補正済み片側paired exact McNemar gateを通らない限りbaselineを採用する。

### 進捗表示

- 緑色、10セル`■/□`、小数1桁%。
- 直近20ラン。
- 外側は本ラン`completed/10000`。
- 内側はportfolio`completed/4000`。
- active workers、elapsed、ETA、last progress age。
- consoleは低頻度、phase変更とterminalは即時。
- background workerはUnity GUIを触らず親が集約。

---

## 4. RNG契約

### 本ラン

- 現在の`AutoRun.MasterSeed`は`0000000000000000`。
- Ultra思考前後で本ランの`GameRng`を呼ばない・進めない。
- actionをcommitした後だけ、本ラン自身のRNGが通常どおり進む。

### portfolio worker

- 本ランseedや本ランRNG stateをコピーしない。
- synthetic masterから1000本を決定論的に導出。
- 全候補へ同じseed vector・同じordinal順を与える。
- 各process内でproduction `GameRng.SetMasterSeed` / `BeginRun`を使う。
- worker完了順は選択結果へ影響させない。

これはseedを使わない方式ではない。本ランもworkerもそれぞれseedで決定論的に進む。思考が本ランの未来乱数を読まないだけである。

### 「rollout は未来予知ではないか」への回答（2026-08-18）

**リテラルには「シミュレートした未来で勝てなかった手を避ける」をやっている。**
成否は「その未来が本ランの辿る未来か」の一点で決まる。時刻 X の状態を 3 つに割ると:

| | 中身 | 扱い |
|---|---|---|
| **S** 公開状態 | 観測できるもの | 真値を使う ✅ |
| **H** 隠れた既定事実 | 偽商人・霧の下のタイル | **veil で信念から引き直す** |
| **R** 未抽選の乱数 | この先の出目・抽選 | synthetic seed で**新規に引く** |

R を使えば未来予知、H を使えば現在の隠れ状態の窃視。
**S だけ使い H を信念で積分し R を新規に引く**のが計画で、rollout はこれ。
`(S, H~信念, R~新規)` からの標本であって、本ランが辿る `(S, H真値, R_live)` ではない。

**巻き戻しでもない。** 本ランを時刻 X へ戻して打ち直せば `GameRng` を消費して状態が変わる。
実際にやっているのは状態の**複製**で、本ランは 1 ステップも動かない ＝ 木の別の枝を歩いている。

#### 議論ではなくテストで固定した

[UltraNoFutureKnowledgeTests.cs](../Assets/Editor/Tests/UltraNoFutureKnowledgeTests.cs)。

| 性質 | テスト |
|---|---|
| live RNG を変えても判断が変わらない | `TheSameObservationDecidesTheSameWayUnderADifferentLiveRngState`（seed・run・5000 draw を変えても一致） |
| 多数の live 状態で不変 | `TheDecisionIsStableAcrossManyDifferentLiveStates`（8 通り） |
| **思考が live RNG を進めない** | `ThinkingConsumesNoLiveRandomness` |
| 観測が run を乱さない | `CapturingAnObservationConsumesNoLiveRandomness` |
| veil が live RNG を使わない | `VeilingConsumesNoLiveRandomness` |
| 計器そのものが機能している | `TheDrawCounterActuallyCounts` |
| **盤面には反応する**（入力を無視して受かる偽陽性を防ぐ） | `TheDecisionDoesDependOnTheObservation` |

そのために `GameRng.TotalDraws`（**診断専用・抽選に影響しない**）を追加した。
`_counters` はキー別なので、これが無いと「別キーを消費された」ことに気付けず、
RNG 契約は「守っているつもり」で終わる。

#### 人間に模倣可能か（技量帯の意味）

**可能。** rollout が要求する入力は全て公開情報で、ルールブックとサイコロと無限の時間があれば
人間が同じ手順（手作業 Monte Carlo）を実行できる。**超人的なのは計算量であって情報ではない。**

人間が実際に使う手順は「新規標本の代わりに記憶の分布」で、それは既に生産方策そのもの
（`Rank` = 経験的価値関数、`dangerTarget`/`safetyHits` = 「HP 70% 未満でボスへ入らない」の閾値化）。
技量帯は**同じアルゴリズムの予算違い**として並ぶ:

| 技量 | 標本 | 地平 | 終端評価 |
|---|---|---|---|
| 素朴 | 0 | 0 | 固定規則 |
| 最適 | 0 | 1手 | 発見的 |
| 天井 | 12 | 20〜45T | 発見的 |
| Ultra | 16 | **ラン終端** | **0/1 実測** |

推定器の偏り（安いが偏る）と Monte Carlo（高いが不偏）の差が**技量の伸びしろ**そのもの。

> **この梯子が成立するのは全段が同じ情報を使う場合だけ。**
> ~~現状 Super だけが未公開タイルを読んでいる~~ → **誤り。撤回（2026-08-21）。**
> 視界制限の 2 軸（〈戦場の霧〉〈暗夜〉）は廃止済みで未公開タイルは存在しない。
> **全段が同じ盤面を見ている。** 詳細は Phase B の ①。

同じ行動履歴までは同じlive seed/run indexから同じtraceになる。方策が別行動を選んだ後、呼ばれるkeyやcounterが変わって結果が分岐するのは正常で、seed破綻ではない。

---

## 5. 主なファイル

### Runtime

```text
Assets/Scripts/AutoTest/AutoRunner.cs
Assets/Scripts/AutoTest/Ultra/UltraAutoRunProfile.cs
Assets/Scripts/AutoTest/Ultra/UltraAutoRunBridge.cs
Assets/Scripts/AutoTest/Ultra/UltraPolicyPortfolio.cs
Assets/Scripts/AutoTest/Ultra/UltraProductionBatchRecord.cs
Assets/Scripts/AutoTest/Ultra/UltraProductionWorkerBootstrap.cs
Assets/Scripts/AutoTest/Ultra/UltraProductionWorkerDescriptor.cs
Assets/Scripts/AutoTest/Ultra/UltraBuildManifest.cs
Assets/Scripts/AutoTest/Ultra/UltraProgress.cs
Assets/Scripts/AutoTest/Ultra/UltraProgressHud.cs
Assets/Scripts/AutoTest/Ultra/UltraWorkerProtocol.cs
```

### Editor/build

```text
Assets/Editor/AutoRunMenu.cs
Assets/Editor/UltraProductionWorkerBuild.cs
Assets/Editor/UltraWorkerBuildManifest.cs
Assets/Editor/UltraFoundationVerify.cs
```

### Tests

```text
Assets/Editor/Tests/UltraAutoRunIntegrationTests.cs
Assets/Editor/Tests/UltraDeterminismExtendedTests.cs
Assets/Editor/Tests/UltraFoundationTests.cs
Assets/Editor/Tests/UltraProgressTests.cs
Assets/Editor/Tests/UltraWorkerBuildManifestTests.cs
Assets/Editor/Tests/UltraWorkerProtocolTests.cs
Assets/Editor/Tests/GameRngDeterminismTests.cs
```

---

## 6. 最短の再開・起動手順

1. ~~Section 2のprofile parityを修正。~~ **完了（2026-08-18・§2-9）**
2. ~~Play Mode停止を確認。~~ **確認済み**（`is_playing=false`）
3. ~~Asset refresh + script compile。~~ **完了**
4. ~~`isCompiling=false` / `isUpdating=false`まで待つ。~~ **確認済み**
5. ~~ConsoleのErrorが0件であることだけ確認。~~ **確認済み**（残存 1 件は前回セッションの
   `START REJECTED` ログで、compile error ではない。起動前に Console を clear すること）
6. **ここから。** 次のmenuを実行。

```text
Tools/AutoRun/Ultra AI/遺物アリ Standard 50pt × 10000ラン
```

期待する最初のconsole:

```text
[Ultra] production worker ready: .../UltraProductionWorker.exe
[AutoRunMenu] ... ①Standard ... ④技量Ultra
[UltraProgress] ... SEARCH ... phase=0/4000 workers=4/4
```

PowerShellでportfolioを見る例:

```powershell
Get-Process -Name UltraProductionWorker

$latest = Get-ChildItem AutoRunLogs\ultra_planning -Directory |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1

Get-ChildItem $latest.FullName -Filter progress.txt -Recurse |
  ForEach-Object {
    $_.Directory.Name
    Get-Content $_.FullName
  }
```

portfolio合格条件:

- workerが4本起動。
- 各`progress.txt`が増える。
- 4候補すべて1000/1000。
- `response.json`が4本。
- `portfolio-evidence.json`生成。
- candidate-specific invalid、seed/hash/artifact mismatchがない。

本10K開始確認:

- worker processが0へ戻る。
- HUD外側が0.1%以上へ進む。
- `AutoRunLogs/challenge_sweep_progress.txt`が更新。
- `skill=Ultra`、`score=50`、`runs=N/10000`、`records=N`。
- heartbeatと更新時刻が増える。
- summaryの実効状態がSection 2と一致。

1件以上完了すれば「起動完了」。10,000件の完走待ちは別作業でよい。

---

## 7. 最小検証方針

ユーザー指示は「検証コストを最低限にして稼働可能を優先」。起動前gateは次の3つに絞る。

1. C# compile error 0。
2. 4 production workerが実際に進み4 responseが揃う。
3. 本10Kが1件以上完了し進捗ファイルが更新される。

ただし次は正しさの前提なので省略不可。

- workerと本ランのmechanical profile一致。
- 同一seed vector / ordinal順。
- artifact hash確認。
- candidate固有invalidを拒否。
- 本ラン`GameRng`非消費。
- worker failure時にUltra結果と偽って本10Kを走らせない。

後回し可: 全EditMode suite、100k soak、大規模fuzz、holdout再評価、mid-run checkpoint。

---

## 8. 既知の非致命リスク

### learning snapshot書込み競合

4 workerは同じbuild横のlearning snapshotを読む。学習OFFでも`score_assignment.json`書込み競合warningが出る場合がある。直近workerは完走したため初回起動は止めない。

後続ではcandidateごとのdisposable learning directory、またはmechanicsを変えないread-only providerを使う。

### worker停滞watchdog

worker内部coroutineだけ止まるとprocessが残る可能性がある。`progress.txt`のlast-write ageを監視し60〜120秒無更新でprocess終了、portfolio全体fail-closedとする。crashを敗北runへ混ぜず、同候補を無限再試行しない。

### 休憩分岐の整数オーバーフロー（既存・未修正）

Phase D の hook を入れる過程で見つけた。**Ultra とは独立の既存挙動**なので触っていない。

`AutoRunner` の休憩判断:

```csharp
bool richMaterials = run.weaponMaterials >= cost * 2;   // cost = WeaponUpgradeCost(run)
```

武器が最大強化だと `cost == int.MaxValue` で、`cost * 2` は unchecked で **−2**。
つまり `richMaterials` が**常に true** になり、`hpR > 0.30f` なら
**強化不能でも `RestUpgrade()` が呼ばれ、回復の機会が失われている**。

影響は「最大強化済みで HP 30〜40% の休憩」に限られるが、
7層まで到達するランは武器 T4 到達率 79.1% なので**稀ではない**。

修正すると休憩の挙動が変わる ＝ **既存の全基準値が無効になる**ので、
バランス変更として別途採否を取ること。

### ~~candidate識別力~~ → **原因判明・解消（2026-08-18）**

直近のSuper baselineとOptimalは988/1000 digest一致、clear差0。直ちにバグとは断定しないが、方策ごとの戦闘decision countをバッチ累積で保存する。全digestが100%一致する候補はconfiguration no-op警告を出す。

> **原因は §2 の mechanical profile 不一致そのものだった。**
>
> worker は `useMutualAttackPipeline` を代入しておらず AutoRunner 既定値の `false` で走っていた。
> [AutoRunner.cs:5358](../Assets/Scripts/AutoTest/AutoRunner.cs#L5358) で配線・リロール・役の
> 3 方策の注入は丸ごと `if (CombatManager.UseMutualAttackPipeline)` の内側にあり、else 節が無い。
> つまり **`wiringSkill` が何であっても方策が一度も注入されず、4 候補が同一挙動になっていた。**
>
> §2-9 の修正後に再実走した結果（session `20260817221713-...`）:
>
> | 比較 | 修正前 | 修正後 | 方策が実際に違う範囲 |
> |---|---|---|---|
> | baseline vs optimal | 988/1000 一致 | **12/999 (1.2%)** | 戦闘AI が別 → 1 戦目から分岐 |
> | baseline vs super-pure | — | 867/999 (86.8%) | 6層ボスルーチンのみ → 6層以降 |
> | baseline vs high-variance | — | 747/999 (74.8%) | Λタイル 6 vs −3 → Λ判断以降 |
>
> **一致率が「方策が実際に違い始める地点」と対応している**ので、単に乱れたのではなく
> 正しく分岐している。full-clear の不一致も 0 件から 17〜38 件へ増えた。

#### no-op 検出を実装した（2026-08-18）

`UltraPortfolioSelector.DetectConfigurationNoOps` を追加。選択の直後に必ず通り、
結果は `[AutoRunner][Ultra]` の 1 行と、疑いがあれば `Debug.LogWarning` に出る。

**提案されていた「100% 一致で警告」では今回の事故を検出できない。** 実際の値は
**98.8%** で、100% ではなかった（数ラン は偶然分岐していた）。閾値は実測から較正した:

| 状況 | fingerprint 一致率 |
|---|---|
| 事故時（4候補が同一挙動） | **98.8%** |
| 正常・6層のみ違う (`super-pure`) | 86.8% |
| 正常・Λ判断以降違う (`high-variance`) | 74.8% |
| 正常・戦闘AI が別 (`optimal`) | 1.2% |

→ `NoOpAgreementThreshold = 0.95`。正常値の最大 86.8% より上、事故値 98.8% より下。

**停止はさせない（警告のみ）。** 稀にしか到達しない分岐だけが違う候補は正当に高一致に
なりうるため。fail-closed にすると正当な portfolio を止める。

> **実装中に見つけた罠**: `UltraPortfolioProtocol.ComputeRunDigest` は hash に `policyId` を
> 混ぜている。したがって `runDigestSha256` は**候補間で構造上必ず全件不一致**になり、
> これを使った検出は「どれだけ壊れていても健全」と報告する。検出は policy 非依存の
> `runFingerprints` を使うこと。テスト `RunDigest_IsPolicySalted_...` がこれを固定している。

---

## 9. 障害時の復旧

### compileが挟まった

1. Play Mode停止。
2. 残っている`UltraProductionWorker`だけ終了。
3. Unity refresh/compile完了待ち。
4. Console Error 0。
5. 専用menuを最初から再実行。

途中portfolioは再開しない。4候補の同一seed vectorを保つためsession単位でやり直す。

### portfolio停止

調査順:

1. 最新`AutoRunLogs/ultra_planning/<session>`。
2. candidateごとの`progress.txt`。
3. `response.json`有無。
4. candidate側`AutoRunLogs/challenge_sweep_progress.txt`。
5. Player log。
6. 親Consoleの`START REJECTED`理由。

判定:

- 全candidate同一ordinalのinvalid: 共通除外対象。
- candidate固有invalid: rejectが正しい。
- seed/ordinal/hash不一致: 緩和禁止。
- artifact missing/hash不一致: 緩和禁止。
- processだけ残りprogress停止: watchdog不足。

### 本10KがRun=NULL

HUDのlast progress ageと`challenge_sweep_progress.txt`を見る。heartbeatが増えるなら継続中。heartbeatもrecordsも止まれば停止候補。本ラン中にscript編集・compileせず、必要なら停止して10Kを最初からやり直す。

---

## 10. 完全版Ultraへの残作業

RunStartOnly後は、production gameを直接叩く原則を維持しdecision boundaryを1種類ずつ増やす。

### Phase A: 公開観測 — **中核を実装済み（2026-08-18）**

`UltraObservation`をwhitelistで作る。

含める: revealed map、現HP/hope/coins/turn/floor、inventory/装備/passive/消耗品、表示中shop/event/telegraph、challenge axis vector、relic/meta/phenomena。

含めない: 本ランseed/RNG state/次の値、unrevealed tile type、未公開deck順、将来enemy roll。

現行`DoNavigate`は`EffectiveType`を直接読むためUltra continuationへそのまま使わない。
（**2026-08-21 注**: 視界制限の2軸は廃止済みで、今は未公開タイルが無いので実害は出ていない。
この分離は視界制限が復活したときに自動的に正しくあり続けるための構造。）

#### 実装（[UltraObservation.cs](../Assets/Scripts/AutoTest/Ultra/UltraObservation.cs)）

`UltraObservation` / `UltraRunView` / `UltraMapView` / `UltraMapNodeView` と
`UltraObservationBuilder.Capture()`。ミューテータを一切呼ばず RNG も消費しないので、
**観測が対象のランを乱さない**（同じランを観測してから決定論的に継続できる前提）。

隠蔽規則は `FillMapFrom` の 1 箇所だけに置いた ── 下流は `view` しか読まないので、
規則を書き忘れる第 2 の経路が構造上できない。

| 隠すもの | 扱い |
|---|---|
| 未公開タイル | `UltraTileView.Unknown`。**欠測ではなく「まだ知らない」という 1 つの答え** |
| `MapNode.isFalseMerchant` | **型に置き場所を作らない。** 「revealed だから全フィールド写す」が危険な実例 |
| `lambdaPoolRemaining*` | 未抽選プールの残数（デッキは数えられない） |
| `chronicle` / `judgment*` | 計装・診断であってゲーム内の量ではない |

#### 分類ガード（[UltraObservationTests.cs](../Assets/Editor/Tests/UltraObservationTests.cs)）

**`RunState` に public フィールドを足すとテストが落ちる。** 落ちたら
「プレイヤーに見えるか」を判断して `PlayerVisibleFields` か `HiddenRunStateFields`
（理由つき）へ入れる。境界が**黙って広がる**ことがこの仕組みの防ぎたい唯一の事故なので、
アサートを消して直してはいけない。

導入時に **74 フィールドが未分類**として検出され、全件を分類した。判断の要点:

- `pending*` 群（鑑定の眼鏡 / 加速の粉 / 奇襲 / 賭博師 …）は**プレイヤーが消耗品を使って
  自分で仕込んだ効果**。「使ったものが待機中だと知っている」のは未来予知ではない → 可視
- `nextLootMinRarity` は「次の宝箱の最低レア」だが、これも鑑定の眼鏡で**買った保証**であって
  デッキを覗いた結果ではない → 可視
- `judgmentHeal*` / `judgmentPostDamage` は断罪の会計計装 → 隠す

3 集合に分けてある。`可視` ⊇ `実際に載せている` が安全性の本体で、
`EverythingSurfaced_IsClassifiedAsPlayerVisible` がそれを固定する
（可視だが未搭載のものは、後続 Phase が必要になった時点で載せる）。

**残り**: 表示中の shop / event / telegraph ビュー、challenge axis vector、relic/meta。
戦闘中の観測は Phase E と一緒に決める。

### Phase B: checkpoint — **枠組み実装済み・境界は 1/7（2026-08-18）**

全状態を一度にserializeせず次の順で安定境界を追加。

1. Run declaration。
2. Map navigation直前。
3. Reward choice。
4. Event choice。
5. Rest choice。
6. Shopの1 action境界。
7. Combat turn境界。

各boundaryのgate: restore直後public mechanical hash一致、legal action set一致、deterministic 1手後state一致、未登録fieldでtest fail、stale phase/epoch/actionをcommit前拒否。

#### 実装（[UltraCheckpoint.cs](../Assets/Scripts/AutoTest/Ultra/UltraCheckpoint.cs)）

`UltraDecisionPoint`（7境界）/ `UltraActionKind` / `UltraLegalAction` / `UltraCheckpoint` /
`UltraCheckpointProtocol`。

**合法手集合は「要求」ではなく「発行」する。** controller が手を捏造しても
`TryValidateCommit` が発行済み集合と id で突き合わせて弾く。id は値ベース
（controller は別プロセスなので、参照ではなく文字列で戻ってくる）。

`MapNavigationActions` は**ライブのマップではなく observation から**手を作る。
`AutoRunner.DoNavigate` は `EffectiveType` を直接読むので Ultra には使えない
（Phase A に既述）。observation から導出すれば、**プレイヤーが持たない情報で選ばれた手が
そもそも集合に入らない**。
（**2026-08-21 注**: 視界制限の2軸は廃止済みなので、現時点で両者が見る盤面は同一。）

mechanical hash は observation を**シリアライズ形で**取り込む。フィールドを手書きで
列挙すると新フィールドを静かに取りこぼす ── portfolio profile fingerprint v1 が
まさにそれで無意味になっていたので、同じ轍を踏まない形にした。
ラベルは意図的にハッシュ対象外（文言修正で既存 checkpoint が無効になると、
最初に踏んだ人が検査を緩めたくなる）。

#### gate の充足状況

#### ラン状態の capture/restore（2026-08-18・**残りの Phase の要石**）

[UltraRunSnapshot.cs](../Assets/Scripts/AutoTest/Ultra/UltraRunSnapshot.cs)。
Phase B の restore ゲート・Phase E の worker rollout・Phase G の policy iteration が
すべてこれに依存する。

**リフレクションで全フィールドを走査する。手書きの一覧にしていない。**
素直な実装 `JsonUtility.ToJson(run)` は **`HashSet` と `Dictionary` を黙って落とす**。
`RunState` でそれは ownedFlags / timedBuffs / timedDebuffs / permanentDebuffs /
seenOnceEvents / seenPassiveItemIds / shopPurchasedCounts / lambdaDebuffs を失うことを意味し、
**復元したランは正常に見えて別物になる**。テスト
`JsonUtilityAlone_WouldHaveLostThem` がこの事実を固定している（実測で空になる）。

対応する型: int / long / bool / float / string / enum /
`List<string>` / `HashSet<string>` / `List<int>` / `Dictionary<string,int>` /
`List<enum>` / `List<[Serializable]>`。
**未対応の型は例外を投げる** ── 飛ばさない。
`EveryRunStateFieldTypeIsSupported` が新しい型のフィールド追加で落ちる。

復元は**過不足の両方を拒否する**:

- 知らないフィールド名 → 別ビルドの checkpoint なので拒否（部分復元すると
  どちらのビルドにも存在しないランができる）
- 足りないフィールド → 拒否（復元先の古い値が残るため）

`HashSet` と `Dictionary` は列挙順を持たないので**整列してから**符号化する。
しないと同じ状態が別のハッシュになり、restore ゲートが意味を失う。

RNG は**入っていない**。復元したランは synthetic seed で未来を標本化する出発点であって、
元のランの再生ではない。

#### マップの capture/restore

[UltraMapSnapshot.cs](../Assets/Scripts/AutoTest/Ultra/UltraMapSnapshot.cs) と
`MapManager.RestoreState(map, currentNodeId, hungerCurrent, hungerMax)`。
reflection ではなく**小さな正式口**を game 側へ足した（§10 の方針どおり）。通常進行では呼ばれない。

**`GenerateFloor` との違いは 2 つ。** 生成せず与えられた盤面を据えること、そして
**視界を再計算しないこと** ── snapshot の `revealed` は「その時点で何が見えていたか」の
記録なので、復元後に `ApplyMetaSightLimit` を掛け直すと現在のデバフ状態で塗り替えてしまう。

`Hunger` は `MapManager` 側の状態なので、マップ snapshot が運ばないと失われる。

#### rollout 経由の情報漏れと veil（**新規に発見した問題**）

worker 実処理へ進む前に、observation 境界では塞げない穴が 1 つある。

> **rollout の結果そのものが隠れた事実を運ぶ。**
> controller は「この手を打ったらどうなるか」を尋ねて答えを読む。 worker が
> **真値の盤面**で rollout すると、偽商人へ歩いた枝は負けとして返り、
> **何も開示していないのに controller は「あの商店は偽物だ」を学習する**。
> observation は 1 フィールドも公開情報を漏らしていないのに、
> **評価チャネル経由で情報が流れる**。

[UltraSnapshotVeil.cs](../Assets/Scripts/AutoTest/Ultra/UltraSnapshotVeil.cs)。
rollout の前に、プレイヤーが知り得ない事実を worker 固有のシードで**引き直す**
（真値の再生ではなく、プレイヤーの信念からの標本にする）。

- **未訪問の商店**の偽商人フラグを `GetFalseMerchantChance()` で引き直す
  ── 生成時と同じ分布から引く（定数を二重に持たない）
- **訪問済みの商店は引き直さない** ── 踏んだ時点でプレイヤーは正体を知っている。
  引き直すと**正当に持っている知識を消す**ことになる
- 3層未満は生成器が偽商人を置かないので触らない

- **未公開タイルの種別**を `MapGenerator.RandomRowTileWeights` から引き直す。
  重みは公開アクセサ経由で**生成器から直接引く** ── veil 側に書き写すと、
  タイル重みを調整したときに静かに食い違い、
  **盤面が作られたことのない分布から標本を採る**ことになる

**構造的に決まっている行は引き直さない**（前哨 / ショップ保証行 Row8 / 休憩行 Row9 / ボス）。
プレイヤーは霧に隠れていても層構造から種別を知っているので、
引き直すと**保護ではなく知識の削除**になる。訪問済みタイルも同様。

**塞げていない漏れは数える。隠さない。**

| 未 veil | 状態 |
|---|---|
| 未踏の解決済み ? マス | **実際には到達しない** ── `Mystery` は廃止済みで生成されない（`TileType.cs`）。 enum 互換のため残っているだけ |

`UltraVeilReport.HasKnownLeak` と `UltraVeilStats` が比率を出すので、
**漏れのある盤面で採った rollout かどうかを呼び出し側が判断できる**。
これが無いと漏れが不可視のまま残る。

> **veil を入れずに rollout 評価を作ると「強い Ultra」はできるが、
> その強さは情報漏れ由来で Phase H の holdout 検証を通らない。**
> 順序として veil が先。

> **snapshot と observation は同じデータで規則が正反対。**
> `isFalseMerchant` は **snapshot には必須**（落とすと罠が本物の商店になり、worker が
> 評価対象と別のゲームを打つ）で、**observation には禁止**（プレイヤーに見えない）。
> `MapNodeFields_AreAllAccountedFor` が `MapNode` の変更で落ち、
> 「真値として要るか」「見せてよいか」を別々に判断させる。

| gate | 状態 |
|---|---|
| 未登録fieldでtest fail | ✅ `CheckpointFields_AreRegistered` |
| stale epoch を拒否 | ✅ `FailureStaleEpoch` |
| stale decision point を拒否 | ✅ `FailureStalePoint` |
| 未発行 action を拒否 | ✅ `FailureIllegalAction` |
| **改竄 checkpoint を拒否** | ✅ `FailureTampered`（元の一覧に無いが必要だった ── controller が自分の選択肢を増やす細工はハッシュで落とす） |
| legal action set 一致 | ✅ 発行集合と値で照合 |
| 同一状態で hash 一致 | ✅ 列挙順・重複・ラベルに依存しない |
| **restore 直後の hash 一致** | ✅ `RunState` と**マップ**について達成。**戦闘状態は未**（Phase E-4） |
| **1手後 state 一致** | ⏳ apply が未実装（Phase D の worker 側） |

#### 境界ごとの進捗

| # | 境界 | 合法手ビルダ | dispatch |
|---|---|---|---|
| 1 | Run declaration | ⏳（手集合が空の checkpoint は作れる） | — |
| 2 | **Map navigation** | ✅ `MapNavigationActions` | ✅ `UltraOverrideMove` |
| 3 | **Reward choice** | ✅ `ChoiceActions` | ✅ `UltraOverrideChoice` |
| 4 | Event choice | ⏳ | — |
| 5 | **Rest choice** | ✅ `ChoiceActions` | ✅ `UltraOverrideChoice` |
| 6 | Shop action | ⏳ | — |
| 7 | Combat turn | ⏳ Phase E-4 と同時 | — |

離散選択は `UltraChoiceView` / `UltraChoiceOption` を観測へ載せ、
`ChoiceActions` / `ResolveChoiceIndex` で扱う。Event と Shop も同じ型で足せる。

**取れない選択肢は「見えるが選べない」**。観測には残す（プレイヤーには灰色のボタンと
値段が見えている）が、合法手には入れない ── 出したうえで本体に弾かれると、
**controller が辞退した場合と結果が区別できなくなる**。

`ResolveChoiceIndex` は不一致で **−1** を返す。0 は正当な選択肢の添字なので、
「見つからない」が 0 に見えると黙って先頭を選んでしまう。

enum は**追記のみ**。値が hash に入るので、番号を振り直すと保存済み checkpoint が
エラーも出さずに無効化される。`DecisionPointValues_AreStable` が固定している。

### Phase C: worker protocol — **実装済み（2026-08-18）**

versioned message kindを追加: checkpoint、legal actions、apply one action、continue policy、heartbeat、cancel。IPCはlength cap、checksum、request ID、build/profile fingerprint必須。

#### 実装

既存の `UltraWorkerProtocol` を**拡張**した（並行スキームを作らない）。
「checkpoint / apply one action / continue policy」は既存の `ultra.episode.request` が
既に担っている（checkpoint 復元 → 1手適用 → 方策で継続 → 結果報告）ので、追加したのは
不足していた 3 kind と checksum。

| kind | 用途 |
|---|---|
| `ultra.legal-actions.request/response` | checkpoint を渡し、worker 側の合法手集合を返させる |
| `ultra.heartbeat.request/response` | 生存確認（§8 の watchdog の土台） |
| `ultra.cancel.request/response` | 中止 |

**別 kind にした理由**: heartbeat と cancel は**エピソード実行中に答えられなければならない**。
episode メッセージのフラグにすると「worker は待機中」を前提にした経路を共有してしまう。

#### checksum（`payloadChecksum`）

length cap と property whitelist は「大きすぎる」「形が違う」を捕まえるが、
**値の途中で切れた pipe 書き込み**や**kill された worker の書きかけファイル**は捕まえない ──
それは「小さくて・整形式で・完全にもっともらしい」メッセージとして届く。
**完全か否かを区別できるのは checksum だけ。**

> **踏んだ罠**: `JsonUtility` は入れ子の serializable フィールドの null を往復できず、
> **プロパティが無いメッセージは既定インスタンスとして復元される**。
> 「不在」と「全既定」を別ハッシュにすると、**checksum が守るはずの境界を越えた瞬間に
> 全メッセージが自分の checksum で落ちる**。`IsEpisodePayloadEmpty` /
> `IsControlPayloadEmpty` で正規化してある。同じ理由で `!= null` による
> 「ペイロードを持っているか」判定も使えない。

応答 checksum は `uptimeMs` / `lastProgressUnixMs` を**除外**する ──
composing と送信の間に動くので、含めると正しい heartbeat が自分の検査で落ちる。

#### legal action set の突き合わせ

`LegalActionSetsAgree(parent, worker, out difference)`。順序と重複は無視し、
**不一致は左右どちらにしか無いかを名指しで返す**。
worker の集合は*証拠であって許可ではない* ── 親は既に自分の集合を発行済みで、
食い違いは「両者が別のルールで動いている」ことの検出に使う
（2026-08-17 の portfolio を無意味にした障害クラスそのもの）。

**未実装**: worker 側でこれらの kind に応答する実処理（`UltraProductionWorkerBootstrap`
は現在 portfolio ジョブ専用）。Phase D の dispatch と同時に入れる。

### Phase D: AutoRunner dispatch — **Map navigation のみ実装（2026-08-18）**

private `Step/Do*`をreflectionで操作しない。小さな正式hookを追加。

#### 実装

`AutoRunner.ultraSink`（`IUltraDecisionSink`・**既定 null**）と
[UltraDecisionSink.cs](../Assets/Scripts/AutoTest/Ultra/UltraDecisionSink.cs) の
`UltraDispatch` / `UltraDispatchStats`。reflection は使っていない。

`DoNavigate` の変更は **1 行**:

```csharp
best = UltraOverrideMove(gm, mm, best) ?? best;   // sink が null なら即 null
gm.MoveToNode(best.id);
```

**sink 未接続時は挙動がビット単位で不変**（既存の Optimal / Super の全基準値が生き続ける
ことがこの hook の前提）。`UltraOverrideMove` は最初の条件で return し、
RNG も状態も触らない。`AutoRunner_HasNoControllerAttachedByDefault` が既定 null を固定する。

controller は**投げても・黙っても・嘘をついても run を壊せない**:
例外は decline に畳み、回答は発行済み checkpoint で再検証し、
さらに主スレッドで「その id が今の盤面で実際に踏める接続か」を引き直す。
どれかで落ちたら生産方策の選択がそのまま通る。

#### 調査で判明した 2 点（重要）

**① ~~生産方策は未公開タイルの正体を読んでいる~~ → 誤り。撤回（2026-08-21 検証）。**

当初こう書いた:

> `DoNavigate` の `Rank` ループは `n.EffectiveType` を pool とその**後続**の両方に対して
> 呼ぶので、〈戦場の霧〉〈暗夜〉で未公開の後続ノードの正体まで見ている。したがって
> 「Ultra が Super に勝てない」＝「Ultra が弱い」ではない。

**実コードを辿ったら、そもそも未公開タイルが存在しなかった。**

- 視界制限を持っていたのは 〈戦場の霧〉(軸13) と 〈暗夜〉(T4-C) の 2 軸だけ
- どちらも**廃止済み**。`MetaDebuffApplicator.GetMapSightLimit()` は
  「廃止した軸 — 呼び出し元互換のため中立値を返す」節で **`=> -1` (制限なし)** にハードコード
- よって `MapManager.ApplyMetaSightLimit()` は常に即 return し、
  `MapNode.revealed` は既定の **true のまま**（「難易度0では全可視」）
- Ultra 側の唯一の遮蔽点 `FillMapFrom` は `node.revealed ? Classify(EffectiveType) : Unknown`
  なので、**全ノードの種別がそのまま観測に載る**。`Classify` は TileType との 1:1 写像で無損失
- `EffectiveType => resolvedType ?? type` なので、未解決 Mystery は**両者とも** `Mystery` としか見えない

→ **情報は対称。Phase H の比較は公平で、Ultra は正面から負けた。**

**なぜ間違えたか**: `AutoRunner.UltraOverrideMove` の XML コメントが軸の廃止後も更新されず、
廃止済みの機構を現役として説明していた。それを実コードで確認せずに引用した。
**正本は実コード。コメントを根拠に測定結果を説明しない。**

観測の白名簿そのものは残す価値がある ── 視界制限が復活したときに自動的に正しくあり続ける。

**② 横移動の希望コストは方策側では払われない。**
`GameManager.MoveToNode` が `hopeFromNode.row == CurrentNode.row` から判定して
`HopeSystem.ApplyMove` で徴収する。**誰が選んでも同じように払われる**ので、
Ultra の手集合を生産 pool（希望残量で横移動を足すか決める発見的な絞り込み）ではなく
**合法手全体**にしても取りこぼしは起きない。pool を継承すると Ultra の選択肢が
Super の選択肢に頭打ちされるので、そちらの方が有害だった。

#### 計装

サマリに `Ultra委譲 :` 行を追加。**提示 / 上書き / 同意 / 辞退 / 却下 / 例外**を数える。

> 「一度も答えなかった controller」と「毎回 baseline に同意した controller」は
> **run の結果が完全に同一**になる。カウンタが無ければ両者を区別できず、
> 2026-08-17 の「4 候補が実は同一」と同じ取り違えが起きる。

#### 疎通確認（2026-08-18 実施・**実際に走らせた**）

hook を入れただけでは「呼ばれているが選択がゲームへ届いていない」と
「controller が毎回 baseline に同意した」が**結果で区別できない**。
そこで意図的に弱く偏った sink を 2 つ用意し、同一条件で走らせた。

メニュー: `Tools/AutoRun/Ultra AI/診断: dispatch 疎通 (先頭手/末尾手・30ラン)`
（0pt・遺物なし・Super 戦闘・30ラン。sink キーは 1 回で消費され、既定では絶対に差さらない）

| sink | 提示 | 上書き | 同意 | 平均R帯 | 6F到達 | 却下 / 例外 / Deadlock |
|---|---:|---:|---:|---:|---:|---|
| 先頭の合法手 | 889 | 766 (86.2%) | 123 | **1.0** | 0.0% | 0 / 0 / 0 |
| 末尾の合法手 | 2032 | 786 (38.7%) | 1246 | **6.7** | 73.3% | 0 / 0 / 0 |

- **hook は呼ばれている**（提示 > 0）
- **選択はゲームへ届いている**（2 つの sink で結果が R1.0 対 R6.7 と別物）
- **検証経路が過剰に弾いていない**（却下 0・例外 0）
- **配線が壊れていない**（Deadlock 0・Crash 0。全 30 ランが通常の死）

> 先頭手 sink が 1 層で全滅するのは正常。 横移動を選び続けて希望が尽きる。
> **弱い sink を使うのが要点** ── baseline と同じ強さの sink だと
> 「動いた」と「一度も動かなかった」が同じ結果になり、何も確認できない。

#### 残り

境界は 7 つ中 4 つ（Map / Reward / Rest / Event）。Shop と Combat turn が残り。
Shop は購入ループを「1 アクションずつ」へ組み替える必要があり、
経済経路（記憶: 「BOTの購入は経路①が本体」）に触るので単独で扱う。

**worker 経由の非同期 dispatch は未接続** ── 現状の `IUltraDecisionSink` は同期呼び出しで、
§10 が求める「controller へ非同期 dispatch・main thread は frame を返す」形は、
Phase C の control message を worker 側で実装してから繋ぐ。

1. 実効Super actionをside-effectなしで確保。
2. public observation capture。
3. controllerへ非同期dispatch。
4. main threadはframeを返しHUD更新。
5. timeout/invalid/OODならSuper。
6. main threadで合法性再検査。
7. 1手だけcommitして再snapshot。

### Phase E: 戦闘Ultra — **E-1（cap/cancel/fallback）実装済み（2026-08-18）**

特殊な`ApplyTurn`再実装を正本にせずproduction CombatManagerをworkerで動かす。

優先順: 現ターン合法配線、reroll mask、role fire/defer、ghost、production combat terminal rollout、Superとのsuccessive halving、7層multi-phaseを含むrun continuation。

固定work/memory cap、cancel、fallbackを先に実装。GPUよりprocess-level CPU並列を優先。

#### E-1 実装（[UltraCombatBudget.cs](../Assets/Scripts/AutoTest/Ultra/UltraCombatBudget.cs)）

指示どおり**探索より先に**境界を作った。

**予算の単位は「評価回数」。** 過去 2 度の Editor フリーズは、予算の単位が実際の仕事量と
噛み合っていなかったこと（展開した状態数を数えていたが、コストは葉にあった）が原因。
単位を評価そのものにすれば `上限 × 1回あたりのコスト` が本物の時間上界になる。
実測 1.7µs/評価（[design-exact-ai.md](design-exact-ai.md) §12-1）から
`Batch()` は 20万評価 ≒ 単コア 0.34 秒。

**メモリ上限は「バイト」ではなく「エントリ数」。** バイト上限は hot path で
`GC.GetTotalMemory` を呼ぶ必要があり、高価かつ不正確 ── **守れない上限は上限ではない**。
実際に伸びる容器（メモ表・フロンティア）を縛る方が無コストで強制できる。

**壁時計上限も併設**（既定 2 秒）。コストモデルが再び外れた場合の最後の砦
── 実際に 3 回外している。512 回に 1 回だけ時計を見る（毎回見ると評価より高くつく）。

`Cancel()` は別スレッドから安全。`TryConsumeEvaluation` は**仕事の前**に課金する
（後払いにすると最後の 1 評価が無制限に走り、1 評価が高価な場合に固まる）。

`UltraCombatBudgetStats` が**完了率**を記録する。
**上限に当たり続ける探索は、探索ではなくフォールバック発見的手法が探索の衣を着ているだけ**で、
比率を残さないとそれが見えない。

#### E-2/E-3 実装（[UltraCombatView.cs](../Assets/Scripts/AutoTest/Ultra/UltraCombatView.cs)）

観測と行動符号化。

**telegraph は丸ごと写して安全。** ADR-0009 柱3 により
`MutualTurnTelegraph` は完全情報の開示で、敵の今ターンの出目もヴェスカの抽選結果も
配線前に画面に出ている ── 既に起きたことなので未来予知ではない。
含めないのは先のターンの敵ロール・RNG・非開示のパッシブ内部値・各デッキの中身。

行動符号化:

| 決定 | 符号 | 備考 |
|---|---|---|
| 配線 | ダイスごとの端子桁 `"01203"` | 合法集合は `MaxLegalActionCount` に収まる |
| リロール | ダイス添字のビットマスク | **0 =「振り直さない」を明示的な選択肢として出す** |
| 役 | RoleKind のビットマスク | **0 =「全て温存」。役は 1 戦闘 1 回なので温存は正当な手** |

役マスクが**提示されていない役**を指したら decode を失敗させる。
黙ってビットを落とすと、両者が盤面について食い違っている事実が隠れる。

#### E-4 の設計（2026-08-21 確定・実装着手）

**方針転換の経緯**: Phase H の初回測定で Ultra はマップ航行では Super に勝てなかった。
マップ層に天井があるかは安く切り分けられず（下手な方策との差は「上に伸びしろがあるか」を
答えない）、一方 **ADR-0010 はこのプロジェクトの技量を戦闘に置いている**
（「技術介入が実質オーバーロードのみで天井が薄い」→ ダイス分割の最適化問題へ移す）。
天井の計器を戦闘へ配備し直す。Ultra の資産（worker/resume/veil/checkpoint/非同期決定/
ペア比較）はそのまま使え、変えるのは委譲する決定点。

##### 採用: (A) 戦闘状態を直接 snapshot

**棄却したのは (B)「戦闘開始から手を再生する」。** 過去のダイス目を再現するには
RNG ストリームの復元が要り、そうすると**「未来の乱数は rollout ごとに引き直す」という
veil の前提が壊れる**。出目も記録して強制注入する道はあるが、RNG 経路に手を入れるのは
静かに壊れる典型。(A) なら `UltraRunSnapshot` と同じ規律
（未対応の型は**例外で落ちる** / 復元時は未知フィールドも**欠落フィールドも拒否**）が効く。

##### 対象の実体（調査済み）

| | 中身 |
|---|---|
| `CombatContext` | public フィールド 225。perTurn/decay/persistent の 3 区分あり |
| `CombatManager` | インスタンス状態は約 25 個のスカラ + `currentEnemy`(EnemyData) + `turnLog`(ログ専用) + `ledManager`(Unity参照) |

##### 実装順

1. **`UltraFieldSnapshot` へ切り出す。** 現行 `UltraRunSnapshot` は `Fields()` が
   `typeof(GameLoop.RunState)` にハードコードされている。型と対象を引数に取る形へ一般化し、
   `UltraRunSnapshot` はその薄いラッパにする（既存のテストと呼び出し元は不変）。
2. **`UltraCombatSnapshot`** ── `CombatContext`(public instance) を全フィールド捕捉。
3. **`CombatManager` は明示的なフィールド白名簿。** 全フィールド走査だと `ledManager` や
   event で例外になる。ただし**「対象外」も明示的に列挙**し、
   *どちらにも載っていないフィールドが現れたらテストが落ちる*ようにする ──
   黙って落とすと復元後に別の戦闘になる。`currentEnemy` は id で復元。
4. **境界 7 (CombatTurn) の dispatch。** 差し込み口は
   [AutoRunner.cs:6342-6368](../Assets/Scripts/AutoTest/AutoRunner.cs#L6342) の
   `cm.WiringPolicy` / `RerollPolicy` / `RolePolicy`。Ultra はこれらをラップし、
   辞退したら production（`superAI.ChooseWiringPlan` 等）へ落とす。
   行動符号化は E-2/E-3 で実装済み（配線=端子桁文字列 / リロール=ビットマスク /
   役=RoleKind ビットマスク、いずれも「何もしない」を明示的な手として出す）。
5. **worker 側 resume** ── `UltraResumePayload` に戦闘セクションを足す。
6. 通ったら Phase H を戦闘で測り直す（`Tools/paircmp_runs.py` でペア比較）。

##### veil の面は小さい

ADR-0009 柱3 により `MutualTurnTelegraph` は完全情報の開示で、敵の今ターンの出目も
配線前に画面に出ている。戦闘中の「隠されているが確定済み」の状態はほとんど無いので、
マップ側のような大掛かりな引き直しは要らない。**ただし確認してから断定すること。**

#### 残り（E-4 以降・未着手）

- **production CombatManager を worker で動かす**戦闘継続。戦闘状態の
  snapshot/restore が要るので Phase B の境界 7（Combat turn）と同時になる
- Super との successive halving
- 7層 multi-phase を含む run continuation
- CPU 並列（GPU より優先、と §10 が明記）

> **関連**: 中断中の `ExactCombatAI` は「独自 `ApplyTurn` を正本にしない」という点で
> ここと同じ結論に達している（[design-exact-ai.md](design-exact-ai.md) §11）。
> あちらは Super のモデルを共有する方向、こちらは production を worker で動かす方向で、
> **後者の方が忠実**。E-4 が動けば ExactCombatAI 側は不要になる可能性がある。

### Phase F: マクロ判断

同じ目的`P(7層完全クリア)`でroute、consumable、reward、shop、event、rest、Lambda、inventory/昇華を接続。中間到達点を単純加重和にせず、P7が実質同等な時だけ6F到達、5Fクリア、残HP/資源を辞書式tie-break。

### Phase G: policy iteration — **決定ロジック実装済み（2026-08-18）**

[UltraRolloutEvaluator.cs](../Assets/Scripts/AutoTest/Ultra/UltraRolloutEvaluator.cs) /
[UltraResumePayload.cs](../Assets/Scripts/AutoTest/Ultra/UltraResumePayload.cs)。

各合法手について「その手を打ってから**生産方策で最後まで打たせ**、7層完全クリアを数える」。
学習した遷移モデルは一切関与しない ── 結果を生むのは worker 内の実ゲームだけ。

**順位付けは Wilson 下限で行う。** 生の勝率だと 1/1 の手が 40/100 の手に勝ってしまう。

**veil 種は rollout 添字だけで決まる** ＝ 全候補が**同じ標本盤面の集合**で測られる（共通乱数）。
候補ごとに別の盤面を引くと、手ではなく盤面を測ることになる。

**答えないことが正規の結果。** 使える rollout が閾値に届かない、あるいは**一部の候補だけ
測れていない**場合は unusable を返し、AutoRunner は生産方策の選択をそのまま通す。
必ず何かを返す評価器は、worker の故障を自信のある出鱈目に変える。

`UltraResumePayload.Create` は **veil を飛ばす経路を持たない**。
veil 無しの resume payload は「数字が良く見える正しさのバグ」で、レビューを生き延びる種類のもの。

#### resume 入口（2026-08-18）

`AutoRunner.ultraResumeFrom` / `ultraResumeForcedActionId`（**既定 null**）と
`GameManager.ResumeAtPhase(phase)`。

**`StartNewRun()` が正規に走った後**に checkpoint を流し込む ──
パッシブレジストリのリセットや戦闘乱数の通し番号リセットといった
「新規ランで必ず起きること」を飛ばさないため。上書きするのは `Run` の中身とマップだけで、
シングルトンの初期化経路には触らない。`Run` は差し替えず**中身を書き換える**
（他所が参照を掴んでいても矛盾しない）。

`ResumeAtPhase` は `SetPhase` をそのまま通す。ボスなし層のクリア判定など
フェーズ遷移に紐づく規則が**通常と同じように効く** ── ここだけ別経路にすると、
worker の中でだけ規則が違うという最悪の形になる。

**復元に失敗したランは `Outcome.Crash` で明示的に落とす。**
「普通のラン」として集計へ混ぜると worker の故障が敗北として統計に入る（§12 の禁止事項）。

`UltraForcedFirstActionSink` は**1 手だけ**強制して以降は退く。
推定量が「A を打った後、**生産方策が**打つ」なので、2 手目以降まで固定すると別の量になる。
提示されていない決定点では発火せず待機する ── 別の場所で撃つと黙って別物を測ることになる。

#### worker 側 handler（2026-08-18）

[UltraEpisodeWorkerBootstrap.cs](../Assets/Scripts/AutoTest/Ultra/UltraEpisodeWorkerBootstrap.cs)。
起動引数 `--ultra-episode-job <path>`。

checkpoint から resume → 指定の 1 手を強制 → **生産方策で最後まで打つ** → 0/1 を返す。
遷移モデルも代理も無く、production の `GameManager` / `CombatManager` / `AutoRunner` が
通常ランと同じことをする。

**crash / deadlock を敗北として報告しない。** `primaryReward=0` で返すと
「worker が壊れやすい手」を評価器が避けるように学習してしまう。
usable でない結果として返し、評価器が破棄する（§12「worker failureを敗北runとして
統計へ混ぜない」）。

[UltraWorkerRunnerSetup.cs](../Assets/Scripts/AutoTest/Ultra/UltraWorkerRunnerSetup.cs) を新設し、
**portfolio worker と episode worker が同じ profile 適用コードを共有**する。
2026-08-17 の障害は「同じことを 2 箇所で設定して食い違った」ものなので、
episode 用に 2 つ目のコピーを作れば同じ事故を一段下で再現することになる。
portfolio worker 側も共有版を呼ぶよう置き換えた（挙動は同一）。

#### 親側 oracle（2026-08-18）── **縦一本が繋がった**

[UltraProcessEpisodeOracle.cs](../Assets/Editor/UltraProcessEpisodeOracle.cs)。
episode worker を起動して `UltraEpisodeResult` を読み戻す。

**バッチ実行にした。** Unity Player の起動コストは短いラン 1 本と同程度で、
5 手 × 16 rollout を直列に投げると大半が起動待ちになる。そのため
`IUltraProductionOracleBatch` を追加し、評価器は **(手 × rollout) の全組を先に作ってから
一度に渡す**。実装側が並列起動でコストを重ねられる。

**あらゆる失敗が同じ形（null）で返る。** timeout / crash / 応答なし / 壊れた応答 ──
全て「この rollout は答えを出さなかった」であって「この手が負けた」ではない。
故障を敗北として返すと、**評価器は「worker が壊れやすい手」を避けるように学習する**。

### 縦一本の到達状況

| 部品 | 状態 |
|---|---|
| checkpoint 作成・veil・base64 | ✅ |
| worker の message kind | ✅ |
| worker プロセス起動機構 | ✅ |
| AutoRunner の resume 入口 | ✅ |
| worker 側 handler | ✅ |
| **親側 oracle（起動・回収・バッチ）** | ✅ |
| 評価・順位付け・棄権判断 | ✅ |
| 未来予知でないことの検証 | ✅ |

### 初回実走（2026-08-18）── **経路は繋がった。合成 checkpoint が非現実的**

メニュー `Tools/AutoRun/Ultra AI/診断: rollout 1 決定のコスト実測`。

**繋がったこと**（worker.log と result.json で確認）:

```
親: checkpoint 作成 → veil → base64 → job.json → プロセス起動
worker: checksum 検証 → RunState/マップ復元 → AutoRunner が MapNavigation から再開
        → 移動 → 戦闘突入 → バッチ完了 → result.json
親: result.json を読み戻し → 評価器が集計
```

途中で 2 件の実バグを踏み、どちらも**計装が検出した**:

| 症状 | 原因 |
|---|---|
| `checksum_mismatch` | `expectedFingerprints=null` で封をした request が、JSON 往復で既定インスタンスになり「不在」と「全空」が別ハッシュになっていた。**JsonUtility の null 復元、4 度目**。`IsFingerprintSetEmpty` で正規化 |
| `no run record` | `ultraProductionScenarioSeedVectorHash` を空にしていて `START REJECTED`。空欄が**ずっと後の「run record が無い」としてしか表面化しない**ので、単一 seed でもハッシュを入れる |

**残っている問題**: 合成した checkpoint が実際には遊べない。
`RunState` を手組み（5 フィールドだけ設定）したため装備・ダイス・希望などが既定のままで、
`ストール phase=Combat node=r1l0 floor=1` で deadlock する。

> **パイプラインの不具合ではなく、テスト冶具の不備。**
> `RunState.Initialize` と `MapGenerator.Generate` はメタ進行のシングルトンに触るため
> play mode 外では呼べず、手組みで代用したのが原因。

**したがって現時点の所要時間は信用できない**（deadlock で終わったランの時間なので）。
参考値: 直列 0.90 秒/本、12本並列 0.36 秒/本（起動コストは並列で 2.5 倍償却できている）。

#### 実 checkpoint の採取（2026-08-18）── 採れた。**が resume 後に戦闘が始まらない**

メニュー `Tools/AutoRun/Ultra AI/診断: 実ランから checkpoint を採取 (1ラン)`。
`AutoRunner.ultraCaptureCheckpointPath` を差すと、航行 N 回目の判断直前で 1 回だけ書き出す。

採れた checkpoint は実物:

```
1層 / 29 ノード / 現在地 r5_l1 / HP 93/93 / 所持金 5 / hope 97 / axe_t1+1
lambdaPoolRemainingAll = -1, chronicle = 空   ← RunState の veil が効いている
```

**残っている不具合**: これを resume すると、ランは進むが
`ストール phase=Combat node=boss floor=1` / **`combats 0/0`** で deadlock する。

- 合成盤面のとき（前回）は 1 個目の戦闘マスで固着 → 今回はボスまで到達しているので**前進した**
- `combats 0/0` ＝ **戦闘が 1 度も成立していない**。フェーズは Combat に入るのに
  `CombatManager` が動いていない

> つまり **resume が何かを壊しており、それが戦闘開始を妨げている。**
> portfolio worker は同じビルドで 1000 ラン 完走しているので、
> 差分は `TryApplyUltraResume`（`Run` の中身上書き + `MapManager.RestoreState`
> + `ResumeAtPhase`）に限られる。

**切り分け済み**（2026-08-18 実測）:

| 確認したこと | 結果 |
|---|---|
| worker のビルドが古くないか | ✅ 最新（`Assembly-CSharp.dll` が実行直前のタイムスタンプ） |
| checkpoint が worker に届いているか | ✅ `resume=True 現在地=r5_l1 ノード=29 強制手='2:r6_l0:-1' 技量=Super 相互攻撃=True` |
| checkpoint 自体が壊れていないか | ✅ 29ノード・接続あり・現在地は実在・veil 済み |
| 戦闘 0 回は異常か | ⚠️ **異常とは限らない**。r6/r7 が非戦闘マスなら 26% で起こる |
| ボス戦が始まらない | ❌ `phase=Combat node=boss` で `IsCombatActive=false` |

`StartBossTile()` は最後に `SetPhase(Combat)` → `CombatManager.StartCombat(...)` を呼ぶ。
`StartCombat` は敵が null のときだけ早期 return して `LogError` を出すが、ログには出ていない。

#### 追い込み（2026-08-18・**worker 限定の不具合まで特定**）

ラン中の `Debug.Log` は `filterLogType = LogType.Error` で落ちる（`AutoRunner.cs:1679`）。
**Error だけが worker.log に届く**ので、恒久的な診断を Error で 1 本入れた:

```
[AutoRunner] 戦闘フェーズだが戦闘が非アクティブ:
  cm=有 enemy=boss_layer1 node=boss floor=1 gmEnemy=boss_layer1
```

`AutoRunner.cs` の `case GamePhase.Combat` に追加（20 連続で 1 回だけ吠える）。
**これが無いと「ストール phase=Combat」としか残らず理由が追えない。**

あわせて `Ultra resume:` 行をサマリへ追加した（ラン中の Log は落ちるので、
これが resume の実効状態を残す唯一の場所）。

##### Editor と worker の差分を潰した記録

| 試したこと | 結果 |
|---|---|
| Editor で同じ checkpoint を resume | ✅ **完走**（deadlock 無し） |
| Editor で**強制手つき**で resume | ✅ **完走**（`提示 66 / 同意 1 / 辞退 65`） |
| worker の profile を採取条件（0pt・遺物なし）へ揃える | ❌ 変わらず deadlock |

resume の実効状態は Editor と worker で**完全に一致**している:

```
1層 / 現在地 r5_l1 (要求 r5_l1) / HP 93/93 / 武器 axe_t1+1 / パーツ 0
強制手 '2:r6_l0:-1' / 合法手 3
```

##### 原因特定と修正（2026-08-18）── **`OnCombatEnd` の購読者が居なかった**

計装を 3 段に足して 3 回走らせ、以下の順で絞り込んだ。

| 計装 | 出力 | 分かったこと |
|---|---|---|
| `startEntered` / `startCompleted` | `0 / 1` | **開始処理は完了している**。途中で抜けてはいない |
| `pHP` / `eHP` / `eMax` | `93 / 0 / 674` | 敵HPが0 ＝ **戦闘は実際に行われ、勝っている** |
| `OnCombatEnd == null` 検査 | **購読者ゼロ** | 終了通知が誰にも届いていない |

```
[CombatManager] 戦闘終了イベントの購読者が居ない ── フェーズが進まず停止する
[CombatManager] 戦闘終了を通知したのにフェーズが Combat のまま (敵=boss_layer1 勝敗=勝 13T)
```

**13 ターン戦って勝っているのに、誰も聞いていないのでフェーズが Combat のまま残り、
`RunCombatWithItems` が即 return してループが空回りしていた。**

**修正**: `GameManager.StartNewRun()` で購読を張り直す（`-=` してから `+=` なので二重購読しない）。
購読は `Start()` で 1 回だけだったが、それだと
「GameManager の Start より後に CombatManager の実体が入れ替わる」順序で購読が宙に浮く。
**ラン開始ごとに張り直せば起動順に依存しなくなる。**

**恒久的に残した検査**（同じ事故を二度と黙って通さない）:

- `CombatManager`: `OnCombatEnd` に購読者が居なければ Error
- `CombatManager`: 通知後もフェーズが Combat のままなら Error
- `AutoRunner`: 戦闘フェーズなのに非アクティブが 20 連続で Error（敵・HP・開始通番つき）
- `AutoRunner`: サマリに `Ultra resume:` 行

> ラン中は `filterLogType = LogType.Error` なので、**この種の診断は Error でないと消える**。

### **Ultra が初めて手を評価した（2026-08-18）**

購読修正の後、同じコスト実測メニューが通った:

```
① rollout 3 本 (各手1本): 3.8 秒 / 1本 1.26 秒 / 使えた 3 失敗 0
② rollout 12 本 (各手4本): 7.8 秒 / 1本 0.65 秒 / 使えた 12 失敗 0
判定: best=2:r6_l2:-1  clear 2/4 (下限 0.150)  over 3 actions
```

**3 手それぞれを production の実ゲームで 4 回ずつ最後まで打たせ、
7層完全クリア率で順位を付けて `r6_l2` を選んだ。** 失敗 0。

これで §10 の縦一本が実際に機能したことになる:
観測 → 合法手 → veil → checkpoint → worker 起動 → resume → 1手強制 → 生産方策で完走
→ 0/1 回収 → Wilson 下限で順位付け → 採用。

#### 実測コスト（**これは信用してよい数字**）

| | 値 |
|---|---|
| 1 rollout（直列） | 1.26 秒 |
| 1 rollout（12 並列） | 0.65 秒 |
| 並列の効き | 比 0.52（起動コストが約 2 倍償却） |
| 1 決定（3手 × 4本） | 7.8 秒 |
| 外挿: 1 ラン（マクロ決定 30 回） | **約 4 分** |
| 外挿: 1000 ラン | **約 65 時間** |

**速度が本番の壁であることが実データで確定した。** 10,000 ラン は現状不可能。

### **時間の行き先を実測した ── 起動コストではなく学習データ再読込だった（2026-08-18）**

上の表の「1本 0.65 秒」は**スループットであって 1 本の所要時間ではない**。
`MaxParallelEpisodes = ProcessorCount - 1 = 23`（Ryzen 9 7900 / 12C24T）なので
12 本も 1 波で同時起動しており、実際は **1 rollout ≒ 7 秒**、3→12 で同時数を 4 倍にして
スループットは 2 倍 ＝ **並列効率は既に約 50%**。並列度を上げる余地はほぼ無い。

#### 計装

`UltraPhaseClock`（[Assets/Scripts/AutoTest/Ultra/UltraPhaseClock.cs](../Assets/Scripts/AutoTest/Ultra/UltraPhaseClock.cs)）を追加。
既定 OFF、worker bootstrap だけが有効化する。`worker.log` に **Error で** 1 ブロック出す
（バッチ中は `filterLogType = LogType.Error` なので Log/Warning は届かない）。

計装前の実測（`ultra_cost_141947` / ep_000000、3 並列 ＝ 非競合）:

| 区間 | ms | 比率 |
|---|---|---|
| エンジン起動 | 16 | **1.2%** |
| worker準備 | 19 | 1.5% |
| └ 学習Reload（×4） | **2174** | **77%** |
| └ └ 回帰OLS | 1971 | 70% |
| └ └ └ csv読込 | 862 | 31% |
| └ └ item_stats読込 | 171 | 6% |
| 合計 | 2809 | |

**「プロセス起動が支配的」という見立ては外れていた。エンジン起動は 16ms、実質ゼロ。**
支配していたのは `LearnedPriorityProvider.Reload` ── 1 プロセスにつき **4 回**走り、
その中身が N=14365 × K=198 の Ridge 回帰と 12MB の csv パースだった。

見落とされていた理由:

> **通常バッチはこの固定費を 1000 ラン に償却するが、rollout は 1 ラン にしか償却できない。**
> 同じコードが Ultra では相対的に約 1000 倍重い。だから 1000ラン バッチでは誰も気付かなかった。

#### 修正（2 段）

1. **プロセス内キャッシュ** ── `ItemRegression.Recompute` は入力 csv の指紋
   （絶対パス + 長さ + 最終更新時刻）が前回と同じなら再計算しない。
   OLS は同じ行集合に対して決定的なので、係数は同一 ＝ **BOT の判断は 1 ビットも変わらない**。
2. **ディスクキャッシュ** ── 係数表を csv と同じディレクトリに `regression_fit.txt` として保存。
   rollout は毎回まっさらなプロセスなので、1 回目の OLS が全 rollout に課金され続けるため。

ディスクキャッシュで守った点:

- **自分の入力ファイルの指紋でしか成立させない。** 係数を job 経由で外から注入する案は却下した ──
  worker が自分で計算したはずの値と食い違いうる。2026-08-17 の portfolio 障害
  （両側が別設定で走っていた）と同じ形の事故になる。返るのは
  「この worker が自分で計算したのと同じ値」だけ。
- **件数行を必ず突き合わせる。** 差し替えは原子的でないので途中まで書かれたファイルを読みうるが、
  それは「係数が少し足りない、一見正しい表」として通ってしまい、BOT の判断だけが静かに変わる。
  一番たちの悪い壊れ方なので、数が合わなければキャッシュ不成立にする。
- `JsonUtility` は使わない（本リポジトリで 4 回事故、かつ double 配列の扱いが版依存）。
  `double` は `"R"` 書式で往復。
- テスト: `ItemRegressionFitCacheTests`（5 本）── 往復の完全一致 / 入力変更で無効化 /
  **切り詰めを信用しない** / 壊れていたら計算し直す / プロセス内キャッシュが効く。

#### 結果

| 版 | 1 決定 (3手×4本) | 外挿 1000ラン | 判定 |
|---|---|---|---|
| 修正前 | 6.5 秒 | 54 時間 | `best=2:r6_l2:-1` 2/4 下限 0.150 |
| プロセス内キャッシュ | 4.3 秒 | 36 時間 | 同じ |
| ＋ディスクキャッシュ | **3.5 秒** | **29 時間** | 同じ |

**判定は 3 版とも完全に一致**（同じ手・同じクリア数・同じ下限）。
高速化が挙動を変えていないことの確認になっている。

1 rollout の内訳も反転した（ep_000012、12 並列）:
`実ラン 2850ms (95.6%) / 学習Reload 261ms (8.8%) / 回帰OLS 5ms`。
**残りはほぼ本物のゲーム進行**で、律速は Super の CPU 競合に移った。

### **マクロ決定の国勢調査 ── 「30回/ラン」は 74.8回/ラン だった（2026-08-18）**

コスト外挿がずっと使ってきた「マクロ決定 30 回/ラン」は実測ではなく、
`UltraEpisodeCostMenu` に直書きされた定数だった。数えた（0pt・遺物なし・Super・30ラン）:

```
[Ultra決定 国勢調査] ラン 30 / マクロ決定 2245 (1ラン平均 74.8)
  うち 手=1 で rollout 不要: 545 (24.3%) → 実質 56.7 決定/ラン
  決定点別:
    名前                  決定/ラン   手=1   手=2  手≥3  平均手数  最大
    MapNavigation          46.47    477    409   508    2.10     5
    RewardChoice           14.83      0    445     0    2.00     2
    EventChoice             7.97      0    208    31    2.16     4
    RestChoice              5.57     68     99     0    1.59     2
  層別:
    1層 16.70 / 2層 14.00 / 3層 13.00 / 4層 11.97 / 5層 14.53 / 6層 3.03 / 7層 1.60
```

**2.5 倍外していた。** 実際のコストは 44 時間/1000ラン（3.5秒/決定ではなく本数で外挿。
56.7 決定 × 平均 2.10 手 × 4 本 ≒ **541 rollout/ラン**）。

#### 想定を壊した 3 点

1. **6-7層にはほとんど決定が無い。** 6層 3.03/ラン のうち 75/91 が手=1、7層は 44/48 が手=1。
   「層の入口・ボス前に絞れば良い」という前提は外れ ── **決定は 1〜5層に集中していて、
   そこは rollout が一番高い（残りのランが長い）場所**。
2. **MapNavigation が 62%**（46.5/ラン）。ここを絞るのが最大のレバー。
3. **`RewardChoice` は常にちょうど 2 択**（445/445）。手=1 による無料の間引きが 1 回も効かない。

#### 無料の間引き（実装済）

`UltraRolloutEvaluator` は**合法手が 1 つの決定点では rollout を撃たない**。
何本撃っても選ぶ手は同じなので、これは発見的な間引きではなく**定義上のただ働き**。
実測 24.3% がここで消える。テスト 2 本（本数ゼロで即答する / 答えが変わらない）。

計装（`UltraDecisionCensus`、既定 OFF・キーは 1 回で消費）は
`Tools/AutoRun/Ultra AI/診断: マクロ決定の国勢調査 (0pt・30ラン)`。**判断は一切変えない。**

### **rollout 方策を実バッチへ接続（2026-08-18）**

ここまで `UltraRolloutSink` は**呼び出し元が無かった**（コスト計測メニューとテストだけ）。
`AutoRunMenu` から実オラクルを差して、初めて方策として走る経路を通した。

- `Tools/AutoRun/Ultra AI/rollout 方策: 疎通 (0pt・10ラン・1手4本)`
- `Tools/AutoRun/Ultra AI/rollout 方策: 本番 (0pt・100ラン・1手8本)`

撃つのは **MapNavigation のみ**（`UltraRolloutSink.AllowedPoints`）。
国勢調査の実測に基づく判断で、絞りは**方策の一部**なのでサマリに残す。

#### 接続で潰した 3 つの地雷

1. **番犬 20 秒/ラン で全ラン DEADLOCK になるところだった。**
   rollout は worker の完了を同期で待つので 1 ラン が実時間 2 分を超える。
   [AutoRunner.cs](../Assets/Scripts/AutoTest/AutoRunner.cs) に `_ultraThinkingSeconds` を足し、
   **番犬から思考時間を控除**する形にした。番犬自体は殺していない ──
   殺すと本物の固着でバッチが永久に止まる。サマリに `Ultra思考` 行を出す。
2. **payload を sink 生成時に固定してはいけない。** 盤面は決定ごとに違うので、
   固定すると全決定を同じ古い盤面から評価することになる。
   provider を `Func<UltraCheckpoint, int, UltraResumePayload>` にして毎回作り直す。
   scenario seed も `epoch` で混ぜる（同じ base のままだと全決定が同じ未来を引き直す）。
3. **worker のビルドは Play Mode に入る前に。** `TryInstallRolloutSink` は
   `OnPlayModeChanged` から呼ばれるが `BuildPipeline` は Play Mode 中に走らない
   （`EnsureBuilt` が "must finish before Play Mode" で断る）。
   ビルドは `LaunchUltraRollout` へ移し、sink 側は `TryLoadDescriptor` で確認のみ。

テスト 4 本追加（絞った決定点で辞退する / 撃つ決定点では決める /
payload が決定ごとに作り直される / 別の決定は別の未来を引く）。計 233 本。

### **同点は辞退する ── 上書き率を強さと読んではいけない（2026-08-18）**

疎通 (1手4本) の上書き 36.5% を成果として読みかけたが、**中身の半分はタイブレークだった**。

0pt のクリア率は 13%。1手4本 なら P(4本とも失敗) = 0.87⁴ ≈ 57% で、2 手とも 0/4 になる決定が
珍しくない。そのとき Wilson 下限は**どちらも 0.000 の同点**で、`Rank` の同点処理は
`CompareOrdinal` ── **辞書順で先のノード id** を返していた。

計装 (`UltraRolloutEvaluator.DescribeCounters`) で数えた結果:

| | 1手4本 | 1手8本 |
|---|---|---|
| 差がついた (下限が 2 位より厳密に大) | 47.2% | **64.7%** |
| 全手0クリア | 31.0% | **9.9%** |
| 上書き | 36.5% | 13.9% |

**上書き率が下がったのが改善**。同点なら辞退して生産方策に任せるようにした
（[UltraRolloutEvaluator.cs](../Assets/Scripts/AutoTest/Ultra/UltraRolloutEvaluator.cs) `Rank`）。
下限が並んだ手のあいだで順位を付ける根拠は無く、辞書順で決めるのは**方策の恰好をした事故**。

1手8本 の内訳（10ラン）はすべて整合する:

- 評価 303 − 差がついた 196 = **107 が同点辞退**
- 辞退 362 − 107 = **255 が絞り込み**（非 MapNavigation。国勢調査の 28.3/ラン と一致）
- 同意 225 のうち **124 は手=1**（選びようがない）。差し引き 101 が本当の同意
- **差がついた 196 件: 上書き 95 (48.5%) / 同意 101 (51.5%)**

→ **Ultra は根拠がある場面の約半分で Super と違う手を選ぶ。** 却下 0 / 例外 0。

### **決定をフレーム跨ぎにした ── Editor が止まらないように（2026-08-18）**

worker を同期で待つとメインスレッドが数秒停車し、Editor が 1 フレームも描画しない。
決定を「開始」と「回収」に分割した。**スレッドは使わない** ── 待ちを別スレッドへ出すと
`JsonUtility` のパースが付いてきて、そこはメインスレッド前提。

| 層 | 追加 |
|---|---|
| オラクル | `BeginEpisodes` → `IUltraEpisodeBatchRun.Poll` (1 掃引 = 起動できるだけ起動し、終わったものを回収) |
| 評価器 | `BeginEvaluate` / `PollEvaluate` / `CancelEvaluation` |
| sink | `BeginDecide` / `PollDecide` / `CancelDecision` |
| `DoNavigate` | 評価中は**盤面にも RNG にも触れず**に return |
| run ループ | 待機中は必ず 1 フレーム譲る (`stepsPerYield=1000` を迂回) |

ブロッキング形は残す（コスト実測メニューとテストは止まって構わない）。

#### 設計中に踏みかけた 3 点

1. **epoch を開始時に進めると commit 検証が必ず落ちる。** 待機中に checkpoint と runner の
   epoch がずれるので、検証を素通しにするか全決定を却下するかの二択になる。**回収時に進める**。
2. **`UltraDispatch.TryResolve` を迂回すると検証と計数が消える。**
   `NoteOffered` / `CompleteResolve` / `NoteFault` を足し、同期経路と同じ受け入れ規律を通す。
   **フレームを跨いだからといって commit ゲートは飛ばさない。**
3. **ストール検出 (`stallLimit=400`) と反復上限が待機を「進展なし」と数える。**
   除外した。stall は 0 に戻さず据え置き ── 待機前から続いていた本物の停滞を帳消しにしないため。

テスト 5 本追加（非同期と同期で答えが一致する / 待っている間は答えない / 捨てたら worker を殺す /
sink が遅延して確定する / 絞った決定点は待たずに即辞退）。計 241 本。

### クラッシュハンドラの除去（2026-08-18）

worker は起動のたびに `UnityCrashHandler64.exe` を随伴させるので、**1 rollout でプロセス生成が 2 回**、
1 決定 16 worker なら 32 プロセスがタスクマネージャーに明滅する。
ビルド後に削除するようにした（`UltraProductionWorkerBuild.RemoveCrashHandler`）。
使い捨ての rollout にクラッシュ報告は要らず、クラッシュは既に「答えなし」として扱われている。

### **Phase H 初回測定: Ultra は Super に勝てなかった（2026-08-21）**

同一シード (seed 0) の 100 ラン ペア比較。0pt・遺物なし・技量 Super・MapNavigation のみ・1手8本。
対照は同じ起動経路で sink だけ外したもの（条件がずれないよう別メニューを組まない）。

| 指標 | 対照 Super | 変種 Ultra | 不一致 (壊した/救った) | McNemar p |
|---|---|---|---|---|
| 7層クリア | **19/100** | 13/100 | 16 / 10 | 0.327 |
| 6F到達 (`reachedFloor>=6`) | **47/100** | 34/100 | 29 / 16 | 0.073 |

**有意差なし。ただし両指標とも符号は一貫して Ultra 不利。**
「有意差なしだから互角」とは読まないこと ── この不一致数では**差を検出する力が無い**だけで、
19%→13% 程度の劣化は現状の n では検出できない。

Ultra 側の内訳（100ラン）:

```
Ultra委譲 : 提示 8259 / 上書き 1337 (16.2%) / 同意 2631 / 辞退 4291 / 却下 0 / 例外 0
Ultra評価 : rollout 評価 3542 件 / 差がついた 2367 件 (66.8%) / 全手0クリア 331 件 (9.3%)
Ultra思考 : 計 367.3分 (220.4秒/ラン)
```

**却下 0 / 例外 0 で 100 ラン 完走した** ── 配管とフレーム跨ぎの commit ゲートは正しい。
壊れているのは配管ではなく**評価の質**。

#### なぜ勝てなかったか ── **断定しない。仮説は 2 つ、どちらも未検証**

1. **証拠が薄い。** 0pt のクリア率は 13% なので、1手8本 の期待クリア数は約 1。
   「差がついた」の判定は下限の厳密不等号なので、**1/8 対 0/8 でも成立する**。
   手調整のヒューリスティックを、ほぼ雑音で上書きしている可能性がある。
2. ~~**情報の非対称。** Super は未公開タイルを読む~~
   → **棄却（2026-08-21 検証）。§Phase B の ① 参照。**
   視界制限は 〈戦場の霧〉〈暗夜〉 の 2 軸だけが持っていたが両方とも廃止済みで、
   `GetMapSightLimit()` は `-1` を返すだけ。`MapNode.revealed` は常に true、
   observation も全ノードの種別をそのまま渡す。**Super と Ultra は同じ盤面を見ていた。**

**残る仮説は 1 だけ。比較は公平で、Ultra は正面から負けた。**
1 を潰すには本数を増やす（線形にコスト増）か、クリア率の高い帯で測る（測る対象が変わる）。

### **階層DP航行（④）── 実装済み・既定 OFF・評価中（2026-08-21）**

Ultra を畳む代わりに **Super の探索そのものを強くする**方向。ユーザー案
（「super を別プロファイルにコピペして改良する方がローコスト」）で、
`docs/design-exact-ai.md` §11 の棚上げ設計（Super をモデルとして合成し探索だけ変える）と一致。

`AutoRunner` に階層まるごとの後ろ向き DP を追加。状態は **(ノード, HP帯)**。

```csharp
[NonSerialized] public bool  useFloorDpNavigation;      // 既定 OFF
[NonSerialized] public float floorDpDiscount = 0.6f;
private const int FloorDpHpBands = 11;                  // 0.1 刻み
```

- **既定 OFF。** Super は全測定の基準線なので、黙って強くすると既存の基準値が全部無効になる
- **再帰は前進辺のみ**（row が増える辺）。横移動辺を DP 内で辿ると同一行を往復して循環する。
  横移動は呼び出し側の pool が既に候補に持つので選択肢は失われず、DAG が構造的に保証される

#### 測定（対照 = 同一シードの Super 100ラン）

| アーム | 7層クリア | 不一致 (壊/救) | p |
|---|---|---|---|
| 対照 Super | 19/100 | — | — |
| γ=0.90 | 16/100 | 12 / 9 | 0.664 |
| γ=0.60 | 23/100 | 10 / 14 | 0.541 |

#### **自分で持ち込んだ交絡（未解決・測定中）**

γ=0.60 は「0.6^10 ≒ 0.006 だから 1 手先読みと一致するはず」と考えて正当性チェックに
使ったが、**この算術が誤り**。既存の 1 手先読みは既に 0.6×(次) を含んでおり、DP が足すのは
0.36×、0.216× ── 無視できない。γ=0.60 でも本物の 10 段探索なので、
「バグ」と「本当に違う探索」を区別できないチェックだった。

そして切り分けようとして**自分が持ち込んだ交絡**に気付いた:

> 1 手先読みは `nextHpRatio` を **float のまま** `Rank` へ渡す。
> DP は **0.1 幅の帯に量子化**して渡す。`Rank` は HP 比を `lowBar` 等と比較するので、
> 量子化で判定が反転しうる。**γ=0.90 の悪化は「深く読んだから」ではなく
> 「HP を粗くしたから」かもしれない。**

#### **結論（2026-08-22）: 採用しない。効果はビルド依存で符号が反転する**

`useFloorDpNavigation` は **既定 OFF のまま**。0pt 基準値 17.6% は有効。

| 条件 | 変種 (γ=0.01・帯101) | 不一致 (壊/救) | p |
|---|---|---|---|
| 0pt 遺物なし seed0 | 178→199 (+2.1pt) | 37 / 58 | 0.040 |
| 0pt 遺物なし seed2 | 175→199 (+2.4pt) | 40 / 64 | 0.024 |
| 0pt 遺物なし **併合** | **+2.3pt** | 77 / 122 | **0.0017** |
| 30pt 遺物あり | 測定不能 (クリア3.8%) | 11 / 10 | 1.00 |
| **0pt 遺物あり** | **542 vs 575 (−3.3pt)** | **93 / 60** | **0.0095（対照が有意）** |

**同じ変更が遺物の有無で符号を変え、どちらも有意。**

##### これが指している本当の欠陥

符号が反転する ＝ **同点の決着に一貫した「正しい向き」が無い**。
つまり問題はタイブレークの方法ではなく、**タイが発生していること自体**:

> `Rank()` は `int` を返す。同点の状況は「本当に等価」ではなく、
> **解像度不足で別物が潰れている**。何で割ってもビルドに対して恣意的になる。

今日試した 3 方向（Ultra の production rollout / 深い DP / HP 量子化）はすべて**探索側**への
介入で、どれも実らなかった。実データが指しているのは**評価関数側の粒度**。
次にやるなら `Rank()` を int から連続値へ、あるいは同点が実際どれだけ起きているかの計装から。

##### 手順として効いたこと

2 シード再現（併合 p=0.0017）の時点で採用を提案していた。
**「先に他の難易度で確かめる」というユーザーの判断が、他ビルドでの有意な劣化を止めた。**
1 条件での再現は「その条件で本物」であって「一般に本物」ではない。

#### 切り分けの結果 ── 交絡は実在し、しかも**有利側**（n=1000・0pt 遺物なし限定）

γ=0.01（末尾項をほぼ消し、残る違いを HP 量子化だけにする）を 1000ラン で:

| 指標 | 対照 Super | γ=0.01 | 不一致 (壊/救) | p | 有意に必要な偏り |
|---|---|---|---|---|---|
| 7層クリア | 178/1000 (17.8%) | **197/1000 (19.7%)** | 45 / 64 | 0.084 | 109 件中 66（いま 64） |
| 6F到達 | 457/1000 | **475/1000** | 59 / 77 | 0.145 | 136 件中 80（いま 77） |

**両指標とも境界のすぐ手前**、n=100 のとき（10 対 4）とも符号が一致。3 標本すべて同じ向き。
まだ有意ではないので「改善した」とは言わない。ただし本物なら含意は明確:

> **`Rank()` は HP 比の細かい違いに過敏で、0.1 幅に丸めた方が良い判断になる。**
> 直すべきは DP ではなく `Rank()` の HP 依存の形。**深く読むことより、
> 価値関数の過敏さを削ることの方が効いている**可能性がある。

#### **n=100 では何も言えなかった（重要）**

2026-08-21 のマップ層の測定はすべて n=100 で、クリア率は 13%〜25% に散らばったのに
不一致 14〜26 件では McNemar がどれも p>0.05 ── **「Ultra は負けた」も「DP は少し悪い」も、
主張できる強さの結論ではなかった**。DP アームは rollout を撃たないので通常バッチ速度
（1000ラン ≒ 20分）で、100 に留める理由は無かった（Ultra 側の 5 時間に合わせただけ）。

`Tools/paircmp_runs.py` の「有意になるには片側が 0 対 N 近くまで偏る必要がある」という
文言も**誤りだったので修正した** ── 不一致が数件のときしか正しくなく、n=1000 の結果を
「絶望的に遠い」と読み違えるところだった。実際に必要な偏りを二項検定で解いて印字する。

#### 計測基盤の副産物

- `Tools/paircmp_runs.py` ── `runs.jsonl` を index でペアにして McNemar 正確二項検定。
  平均比較はしない。index の重複は「同じ世界の 1:1 集合ではない」として拒否する。
- **サマリの「6F到達」列は `reachedFloor>=6` ではない。** band ランク >= 7 で数えており、
  実測では 60% 対 47% とずれる（[AutoRunner.cs](../Assets/Scripts/AutoTest/AutoRunner.cs) の
  `BuildPersonaBlock`）。比較には `runs.jsonl` の実フィールドを使うこと。

#### 次にやること（**優先順位を実測で入れ替えた**）

1. **どの決定点で rollout を使うか絞る** ── 全 30 決定でやる必要はない。
   分岐が効く所（層の入口・ボス前・Λ突入）だけに限れば 1 桁減る。**最大の残レバー**
2. **rollout 数を減らす** ── successive halving（§10 Phase E）。
   ただし現状 4本/手 では Wilson 下限 0.150 でほぼ何も言えていない。
   **本数は減らす方向ではなく、単価を下げて増やす方向**
3. ~~**起動コストの償却（常駐 worker）**~~ ── **不要と判明。エンジン起動は 16ms しかない。**
   プロセス生成込みの固定費でも約 350ms。常駐化の複雑さ（ラン跨ぎ状態汚染、`_combatSeq` の前科）
   に見合わない。将来 1 rollout が 1 秒を切ってから再検討する
4. 残る小口: `item_stats.json` パース ×4（約 180ms）と
   `WriteLogs後 Reload`（[AutoRunner.cs:8185](../Assets/Scripts/AutoTest/AutoRunner.cs#L8185)、
   worker では捨てられる計算）。合わせて 5% 程度なので優先度は低い
5. ~~そのうえで holdout 比較（Phase H）~~ → **2026-08-21 に実施済み。上の節。**
   （かつてここに書いていた「Super が未公開タイルを読んでいる非対称」は**誤りで撤回済み**）

### ~~速度の見積もり（未実測）~~ ── **実測で置き換え済み。上の節を見ること**

> 以下は 2026-08-18 の実測前の見積もり。**外れていた**ので参照しない。
> 特に「起動コストの償却（常駐 worker）」は不要（エンジン起動 16ms）。記録として残す。


1 決定 = 候補数 × rollout 数 のフルラン。0pt で 1 ラン 0.25 秒として:

| | 直列 | 15 並列 |
|---|---|---|
| 5 手 × 16 rollout | 20 秒 + 起動 80 回 | ~2 秒 + 起動 6 波 |

マクロ決定はラン あたり 30 回程度あるので、**1 ラン が数分〜数十分**になる。
10,000 ラン は現実的でない。実走前に決めること:

- rollout 数と候補数の削減（successive halving ＝ §10 Phase E の記述）
- **どの決定点で rollout を使うか**（全部でやる必要はない。分岐が効く所だけ）
- 起動コストの償却（常駐 worker ＝ Phase C の control message を使う形）

まず 1 決定ぶんの実測から。

### Phase G（元の記述）: policy iteration

最初はone-step production rollout improvementでよい。候補を1手production実行し、実効Superで終端まで継続してfull-clear率を比較。採用済みUltra policyをfreezeして次versionのcontinuationにする。

学習器は候補順、予算配分、rollout policyの補助だけ。next state/damage/rewardを生成させず、予測だけで不可逆actionをcommitしない。

### Phase H: 完全版の検証

- 同じpublic observation + 異なるlive RNG stateでaction bit一致。
- 思考前後のlive RNG call/mutation serial不変。
- Editorとstandalone workerのrecorded-chance replay一致。
- passive/challenge/enemy/special/role coverage 100%。
- worker 100k soak、memory plateau、forced crash/timeout/cancel。
- locked holdoutでSuperより高いP7、下側CI>0。
- subsystem別kill switch。

---

## 11. 完了条件

### RunStartOnly v1

2026-08-18 のセッション（`20260817221713-9ba8d5f125084525819cab2795165594`）で以下まで到達。

- [x] profile parity修正。（§2-9）
- [x] compile error 0。（Ultra EditModeテスト 54/54 pass）
- [x] worker build成功。（`build=eed67b0481f15b8d...`）
- [x] 4候補 x 1000 production run完走。（各 valid 999 / 共通invalid は ordinal 0 の Deadlock 1 件のみ）
- [x] evidence / seed / artifact検証PASS。
- [x] trusted selector完了。→ **`super-baseline-v1` を維持**
- [x] 本10K開始。
- [x] HUD/console更新。
- [x] 1件以上完了。→ **10,000/10,000 完走**（2477秒 / 0.25秒per ラン）
- [x] summary実効条件一致。

### 本10K 結果（`batch_20260818_080655_n10000_buffOn_debuffOff`）

7層クリア **2.1%**（6F到達 44.2% / 平均R帯 5.9）。portfolio の baseline 2.7%（n=999）と
矛盾しない（95%CI 約 1.8〜3.9%）。

| ボス | 勝率 | | ボス | 勝率 |
|---|---:|---|---|---:|
| 1層 | 100.0% | | 6層 | 42.3% |
| 2層 | 87.0% | | 7層p1 | 99.7% |
| 3層 | 83.5% | | 7層p2 | 87.3% |
| 4層 | 83.1% | | 7層p3 | 58.1% |
| 5層 | 65.2% | | 7層p4 | 40.2% |

summary の実効条件は §2 の指定と一致:

| 条件 | summary の記載 |
|---|---|
| `useMutualAttackPipeline=true` | 戦闘経路: ADR-0009 相互攻撃 |
| `suppressFacePartOffers=false` | 平均面数 **7.02**（素の 6 面 + パーツ取得ぶん） |
| `shieldAbsorbsUnmitigable=true` | 軽減無視の盾肩代わり: あり (製品挙動) |
| `enableAllDebuffs=false` | メタデバフ: 全OFF |
| `sweepAllMetaAxes=false` | 単一配分: Balanced |
| `challengeSpec=""` | 挑戦スコア ロードアウト 50pt / 解決済み 50pt |

**パーツ抑止だけは直接の記載が無く、平均面数からの逆算だった。**
条件の取り違えが今回の主題なので、`AutoRunner` のサマリへ
`実効トグル:` 行を追加した（値そのものを印字する）。次回以降は逆算不要。

### 気付いた点（Ultra とは別件・未対応）

- 役〈過不足なし〉が **成立 0.1% / 発動 0.0%**（1,647,546 ターン中 763 回）。
  ADR-0010 Verification ④「実測 0% に近い役が無いか」に抵触する
- 充電収支が **+2.39/ターン**（上限で破棄が収入の 12.1%）＝ 使い切れていない

### portfolio 結果（50pt・理論値遺物・各1000ラン）

| 候補 | 7層クリア | baseline との不一致 (自/他) | 片側 exact p |
|---|---:|---|---:|
| `super-baseline-v1` | 27 / 999 (2.7%) | — | — |
| `optimal-v1` | 22 / 999 (2.2%) | 14 / 9 | 0.202 |
| `super-pure-v1` | 24 / 999 (2.4%) | 10 / 7 | 0.315 |
| `super-high-variance-v1` | 32 / 999 (3.2%) | 11 / 16 | 0.221 |

crash 0 / deadlock は全候補で ordinal 0 の 1 件のみ（共通除外）。
**どの challenger も gate を通らないので baseline 維持が正しい判断。**
`super-high-variance-v1` は素の数字では上回るが 16 対 11 では有意でない ──
差を検出するには標本が足りない（1000 ラン ではクリアが 30 件前後しか出ないため）。

### 完全版Ultra

- [ ] run途中の全主要decisionをproduction workerへ接続。
- [ ] hidden情報とlive RNGを参照しない。
- [ ] mechanics変更時にworker rebuild/fingerprintで追従。
- [ ] learned modelはnon-binding。
- [ ] failure時は実効Superへ局所fallback。
- [ ] bounded work/memory、crash/deadlockなし。
- [ ] locked holdoutでSuper超え。

---

## 12. 禁止事項

- 本ランseed、RNG state、次の値をworkerへコピーしない。
- 思考で本ラン`GameRng`を消費しない。
- surrogateだけで不可逆actionをcommitしない。
- mechanicsをUltra専用に再実装して正本化しない。
- hash mismatchやcandidate固有invalidを握り潰さない。
- 複数Unity Editor instanceで同projectを開かない。
- 10K中にscript編集・compileしない。
- worker failureを敗北runとして統計へ混ぜない。
- Super fallbackだけの結果をUltra成果として表示しない。

以上。
