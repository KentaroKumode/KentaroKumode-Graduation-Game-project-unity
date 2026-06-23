---
name: presentation-html
description: Generate landscape-A4 presentation slides as a self-contained HTML file with Mermaid diagrams, warm sepia/beige color palette, and print-PDF optimization. Use when user asks for プレゼン / スライド / presentation / PDF 化用 HTML / プレゼン資料. Trigger by /presentation-html [scope] or natural language requests for slide-style documents.
effort: medium
---

# Presentation HTML Skill

Build polished landscape-A4 slide decks as self-contained HTML files that can be
opened in any browser and exported to PDF via Ctrl+P. Uses warm sepia/beige
palette consistent with the "古書 / 羊皮紙" theme. Mermaid diagrams render
client-side via CDN.

## Trigger

### コマンド書式

```
/presentation-html [topic] [serial] [author]
```

- `topic`     ── プレゼンのトピック / スコープ (必須)
- `serial`    ── 連番 (例: 22) ── タイトルスライドの「第 N 回」 + 各 brand に `#N`
- `author`    ── 署名 (例: 雲出健太郎) ── タイトルスライド + 各 brand 末尾

例:
```
/presentation-html 改修ハイライト 22 雲出健太郎
/presentation-html バランス調整報告 5 雲出健太郎
/presentation-html 新機能Xリリース 1 開発チーム
```

### 自然言語トリガー

- "プレゼン HTML 作って", "スライド形式で出力", "PDF化用 HTML"
- "見栄えのいい資料に", "横向きA4スライド", "見映えのいい PDF"
- "前回のプレゼンに追記して" (= existing HTML を拡張)
- "#NN 回目のプレゼン" (serial 明示)

引数不足時は `topic` のみ訊き、 `serial`/`author` は省略可 (デフォルト: 連番無し / 署名無し)。

## Output Specification

### File location
Default: `docs/presentation/{topic}_{yyyy-mm}.html`
Custom path acceptable if user specifies.

### Page format
- `@page { size: A4 landscape; margin: 10mm 14mm; }`
- Each `<section class="slide">` is one A4 page
- `page-break-after: always` to enforce 1 slide / 1 page

### Color palette (warm sepia)
- Background: `#fbf9f3` (off-white parchment)
- Border/accent: `#8a7355` (sepia brown)
- Headers: `#4a3c25` (dark brown)
- Sub-headers: `#6a5a3c` (mid brown)
- Table header bg: `#ede0c8`
- Table even row bg: `#fbf6ec`
- Body bg (outside slides): `#ece4d2`

### Typography
- `font-family: "Hiragino Sans","Yu Gothic","Meiryo",sans-serif;`
- Title slide h1: 44pt (centered)
- Section h1: 36pt (bordered)
- h2: 24pt, h3: 14pt
- Body text: 11-12pt, tables: 10pt compact 9.5pt

### Standard slide components

#### Section element
```html
<section class="slide">
  <h1>Slide Title</h1>
  <!-- content -->
  <div class="brand">Section Label</div>
  <div class="slide-num">N</div>
</section>
```

#### Two-column layout
```html
<div class="two-col">
  <div>Left content</div>
  <div>Right content</div>
</div>
```

#### Three-column layout
```html
<div class="three-col"> ... </div>
```

#### KPI boxes
```html
<div class="kpi">
  <div><b>54.1</b><span>label</span></div>
  <div><b>+29.4</b><span>label</span></div>
  <div><b>~68</b><span>label</span></div>
</div>
```

#### Mermaid diagram
```html
<div class="mermaid">
graph LR
    A[Start] --> B[End]
</div>
```
Initialize at top:
```html
<script src="https://cdn.jsdelivr.net/npm/mermaid/dist/mermaid.min.js"></script>
<script>mermaid.initialize({startOnLoad:true, theme:'base', themeVariables:{primaryColor:'#f4ebd6', primaryTextColor:'#2b2b2b', primaryBorderColor:'#8a7355', lineColor:'#5a4a35', secondaryColor:'#ede0c8', tertiaryColor:'#f9f4e6'}});</script>
```

#### Badge / tag
```html
<span class="badge">Label</span>
```

#### Blockquote (for callouts)
```html
<blockquote>重要な注記や引用</blockquote>
```

#### Compact table
```html
<table class="compact">
  <tr><th>列1</th><th>列2</th></tr>
  ...
</table>
```

### Mandatory full template

See `templates/base.html` for the full self-contained template with all
CSS and Mermaid initialization. Always start from this template.

## Default slide structure (when no specific topic)

1. **Title slide** — `h1.title-main`, lead paragraph, badges, **第 N 回 / author 署名**
2. **全体俯瞰** — Mermaid `graph LR` system overview
3. **N × Phase slides** — One topic per slide, two-col or three-col layout
4. **現在地と次の打ち手** — Status + remaining tasks
5. **語彙リスト** — Two-col table (システム関連 / 測定指標)

## Serial / Author 埋め込み規約

引数で `serial` (連番) と `author` (署名) を受け取った場合の表示位置:

### Title slide
タイトル + lead + badges の下、 中央配置:
```html
<div style="text-align:center; margin-top:12mm; font-size:14pt; color:#4a3c25; letter-spacing:0.08em;">
  第 <strong style="font-size:18pt;">{{SERIAL}}</strong> 回 / {{AUTHOR}}
</div>
```

### 各スライド brand (左下フッタ)
```html
<div class="brand">{{SECTION_LABEL}} ─ #{{SERIAL}} / {{AUTHOR}}</div>
```

例 (serial=22, author=雲出健太郎):
- タイトル: 「第 **22** 回 / 雲出健太郎」
- 各 brand: 「Phase 1 ─ 旅団契約システム ─ #22 / 雲出健太郎」

### 省略時のフォールバック
- `serial` 無し → `#{{SERIAL}}` 部分を出さない
- `author` 無し → `/ {{AUTHOR}}` 部分を出さない
- 両方無し → タイトル中央ブロックは出さず、 brand は section label のみ

## Build Process

1. Ask user for scope/topic if unclear (or infer from context)
2. Determine slide count (typical: 8-16 slides)
3. Copy `templates/base.html` skeleton
4. Fill in slides one-by-one
5. Number each slide via `<div class="slide-num">N</div>`
6. Brand each section via `<div class="brand">Section Label</div>`
7. Verify slide content fits in `190mm` height (each slide should not overflow)
8. Tell user the path + how to convert to PDF

## PDF Conversion Instructions to User

> ファイルをブラウザで開く → Ctrl+P → 「向き: 横」 → 「背景のグラフィック」 ON → PDF 保存

## Examples of when to invoke

- "今月の改修まとめスライド作って"
- "/presentation-html 戦闘システム"
- "新機能 X のプレゼン資料"
- "前回のスライドに Phase 7 追加して" (existing extension)

## When NOT to invoke

- Simple markdown documentation → use docs-restructure or write `.md`
- Code samples without need for visual presentation
- One-off analysis answers (chat reply suffices)
- Interactive web tools (use proper React/Vue not this static template)

## File output rules

- Self-contained HTML (all CSS inline, only Mermaid via CDN)
- UTF-8, Japanese OK
- File extension `.html`
- Default location `docs/presentation/`
- Date in filename for versioning: `{topic}_{yyyy-mm}.html`

## Skill internals

- `templates/base.html` — minimal slide skeleton (use as starting point)
- `templates/styles.css` — extracted CSS for reference
- `references/example_2026-06.html` — full example from 2026-06 batch
