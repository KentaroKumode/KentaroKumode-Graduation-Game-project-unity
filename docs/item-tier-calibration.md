# 難易度別 Item Tier 更新

Item Tier は挑戦難易度 `0pt`、`30pt`、`50pt` ごとに独立して学習する。
異なる難易度のランは同じ `item_stats.json` へ混ぜない。

## 固定条件

- ビルドペルソナ: `Standard`
- 配線AI: `Optimal`（ゲーム上の通常AI）
- メタ進行: `Standard / Balanced`
- アイテム取得: `BuildFocused`
- ボス調整: OFF
- BOTルーチン学習: OFF
- Tier学習のみ: ON
- 1条件: 1000ラン

メタ遺物は通常バッチと同じ扱いとし、理論値遺物や遺物なしには固定しない。
マスターシードを指定した場合、3難易度は同じrun indexを使うため同一シード比較になる。

## Unityメニュー

- `Tools/AutoRun/Item Tier更新/難易度 0pt (Standard・通常AI・1000ラン)`
- `Tools/AutoRun/Item Tier更新/難易度 30pt (Standard・通常AI・1000ラン)`
- `Tools/AutoRun/Item Tier更新/難易度 50pt (Standard・通常AI・1000ラン)`
- `Tools/AutoRun/Item Tier更新/0・30・50pt 一括 (各1000ラン)`
- `Tools/AutoRun/Item Tier更新/0・30・50pt × 1000ラン × 10セット`
- `Tools/AutoRun/Item Tier更新/動作確認 0・30・50pt (各10ラン)`

## 出力

学習データ:

- `AutoRunLogs/learning/buffOn_debuffOff/tier_score_0/`
- `AutoRunLogs/learning/buffOn_debuffOff/tier_score_30/`
- `AutoRunLogs/learning/buffOn_debuffOff/tier_score_50/`

Tier表:

- `BALANCE_TIER_LIST_buffOn_debuffOff_score0.md`
- `BALANCE_TIER_LIST_buffOn_debuffOff_score30.md`
- `BALANCE_TIER_LIST_buffOn_debuffOff_score50.md`

通常の `buffOn_debuffOff` 学習データ、policy、イベント判断、ボス係数は更新しない。

10セット版は各1000ランの終了時にTierを保存・再読込する。次セットのStandard AIは、
直前セットまでに更新されたTierを使用する。セットごとにrun indexの帯を分け、同じ難易度内で
同一シードを重複使用しない。


Tier採用には既存の信頼性ゲート `acq6F >= 30` を使う。50ptでは6層到達自体が少ないため、
最初の1000ランでは確定Tierが少数になる場合がある。その場合は50ptメニューを繰り返し、
同じディレクトリへ累積してから判定する。サンプル不足品を推測でTierへ割り当てない。
