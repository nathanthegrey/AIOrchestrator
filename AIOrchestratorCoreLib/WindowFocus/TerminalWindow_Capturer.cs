using System.Runtime.InteropServices;
using AIOrchestratorCoreLib.Imaging;

namespace AIOrchestratorCoreLib.WindowFocus;

/// <summary>
/// Photographs a session's terminal window for /screen: raises it, maximises it, takes the picture,
/// puts it back to the size it was, and LEAVES IT IN FRONT — the owner asked for the window to still
/// be there when they look up from their phone.
///
/// WHY IT MAXIMISES AT ALL: a tiled session window is a few hundred pixels wide, and a screenshot of
/// one is unreadable on a phone. Maximising is what makes the picture worth sending.
///
/// WHY IT HOLDS THE HANDLE: every other entry point in this folder re-finds the window by title per
/// call, which is right for a single action and wrong for a sequence. Between maximise and restore a
/// second lookup could land on a DIFFERENT window (the owner spawns sessions while this runs), and
/// the restore would then resize a window nobody had maximised.
/// </summary>
public static class TerminalWindow_Capturer
{
    /// <summary>
    /// How long the window gets to actually paint at its new size before the picture is taken.
    /// Windows Terminal re-lays its grid out and repaints asynchronously, so capturing immediately
    /// after SW_MAXIMIZE photographs the OLD contents stretched, or a blank frame.
    /// </summary>
    const int REPAINT_SETTLE_MILLISECONDS = 400;

    /// <summary>The same pause after putting the window back, so the restore is on screen before we return.</summary>
    const int RESTORE_SETTLE_MILLISECONDS = 120;

    const int SW_RESTORE = 9;
    const int SW_MAXIMIZE = 3;

    const int SRCCOPY = 0x00CC0020;
    const int BI_RGB = 0;
    const int DIB_RGB_COLORS = 0;
    const int BITS_PER_PIXEL = 32;
    const int BYTES_PER_PIXEL = 4;

    /// <summary>DWMWA_EXTENDED_FRAME_BOUNDS — the window's VISIBLE rectangle, drop shadow excluded.</summary>
    const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>A sanity ceiling: a bogus rectangle must not turn into a multi-gigabyte allocation.</summary>
    const int MAXIMUM_EDGE_PIXELS = 16384;

    /// <summary>
    /// ONE CAPTURE AT A TIME, PROCESS-WIDE — and this is the difficulty the owner named when they
    /// asked for a screenshot on the periodic status (2026-08-24): *"every topic has that message
    /// synched at :00 and :30, so the screenshots are going to overlap badly, this has to be queued
    /// properly."*
    ///
    /// They are exactly right, and the reason is that a capture is not a passive read. It RAISES and
    /// MAXIMISES a real window, so two running at once photograph each other: the second window comes
    /// up over the first while the first is being copied off the screen, and both pictures are wrong.
    /// Restores collide the same way, and a window can be left maximised by a restore that ran before
    /// the maximise it was undoing.
    ///
    /// THE GATE LIVES HERE, NOT IN THE CALLER, because there is more than one caller and they do not
    /// know about each other: the half-hourly sweep walks every topic in turn, while /screen arrives
    /// from the phone on a different loop entirely. A queue maintained by either one would not cover
    /// the other. Held across the WHOLE cycle — raise, maximise, settle, copy, restore — because it
    /// is the visible screen state that is shared, not the pixels.
    /// </summary>
    static readonly SemaphoreSlim CAPTURE_GATE = new(1, 1);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWPLACEMENT
    {
        public int Length;
        public int Flags;
        public int ShowCommand;
        public POINT MinimisedPosition;
        public POINT MaximisedPosition;
        public RECT NormalPosition;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPixelsPerMeter;
        public int YPixelsPerMeter;
        public int ColoursUsed;
        public int ColoursImportant;
    }

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT placement);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

    [DllImport("dwmapi.dll")]
    static extern int DwmGetWindowAttribute(IntPtr hWnd, int attribute, ref RECT value, int size);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER header, int usage, out IntPtr bits, IntPtr section, int offset);

    [DllImport("gdi32.dll")]
    static extern IntPtr SelectObject(IntPtr hdc, IntPtr handle);

    [DllImport("gdi32.dll")]
    static extern bool DeleteObject(IntPtr handle);

    [DllImport("gdi32.dll")]
    static extern bool BitBlt(IntPtr destinationDc, int x, int y, int width, int height, IntPtr sourceDc, int sourceX, int sourceY, int rasterOperation);

    /// <summary>
    /// Raise, maximise, photograph, restore, leave in front. Writes a PNG to
    /// <paramref name="outputFilePath"/> and returns null on success, or a SHORT owner-facing reason
    /// on failure — every one of these ends up on the owner's phone, so it says what they can do
    /// about it rather than naming an API.
    ///
    /// The window is put back even when the capture itself fails: leaving a session maximised
    /// because a bitmap allocation failed would be a visible side effect of an invisible error.
    /// </summary>
    public static async Task<string?> Try_CaptureSessionWindow_Async(
        string titleFragment,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        await CAPTURE_GATE.WaitAsync(cancellationToken);

        try
        {
            return await Capture_Inside_Gate_Async(titleFragment, outputFilePath, cancellationToken);
        }
        finally
        {
            CAPTURE_GATE.Release();
        }
    }

    /// <summary>
    /// THE WINDOW IS FOUND INSIDE THE GATE, not before it. A handle taken while queueing could have
    /// been closed, moved or maximised by the capture ahead of us by the time our turn came, and the
    /// placement we then saved and restored would be that other capture's, not the window's own.
    /// </summary>
    static async Task<string?> Capture_Inside_Gate_Async(
        string titleFragment,
        string outputFilePath,
        CancellationToken cancellationToken)
    {
        var windowHandle = TerminalWindow_Focuser.Find_Handle_ByTitleFragment_OrZero(titleFragment);

        if (windowHandle == IntPtr.Zero)
            return "No terminal window for this orchestration is on screen.";

        if (IsIconic(windowHandle))
            ShowWindow(windowHandle, SW_RESTORE);

        TerminalWindow_Focuser.Try_Focus_Handle(windowHandle);

        var savedPlacement = Read_Placement_OrNull(windowHandle);

        ShowWindow(windowHandle, SW_MAXIMIZE);

        try
        {
            await Task.Delay(REPAINT_SETTLE_MILLISECONDS, cancellationToken);

            var bounds = Read_VisibleBounds_OrNull(windowHandle);

            if (bounds == null)
                return "Could not measure the terminal window — nothing was captured.";

            var (left, top, width, height) = bounds.Value;

            if (width <= 0 || height <= 0 || width > MAXIMUM_EDGE_PIXELS || height > MAXIMUM_EDGE_PIXELS)
                return $"The terminal window reported an unusable size ({width}x{height}).";

            var pixels = Copy_ScreenRectangle_OrNull(left, top, width, height);

            if (pixels == null)
                return "Windows refused the screen copy — nothing was captured.";

            var png = Png_Encoder.Encode_Bgra32(width, height, pixels);

            var directory = Path.GetDirectoryName(outputFilePath);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllBytesAsync(outputFilePath, png, cancellationToken);

            return null;
        }
        finally
        {
            // PUT IT BACK, then raise it again: SetWindowPlacement on a window that is not the
            // foreground can leave it behind whatever is, and the owner asked for it to stay in front.
            if (savedPlacement != null)
            {
                var placement = savedPlacement.Value;
                SetWindowPlacement(windowHandle, ref placement);
            }
            else
            {
                ShowWindow(windowHandle, SW_RESTORE);
            }

            TerminalWindow_Focuser.Try_Focus_Handle(windowHandle);
        }
    }

    static WINDOWPLACEMENT? Read_Placement_OrNull(IntPtr windowHandle)
    {
        var placement = new WINDOWPLACEMENT { Length = Marshal.SizeOf<WINDOWPLACEMENT>() };

        return GetWindowPlacement(windowHandle, ref placement) ? placement : null;
    }

    /// <summary>
    /// Where the window VISUALLY is. GetWindowRect includes the invisible resize border and drop
    /// shadow since Vista, so capturing that rectangle frames the terminal in a band of whatever
    /// happens to be behind it. DWM knows the real edges; GetWindowRect is only the fallback.
    /// </summary>
    static (int Left, int Top, int Width, int Height)? Read_VisibleBounds_OrNull(IntPtr windowHandle)
    {
        var visible = new RECT();

        if (DwmGetWindowAttribute(windowHandle, DWMWA_EXTENDED_FRAME_BOUNDS, ref visible, Marshal.SizeOf<RECT>()) == 0)
            return (visible.Left, visible.Top, visible.Right - visible.Left, visible.Bottom - visible.Top);

        if (GetWindowRect(windowHandle, out var outer))
            return (outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top);

        return null;
    }

    /// <summary>
    /// Copies a rectangle of the screen into a top-down 32-bit BGRA buffer, which is exactly what
    /// <see cref="Png_Encoder.Encode_Bgra32"/> consumes. A NEGATIVE height in the header is what
    /// asks Windows for top-down rows; the default bottom-up order would hand the encoder an
    /// upside-down picture.
    /// </summary>
    static byte[]? Copy_ScreenRectangle_OrNull(int left, int top, int width, int height)
    {
        var screenDc = GetDC(IntPtr.Zero);

        if (screenDc == IntPtr.Zero)
            return null;

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;

        try
        {
            memoryDc = CreateCompatibleDC(screenDc);

            if (memoryDc == IntPtr.Zero)
                return null;

            var header = new BITMAPINFOHEADER
            {
                Size = Marshal.SizeOf<BITMAPINFOHEADER>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = BITS_PER_PIXEL,
                Compression = BI_RGB,
            };

            bitmap = CreateDIBSection(memoryDc, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);

            if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                return null;

            previousBitmap = SelectObject(memoryDc, bitmap);

            if (!BitBlt(memoryDc, 0, 0, width, height, screenDc, left, top, SRCCOPY))
                return null;

            var buffer = new byte[width * height * BYTES_PER_PIXEL];
            Marshal.Copy(bits, buffer, 0, buffer.Length);

            return buffer;
        }
        finally
        {
            if (memoryDc != IntPtr.Zero && previousBitmap != IntPtr.Zero)
                SelectObject(memoryDc, previousBitmap);

            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);

            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);

            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}
