using GameLoop;
using UnityEngine;

namespace UI.Game2D
{
    /// <summary>
    /// 中断したランの窓 (issue #84・<b>仮置き</b>・docs/design/gui-needed.md #41)。 共通の板のモーダル (<see cref="Game2DModal"/>) に出す。
    ///
    /// <para>出るのは<b>タイトルで「はじめる」を押した時、 中断セーブがある場合</b>だけ (<see cref="TitleMenuRouter"/> が呼ぶ)。
    /// 「続きから」= 再開 (セーブは消える・再開は 1 回きり) ／ 「新しく始める」= 確かめてからセーブを消し、 いつもの職業選択へ ／ 「やめる」= 閉じる。
    /// 中断する側の口は設定の窓 (Esc・航行のマップで) の「中断して終える」(<see cref="Game2DSettingsPanel"/>)。</para>
    ///
    /// <para>タイトルにはまだ専用のボタンの絵が無いので、 既存の「はじめる」に相乗りしている (絵は足さない)。</para>
    /// </summary>
    public class Game2DResumePanel : MonoBehaviour
    {
        public Game2DModal modal;

        private static Game2DResumePanel _inst;
        private ScreenDef _def;
        private bool _confirmNew, _lockedInput, _prevLock;
        private System.Action _startNew;
        private RunSave.Data _data;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Attach()
        {
            var m = FindObjectOfType<Game2DModal>();
            if (m != null && m.GetComponent<Game2DResumePanel>() == null) m.gameObject.AddComponent<Game2DResumePanel>().modal = m;
        }

        private void Awake() { _inst = this; }
        private void OnDestroy() { if (_inst == this) _inst = null; Unlock(); }

        public bool IsShown => modal != null && _def != null && modal.IsOpen && modal.Current == _def;

        /// <summary>中断セーブがあれば窓を出して true (呼んだ側はラン開始を止める)。 無い・読めない時は false (いつもどおり始める)。
        /// <paramref name="startNew"/> は「新しく始める」を確かめた後に呼ぶ (職業選択へ進む口)。</summary>
        public static bool TryOffer(System.Action startNew)
        {
            if (_inst == null || _inst.modal == null || _inst.modal.IsOpen) return false;
            if (!RunSave.Exists) return false;
            if (!RunSave.TryRead(out var data, out var why))
            {
                Debug.LogWarning($"[Game2DResumePanel] 中断セーブを使えない: {why}");
                return false;      // 読めないセーブは TryRead が捨てている。 いつもどおり新しく始める
            }
            _inst.Open(data, startNew);
            return true;
        }

        private void Open(RunSave.Data data, System.Action startNew)
        {
            _data = data; _startNew = startNew; _confirmNew = false;
            if (!_lockedInput) { _prevLock = SpriteButton.InputLocked; SpriteButton.InputLocked = true; _lockedInput = true; }   // 後ろのタイトルのボタンを押さない
            _def = Build(); modal.Open(_def);
        }

        private void Update()
        {
            if (modal == null) return;
            if (_def != null && !IsShown) { _def = null; Unlock(); }      // ほかの画面が窓を取った・閉じた
            if (IsShown && Input.GetKeyDown(KeyCode.Escape)) { if (_confirmNew) { _confirmNew = false; Rebuild(); } else Close(); }
        }

        private void Close()
        {
            if (IsShown) modal.Close();
            _def = null; _data = null; Unlock();
        }

        private void Unlock() { if (_lockedInput) { SpriteButton.InputLocked = _prevLock; _lockedInput = false; } }

        private void Rebuild() { _def = Build(); modal.Replace(_def); }

        private static string ClassName(int cls)
        {
            foreach (var e in ClassSelect.ClassDossierData.Entries) if ((int)e.type == cls) return e.className;
            return "";
        }

        private static string SavedAt(string iso)
        {
            if (System.DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out var t))
                return t.ToLocalTime().ToString("yyyy/MM/dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            return "";
        }

        private ScreenDef Build()
        {
            var d = new ScreenDef { id = "resume_panel", jp = true, w = 360 };
            var s = _data;
            if (!_confirmNew)
            {
                d.title = "中断したラン";
                d.sub = "保存 " + SavedAt(s.savedAtUtc);
                d.body = "第 " + s.sumFloor + " 層 ／ " + ClassName(s.sumClass) + " ／ HP " + s.sumHp + "/" + s.sumMaxHp + " ／ 所持金 " + s.sumCoins + "G"
                       + (s.sumDifficulty > 0 ? " ／ 難易度 " + s.sumDifficulty : "")
                       + "\n\n続きから再開すると、 この記録は消える。";
                d.bodyLines = 4;
                d.buttons.Add(new ModalBtn { label = "続きから", action = "resume", primary = true });
                d.buttons.Add(new ModalBtn { label = "新しく始める", action = "new" });
                d.buttons.Add(new ModalBtn { label = "やめる", action = "close" });
                d.footer = "Esc で閉じる";
            }
            else
            {
                d.title = "新しく始める";
                d.body = "中断したランの記録を消して、 新しいランを始める。\n消した記録は戻せない。";
                d.bodyLines = 3;
                d.buttons.Add(new ModalBtn { label = "消して始める", action = "new_ok", primary = true });
                d.buttons.Add(new ModalBtn { label = "戻る", action = "back" });
                d.footer = "Esc で戻る";
            }
            d.onAction = (a, _) =>
            {
                switch (a)
                {
                    case "close": Close(); return;
                    case "new": _confirmNew = true; Rebuild(); return;
                    case "back": _confirmNew = false; Rebuild(); return;
                    case "new_ok":
                    {
                        var start = _startNew;
                        Close();
                        RunSave.Delete();
                        start?.Invoke();
                        return;
                    }
                    case "resume":
                    {
                        var gm = GameManager.Instance;
                        string why = "ゲームの準備ができていない";
                        if (gm != null && gm.ResumeFromSave(out why)) { Close(); return; }
                        Debug.LogWarning($"[Game2DResumePanel] 再開できない: {why}");
                        // 読めなかった記録は消えている。 理由だけ見せて閉じる (「はじめる」をもう一度押せば新しく始まる)
                        var err = new ScreenDef { id = "resume_panel", jp = true, w = 360, title = "再開できない", body = why + (RunSave.Exists ? "" : "\n記録は消えた。"), bodyLines = 3 };
                        err.buttons.Add(new ModalBtn { label = "閉じる", action = "close", primary = true });
                        err.onAction = (a2, __) => Close();
                        _def = err; modal.Replace(err);
                        return;
                    }
                }
            };
            return d;
        }
    }
}
