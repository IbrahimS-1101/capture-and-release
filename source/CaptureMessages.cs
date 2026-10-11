namespace GameplayRecorder;

internal static class CaptureMessages
{
    internal static string VideoStopped(bool displayAccessLost) => displayAccessLost
        ? "Screen capture lost access during a fullscreen or display change. Automatic resume is not available."
        : "Video capture ended unexpectedly. The session log contains details.";

    internal static string Saved(string path, string? interruption) => interruption==null
        ? "Saved: "+Path.GetFileName(path)
        : "Recording interrupted: "+interruption+"\nSaved captured part: "+Path.GetFileName(path);
}
