# UltraAI — Production-Oracle 設計

更新: 2026-08-17

## 1. 確定事項

UltraAI は独自のゲーム遷移モデルを持たない。候補の評価は、同一 production build の
隔離 worker で実ゲームを実行した結果だけを使う。

- ダメージ、回復、報酬、ショップ、イベント、パッシブ、挑戦効果を Ultra 側へ再実装しない。
- `SuperCombatAI.ApplyTurn` や `ExactCombatAI` の近似状態をラン全体の正本にしない。
- learned transition、`CombatOutcomeKernel`、未完走枝の learned value 代替を禁止する。
- 学習器を使う場合は、候補順、探索幅、rollout 継続方策、予算配分に限る。
- 最終候補は、選抜に使っていない production episode で再確認する。
- worker 不調、checkpoint 不完全、build 不一致、証拠不足では実効 Super へ戻る。

主目的は `P(7層完全クリア)` のみとし、各terminal episodeの主報酬は完全クリア=`1`、
それ以外=`0`に固定する。層到達、残HP、所持金などを加重して主報酬へ混ぜない。
完全クリア確率が統計的に同率の時だけ、別フィールドによる辞書式tie-breakを将来追加できる。

既存 [design-exact-ai.md](design-exact-ai.md) §11 は ExactCombatAI の履歴であり、
UltraAI には適用しない。

## 2. 決定性とseedの契約

本ランは従来どおり `GameRng` の live master seed / run index で進行する。Ultra の思考は
live seed、`GameRng` の状態、次の出目を参照・消費しない。

Ultra の候補比較には、live seed と無関係な synthetic scenario bank を使う。ただし
候補ごとに別の scenario を与えてはならない。同じ decision の全候補へ同じ scenario
indexを与え、paired common random numbers として比較する。

```text
scenario 0: baseline / candidate A / candidate B = 同じ synthetic seed
scenario 1: baseline / candidate A / candidate B = 同じ synthetic seed
...
```

検索用と最終確認用は domain を分ける。候補選抜後は fresh confirmation bank で
提案手と baseline を再評価する。

保証範囲:

1. 同一 build・設定・live seed・Ultra planner salt・固定仕事量なら、各 mode の再実行は同一。
2. Super と Ultra が同じ本番行動履歴を持つ間、production state と同じ `(key,index)` の
   `GameRng` 結果は一致する。
3. Ultra の思考による live `GameRng` 呼出しは 0。
4. 最初の本番行動差以後、経路や同一キー内の発生回数が違えば結果は合法的に分岐する。
   これはseed破綻ではなく `Outcome = F(seed, policy)` の policy 差である。
5. 無関係な名前付き乱数列を思考によってずらすことは禁止する。

`GameRng`は一本の逐次ストリームではなく、`hash(runSeed, runIndex, key, index)`で値を作る。
そのため、あるキーの抽選回数が増えても別キーの結果はずれない。`RangeAuto`のようにindexを
省略した呼出しだけは、その同じキー内のcounterを進める。Ultraが本番で別行動を選んだ後に
同一キーの呼出し回数まで変われば、そのキーの後続結果は分岐し得るが、全乱数列の破綻ではない。

Ultra の並列処理はwall-clock打切りを主予算にしない。固定episode数、固定候補順、固定scenario、
固定merge順を使い、完了したroundだけを採用する。

## 3. 「実ゲームを叩く」の意味

初期構成は同一buildのheadless standalone Playerを永続worker poolとして起動する。
Editor instanceを複数起動してはならない。

```text
live run ── public checkpoint ──> Ultra controller
                                      ├─ production worker 0
                                      ├─ production worker 1
                                      └─ production worker N
                                             │
                                      terminal outcome
```

workerはcheckpointを復元し、候補の最初の行動をproduction APIへ渡し、その後もproduction
`GameManager` / `CombatManager` / `ShopManager` / event処理を使って終端まで走る。

workerの乱数は独立したsynthetic seedをproduction `GameRng`へ与える。live runの乱数状態は
workerへ渡さない。未公開マップ、未公開商品、未来のイベントもコピーせず、公開履歴と整合する
条件下でproduction generatorから生成する。

controllerが生成する乱数情報は、worker episodeの開始seedだけである。workerはそのseedを
production `GameRng.SetMasterSeed` / `BeginRun`へ渡し、以後の全抽選をproduction `GameRng`へ
委譲する。Ultra独自の「次の乱数」APIや確率分布実装は持たない。

## 4. checkpoint

checkpointはゲームモデルではなくproduction stateの移送形式である。最低限、以下を完全に
round-tripできなければならない。

- `RunState`のmechanical persistent state
- map graph、公開状態、現在地、訪問・pool状態
- phase、pending reward/event/choice、boss chain
- shop stock、reroll、価格・割引状態
- combat context、敵形態、turn、dice face identity、role、buff/debuff/stack
- inventory、passive、sigil、ascension、consumable
- challenge全軸tier、meta、relic、phenomenon、Lambda、sin

snapshot対象外のmechanical fieldが1件でも見つかった場合、そのsubsystemのUltraを有効化しない。
live singletonを一時変更して巻き戻す方式は禁止する。

## 5. worker handshake

controllerとworkerはjob受付前に以下を完全一致させる。

- executable / Assembly-CSharp mechanics hash
- build GUIDとscripting defines
- items / enemies / events / challenge / relic / map / shop data hash
- checkpoint / action protocol schema
- objectiveとcontinuation policy version

不一致時はjobを実行せずSuperへ戻る。compileまたはdata refresh後はworker poolを停止し、
新buildの起動が完了するまでUltraを無効にする。Ultra実行中にEditor compileを開始しない。

## 6. shadow gate

Ultraが本番行動を変更する前に、実効Super手をそのまま実行するshadow modeを通す。

1. Super 1000ランとUltra-shadow 1000ランを同じlive seed列で実行する。
2. 毎decisionのproduction state hash、合法手集合、選択手、`GameRng (key,index)` trace、run digestを比較する。
3. 1000/1000一致、思考によるlive RNG呼出し0、worker例外0を要求する。
4. 1件でも不一致ならEnabled modeを拒否する。

Enabled後は最初の方策差を記録し、それ以前のtrace完全一致を引き続き要求する。

## 7. 安全性

- main側は探索前に実効Super actionを確保する。
- workerは別process、worker別disposable profile、保存先、logを使う。
- jobごとのstep、turn、memory、wall-time上限とheartbeatを持つ。
- timeout/crash時はworkerをkill/restartし、そのepisodeを勝敗へ混ぜない。
- quorum未達、illegal、NaN、schema不一致ではbaselineを実行する。
- 最終action commit時にrun epoch、phase、合法性を再検証する。
- 候補数、episode数、checkpoint/action payload、production step数にはcontroller側の絶対上限を置く。
- worker APIの例外またはstep上限違反は一度でそのdecisionを失敗させ、同じjobを無限再試行しない。

## 8. 進捗表示

Ultraの探索は1ラン内でも長くなるため、外側のラン進捗と内側の探索進捗を分離して表示する。

- 画面右下に緑色で、10セルの `■/□` バーと小数第1位の百分率を表示する。
- 主バーは `完了ラン数 / 総ラン数` だけを表し、SEARCHからCONFIRMATIONへ移っても後退しない。
- SEARCH／CONFIRMATION／EXECUTINGのphase進捗、稼働worker数、経過時間、ETA、最終更新からの秒数を併記する。
- 完了ランの履歴は直近20件だけを固定長で保持する。
- 画面キャッシュは既定0.5秒間隔で更新する。`OnGUI`ではキャッシュ済み文字列だけを描画する。
- Consoleは開始・phase変更・終了・失敗・fallbackを即時表示し、通常進捗は既定で最短10秒、
  主進捗5ポイントまたはphase進捗10ポイント、heartbeat 30秒のいずれかを満たした時だけ表示する。
- 子workerは画面やConsoleへ直接出力しない。controllerがworker応答を集約して表示するため、wire protocolをログで汚染しない。
- generation付きsessionにより、cancel／再起動前のworkerから遅着した進捗は無視する。
- Ultraの思考をmain thread上で同期実行すると画面更新自体が止まるため、正式controllerは探索を別process／background jobで実行し、
  main threadはpollと最終action commitだけを行う。

## 9. 実装順

1. deterministic scenario bank、worker protocol、shadow/fallback controller。
2. run-start production workerとbinary/data handshake。
3. public state hash、action trace、live RNG非干渉計装。
4. stable decision boundaryごとのcheckpoint export/import。
5. Super継続方策によるproduction one-step policy improvement。
6. route/shop/event/rest/Lambda/combatの順にUltra候補を有効化。
7. 全run tree searchとproduction combat BIPI。

Phase 4まではUltraはshadow専用であり、「Ultraが実装済み」と表記しない。

## 10. 現在の実装状態（2026-08-17）

実装・検証済み:

- live seedと独立したdeterministic scenario bank（全候補へ同じscenarioを割当）。
- 候補順に依存しない固定仕事量のdecision engine。
- 実効Superを常時確保し、Shadow／証拠不足／worker異常／上限違反で即時fallback。
- 選抜bankと分離したfresh confirmation、および7層完全クリア=`1`／その他=`0`の主目的。
- worker wire protocol。wire上の乱数情報はsynthetic episode開始seed 1個だけ。
- build/data/checkpoint/action/objectiveの5 SHA-256 fingerprint完全一致、未知JSON field拒否。
- production step、候補数、episode数、checkpoint、action payloadのhard cap。
- 実worker buildのscene順・company/product identity・mechanics・Assembly-CSharp graph・
  frozen learning snapshotを対象にしたcanonical manifest生成基盤。
- thread-safeな進捗集約、generationによる遅着拒否、直近20件、緑色10セルHUD、低頻度Console出力。
- decision engineのSEARCH／CONFIRMATION／EXECUTING進捗通知。外側の完了ラン数は将来のAutoRunner/controllerが通知する。
- Unity compile成功、Ultra EditMode 41/41件合格、基盤診断PASS。

未実装（したがってEnabledにはしない）:

- standalone production worker executable／process pool／heartbeat／強制再起動。
- AutoRunnerの完了通知とUltra mode dispatch hook。
- 任意のrun途中状態を公開情報だけで移送するcheckpoint export/import。
- production `GameRng` draw traceとSuper対Ultraの1000-run shadow gate。
- route、shop、event、rest、Lambda、combat各decisionへの実接続。

これらは既存の実装中ファイルへhookを入れる段階まで保留する。土台だけをUltraとして選択可能にしたり、
fake oracleの結果で本番行動を変更してはならない。
