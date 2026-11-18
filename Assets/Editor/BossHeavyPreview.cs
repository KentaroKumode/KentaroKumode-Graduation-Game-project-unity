using GameLoop;
using UI.Game2D;
using UI.Game2D.Battle;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ボスの大技の演出を、 ボタン 1 つで最初から最後まで流す (2026-11-17・作者の依頼「ボスの大技のチャージから発動までの演出を流せるボタンが欲しい」)。
/// 流れは <b>チャージ → 待機 (予兆のループ) → 溜め → 全画面 → 着弾・ダメージの数字</b> (大技の型の決まり・演出の中身は Game2DBattleFx / BossFx のまま)。
/// <b>エディタ専用</b> (Assets/Editor・製品に入らない)。 押せるのは Play の間だけ。
///
/// <para>押す所: メニュー <c>Tools/DICE BOUND/ボスの大技を流す/…</c> (ボスごと・もう一度・止める・速さ) と、 演出テストの窓 (<see cref="Game2DFxTestWindow"/>) の「通しで流す」。</para>
///
/// <para><b>汚さない工夫</b>:
/// <list type="bullet">
/// <item>演出は確認の口 <see cref="QaBattle"/> の Demo* (<c>Game2DBattleFx.BossCharge / BossHeavy</c>) で流す。 これは絵と音だけで、 戦闘の状態・HP・乱数 (GameRng)・保存には触らない。</item>
/// <item>戦闘中に押した時は、 今の戦闘の上で流す。 ただし判断待ち (手札を挿す番) で演出が止まっている時だけ始める
///   (解決の途中で流すと、 敵の攻撃の当たりの拍子 <c>EnemyHitLanded</c> を戦闘の画面が読むため)。</item>
/// <item>戦闘でない時は、 <b>タイトル (ランが無い) の時だけ</b>、 使い捨ての戦闘を <see cref="QaBattle.Start"/> で立ち上げてから流す
///   (セーブ抑止 <c>SaveGuard.Suppressed</c> を立てる・中断セーブ・メタ進行・設定・手引きの印を書かない)。
///   航行中などランがある時は、 ランの HP や武器を書き換えないよう、 始めずに断る (戦闘に入ってから押す)。</item>
/// </list></para>
/// </summary>
public static class BossHeavyPreview
{
    /// <summary>流せる大技 (表示名・演出の種類・ヴェスカの天与・使い捨ての戦闘で呼ぶ敵)。</summary>
    public struct Entry { public string label, kind, tenyo, enemyId; }

    public static readonly Entry[] Entries =
    {
        new Entry { label = "灰燼の王〈業火〉",            kind = "King",       tenyo = "none",   enemyId = BossIds.Layer6Ash },
        new Entry { label = "審判官",                       kind = "Judge",      tenyo = "none",   enemyId = BossIds.Layer5Judgment },
        new Entry { label = "シュヴァリエ",                 kind = "Chevalier",  tenyo = "none",   enemyId = BossIds.Layer5Hidden },
        new Entry { label = "ヴェスカ 3 段 天与の剣",       kind = "Vesca",      tenyo = "sword",  enemyId = BossIds.VescaP3 },
        new Entry { label = "ヴェスカ 3 段 天与の盾",       kind = "Vesca",      tenyo = "shield", enemyId = BossIds.VescaP3 },
        new Entry { label = "ヴェスカ 3 段 天与の指輪",     kind = "Vesca",      tenyo = "ring",   enemyId = BossIds.VescaP3 },
        new Entry { label = "ヴェスカ 時計 (天与なし)",     kind = "Vesca",      tenyo = "none",   enemyId = BossIds.VescaP3 },
        new Entry { label = "ヴェスカ 4 段 天与の剣",       kind = "VescaFinal", tenyo = "sword",  enemyId = BossIds.VescaFinal },
        new Entry { label = "ヴェスカ 4 段 天与の盾",       kind = "VescaFinal", tenyo = "shield", enemyId = BossIds.VescaFinal },
        new Entry { label = "ヴェスカ 4 段 天与の指輪",     kind = "VescaFinal", tenyo = "ring",   enemyId = BossIds.VescaFinal },
    };

    /// <summary>待機 (予兆のループ) を見せる長さ (秒・演出の時計の秒)。</summary>
    public static float IdleSeconds = 2.0f;
    /// <summary>着弾で出すダメージの数字。</summary>
    public const int Taken = 12;

    private enum Step { None, WaitBattle, Charge, WaitIdleLoop, Idle, Heavy, WaitDone }
    private static Step _step;
    private static Entry _cur; private static bool _hasLast;
    private static double _t0, _deadline;
    /// <summary>今の様子 (窓に出す)。</summary>
    public static string State = "待ち";

    private static Game2DBattleFx Fx => Object.FindObjectOfType<Game2DBattleFx>(true);
    private static Game2DBattleSim Sim => Object.FindObjectOfType<Game2DBattleSim>(true);

    /// <summary>i 番目の大技を通しで流す。 流せない時は理由を State と Console に出す。</summary>
    public static void Play(int i)
    {
        if (i < 0 || i >= Entries.Length) return;
        if (!Application.isPlaying) { Say("Play に入ってから押してください"); return; }
        _cur = Entries[i]; _hasLast = true;
        var gm = GameManager.Instance;
        bool inCombat = gm != null && gm.CurrentPhase == GameManager.GamePhase.Combat && Game2DBattleFx.Showing;
        if (!inCombat)
        {
            bool title = gm == null || gm.Run == null || gm.CurrentPhase == GameManager.GamePhase.Title || gm.CurrentPhase == GameManager.GamePhase.GameOver;
            if (!title) { Stop(); Say("航行中などランがある時は始めない (ランの HP・武器を書き換えないため)。 タイトルか戦闘の判断待ちで押してください"); return; }
            if (!QaBattle.Start(_cur.enemyId, "sword")) { Stop(); Say("使い捨ての戦闘を始められない: " + _cur.enemyId); return; }
            Say(_cur.label + ": 使い捨ての戦闘を立ち上げ中 (セーブ抑止)");
        }
        else Say(_cur.label + ": 今の戦闘の上で流す (判断待ちを待つ)");
        _step = Step.WaitBattle; _deadline = Now + 15.0;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    /// <summary>直前の大技をもう一度。</summary>
    public static void Again() { if (_hasLast) Play(System.Array.FindIndex(Entries, e => e.label == _cur.label)); else Say("まだ何も流していない"); }

    /// <summary>止める (待機を取り消す)。</summary>
    public static void Stop()
    {
        if (_step != Step.None && Application.isPlaying) QaBattle.DemoCancel();
        _step = Step.None; EditorApplication.update -= Tick;
    }

    private static double Now => EditorApplication.timeSinceStartup;
    private static void Say(string s) { State = s; Debug.Log("[ボスの大技を流す] " + s); }

    private static void Tick()
    {
        if (!Application.isPlaying) { _step = Step.None; EditorApplication.update -= Tick; return; }
        var fx = Fx; var sim = Sim;
        if (Now > _deadline) { Say(_cur.label + ": 待ちが長すぎるので止めた (段 " + _step + ")"); Stop(); return; }
        switch (_step)
        {
            case Step.WaitBattle:
                // 戦闘の画面が出て、 判断待ち (手札を挿す番) で演出が止まっている
                if (fx == null || sim == null || !Game2DBattleFx.Showing || Game2DLoadingScreen.Blocking) return;
                if (sim.phase != Game2DBattleSim.P.Wire || !fx.IsIdle) return;
                _step = Step.Charge; return;
            case Step.Charge:
                Demo(); QaBattle.DemoCharge();
                Say(_cur.label + ": チャージ"); _step = Step.WaitIdleLoop; _deadline = Now + 15.0; return;
            case Step.WaitIdleLoop:
                if (fx == null || !fx.BossIdling) return;
                Say(_cur.label + ": 待機 (予兆)"); _step = Step.Idle; _t0 = Now; _deadline = Now + 60.0; return;
            case Step.Idle:
                // 待機の長さは演出の時計で数える (速さ 1/2 なら倍の実時間)
                if (Now - _t0 < IdleSeconds / Mathf.Max(0.05f, Game2DBattleFx.QaSpeed)) return;
                _step = Step.Heavy; return;
            case Step.Heavy:
                Demo(); QaBattle.DemoHeavy(3, Taken);      // 待機を入ってよいコマまで回してから 溜め → 全画面 → 着弾
                Say(_cur.label + ": 大技 (溜め → 全画面 → 着弾)"); _step = Step.WaitDone; _deadline = Now + 60.0; return;
            case Step.WaitDone:
                if (fx == null || !fx.IsIdle) return;
                Say(_cur.label + ": 終わり (「もう一度」で繰り返す)"); _step = Step.None; EditorApplication.update -= Tick; return;
        }
    }

    /// <summary>演出のボスと天与を選び直す (演出テストの窓が開いていると、 窓の選択で毎回上書きされるため、 流す直前に入れる)。</summary>
    private static void Demo() { QaBattle.DemoBoss(_cur.kind); QaBattle.DemoTenyo(_cur.tenyo); }

    /// <summary>演出の速さ: 1 ⇔ 1/2 (戦闘の演出の時計だけ・Time.timeScale は触らない)。</summary>
    public static void ToggleHalf() { if (!Application.isPlaying) return; QaBattle.SetSpeed(Game2DBattleFx.QaSpeed < 0.99f ? 1f : 0.5f); Say("速さ ×" + Game2DBattleFx.QaSpeed); }

    // ── メニュー (Tools/DICE BOUND/ボスの大技を流す/…・Play の間だけ押せる) ──
    private const string Root = "Tools/DICE BOUND/ボスの大技を流す/";
    [MenuItem(Root + "灰燼の王〈業火〉", priority = 400)] private static void M0() => Play(0);
    [MenuItem(Root + "審判官", priority = 401)] private static void M1() => Play(1);
    [MenuItem(Root + "シュヴァリエ", priority = 402)] private static void M2() => Play(2);
    [MenuItem(Root + "ヴェスカ 3 段 天与の剣", priority = 403)] private static void M3() => Play(3);
    [MenuItem(Root + "ヴェスカ 3 段 天与の盾", priority = 404)] private static void M4() => Play(4);
    [MenuItem(Root + "ヴェスカ 3 段 天与の指輪", priority = 405)] private static void M5() => Play(5);
    [MenuItem(Root + "ヴェスカ 時計 (天与なし)", priority = 406)] private static void M6() => Play(6);
    [MenuItem(Root + "ヴェスカ 4 段 天与の剣", priority = 407)] private static void M7() => Play(7);
    [MenuItem(Root + "ヴェスカ 4 段 天与の盾", priority = 408)] private static void M8() => Play(8);
    [MenuItem(Root + "ヴェスカ 4 段 天与の指輪", priority = 409)] private static void M9() => Play(9);
    [MenuItem(Root + "もう一度", priority = 430)] private static void MAgain() => Again();
    [MenuItem(Root + "止める", priority = 431)] private static void MStop() => Stop();
    [MenuItem(Root + "速さ 1 ⇔ 1/2", priority = 432)] private static void MHalf() => ToggleHalf();

    [MenuItem(Root + "灰燼の王〈業火〉", true)] [MenuItem(Root + "審判官", true)] [MenuItem(Root + "シュヴァリエ", true)]
    [MenuItem(Root + "ヴェスカ 3 段 天与の剣", true)] [MenuItem(Root + "ヴェスカ 3 段 天与の盾", true)] [MenuItem(Root + "ヴェスカ 3 段 天与の指輪", true)]
    [MenuItem(Root + "ヴェスカ 時計 (天与なし)", true)] [MenuItem(Root + "ヴェスカ 4 段 天与の剣", true)] [MenuItem(Root + "ヴェスカ 4 段 天与の盾", true)]
    [MenuItem(Root + "ヴェスカ 4 段 天与の指輪", true)] [MenuItem(Root + "もう一度", true)] [MenuItem(Root + "止める", true)] [MenuItem(Root + "速さ 1 ⇔ 1/2", true)]
    private static bool CanPlay() => Application.isPlaying;
}
