using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Dedsec.OpenGlScene;

internal sealed class Scene : Form
{
    [StructLayout(LayoutKind.Sequential)]
    private struct PixelFormat
    {
        public ushort Size, Version;
        public uint Flags;
        public byte Type, Color, Red, RedShift, Green, GreenShift, Blue, BlueShift, Alpha, AlphaShift,
            Accum, AccumRed, AccumGreen, AccumBlue, AccumAlpha, Depth, Stencil, Aux, Layer, Reserved;
        public uint LayerMask, VisibleMask, DamageMask;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern int ChoosePixelFormat(nint dc, ref PixelFormat format);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetPixelFormat(nint dc, int id, ref PixelFormat format);
    [DllImport("gdi32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool SwapBuffers(nint dc);
    [DllImport("opengl32.dll")] private static extern nint wglCreateContext(nint dc);
    [DllImport("opengl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool wglMakeCurrent(nint dc, nint context);
    [DllImport("opengl32.dll")][return: MarshalAs(UnmanagedType.Bool)] private static extern bool wglDeleteContext(nint context);
    [DllImport("opengl32.dll")] private static extern void glClearColor(float r, float g, float b, float a);
    [DllImport("opengl32.dll")] private static extern void glClear(uint mask);
    [DllImport("opengl32.dll")] private static extern void glViewport(int x, int y, int width, int height);
    [DllImport("opengl32.dll")] private static extern void glBegin(uint mode);
    [DllImport("opengl32.dll")] private static extern void glEnd();
    [DllImport("opengl32.dll")] private static extern void glColor3f(float r, float g, float b);
    [DllImport("opengl32.dll")] private static extern void glVertex2f(float x, float y);
    [DllImport("opengl32.dll")] private static extern void glReadBuffer(uint mode);
    [DllImport("opengl32.dll")] private static extern void glReadPixels(int x, int y, int width, int height, uint format, uint type, [Out] byte[] data);
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 16 };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private nint _dc, _context;
    private readonly string? _marker, _capture;
    private bool _captured;

    private Scene(string[] args)
    {
        if (args.Length == 2) { _marker = args[0]; _capture = args[1]; }
        Text = "Dedsec / OpenGL capture test (not a game benchmark)"; Width = 1000; Height = 640;
        Shown += (_, _) => Initialize();
        _timer.Tick += (_, _) => Render();
        FormClosed += (_, _) => { _timer.Stop(); _timer.Dispose(); wglMakeCurrent(0, 0); if (_context != 0) wglDeleteContext(_context); if (_dc != 0) ReleaseDC(Handle, _dc); };
        KeyDown += (_, e) => { if (e.KeyCode == Keys.F11) { FormBorderStyle = FormBorderStyle == FormBorderStyle.None ? FormBorderStyle.Sizable : FormBorderStyle.None; WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; } };
    }
    private void Initialize()
    {
        _dc = GetDC(Handle);
        var format = new PixelFormat { Size = (ushort)Marshal.SizeOf<PixelFormat>(), Version = 1, Flags = 0x25, Color = 32, Depth = 24 };
        var id = ChoosePixelFormat(_dc, ref format);
        if (id == 0 || !SetPixelFormat(_dc, id, ref format)) { Close(); return; }
        _context = wglCreateContext(_dc);
        if (_context == 0 || !wglMakeCurrent(_dc, _context)) { Close(); return; }
        _timer.Start();
    }
    private void Render()
    {
        if (_clock.Elapsed.TotalSeconds > 55) { Close(); return; }
        glViewport(0, 0, ClientSize.Width, ClientSize.Height); glClearColor(.04f, .07f, .04f, 1); glClear(0x4000);
        var time = (float)_clock.Elapsed.TotalSeconds;
        glBegin(4);
        for (var i = 0; i < 15; i++)
        {
            var x = MathF.Sin(time + i) * .5f; var y = MathF.Cos(time * .7f + i) * .5f;
            glColor3f(.76f, .98f, .44f); glVertex2f(x, y + .2f); glColor3f(.2f, .4f, .15f);
            glVertex2f(x - .2f, y - .2f); glVertex2f(x + .2f, y - .2f);
        }
        glEnd(); SwapBuffers(_dc);
        if (!_captured && _marker is not null && _capture is not null && File.Exists(_marker) && DateTime.UtcNow - File.GetLastWriteTimeUtc(_marker) > TimeSpan.FromMilliseconds(500))
        {
            _captured = true; CaptureFrame(_capture);
        }
    }
    private void CaptureFrame(string path)
    {
        var width = ClientSize.Width; var height = ClientSize.Height;
        var bytes = new byte[checked(width * height * 4)];
        glReadBuffer(0x0404); glReadPixels(0, 0, width, height, 0x80E1, 0x1401, bytes);
        using var bitmap = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
        try { Marshal.Copy(bytes, 0, data.Scan0, bytes.Length); } finally { bitmap.UnlockBits(data); }
        bitmap.RotateFlip(RotateFlipType.RotateNoneFlipY); bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
    [STAThread] private static void Main(string[] args) { ApplicationConfiguration.Initialize(); Application.Run(new Scene(args)); }
}
