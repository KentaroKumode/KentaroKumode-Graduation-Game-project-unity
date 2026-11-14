# 戦闘演出を実機で確かめる (セーブを書かない口)

2026-11-14〜。エディタの Play モード中に、MCP の `execute_code` から 1 行ずつ呼ぶ（キーは押せなくてよい）。正本は `Assets/Scripts/UI/Game2D/Battle/QaBattle.cs`。

1. **戦闘に入る**: `UI.Game2D.Battle.QaBattle.Start("boss_layer6", "axe")`。敵は `boss_layer6`（灰燼の王）・`boss_layer5`（審判官）・`boss_layer5_hidden`（シュヴァリエ）・`boss_layer7` / `_p2` / `_p3` / `_p4`（ヴェスカ）・ふつうの敵の id。武器は `sword` / `axe` / `dagger` / `shield` / `"none"`（素手）。タイトルからでもラン中からでもよい（ラン中に呼ぶと新しいランを始め直す）。
2. **セーブは書かれない**: `Start` が `GameLoop.SaveGuard.Suppressed` を立てる。中断セーブ・メタ進行・設定・手引きの印・観測所・BGM のミュートを書かない（読むのは普段どおり）。Play モードを抜けるまで立ったまま。手引きは全部「見た」にして出さない（これも書かない）。
3. **1 ターン進める**: `QaBattle.Go()`（おまかせで挿して決定）・`QaBattle.GoCrit()`（そのターンの攻撃を会心にして決定）。演出が終わるまで次の `Go` は効かないので、`QaBattle.Status()` の `fxIdle=True` と `sim=Wire` を待つ。
4. **大技を呼ぶ**: 判断待ちの間に `QaBattle.ChargeNextTurn()` → `Go()`（次のターンの敵の行動でチャージ → 待機）→ もう一度 `Go()`（大技）。ダウンは作れないので、取り消しは `QaBattle.DemoCancel()` で見る。
5. **演出だけを直接流す**（キー K / J / H / Space と同じ・戦闘の流れを通さない）: `DemoBoss("Judge")` → `DemoCharge()` → 待機が出たら `DemoHeavy()`。`DemoMove("move-king-regen")`・`DemoAttack("dagger", true)`。種類は `King` / `Judge` / `Chevalier` / `Vesca` / `VescaFinal`。
6. **何が見えれば正しいか**は `docs/GAME.md` §23-5 の「戦闘演出」の行と、fx-integrate の報告（敵ごとの手順）。見本の GIF は `Captures/fxlib/_gouka/flow.html` ほか（git 管理外）。
7. 新しく永続化の書き込み（PlayerPrefs・ファイル）を足す時は、`SaveGuard` を通すこと（通さないと、この確認でセーブが書き換わる）。
