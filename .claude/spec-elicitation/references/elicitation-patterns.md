# Elicitation Patterns

Canonical phrasings for each of the 4 axes. Use these as starting
points; adapt phrasing to the user's tone and the project domain.

## Axis 1 — Interpretation Language (Stage 1)

Goal: restate the user's description in your own words and surface
mismatches.

Good openers:

- "あなたの説明をこう解釈しました。間違いがあれば直してください。"
- "整理すると、つまり <X> を <Y> のために作る、という理解で合っていますか?"
- "私はこう読み取りましたが、強調したかったポイントが他にあれば教えて
  ください。"

Avoid:

- "つまりこういうことですね?" (closes the loop too tightly)
- "では <X> を作ります" (commits before confirming)

## Axis 2 — Ambiguity Surfacing (Stage 2)

Goal: name the implicit assumptions and confirm or defer each.

Categories to scan for in every session:

| Category | Typical question |
|----------|------------------|
| Target user | 自分専用 / チーム共有 / 公開のどれですか? |
| Runtime | ブラウザ / CLI / デスクトップアプリ / サーバ常駐? |
| OS | Linux のみ? macOS や Windows も対象? |
| Data scope | 個人データのみ? 共有データを扱う? |
| Persistence | ファイルベース / DB / クラウド? 揮発でよい? |
| Auth | 認証は不要? シングルユーザー前提? |
| Error policy | 失敗時に黙って続行 / ユーザーに通知 / 即停止? |
| Existing assets | 既存のスクリプト / ファイルと連携する? 置き換える? |
| Scale horizon | 1 ユーザー / 数十 / それ以上を想定? |

Phrasings:

- "<X> という前提で考えていますが、合っていますか?"
- "言われていない部分として <Y> があります。これはどう想定して
  いますか?"
- "ここは決められない / 後で考えたい場合は、open question として
  残します。"

Avoid:

- Asking 5 categories in a single free-text question — split into
  AskUserQuestion entries.
- Forcing a decision when the user hesitates — defer to Stage 4.

## Axis 3 — Alternative Comparison (Stage 3)

Goal: at each decision point, present 2–3 concrete options with
tradeoffs.

Pattern:

```text
分岐点: <decision>
- 案 A: <description> — pro: ... / con: ...
- 案 B: <description> — pro: ... / con: ...
- 案 C: <description> — pro: ... / con: ...
推奨: <one of A/B/C if confidence is high, else neutral>
```

Phrasings:

- "ここは選択肢が分かれます。<A> / <B> / <C> のどれにしますか?"
- "<A> は <pro> ですが <con>、一方 <B> は <con> の代わりに <pro>。
  どちらを優先しますか?"
- "決め手がなければ、暫定で <A> にしておいて、あとで見直すという
  形でもよいです。"

Anti-patterns:

- Presenting one option and asking "OK?" — that is not a decision
  point, that is leading.
- Listing 5+ options — choice fatigue. Merge similar ones.
- Asking the user to weigh tradeoffs you can resolve yourself.

## Axis 4 — Open-Question Structuring (Stage 4)

Goal: convert deferred items into structured open-questions with
urgency and blocks fields.

Phrasings:

- "保留にした項目をまとめます。それぞれ urgency を h/m/l のどれに
  しますか?"
- "<Q1> はどの spec / phase をブロックしますか? 知らなければ "phase
  全体" と書きますが、それでよいですか?"
- "暫定方針として <X> を当てておきますが、変更したいですか?"

Urgency rubric:

| Urgency | Meaning |
|---------|---------|
| high | Phase 0 cannot start until this is decided |
| medium | Phase N (N>0) is held up; Phase 0 can proceed |
| low | Nice-to-decide; nothing is blocked |

If everything looks `medium`, push back — usually one or two
questions are actually `high` and the user underestimated their
impact.

## Cross-axis tips

- **Speak to the user, not at them.** A question phrased as "X か Y
  か?" beats "Please decide between X and Y" every time.
- **Surface uncertainty as your own.** "私はここで迷っています — A
  か B か" beats "What do you want?".
- **Name what you are NOT going to ask.** "今回は <Z> は議論しない
  ので、必要なら指摘してください" prevents scope creep without
  closing the door.
- **Echo decisions immediately.** After every stage, re-emit the
  scratchpad. The user's confidence rises with each visible
  accumulation.
