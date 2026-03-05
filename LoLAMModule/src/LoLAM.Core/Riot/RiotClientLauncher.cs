using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace LoLAM.Core.Riot;

/// <summary>
/// Launches the Riot Client and auto-fills credentials by targeting the client window directly.
/// </summary>
public static class RiotClientLauncher
{
    private static readonly string[] KnownPaths =
    {
        @"C:\Riot Games\Riot Client\RiotClientServices.exe",
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                     @"Riot Games\Riot Client\RiotClientServices.exe"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     @"Riot Games\Riot Client\RiotClientServices.exe"),
    };

    public static string? FindRiotClientPath()
    {
        foreach (var p in KnownPaths)
            if (File.Exists(p))
                return p;

        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Uninstall\Riot Game league_of_legends.live");

            var installLocation = key?.GetValue("InstallLocation") as string;
            if (!string.IsNullOrWhiteSpace(installLocation))
            {
                var riotGamesRoot = Path.GetDirectoryName(installLocation);
                if (riotGamesRoot is not null)
                {
                    var candidate = Path.Combine(riotGamesRoot, "Riot Client", "RiotClientServices.exe");
                    if (File.Exists(candidate))
                        return candidate;
                }
            }
        }
        catch { }

        return null;
    }

    public static async Task<bool> LaunchAsync(string? username = null, string? password = null)
    {
        var exePath = FindRiotClientPath();
        if (exePath is null)
        {
            Log("LaunchAsync: Riot Client exe NOT FOUND");
            return false;
        }

        Log($"LaunchAsync: found exe at {exePath}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--launch-product=league_of_legends --launch-patchline=live",
                UseShellExecute = true
            };

            // Check if a Riot Client window already exists before launching
            var existingHwnd = FindWindowByTitle("Riot Client");
            var isColdStart = existingHwnd == IntPtr.Zero;
            Log($"LaunchAsync: cold start={isColdStart}, existing hwnd=0x{existingHwnd:X}");

            Process.Start(psi);
            Log("LaunchAsync: process started");

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                Log("LaunchAsync: no credentials, done");
                return true;
            }

            IntPtr hwnd;

            if (isColdStart)
            {
                Log("LaunchAsync: cold start — polling for window...");
                hwnd = await WaitForRiotClientWindowAsync(timeoutMs: 30_000);
            }
            else
            {
                Log("LaunchAsync: warm start — using existing window");
                hwnd = existingHwnd;
            }

            if (hwnd == IntPtr.Zero)
            {
                Log("LaunchAsync: window NOT FOUND");
                return true;
            }

            Log($"LaunchAsync: found window hwnd=0x{hwnd:X}");

            // List all visible windows with "Riot" in title for debugging
            EnumWindows((h, _) =>
            {
                var sb = new System.Text.StringBuilder(256);
                GetWindowText(h, sb, sb.Capacity);
                var title = sb.ToString();
                if (title.Contains("Riot", StringComparison.OrdinalIgnoreCase) && IsWindowVisible(h))
                    Log($"  visible window: hwnd=0x{h:X} title=\"{title}\"");
                return true;
            }, IntPtr.Zero);

            if (isColdStart)
            {
                Log("LaunchAsync: cold start — waiting for UI to stabilize...");
                hwnd = await WaitForWindowStabilizedAsync(hwnd, stableMs: 1500, timeoutMs: 25_000);
                if (hwnd == IntPtr.Zero)
                {
                    Log("LaunchAsync: window lost during stabilization");
                    return true;
                }
                Log("LaunchAsync: UI stabilized");
            }
            else
            {
                Log("LaunchAsync: warm start — short settle...");
                await WaitForWindowReadyAsync(hwnd, settleMs: 800, timeoutMs: 10_000);
                Log("LaunchAsync: window ready");
            }

            await TypeCredentialsAsync(hwnd, username, password);
            Log("LaunchAsync: credentials typed, done");

            return true;
        }
        catch (Exception ex)
        {
            Log($"LaunchAsync: EXCEPTION: {ex}");
            return false;
        }
    }

    // ─── Credential entry (focus-safe) ──────────────────────────

    private static async Task TypeCredentialsAsync(IntPtr hwnd, string username, string password)
    {
        // Save whatever's on the clipboard so we can restore it after
        string? originalClipboard = null;
        try { originalClipboard = GetClipboardText(); } catch { }

        try
        {
            // Focus + paste username
            ForceForeground(hwnd);
            await Task.Delay(200);

            ForceForeground(hwnd);
            SendCtrlA();
            await Task.Delay(50);
            PasteText(username);
            Log($"TypeCredentials: pasted username ({username.Length} chars)");
            await Task.Delay(150);

            // Tab to password
            ForceForeground(hwnd);
            SendKey(VK_TAB);
            await Task.Delay(150);

            // Paste password
            ForceForeground(hwnd);
            SendCtrlA();
            await Task.Delay(50);
            PasteText(password);
            Log($"TypeCredentials: pasted password ({password.Length} chars)");
            await Task.Delay(150);

            // Submit
            ForceForeground(hwnd);
            SendKey(VK_RETURN);
            Log("TypeCredentials: sent ENTER");
        }
        finally
        {
            // Clear the password from clipboard immediately, then restore original
            await Task.Delay(200);
            try
            {
                if (!string.IsNullOrEmpty(originalClipboard))
                    SetClipboardText(originalClipboard);
                else
                    ClearClipboard();
            }
            catch { }

            Log("TypeCredentials: clipboard restored");
        }
    }

    private static void PasteText(string text)
    {
        SetClipboardText(text);
        Thread.Sleep(50);
        SendCtrlV();
    }

    private static string? GetClipboardText()
    {
        if (!OpenClipboard(IntPtr.Zero)) return null;
        try
        {
            if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return null;
            var hGlobal = GetClipboardData(CF_UNICODETEXT);
            if (hGlobal == IntPtr.Zero) return null;
            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) return null;
            try { return Marshal.PtrToStringUni(ptr); }
            finally { GlobalUnlock(hGlobal); }
        }
        finally { CloseClipboard(); }
    }

    private static void SetClipboardText(string text)
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return;
            var ptr = GlobalLock(hGlobal);
            if (ptr == IntPtr.Zero) { GlobalFree(hGlobal); return; }
            try { Marshal.Copy(text.ToCharArray(), 0, ptr, text.Length); Marshal.WriteInt16(ptr, text.Length * 2, 0); }
            finally { GlobalUnlock(hGlobal); }
            SetClipboardData(CF_UNICODETEXT, hGlobal);
        }
        finally { CloseClipboard(); }
    }

    private static void ClearClipboard()
    {
        if (!OpenClipboard(IntPtr.Zero)) return;
        try { EmptyClipboard(); }
        finally { CloseClipboard(); }
    }

    // ─── Window detection ───────────────────────────────────────

    private static async Task<IntPtr> WaitForRiotClientWindowAsync(int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var hwnd = FindWindowByTitle("Riot Client");
            if (hwnd != IntPtr.Zero)
            {
                Log($"WaitForWindow: found at {sw.ElapsedMilliseconds}ms");
                return hwnd;
            }

            await Task.Delay(500);
        }
        return IntPtr.Zero;
    }

    private static async Task WaitForWindowReadyAsync(IntPtr hwnd, int settleMs, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        var responsiveSince = (long?)null;

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            var responding = IsWindowResponding(hwnd);

            if (responding)
            {
                responsiveSince ??= sw.ElapsedMilliseconds;

                if (sw.ElapsedMilliseconds - responsiveSince.Value >= settleMs)
                {
                    Log($"WaitForReady: settled after {sw.ElapsedMilliseconds}ms");
                    return;
                }
            }
            else
            {
                if (responsiveSince is not null)
                    Log($"WaitForReady: window went unresponsive at {sw.ElapsedMilliseconds}ms, resetting settle");
                responsiveSince = null;
            }

            await Task.Delay(300);
        }

        Log($"WaitForReady: TIMED OUT after {timeoutMs}ms");
    }

    /// <summary>
    /// Monitors the window's size/position. During cold start, the Riot Client
    /// creates a window, then resizes it as CEF loads. Once the rect hasn't
    /// changed for stableMs, the login form is rendered and ready.
    /// Also re-checks for new windows in case the initial one was a splash.
    /// </summary>
    private static async Task<IntPtr> WaitForWindowStabilizedAsync(IntPtr hwnd, int stableMs, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        RECT lastRect = default;
        GetWindowRect(hwnd, out lastRect);
        var stableSince = sw.ElapsedMilliseconds;
        int minWidth = 400; // login window is at least this wide

        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            await Task.Delay(200);

            // Re-scan for a newer/different Riot Client window
            // (splash might close, login window opens with new hwnd)
            var current = FindWindowByTitle("Riot Client");
            if (current != IntPtr.Zero && current != hwnd)
            {
                Log($"Stabilize: window changed 0x{hwnd:X} -> 0x{current:X}");
                hwnd = current;
                GetWindowRect(hwnd, out lastRect);
                stableSince = sw.ElapsedMilliseconds;
                continue;
            }

            if (!IsWindowVisible(hwnd))
            {
                stableSince = sw.ElapsedMilliseconds;
                continue;
            }

            GetWindowRect(hwnd, out var currentRect);
            int width = currentRect.Right - currentRect.Left;

            if (currentRect.Left != lastRect.Left || currentRect.Top != lastRect.Top ||
                currentRect.Right != lastRect.Right || currentRect.Bottom != lastRect.Bottom)
            {
                Log($"Stabilize: rect changed to {width}x{currentRect.Bottom - currentRect.Top} at {sw.ElapsedMilliseconds}ms");
                lastRect = currentRect;
                stableSince = sw.ElapsedMilliseconds;
            }
            else if (width >= minWidth && sw.ElapsedMilliseconds - stableSince >= stableMs)
            {
                Log($"Stabilize: stable for {stableMs}ms at {width}x{currentRect.Bottom - currentRect.Top}, ready at {sw.ElapsedMilliseconds}ms");
                return hwnd;
            }
        }

        Log($"Stabilize: timed out after {timeoutMs}ms");
        return hwnd; // return whatever we have, try anyway
    }

    private static bool IsWindowResponding(IntPtr hwnd)
    {
        const uint SMTO_ABORTIFHUNG = 0x0002;
        const uint WM_NULL = 0x0000;
        return SendMessageTimeout(hwnd, WM_NULL, IntPtr.Zero, IntPtr.Zero,
                   SMTO_ABORTIFHUNG, 500, out _) != IntPtr.Zero;
    }

    private static IntPtr FindWindowByTitle(string titleSubstring)
    {
        IntPtr found = IntPtr.Zero;

        EnumWindows((hwnd, _) =>
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, sb.Capacity);
            var title = sb.ToString();

            if (title.Contains(titleSubstring, StringComparison.OrdinalIgnoreCase) && IsWindowVisible(hwnd))
            {
                found = hwnd;
                return false;
            }
            return true;
        }, IntPtr.Zero);

        return found;
    }

    // ─── Focus helpers ──────────────────────────────────────────

    private static bool ForceForeground(IntPtr hwnd)
    {
        var foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out _);
        var targetThread = GetWindowThreadProcessId(hwnd, out _);

        if (foregroundThread != targetThread)
        {
            AttachThreadInput(foregroundThread, targetThread, true);
            var result = SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
            AttachThreadInput(foregroundThread, targetThread, false);
            return result;
        }
        else
        {
            var result = SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
            return result;
        }
    }

    // ─── Keyboard simulation via SendInput ──────────────────────

    private const ushort VK_TAB = 0x09;
    private const ushort VK_RETURN = 0x0D;
    private const ushort VK_CONTROL = 0xA2;

    private static uint SendText(string text)
    {
        uint totalSent = 0;
        foreach (var ch in text)
        {
            totalSent += SendCharacter(ch);
            Thread.Sleep(15);
        }
        return totalSent;
    }

    private static uint SendCharacter(char ch)
    {
        var inputs = new INPUT[2];
        inputs[0] = MakeUnicodeKeyDown(ch);
        inputs[1] = MakeUnicodeKeyUp(ch);
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static uint SendKey(ushort vk)
    {
        var inputs = new INPUT[2];
        inputs[0] = MakeVkKeyDown(vk);
        inputs[1] = MakeVkKeyUp(vk);
        return SendInput(2, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendCtrlA()
    {
        var inputs = new INPUT[4];
        inputs[0] = MakeVkKeyDown(VK_CONTROL);
        inputs[1] = MakeVkKeyDown(0x41); // 'A'
        inputs[2] = MakeVkKeyUp(0x41);
        inputs[3] = MakeVkKeyUp(VK_CONTROL);
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendCtrlV()
    {
        var inputs = new INPUT[4];
        inputs[0] = MakeVkKeyDown(VK_CONTROL);
        inputs[1] = MakeVkKeyDown(0x56); // 'V'
        inputs[2] = MakeVkKeyUp(0x56);
        inputs[3] = MakeVkKeyUp(VK_CONTROL);
        SendInput(4, inputs, Marshal.SizeOf<INPUT>());
    }

    // ─── INPUT struct builders ──────────────────────────────────

    private static INPUT MakeVkKeyDown(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = 0 } }
    };

    private static INPUT MakeVkKeyUp(ushort vk) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wVk = vk, dwFlags = KEYEVENTF_KEYUP } }
    };

    private static INPUT MakeUnicodeKeyDown(char ch) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE } }
    };

    private static INPUT MakeUnicodeKeyUp(char ch) => new()
    {
        type = INPUT_KEYBOARD,
        u = new INPUTUNION { ki = new KEYBDINPUT { wScan = ch, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
    };

    // ─── Logging ────────────────────────────────────────────────

    private static void Log(string msg)
    {
        try
        {
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GGLauncherDev", "logs");
            Directory.CreateDirectory(logDir);
            File.AppendAllText(
                Path.Combine(logDir, "launch-client-debug.log"),
                $"[{DateTime.Now:HH:mm:ss.fff}] {msg}{Environment.NewLine}");
        }
        catch { }
    }

    // ─── P/Invoke declarations ──────────────────────────────────

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public INPUTUNION u;
    }

    // The union MUST include MOUSEINPUT so its size matches the native union (32 bytes on x64).
    // Without it, Marshal.SizeOf<INPUT>() returns 32 instead of 40 and SendInput silently fails.
    [StructLayout(LayoutKind.Explicit)]
    private struct INPUTUNION
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    // ─── Clipboard P/Invoke ─────────────────────────────────────

    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenClipboard(IntPtr hWndNewOwner);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EmptyClipboard();

    [DllImport("user32.dll")]
    private static extern IntPtr SetClipboardData(uint uFormat, IntPtr hMem);

    [DllImport("user32.dll")]
    private static extern IntPtr GetClipboardData(uint uFormat);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsClipboardFormatAvailable(uint format);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalLock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalUnlock(IntPtr hMem);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GlobalFree(IntPtr hMem);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
