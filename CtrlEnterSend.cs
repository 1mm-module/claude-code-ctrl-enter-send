using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

// Claude デスクトップアプリの Code タブの入力欄で、Enter を「変換の確定だけ」に、Ctrl+Enter を送信にする常駐キーフック。
// 前面のウィンドウが Claude アプリで、焦点が Code タブのプロンプト入力欄にある時だけ働く。
//   Enter（修飾キーなし） → Ctrl+M（IME の確定キー。変換中は確定し、変換していない時は何も起きない）
//   Ctrl+Enter           → Enter（送信）
//   Shift+Enter          → そのまま（改行）
public static class CtrlEnterSend
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int VK_RETURN = 0x0D, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    const int VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_M = 0x4D;
    const uint LLKHF_INJECTED = 0x10;
    const uint INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2;
    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // このプログラムが送るキーの目印（フックで再処理しない）
    static readonly IntPtr OwnTag = new IntPtr(0x43454E44);
    // 自動テストが送るキーの目印（実際のキー入力と同じく処理する）
    public static readonly IntPtr TestTag = new IntPtr(0x43455453);

    // 対象を Claude アプリ以外に変える時だけ設定する（実行ファイルのパスの末尾、小文字）
    public static string[] OverrideSuffixes;
    public static string LogPath;
    // false にすると焦点の判定をせず、対象アプリのすべての Enter を置き換える（自動テスト用）
    public static bool RequireCodePrompt = true;
    // 焦点の移動をすべて記録する（調査用）
    public static bool VerboseLog;

    // 対象アプリの中で最後に焦点が当たった要素が Code タブのプロンプト入力欄か
    static volatile bool codePromptFocused;
    static readonly Dictionary<int, bool> targetPids = new Dictionary<int, bool>();

    static bool paused;
    static bool swallowEnterUp;
    static IntPtr hook = IntPtr.Zero;
    // フックの関数は GC に回収されないよう静的に保持する
    static readonly LowLevelKeyboardProc proc = HookProc;
    static NotifyIcon tray;
    static ToolStripMenuItem pauseItem;
    static Icon activeIcon, pausedIcon;

    public static void Run()
    {
        hook = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(null), 0);
        if (hook == IntPtr.Zero)
            throw new InvalidOperationException("キーフックを設定できませんでした。エラー番号 " + Marshal.GetLastWin32Error());

        activeIcon = MakeIcon(Color.FromArgb(217, 119, 87));
        pausedIcon = MakeIcon(Color.Gray);
        pauseItem = new ToolStripMenuItem("一時停止", null, delegate { TogglePause(); });
        var menu = new ContextMenuStrip();
        menu.Items.Add(pauseItem);
        menu.Items.Add(new ToolStripMenuItem("終了", null, delegate { Application.Exit(); }));
        tray = new NotifyIcon();
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += delegate { TogglePause(); };
        UpdateTray();
        tray.Visible = true;
        if (RequireCodePrompt) StartFocusWatch();
        Log("開始");

        try { Application.Run(); }
        finally
        {
            UnhookWindowsHookEx(hook);
            tray.Visible = false;
            tray.Dispose();
            Log("終了");
        }
    }

    static void TogglePause()
    {
        paused = !paused;
        UpdateTray();
        Log(paused ? "一時停止" : "再開");
    }

    static void UpdateTray()
    {
        tray.Icon = paused ? pausedIcon : activeIcon;
        tray.Text = paused ? "Ctrl+Enter 送信：一時停止中" : "Ctrl+Enter 送信：有効";
        pauseItem.Text = paused ? "再開" : "一時停止";
    }

    static Icon MakeIcon(Color color)
    {
        using (var bmp = new Bitmap(16, 16))
        {
            using (var g = Graphics.FromImage(bmp))
            using (var brush = new SolidBrush(color))
            using (var pen = new Pen(Color.White, 2))
            {
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                g.FillEllipse(brush, 0, 0, 15, 15);
                // 改行記号の形
                g.DrawLines(pen, new[] { new Point(11, 4), new Point(11, 9), new Point(5, 9) });
                g.DrawLines(pen, new[] { new Point(7, 6), new Point(4, 9), new Point(7, 12) });
            }
            return Icon.FromHandle(bmp.GetHicon());
        }
    }

    static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(lParam, typeof(KBDLLHOOKSTRUCT));
            bool injected = (k.flags & LLKHF_INJECTED) != 0;
            if (k.vkCode == VK_RETURN && (!injected || k.dwExtraInfo == TestTag))
            {
                int msg = wParam.ToInt32();
                if (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN)
                {
                    if (!paused && (!RequireCodePrompt || codePromptFocused) && IsTargetForeground())
                    {
                        bool ctrl = IsDown(VK_CONTROL), shift = IsDown(VK_SHIFT), alt = IsDown(VK_MENU);
                        bool win = IsDown(VK_LWIN) || IsDown(VK_RWIN);
                        if (ctrl && !shift && !alt && !win)
                        {
                            swallowEnterUp = true;
                            SendSubmit();
                            Log("Ctrl+Enter を Enter に置き換え");
                            return (IntPtr)1;
                        }
                        if (!ctrl && !shift && !alt && !win)
                        {
                            swallowEnterUp = true;
                            SendCommit();
                            Log("Enter を Ctrl+M に置き換え");
                            return (IntPtr)1;
                        }
                    }
                }
                else if ((msg == WM_KEYUP || msg == WM_SYSKEYUP) && swallowEnterUp)
                {
                    swallowEnterUp = false;
                    return (IntPtr)1;
                }
            }
        }
        return CallNextHookEx(hook, nCode, wParam, lParam);
    }

    // 押されている Ctrl を一度離して Enter を送り、Ctrl を押し直す
    static void SendSubmit()
    {
        bool l = IsDown(VK_LCONTROL), r = IsDown(VK_RCONTROL);
        var list = new System.Collections.Generic.List<INPUT>();
        if (l) list.Add(Key(VK_LCONTROL, true));
        if (r) list.Add(Key(VK_RCONTROL, true));
        list.Add(Key(VK_RETURN, false));
        list.Add(Key(VK_RETURN, true));
        if (l) list.Add(Key(VK_LCONTROL, false));
        if (r) list.Add(Key(VK_RCONTROL, false));
        Send(list.ToArray());
    }

    static void SendCommit()
    {
        Send(new[] { Key(VK_LCONTROL, false), Key(VK_M, false), Key(VK_M, true), Key(VK_LCONTROL, true) });
    }

    static INPUT Key(int vk, bool up)
    {
        var input = new INPUT();
        input.type = INPUT_KEYBOARD;
        input.u.ki.wVk = (ushort)vk;
        input.u.ki.wScan = (ushort)MapVirtualKey((uint)vk, 0);
        input.u.ki.dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (vk == VK_RCONTROL ? KEYEVENTF_EXTENDEDKEY : 0);
        input.u.ki.dwExtraInfo = OwnTag;
        return input;
    }

    static void Send(INPUT[] inputs)
    {
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
    }

    static bool IsDown(int vk)
    {
        return (GetAsyncKeyState(vk) & 0x8000) != 0;
    }

    // 焦点の移動を UI オートメーションで見張る。キーごとに調べると文字の順序が入れ替わるため、移動時に判定して覚えておく
    static void StartFocusWatch()
    {
        var t = new Thread(delegate ()
        {
            Automation.AddAutomationFocusChangedEventHandler(OnFocusChanged);
            focusSignal.Set();
            FocusCheckLoop();
        });
        t.SetApartmentState(ApartmentState.MTA);
        t.IsBackground = true;
        t.Start();
    }

    // 通知で渡される要素は親をたどれないことがあるため、少し待ってから今の焦点を取り直して判定する
    static readonly AutoResetEvent focusSignal = new AutoResetEvent(false);

    static void OnFocusChanged(object sender, AutomationFocusChangedEventArgs e)
    {
        focusSignal.Set();
    }

    static void FocusCheckLoop()
    {
        while (true)
        {
            focusSignal.WaitOne();
            // 続けて届く通知をまとめる
            while (focusSignal.WaitOne(80)) { }
            try { UpdateFocus(AutomationElement.FocusedElement); }
            catch (Exception) { }
        }
    }

    static void UpdateFocus(AutomationElement el)
    {
        if (el == null) return;
        // 他のアプリへの焦点の移動では変えない（Claude アプリに戻った時は前面の判定で絞る）
        if (!IsTargetPid(el.Current.ProcessId)) return;
        bool now = IsCodePrompt(el);
        if (VerboseLog)
        {
            string cls = el.Current.ClassName ?? "";
            if (cls.Length > 60) cls = cls.Substring(0, 60);
            Log("焦点の移動：" + el.Current.ControlType.ProgrammaticName + " 名前[" + el.Current.Name + "] クラス[" + cls + "] 判定[" + now + "]");
        }
        if (now != codePromptFocused)
        {
            codePromptFocused = now;
            Log(now ? "焦点：Code タブの入力欄" : "焦点：Code タブの入力欄以外");
        }
    }

    // 入力用の要素で、親に Code タブの目印があり、ページ名が「… - Claude Code」のものだけを対象にする。
    // 同じ入力欄の2回目の通知ではクラス名が空になることがあるため、要素の種類で判定する
    static bool IsCodePrompt(AutomationElement el)
    {
        string cls = el.Current.ClassName ?? "";
        if (el.Current.ControlType != ControlType.Edit && !cls.Contains("ProseMirror")) return false;
        bool inPrompt = false;
        var walker = TreeWalker.ControlViewWalker;
        var a = walker.GetParent(el);
        for (int i = 0; a != null && i < 40; i++)
        {
            var c = a.Current;
            if ((c.ClassName ?? "").Contains("epitaxy-prompt-input")) inPrompt = true;
            if (c.ControlType == ControlType.Document)
                return inPrompt && (c.Name ?? "").EndsWith("Claude Code");
            a = walker.GetParent(a);
        }
        return false;
    }

    static bool IsTargetPid(int pid)
    {
        lock (targetPids)
        {
            bool known;
            if (targetPids.TryGetValue(pid, out known)) return known;
            string path = ProcessPath((uint)pid);
            bool result = path != null && IsTargetPath(path.ToLowerInvariant());
            targetPids[pid] = result;
            return result;
        }
    }

    static bool IsTargetForeground()
    {
        string path = ForegroundProcessPath();
        if (path == null) return false;
        return IsTargetPath(path.ToLowerInvariant());
    }

    static bool IsTargetPath(string path)
    {
        if (OverrideSuffixes != null)
        {
            foreach (var s in OverrideSuffixes)
                if (path.EndsWith(s.ToLowerInvariant())) return true;
            return false;
        }
        if (!path.EndsWith("\\claude.exe")) return false;
        // ストア版と通常のインストール版。Claude Code の CLI（同じ claude.exe の名前）は対象外
        return path.Contains("\\windowsapps\\claude_") || path.Contains("\\anthropicclaude\\");
    }

    static string ForegroundProcessPath()
    {
        IntPtr hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        uint pid;
        GetWindowThreadProcessId(hwnd, out pid);
        return ProcessPath(pid);
    }

    static string ProcessPath(uint pid)
    {
        IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var sb = new StringBuilder(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally { CloseHandle(h); }
    }

    static void Log(string text)
    {
        if (LogPath == null) return;
        try { File.AppendAllText(LogPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + Environment.NewLine, Encoding.UTF8); }
        catch (IOException) { }
    }

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode, scanCode, flags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct INPUT { public uint type; public InputUnion u; }

    [StructLayout(LayoutKind.Explicit)]
    struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }

    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")]
    static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
    [DllImport("user32.dll")]
    static extern uint MapVirtualKey(uint uCode, uint uMapType);
    [DllImport("user32.dll")]
    static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("kernel32.dll")]
    static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr h);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref int size);
}
