// 本体の保存 (中断セーブのファイル・メタ進行の PlayerPrefs) が、 落ちた時・壊れた時に記録を失わないかを Unity なしで検算する。
//   GameLoop/SafeFile.cs (G-24) と MetaProgression/BackedPrefs.cs (G-25) をそのままコンパイルして動かす。
//   書き込みの途中で落ちるのは SafeFile.WriteAtomic の afterStep で例外を投げて再現する。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GameLoop;
using MetaProgression;

static class Program
{
    static int _fail, _ok;

    static void Check(bool cond, string what)
    {
        if (cond) _ok++;
        else { _fail++; Console.WriteLine("NG  " + what); }
    }

    sealed class Crash : Exception { }

    // 中断セーブの代役の検証: { で始まり } で終わり "v": を含めば使える
    static string Validate(string s) => s.StartsWith("{") && s.EndsWith("}") && s.Contains("\"v\":") ? null : "形式が壊れている";

    static string Rec(int v) => "{\"v\":" + v + ",\"name\":\"灰燼の王\"}";

    static int Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "save_check_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { Files(dir); Prefs(); }
        finally { try { Directory.Delete(dir, true); } catch { } }
        Console.WriteLine($"{_ok} 件 OK / {_fail} 件 NG");
        if (_fail == 0) Console.WriteLine("ALL OK");
        return _fail == 0 ? 0 : 1;
    }

    static string P(string dir) => Path.Combine(dir, "run_suspend.json");

    static void Clean(string path) { SafeFile.DeleteAll(path, out _); }

    static int ReadV(string path, out SafeFile.ReadResult res, out string used)
    {
        res = SafeFile.TryReadFirst(path, Validate, out var text, out used, out _, out _);
        if (res != SafeFile.ReadResult.Ok) return -1;
        int a = text.IndexOf("\"v\":") + 4, b = text.IndexOf(',', a);
        return int.Parse(text.Substring(a, b - a));
    }

    static void Files(string dir)
    {
        string path = P(dir), bak = SafeFile.BakOf(path), tmp = SafeFile.TmpOf(path);

        // 1. 初めて書く・上書きする: 本番だけが残る
        Clean(path);
        SafeFile.WriteAtomic(path, Rec(1));
        Check(File.Exists(path) && !File.Exists(bak) && !File.Exists(tmp), "初めて書いた後は本番だけ");
        SafeFile.WriteAtomic(path, Rec(2));
        Check(ReadV(path, out _, out var u2) == 2 && u2 == path && !File.Exists(bak) && !File.Exists(tmp), "上書きした後は本番 (新しい記録) だけ");

        // 2. BOM なしの UTF-8 (旧い File.WriteAllText と同じ)
        var bytes = File.ReadAllBytes(path);
        Check(!(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "BOM が付かない");
        Check(File.ReadAllText(path, Encoding.UTF8).Contains("灰燼の王"), "日本語がそのまま読める");

        // 3. 上書きの各手順の後で落ちる: どこで落ちても完全な記録が読める (旧い作りは「元を消した後・移す前」で読める物が無くなった)
        foreach (var step in new[] { "tmp", "bak", "move" })
        {
            Clean(path);
            SafeFile.WriteAtomic(path, Rec(10));
            try { SafeFile.WriteAtomic(path, Rec(11), s => { if (s == step) throw new Crash(); }); Check(false, $"{step} の後で落ちる再現"); }
            catch (Crash) { }
            int v = ReadV(path, out var res, out var used);
            Check(res == SafeFile.ReadResult.Ok && (v == 10 || v == 11), $"{step} の後で落ちても読める (読めた版 {v}・{Path.GetFileName(used ?? "-")})");
            if (step == "tmp") Check(v == 10 && used == path, "書きかけの後で落ちた: 本番 (前の記録) が無傷");
            if (step == "bak") Check(!File.Exists(path) && File.Exists(bak) && File.Exists(tmp) && v == 10 && used == bak,
                                     "控えへ退けた後で落ちた: 本番が無く、 控え (前の記録) から読む");
            if (step == "move") Check(v == 11 && used == path, "移した後で落ちた: 本番が新しい記録");
            // 落ちた後の状態から、 もう一度書ける (残り物で詰まらない)
            SafeFile.WriteAtomic(path, Rec(12));
            Check(ReadV(path, out _, out var u3) == 12 && u3 == path && !File.Exists(bak) && !File.Exists(tmp), $"{step} の後で落ちた状態から書き直せる");
        }

        // 4. 書きかけの途中で落ちた (切れた .tmp が残る)
        Clean(path);
        SafeFile.WriteAtomic(path, Rec(20));
        File.WriteAllText(tmp, "{\"v\":21,\"na");
        Check(ReadV(path, out _, out var u4) == 20 && u4 == path, "切れた書きかけがあっても本番を読む");
        Clean(path);
        File.WriteAllText(tmp, "{\"v\":21,\"na");
        ReadV(path, out var r5, out _);
        Check(r5 == SafeFile.ReadResult.Unusable, "初めての書き込みが切れた: 使えない (捨ててよい)");

        // 5. 本番が壊れていて控えがある → 控えから。 本番の名前へ戻すと残り物が消える
        Clean(path);
        File.WriteAllText(path, "{\"v\":3");          // 切れた本番
        File.WriteAllText(bak, Rec(30));
        int v6 = ReadV(path, out var r6, out var u6);
        Check(r6 == SafeFile.ReadResult.Ok && v6 == 30 && u6 == bak, "本番が壊れている時は控えから読む");
        Check(SafeFile.Promote(path, u6, out _) && ReadV(path, out _, out var u7) == 30 && u7 == path && !File.Exists(bak) && !File.Exists(tmp),
              "控えを本番の名前へ戻すと、 本番だけが残る");

        // 6. 本番が無く書きかけだけ (控えも無い) → 書きかけから
        Clean(path);
        File.WriteAllText(tmp, Rec(40));
        Check(ReadV(path, out _, out var u8) == 40 && u8 == tmp, "本番も控えも無い時は書きかけから読む");

        // 7. 版の違い (代役: 検証が通らない) は 3 つとも駄目なら Unusable
        Clean(path);
        File.WriteAllText(path, "{\"x\":1}"); File.WriteAllText(bak, "{\"x\":2}");
        ReadV(path, out var r9, out _);
        Check(r9 == SafeFile.ReadResult.Unusable, "どれも使えない時は Unusable");

        // 8. 消す時は全部消す (控えが残ると、 終わったランが次の起動で生き返る)
        Clean(path);
        SafeFile.WriteAtomic(path, Rec(50));
        try { SafeFile.WriteAtomic(path, Rec(51), s => { if (s == "bak") throw new Crash(); }); } catch (Crash) { }
        Check(SafeFile.AnyExists(path), "落ちた後は記録がある");
        Check(SafeFile.DeleteAll(path, out _) && !SafeFile.AnyExists(path), "消すと本番・控え・書きかけが全部消える");
        ReadV(path, out var r10, out _);
        Check(r10 == SafeFile.ReadResult.Missing, "消した後は Missing (生き返らない)");

        // 9. 何も無い
        Clean(path);
        ReadV(path, out var r11, out _);
        Check(r11 == SafeFile.ReadResult.Missing && !SafeFile.AnyExists(path), "何も無い時は Missing");
    }

    // ===== メタ進行 (PlayerPrefs の代役 = 辞書) =====

    sealed class DictStore : IPrefsStore
    {
        public readonly Dictionary<string, string> D = new Dictionary<string, string>();
        public int Writes;
        public bool HasKey(string k) => D.ContainsKey(k);
        public string GetString(string k) => D.TryGetValue(k, out var v) ? v : "";
        public void SetString(string k, string v) { D[k] = v; Writes++; }
        public void DeleteKey(string k) => D.Remove(k);
    }

    const string K = "MetaProgressState_v1";
    static bool Valid(string s) => s.Contains("\"tokens\":");
    static string St(int t) => "{\"tokens\":" + t + "}";

    static void Prefs()
    {
        // 1. 初回 (鍵が無い): 空で始める・何も書かない・控えは見ない
        var s = new DictStore();
        s.D[K + BackedPrefs.BakSuffix] = St(999);       // 消した後に控えだけ残っていても
        var src = BackedPrefs.Load(s, K, Valid, out var json, out var note);
        Check(src == BackedPrefs.LoadSource.Empty && json == null && note == null && s.Writes == 0, "鍵が無い時は空 (控えから古い値を戻さない)");

        // 2. 読める本番
        s = new DictStore();
        BackedPrefs.Save(s, K, St(1), Valid);
        Check(s.D[K] == St(1) && !s.HasKey(K + BackedPrefs.BakSuffix), "初めての保存は控えを作らない");
        BackedPrefs.Save(s, K, St(2), Valid);
        Check(s.D[K] == St(2) && s.D[K + BackedPrefs.BakSuffix] == St(1), "2 回目の保存で 1 つ前の値が控えに");
        BackedPrefs.Save(s, K, St(2), Valid);
        Check(s.D[K + BackedPrefs.BakSuffix] == St(1), "同じ値の保存では控えを変えない");
        src = BackedPrefs.Load(s, K, Valid, out json, out _);
        Check(src == BackedPrefs.LoadSource.Main && json == St(2), "読める本番はそのまま読む");

        // 3. 本番が壊れていて控えが読める → 控えから・壊れた値は退避
        s.D[K] = "{\"tokens\":3";   // 途中で切れた
        src = BackedPrefs.Load(s, K, Valid, out json, out note);
        Check(src == BackedPrefs.LoadSource.Backup && json == St(1) && s.D[K + BackedPrefs.CorruptSuffix] == "{\"tokens\":3" && note != null,
              "本番が壊れている時は控えから・壊れた値は .corrupt へ");
        BackedPrefs.Save(s, K, St(5), Valid);
        Check(s.D[K] == St(5) && s.D[K + BackedPrefs.BakSuffix] == St(1), "壊れた値は控えへ写さない (控えまで壊さない)");

        // 4. 本番も控えも駄目 → 空で始める・壊れた値は退避 (次の保存で失わない)
        s = new DictStore();
        s.D[K] = "garbage"; s.D[K + BackedPrefs.BakSuffix] = "{\"x\":1}";
        src = BackedPrefs.Load(s, K, Valid, out json, out note);
        Check(src == BackedPrefs.LoadSource.CorruptReset && json == null && s.D[K + BackedPrefs.CorruptSuffix] == "garbage" && note != null,
              "本番も控えも駄目なら空・壊れた値は .corrupt へ");
        BackedPrefs.Save(s, K, St(7), Valid);
        Check(s.D[K] == St(7) && s.D[K + BackedPrefs.CorruptSuffix] == "garbage", "空で始めた後の保存でも壊れた値は残る");

        // 5. 鍵はあるのに空文字 → 壊れている扱い (初回と区別する)
        s = new DictStore();
        s.D[K] = ""; s.D[K + BackedPrefs.BakSuffix] = St(8);
        src = BackedPrefs.Load(s, K, Valid, out json, out _);
        Check(src == BackedPrefs.LoadSource.Backup && json == St(8), "鍵があって空文字の時は控えから");

        // 6. 検証が例外を出しても壊れている扱い
        s = new DictStore();
        s.D[K] = St(9);
        src = BackedPrefs.Load(s, K, _ => throw new InvalidOperationException(), out json, out _);
        Check(src == BackedPrefs.LoadSource.CorruptReset, "検証の例外は読めない扱い");

        // 7. やり直し: 本番と控えを消す・.corrupt は残す
        s = new DictStore();
        s.D[K] = St(1); s.D[K + BackedPrefs.BakSuffix] = St(0); s.D[K + BackedPrefs.CorruptSuffix] = "x";
        BackedPrefs.DeleteAll(s, K);
        Check(!s.HasKey(K) && !s.HasKey(K + BackedPrefs.BakSuffix) && s.HasKey(K + BackedPrefs.CorruptSuffix), "やり直しは本番と控えを消す");
        src = BackedPrefs.Load(s, K, Valid, out _, out _);
        Check(src == BackedPrefs.LoadSource.Empty, "やり直した後は初回と同じ (空)");
    }
}
