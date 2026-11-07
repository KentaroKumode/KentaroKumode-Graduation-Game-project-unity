using System.Collections.Generic;

namespace AutoTest
{
    /// <summary>
    /// Bot の戦術 (2026-07-16 追加・2026-11-06 にパッシブから読む形へ)。
    /// 目的: AutoRunner の MutualWiringPolicy が「相手を見て」配線を変えられるようにする。
    /// レイヤ 1 (露出): 予兆T/反射/大玉無効 等のフラグを列挙。
    /// レイヤ 2 (プレイブック): ボス ID → 端子重みバイアス + 条件付きブースト。
    ///
    /// 使い方:
    ///   var play = BossPlaybook.From(enemy);
    ///   float wAtk = baseAtk * play.atkBias;
    ///   if (play.IsOmenTurn(turn)) wAtk *= 0.6f, wDef *= 1.8f;  // 予兆Tは防御シフト
    ///
    /// </summary>
    public struct BossPlaybook
    {
        /// <summary>攻撃端子重みの倍率 (1.0 = 変化なし)。</summary>
        public float atkBias;
        /// <summary>ブロック端子重みの倍率。</summary>
        public float defBias;
        /// <summary>充電端子重みの倍率。</summary>
        public float chgBias;

        /// <summary>予兆T のクロージャ (turn, bossHpRatio → is-omen)。null = 予兆なし。
        /// hpRatio は Phase 依存の周期切替 (6F 灰燼の王: >0.5=4T / ≤0.5=2T) を Bot に見せるため。</summary>
        public System.Func<int, float, bool> omenPredicate;
        /// <summary>予兆T の追加 defBias 倍率 (例: 1.8f で通常の 1.8 倍のブロック重み)。</summary>
        public float omenDefBoost;
        /// <summary>予兆T の追加 atkBias 倍率 (例: 0.6f で通常の 60%)。</summary>
        public float omenAtkDamp;

        /// <summary>大玉が無効化されるボス (6F 灰塵の外殻, 5F ChipCap)。
        /// true なら wiring 探索時に「攻撃端子 1個あたりの期待値」を減衰させ、複数小玉を優遇。</summary>
        public bool damageCapped;
        /// <summary>被ダメがキャップされる閾値 (damageCapped=true の時のみ意味を持つ・6F=15/5F=ChipCap)。</summary>
        public int damageCapThreshold;

        /// <summary>反射系: 与ダメがそのまま返る (4F 鏡双子, 7F-p3)。攻撃を控えめに。</summary>
        public bool reflectsAttack;
        /// <summary>反射率 (0.5f 等)。reflectsAttack=true の時のみ。</summary>
        public float reflectRatio;

        /// <summary>DoT 効かない (再生系ボス)。DoT ビルドが判断材料にする。</summary>
        public bool immuneToDot;

        /// <summary>予兆判定 (null 安全)。hpRatio=1 は Phase1 相当。</summary>
        public bool IsOmenTurn(int turn, float hpRatio = 1f)
            => omenPredicate != null && omenPredicate(turn, hpRatio);

        public static BossPlaybook Default => new BossPlaybook
        {
            atkBias = 1.0f,
            defBias = 1.0f,
            chgBias = 1.0f,
            omenDefBoost = 1.0f,
            omenAtkDamp = 1.0f,
            reflectRatio = 0.0f,
        };

        // ================================================================
        //  パッシブの説明から読める範囲だけ (2026-11-06)
        //
        //  旧: ボス ID ごとの手調整の表。 7 月の設計のまま残っていて、 実在しない被ダメ軽減 (灰燼の王・審判官・ヴェスカ p4)
        //  や反射 (ヴェスカ p3・鏡の双子の 50%) を計算に入れていた。 攻略知識で重みを調整する形もやめる ──
        //  相手を見て読めること (説明に周期が書いてある〈業火の予兆〉) だけを使う。
        // ================================================================
        /// <summary>敵のパッシブから戦術を作る。</summary>
        public static BossPlaybook From(CombatSystem.EnemyData enemy)
        {
            var pb = Default;
            if (enemy?.passiveSkills == null) return pb;
            foreach (var ps in enemy.passiveSkills)
            {
                if (ps == null) continue;
                switch (ps.internalName)
                {
                    case "EmberOmen":
                        pb.omenPredicate = (t, hp) =>
                        {
                            if (t < 1) return false;
                            int period = hp <= 0.5f ? 3 : 4;
                            return ((t - 1) % period) == 0;
                        };
                        pb.omenDefBoost = 1.8f; pb.omenAtkDamp = 0.7f;
                        break;
                    case "AshCarapace":
                        pb.damageCapped = true;
                        pb.damageCapThreshold = ItemSystem.PassiveSkills.Effects.AshCarapace.Threshold;
                        break;
                }
            }
            return pb;
        }
    }
}
