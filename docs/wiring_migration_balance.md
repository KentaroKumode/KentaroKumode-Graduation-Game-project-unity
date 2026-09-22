# 配線システム移行 バランス調整ジャーニー (2026-07-15〜18)

ADR-0009「相互攻撃モデル」導入から一連の派生調整までを可視化。矢印は原因→対応関係。

## 全体フロー

```mermaid
flowchart LR
    A[ADR-0009<br/>相互攻撃モデル<br/>2026-07-15]:::root

    subgraph 直接副作用 [直接副作用]
        A --> P1[パッシブ発火経路<br/>OnPreDealDamage 系<br/>enemy 側で壊れる]
        A --> P2[「ロール勝敗」概念消失<br/>収支トリガーに再定義]
        A --> P3[配線判断が<br/>常時発生する設計]
    end

    subgraph 6F灰燼の王 [6F 灰燼の王リワーク]
        P1 --> B1[v1 5パッシブ新設<br/>OnPreReceiveDamage 中心<br/>外殻/予兆/再生/焦土/憤怒]
        B1 --> B2[v2 二相型化<br/>HP50 で Phase 切替<br/>Phase1: 再生 / Phase2: 火力]
        B2 --> B3[Phase2 火力調整<br/>Fury+5→+3<br/>Omen周期 2T→3T<br/>再生 8%→5%]
        B3 --> B4[6F 勝率<br/>13% → 41%]
    end

    subgraph ビルド軸拡張 [キーワードビルド 5 軸追加]
        P3 --> K1[充電 9 種<br/>Battery/Spark/Overload等]
        P3 --> K2[臨界 9 種<br/>メーター蓄積型<br/>Burn 全廃の後継]
        P3 --> K3[毒 9 種<br/>Paralysis 主軸]
        P3 --> K4[鈍器 9 種<br/>非会心強化]
        P3 --> K5[出血拡張 6 種]
        K1 --> KI[アイテム 42 品追加]
        K2 --> KI
        K3 --> KI
        K4 --> KI
        K5 --> KI
    end

    subgraph BOT戦術 [BOT 戦術知性化]
        P3 --> Bot1[MutualWiringPolicy<br/>3端子効用計算]
        Bot1 --> Bot2[BossPlaybook<br/>ボス別バイアス<br/>omen/reflect/damageCapped]
        Bot2 --> Bot3[Persona×Playbook 合成<br/>耐久/火力 で分岐]
        Bot3 --> Bot4[Standard再定義<br/>S/Aハンター]
        Bot3 --> Bot5[Keystone加点<br/>ShieldBash等 +4]
    end

    subgraph アイテム整理 [アイテム肥大整理]
        KI --> C1[curse 系列 4 + dead_staff 削除]
        C1 --> C2[重複ユニーク 5 削除<br/>uniq_earth_guard等]
        C2 --> C3[大掃除 18 削除<br/>低使用ユニーク]
        C3 --> C4[名前/フレーバー刷新<br/>キーワード 42 品]
        C4 --> C5[家系ID統一<br/>PascalCase_N<br/>Might_1/BladeEdge_2 等]
        C5 --> C6[LegacyIdMap<br/>セーブ後方互換]
    end

    subgraph ダイス再設計 [ダイス再設計]
        P3 --> D1[武器ダイス数<br/>T1-T3=3 / T4=4]
        D1 --> D2[LEG値圧縮<br/>moroha 6.33→5.67<br/>perfection 6.50→6.33]
        D2 --> D3[強化システム<br/>共通素材で dice にも]
        D3 --> D4[data-driven化<br/>items.json に<br/>enhanceMaxLevel/Costs]
    end

    subgraph 品質整備 [品質整備]
        Bot5 --> Q1[クリア率バグ修正<br/>R11 判定漏れ<br/>R8b パース失敗]
        C6 --> Q2[死コード掃除<br/>11 class 削除]
        D4 --> Q3[Description ドリフト修正<br/>ダイス 7 品]
        Q2 --> Q4[正本ドキュメント同期<br/>DAMAGE_CALC §9.2<br/>passives.md / boss.md]
        B4 --> Q1
    end

    Q4 --> FIN[現状到達点<br/>10 ペルソナ 68-76% 帯収束<br/>アイテム 213 品 / PNG 132]:::finish

    classDef root fill:#4a5568,color:#fff,stroke:#2d3748
    classDef finish fill:#2b6cb0,color:#fff,stroke:#1a4480
```

---

## 6F 勝率の推移

```mermaid
flowchart LR
    S0[初回計測<br/>13.0%<br/>Judgment 死因ゼロ<br/>= 断罪発火せず]:::bad
    --> S1[v1 パッシブ実装<br/>18.1%]
    --> S2[Playbook 導入<br/>41.0%<br/>ペルソナ別戦術]:::mid
    --> S3[Persona×Playbook 合成<br/>41.4%<br/>耐久系復活]
    --> S4[v2 二相型 過剰<br/>31.0%<br/>Phase2 火力過多]:::bad
    --> S5[Phase2 調整<br/>41.4%<br/>バランス着地]:::good

    classDef bad fill:#c53030,color:#fff
    classDef mid fill:#d69e2e,color:#fff
    classDef good fill:#2f855a,color:#fff
```

---

## ペルソナ勝率の収束 (最新 10000ラン)

```mermaid
flowchart LR
    A[初期<br/>Standard 77.6%<br/>Charge 68.7%<br/>= 9pp 差]
    --> B[Charge/Rinkai<br/>アイテム 18 追加<br/>キーワード実装で拾える]
    --> C[Standard = S/A ハンター<br/>再定義]
    --> D[Universal Boost<br/>Might/Pursuit 全軸へ]
    --> E[現状<br/>10 軸 68-76% 帯<br/>7.6pp 収束]:::good

    classDef good fill:#2f855a,color:#fff
```

---

## アイテム数推移

```mermaid
flowchart LR
    A[起点<br/>198 品] --> B[キーワード追加<br/>+43 品<br/>= 241]
    B --> C[curse+dead_staff<br/>-5<br/>= 236]
    C --> D[重複削除<br/>-5<br/>= 231]
    D --> E[大掃除 18品<br/>-18<br/>= 213]:::good

    classDef good fill:#2f855a,color:#fff
```

---

## ダイス設計の変遷

```mermaid
flowchart LR
    A[初期<br/>武器 T1=1個<br/>配線判断ゼロ]:::bad
    --> B[試行1<br/>T1-T4 = 2/3/4/5<br/>Tier段差 +50%]:::mid
    --> C[試行2<br/>T1-T3=3 / T4=4<br/>段差平準化]:::good
    C --> D[LEG値圧縮<br/>moroha/perfection<br/>-0.5]
    D --> E[強化システム<br/>BRONZE Lv4→avg5.0<br/>素LEG に迫る]
    E --> F[data-driven化<br/>items.json 側で<br/>調整可能]:::good

    classDef bad fill:#c53030,color:#fff
    classDef mid fill:#d69e2e,color:#fff
    classDef good fill:#2f855a,color:#fff
```

---

## 相互攻撃モデルで壊れたパッシブ処理の因果連鎖

```mermaid
flowchart LR
    A[ADR-0009<br/>UseMutualAttackPipeline]
    A --> B[ExecuteTurnMutual<br/>1697行: 自 ProcessDamage<br/>1759行: 敵側トリガー<br/>1762行: 敵 ProcessDamage]

    B --> C[問題点<br/>1759 の OnPreDealDamage は<br/>playerWonRoll==true 時<br/>= 自攻撃の残り値を触る]
    C --> D[6F JudgmentBlaze<br/>×coef+coef 適用先が<br/>敵攻撃でなく残骸]
    D --> E[断罪ダメが敵攻撃に<br/>反映されない<br/>Normal 死 100%]
    E --> F[灰燼の王 v1 全リワーク<br/>OnPreReceiveDamage/<br/>OnRollWin ベースに]:::good

    classDef good fill:#2f855a,color:#fff
```

---

## 未着手の課題

- **7F 覚者連戦の通しクリア率** (現状 p1-p7 各 80-95%、通し 55% 未満)
- **1〜3層 空気化** (100% 勝率、平均 1.2T、配線判断が形式化)
- **Λ層 リスク/リワード** (死亡 27-30% vs 純益 +0.5 アイテム)
- **5層 Chip 死因単色化** (敗北 96-98% が Chip 一色)
- **Charge ペルソナ底値** (依然 68% 帯・充電消費の Bot 学習未熟)
- **レア分布逆ピラミッド** (GOLD+LEG で 61%)
