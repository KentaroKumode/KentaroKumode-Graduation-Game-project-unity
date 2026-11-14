using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UI.Game2D.Battle;

/// <summary>
/// 演出テストの窓 (2026-11-14・作者の依頼「実機テストのために、 こっちで任意の敵エフェクトを起動できるテストがほしい」)。
/// メニュー Tools/DICE BOUND/演出テスト。 Play の間だけ押せる。 中身は確認の口 <see cref="QaBattle"/> を呼ぶだけ
/// (戦闘に入る口は必ずセーブ抑止 GameLoop.SaveGuard を立てる)。 シーンは編集しない・保存しない。 使い方は docs/qa/battle-fx.md。
/// </summary>
public class Game2DFxTestWindow : EditorWindow
{
    private const string FxDir = "Assets/Resources/Game2DFx";
    private static readonly string[] Families = { "sword", "axe", "dagger", "shield", "none" };
    private static readonly string[] BossKinds = { "King", "Judge", "Chevalier", "Vesca", "VescaFinal" };
    private static readonly string[] Cats = { "全部", "大技", "通常の技", "武器", "その他" };
    private static readonly string[] Relics = { "攻撃", "防御", "妨害" };
    private static readonly float[] Speeds = { 1f, 0.5f, 0.25f };

    private string[] _enemies = new string[0];
    private int _enemy, _family, _boss, _cat, _relic, _attackFamily, _speed;
    private int _hp = QaBattle.DefaultHp;
    private bool _crit, _paused;
    private string _search = "", _selected, _status = "", _log = "", _gui = "", _last = "";
    private List<string> _sheets = new List<string>();
    private Vector2 _scrollList, _scrollAll;
    private double _nextRefresh;

    [MenuItem("Tools/DICE BOUND/演出テスト")]
    private static void Open() => GetWindow<Game2DFxTestWindow>("演出テスト");

    private void OnEnable() { EditorApplication.playModeStateChanged += OnPlayMode; RefreshSheets(); }
    private void OnDisable() { EditorApplication.playModeStateChanged -= OnPlayMode; ResetSpeed(); }
    private void OnPlayMode(PlayModeStateChange s) { if (s == PlayModeStateChange.ExitingPlayMode || s == PlayModeStateChange.EnteredEditMode) { ResetSpeed(); _paused = false; _speed = 0; } }
    private static void ResetSpeed() { if (Application.isPlaying) QaBattle.SetSpeed(1f); }

    /// <summary>Resources/Game2DFx の絵 (json と png.bytes の組) を全部並べる。 絵を置くだけで一覧に出る。</summary>
    private void RefreshSheets()
    {
        _sheets.Clear();
        if (!Directory.Exists(FxDir)) return;
        foreach (var f in Directory.GetFiles(FxDir, "*.json"))
        {
            string n = Path.GetFileNameWithoutExtension(f);
            if (File.Exists(Path.Combine(FxDir, n + ".png.bytes"))) _sheets.Add(n);
        }
        _sheets.Sort(System.StringComparer.Ordinal);
    }

    private static int CatOf(string n)
    {
        if (n.StartsWith("move-")) return 2;
        if (n.StartsWith("weapon-")) return 3;
        if (n.StartsWith("gouka-") || n.StartsWith("judge-") || n.StartsWith("chevalier-") || n.StartsWith("vesca-")) return 1;
        return 4;
    }

    private void Update()
    {
        if (!Application.isPlaying || EditorApplication.timeSinceStartup < _nextRefresh) return;
        _nextRefresh = EditorApplication.timeSinceStartup + 0.2;
        if (_enemies.Length == 0)
        {
            var ids = CombatSystem.EnemyDatabase.AllIds();
            var bosses = ids.Where(GameLoop.BossIds.IsBoss).OrderBy(x => x, System.StringComparer.Ordinal);
            var foes = ids.Where(x => !GameLoop.BossIds.IsBoss(x)).OrderBy(x => x, System.StringComparer.Ordinal);
            _enemies = bosses.Concat(foes).ToArray();
            int k = System.Array.IndexOf(_enemies, GameLoop.BossIds.Layer6Ash); if (k >= 0) _enemy = k;
        }
        string st = QaBattle.Status();
        int g = st.IndexOf("gui=[");
        _status = g > 0 ? st.Substring(0, g).Replace(" ", "\n") : st.Replace(" ", "\n");
        _gui = g > 0 ? st.Substring(g) : "";
        _log = QaBattle.MoveLog().Replace(" | ", "\n");
        Repaint();
    }

    private void OnGUI()
    {
        // セーブ抑止 (立っていない時は赤)
        bool guard = GameLoop.SaveGuard.Suppressed;
        var prev = GUI.color; GUI.color = guard ? new Color(0.6f, 1f, 0.6f) : new Color(1f, 0.45f, 0.45f);
        EditorGUILayout.HelpBox(guard ? "セーブ抑止: 立っている (この Play ではセーブを書かない)" : "セーブ抑止: 立っていない (「開始」を押すと立つ)", guard ? MessageType.Info : MessageType.Warning);
        GUI.color = prev;
        if (!Application.isPlaying) { EditorGUILayout.HelpBox("Play に入ってください。", MessageType.Info); return; }

        _scrollAll = EditorGUILayout.BeginScrollView(_scrollAll);

        // ── 戦闘に入る ──
        EditorGUILayout.LabelField("戦闘に入る", EditorStyles.boldLabel);
        if (_enemies.Length > 0) _enemy = EditorGUILayout.Popup("敵", _enemy, _enemies);
        _family = EditorGUILayout.Popup("武器の家系", _family, Families);
        _hp = EditorGUILayout.IntField("HP (最大と今)", _hp);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("開始 (ランが無ければ始める)") && _enemies.Length > 0) _last = "Start → " + QaBattle.Start(_enemies[_enemy], Families[_family], _hp);
            if (GUILayout.Button("敵・武器を替えて始め直す") && _enemies.Length > 0) _last = "Restart → " + QaBattle.Restart(_enemies[_enemy], Families[_family], _hp);
        }

        // ── 流れで進める ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("流れで進める (本物の戦闘の経路)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("1 ターン進める")) _last = "Go → " + QaBattle.Go();
            if (GUILayout.Button("会心で進める")) _last = "GoCrit → " + QaBattle.GoCrit();
            if (GUILayout.Button("次のターンをチャージに")) _last = "ChargeNextTurn → " + QaBattle.ChargeNextTurn() + " (1 = 次のターン・2 以上 = その数 − 1 回進めてから押し直す・−1 = 大技なし)";
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            _relic = EditorGUILayout.Popup("ヴェスカの次の遺物", _relic, Relics);
            if (GUILayout.Button("指定", GUILayout.Width(60))) _last = "ForceRelic → " + QaBattle.ForceRelic(Relics[_relic]);
        }

        // ── ボスの大技を直接流す ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("ボスの大技 (戦闘の流れを通さない)", EditorStyles.boldLabel);
        _boss = EditorGUILayout.Popup("種類", _boss, BossKinds);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("チャージ")) { QaBattle.DemoBoss(BossKinds[_boss]); QaBattle.DemoCharge(); _last = "チャージ " + BossKinds[_boss]; }
            if (GUILayout.Button("大技")) { QaBattle.DemoBoss(BossKinds[_boss]); QaBattle.DemoHeavy(); _last = "大技 " + BossKinds[_boss]; }
            if (GUILayout.Button("取り消し")) { QaBattle.DemoCancel(); _last = "取り消し"; }
        }

        // ── 武器 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("武器の攻撃", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _attackFamily = EditorGUILayout.Popup(_attackFamily, Families);
            _crit = GUILayout.Toggle(_crit, "会心", GUILayout.Width(50));
            if (GUILayout.Button("攻撃")) { string f = Families[_attackFamily]; QaBattle.DemoAttack(f == "none" ? null : f, _crit); _last = "攻撃 " + f + (_crit ? " 会心" : ""); }
        }

        // ── 絵の一覧 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("絵を 1 枚流す (Resources/Game2DFx)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            _search = EditorGUILayout.TextField("検索", _search);
            if (GUILayout.Button("一覧を読み直す", GUILayout.Width(110))) RefreshSheets();
        }
        _cat = GUILayout.Toolbar(_cat, Cats);
        _scrollList = EditorGUILayout.BeginScrollView(_scrollList, GUILayout.Height(160));
        foreach (var n in _sheets)
        {
            if (_cat != 0 && CatOf(n) != _cat) continue;
            if (_search.Length > 0 && n.IndexOf(_search, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
            if (GUILayout.Toggle(_selected == n, n, EditorStyles.radioButton)) _selected = n;
        }
        EditorGUILayout.EndScrollView();
        using (new EditorGUI.DisabledScope(_selected == null))
            if (GUILayout.Button("再生" + (_selected != null ? " : " + _selected : ""))) PlaySelected();

        // ── 見やすさ ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("速さ (戦闘の演出の時計だけ・Time.timeScale は触らない)", EditorStyles.boldLabel);
        using (new EditorGUILayout.HorizontalScope())
        {
            int sp = GUILayout.Toolbar(_speed, new[] { "1 倍", "0.5 倍", "0.25 倍" });
            if (sp != _speed) { _speed = sp; if (!_paused) QaBattle.SetSpeed(Speeds[_speed]); }
            bool p = GUILayout.Toggle(_paused, "一時停止", "Button", GUILayout.Width(80));
            if (p != _paused) { _paused = p; QaBattle.SetSpeed(_paused ? 0f : Speeds[_speed]); }
            using (new EditorGUI.DisabledScope(!_paused))
                if (GUILayout.Button("1 コマ送り", GUILayout.Width(80))) QaBattle.StepFrame();
        }

        // ── 今の状態 ──
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("今の状態 (0.2 秒おき)", EditorStyles.boldLabel);
        if (_last.Length > 0) EditorGUILayout.LabelField("直前の操作: " + _last, EditorStyles.wordWrappedLabel);
        EditorGUILayout.TextArea(_status, GUILayout.MinHeight(60));
        EditorGUILayout.LabelField("GUI に掛かっている値", EditorStyles.miniBoldLabel);
        EditorGUILayout.TextArea(_gui);
        EditorGUILayout.LabelField("流した絵の履歴 (MoveLog)", EditorStyles.miniBoldLabel);
        EditorGUILayout.TextArea(_log, GUILayout.MinHeight(60));
        EditorGUILayout.EndScrollView();
    }

    /// <summary>通常の技は DemoMove・武器は DemoAttack (家系と会心は名前の weapon-家系-normal/crit から)・それ以外は基準点に 1 回 (ループは 4 秒)。</summary>
    private void PlaySelected()
    {
        var n = _selected;
        if (n.StartsWith("move-")) { QaBattle.DemoMove(n); _last = "DemoMove " + n; return; }
        if (n.StartsWith("weapon-"))
        {
            var parts = n.Split('-');      // 演出の絵の名前 (書き出しの道具が付ける・品の id ではない)
            if (parts.Length == 3) { QaBattle.DemoAttack(parts[1], parts[2] == "crit"); _last = "DemoAttack " + n; return; }
        }
        QaBattle.DemoSheet(n); _last = "DemoSheet " + n;
    }
}
