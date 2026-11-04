# 第三者の素材 (Third-party notices)

このリポジトリの大部分は全権利留保 ([LICENSE](LICENSE)) ですが、次のものはそれぞれの作者の条件に従います。

| 素材 | 場所 | 条件 | 本文 |
|---|---|---|---|
| pixelfx (エフェクト集) | `Tools/pixelfx/`、 `Assets/Resources/Game2DFx/` | MIT License (Copyright (c) 2026 Patch, Hugo Duprez) | [Tools/pixelfx/LICENSE](Tools/pixelfx/LICENSE)、 [Assets/Resources/Game2DFx/LICENSE-pixelfx.txt](Assets/Resources/Game2DFx/LICENSE-pixelfx.txt) |
| x8y12pxDenkiChip の追加字 | `Tools/fontext/glyphs.txt` (と、 そこから作る拡張フォント・`Assets/Resources/Fonts/x8y12pxDenkiChip_dot.bytes`) | SIL Open Font License 1.1。 x8y12pxDenkiChip (Copyright 2026 The x8y12pxDenkiChip Project Authors, https://github.com/hicchicc/x8y12pxDenkiChip) の字の部品から作った改変版。 予約フォント名はなく、 拡張フォントは "x8y12pxDenkiChip Ext" と名前を変えている | https://scripts.sil.org/OFL |
| TextMesh Pro | `Assets/TextMesh Pro/` | Unity Companion License (Unity のプロジェクトで使う範囲) | https://unity.com/legal/licenses/unity-companion-license |
| Liberation Sans (TextMesh Pro 同梱) | `Assets/TextMesh Pro/Fonts/` | SIL Open Font License 1.1 | [Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt](Assets/TextMesh%20Pro/Fonts/LiberationSans%20-%20OFL.txt) |

## リポジトリに含めていないもの

次のものはゲームで使っていますが、 再配布できない・または配布物の扱いが別なので、 git で追跡していません。

- フォントの本体 (`*.ttf`): x8y12pxDenkiChip・DotGothic16 (どちらも SIL OFL 1.1)。 ゲームを配布するときは、 フォントと一緒に OFL の本文を同梱する。
- Unity Asset Store で入手した素材 (`Assets/Eric VFX Studio/` など): Asset Store の規約で再配布できない。 2026-11-04 に過去の履歴からも削除した。
- 画像・音の取り込み済みファイル (`*.png`・`*.meta` など): 生成物として管理外 (`.gitignore`)。
