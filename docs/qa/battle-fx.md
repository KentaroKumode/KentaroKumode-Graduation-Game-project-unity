# 戦闘演出を実機で確かめる (セーブを書かない口)

2026-11-14〜。エディタの Play モード中に、MCP の `execute_code` から 1 行ずつ呼ぶ（キーは押せなくてよい）。正本は `Assets/Scripts/UI/Game2D/Battle/QaBattle.cs`（エディタと開発ビルドだけ）。

1. **戦闘に入る**: `UI.Game2D.Battle.QaBattle.Start("boss_layer6", "axe")`。敵は `boss_layer6`（灰燼の王）・`boss_layer5`（審判官）・`boss_layer5_hidden`（シュヴァリエ）・`boss_layer7` / `_p2` / `_p3` / `_p4`（ヴェスカ）・ふつうの敵の id。武器は `sword` / `axe` / `dagger` / `shield` / `"none"`（素手）。3 つ目の引数は HP（最大と今・既定 9999）。タイトルからならランを始め、ロード画面は出さずにすぐ戦闘の画面になる。
2. **敵・武器を替える**: `QaBattle.Restart("boss_layer7_p4", "dagger")`。ランを作り直さず、戦闘中なら決着の処理（報酬・段の移り）を通さずに打ち切って始め直す（`Start` もランがあれば同じ動き）。
3. **セーブは書かれない**: `Start` / `Restart` が `GameLoop.SaveGuard.Suppressed` を立てる。中断セーブ・メタ進行・設定・手引きの印・観測所・BGM のミュートを書かない（読むのは普段どおり）。Play モードを抜けるまで立ったまま。手引きは全部「見た」にして出さない。
4. **1 ターン進める**: `QaBattle.Go()`（おまかせで挿して決定）・`QaBattle.GoCrit()`（そのターンの攻撃を会心に）。1 回呼べば、判断待ちになるのを待ってから、決まるまで押し切る（振り直しの判断で止まっても押し直す）。勝ち / 負けの画面なら「次へ」を 1 回。進んだかは `QaBattle.Status()` の `go=`（`pending` = 待ち中 / `done (N 回押した)` = 進んだ / `timeout` = 20 秒待っても進まない）。続けて呼ぶのは `go=done` を見てから。
5. **大技を呼ぶ**: 判断待ちの間に `QaBattle.ChargeNextTurn()`。戻り値 1 なら、次のターン（T+1）の敵の行動がチャージ、T+2 の敵の行動が大技 ── つまり `Go()` の 1 回目で T が終わり、2 回目の終わりにチャージ → 待機、3 回目の終わりに大技。今のターンは変えない（攻撃値と予告はターンの頭に決まっているため）。2 以上が返ったら「その数 − 1 回 `Go()` してから呼び直す」（灰燼の王の戦闘の最初の数ターン・周期 2 の敵）。−1 は大技を持たない敵。ダウンは作れないので、取り消しは `QaBattle.DemoCancel()` で見る。
6. **演出だけを直接流す**（キー K / J / H / Space と同じ・戦闘の流れを通さない）: `DemoBoss("Judge")` → `DemoCharge()` → 待機が出たら `DemoHeavy()`。`DemoMove("move-king-regen")`・`DemoAttack("dagger", true)`。種類は `King` / `Judge` / `Chevalier` / `Vesca` / `VescaFinal`。
7. **何が見えれば正しいか**は `docs/GAME.md` §23-5 の「戦闘演出」の行と、fx-integrate の報告（敵ごとの手順）。見本の GIF は `Captures/fxlib/_gouka/flow.html` ほか（git 管理外）。
8. 新しく永続化の書き込み（PlayerPrefs・ファイル）を足す時は、`SaveGuard` を通すこと（通さないと、この確認でセーブが書き換わる）。
