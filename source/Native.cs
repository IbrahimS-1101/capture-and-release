using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace GameplayRecorder;

public record GameWindow(nint Handle, int Pid, string Title, string ProcessName)
{
    public override string ToString() => $"{Title}  ({ProcessName}, {Pid})";
}
public record MonitorTarget(string Device, Rectangle Bounds)
{
    public override string ToString() => $"{Device.Replace(@"\\.\", "")} — {Bounds.Width} × {Bounds.Height}";
}
internal static class Native
{
    internal delegate bool EnumProc(nint hwnd, nint param);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint hwnd);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd, StringBuilder text, int size);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd, out int pid);
    [DllImport("user32.dll", SetLastError=true)] internal static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint hwnd, int id);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd, out Rect rect);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd,uint message,nint wparam,nint lparam);
    [DllImport("user32.dll")] internal static extern bool PrintWindow(nint hwnd,nint dc,uint flags);
    [StructLayout(LayoutKind.Sequential)] internal struct Rect { public int Left, Top, Right, Bottom; }

    internal static List<GameWindow> Windows()
    {
        var result=new List<GameWindow>();
        EnumWindows((hwnd, _) => {
            if (!IsWindowVisible(hwnd)) return true;
            var text=new StringBuilder(512); GetWindowText(hwnd,text,text.Capacity);
            if (text.Length==0) return true;
            GetWindowThreadProcessId(hwnd,out var pid);
            if (pid==Environment.ProcessId) return true;
            try { using var p=Process.GetProcessById(pid); result.Add(new(hwnd,pid,text.ToString(),p.ProcessName)); }
            catch (ArgumentException) { }
            catch (System.ComponentModel.Win32Exception) { }
            return true;
        },0);
        return result.OrderBy(w=>w.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
    }
    internal static bool SameProcess(GameWindow game) => IsWindow(game.Handle) && GetPid(game.Handle)==game.Pid;
    private static int GetPid(nint hwnd) { GetWindowThreadProcessId(hwnd,out int pid); return pid; }
}
