# Collection High / Low Builder

> ベース名の末尾に `_high` / `_low` が既に含まれている場合は自動的に正規化され、
> 二重付与されません。例: `TEST_high_low_` → `TEST_1_high` / `TEST_1_low`

Blenderの指定コレクションを、次の形式へ一括変換するアドオンです。

```text
元:  AAAA, BBBB, GGG, YYYY1135
high: TEST_1_high, TEST_2_high, TEST_3_high, TEST_4_high
low:  TEST_1_low,  TEST_2_low,  TEST_3_low,  TEST_4_low
```

元コレクションが `Collection_A` の場合、high側は元の `Collection_A`、low側は新しい
`Collection_A_low` になります。処理後に個数、名前、種類、トランスフォーム、データ構成、
親子関係を照合してポップアップで結果を表示します。

## インストール

1. Blenderで `編集 > プリファレンス > アドオン` を開く。
2. `ディスクからインストール` を押す。
3. `collection_high_low_builder.py` を選択して有効化する。
4. 3Dビューで `N` キーを押し、`High / Low` タブを開く。

## 使用方法

1. `対象コレクション` を選ぶ。
2. `ベース名` に `TEST` などを入力する。
3. 必要に応じて処理順などを変更する。
4. `High / Lowを作成・照合` を押す。

既定の処理順はOutlinerで通常表示される名前順です。`子コレクションも含める` が有効なら、
階層と複数コレクションへの所属もlow側へ再現します。`オブジェクトデータを独立コピー` が
有効なら、low側のメッシュ等はhigh側とは別データになります。

## 安全上の動作

- 完成予定名のオブジェクトが対象外に既にある場合、`.001` などを付けず処理を中止します。
- `{元コレクション名}_low` が既にある場合も処理を中止します。
- 操作はBlenderのUndo対象です。
