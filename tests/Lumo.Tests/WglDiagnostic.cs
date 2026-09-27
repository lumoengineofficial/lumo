using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace Lumo.Tests;

public class WglDiagnostic
{
    private readonly ITestOutputHelper _output;
    public WglDiagnostic(ITestOutputHelper output) => _output = output;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool FreeLibrary(IntPtr hModule);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

    [Fact]
    public void Test_ICD_Loading()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("Windows-only diagnostic; skipped.");
            return;
        }

        _output.WriteLine($"Process is {(Environment.Is64BitProcess ? "64-bit" : "32-bit")}");
        _output.WriteLine($"OS is {(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")}");

        // Check if opengl32.dll is loaded
        IntPtr ogl = GetModuleHandle("opengl32.dll");
        _output.WriteLine($"opengl32.dll loaded: 0x{ogl:X}");

        // Try loading Intel ICD directly
        string icdPath = @"C:\Windows\System32\ig4icd64.dll";
        _output.WriteLine($"Loading: {icdPath}");
        IntPtr hIcd = LoadLibrary(icdPath);
        int loadErr = Marshal.GetLastWin32Error();
        _output.WriteLine($"LoadLibrary = 0x{hIcd:X}, Error={loadErr}");

        if (hIcd != IntPtr.Zero)
        {
            // Check for required OpenGL ICD exports
            string[] exports = { "DrvCopyContext", "DrvCreateContext", "DrvDeleteContext", "DrvMakeCurrent", "DrvSetBuffer", "DrvSwapBuffers", "DrvDescribePixelFormat", "DrvSetPixelFormat", "DrvRealizePixelFormat", "DrvCloseDriver" };
            foreach (var exp in exports)
            {
                IntPtr proc = GetProcAddress(hIcd, exp);
                _output.WriteLine($"  {exp}: {(proc != IntPtr.Zero ? "FOUND" : "missing")}");
            }
            FreeLibrary(hIcd);
        }

        // Try legacy ICD name
        _output.WriteLine("Trying igicd64.dll...");
        IntPtr hIcd2 = LoadLibrary(@"C:\Windows\System32\igicd64.dll");
        _output.WriteLine($"igicd64.dll = 0x{hIcd2:X}, Error={Marshal.GetLastWin32Error()}");
        if (hIcd2 != IntPtr.Zero) FreeLibrary(hIcd2);

        // Check which OpenGL DLLs exist
        string[] candidates = { "ig4icd64.dll", "igicd64.dll", "ig4icd32.dll", "igicd32.dll", "atioglxx.dll", "nvd3dumx.dll", "opengl32.dll" };
        foreach (var c in candidates)
        {
            string p = System.IO.Path.Combine(Environment.SystemDirectory, c);
            _output.WriteLine($"  {c}: {(System.IO.File.Exists(p) ? "EXISTS" : "not found")}");
        }
    }

    [Fact]
    public void Test_WGL_Directly()
    {
        if (!OperatingSystem.IsWindows())
        {
            _output.WriteLine("Windows-only diagnostic; skipped.");
            return;
        }

        IntPtr hDC = GetDC(IntPtr.Zero);
        _output.WriteLine($"GetDC = 0x{hDC:X}");

        try
        {
            var pfd = new PIXELFORMATDESCRIPTOR
            {
                nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
                nVersion = 1,
                dwFlags = 0x2 | 0x20 | 0x1 | 0x200,
                iPixelType = 0,
                cColorBits = 32,
                cDepthBits = 24,
                cStencilBits = 8,
            };

            int format = ChoosePixelFormat(hDC, ref pfd);
            _output.WriteLine($"ChoosePixelFormat = {format}, LastError={Marshal.GetLastWin32Error()}");

            if (format == 0)
            {
                pfd.dwFlags = 0x2 | 0x20 | 0x1;
                format = ChoosePixelFormat(hDC, ref pfd);
                _output.WriteLine($"ChoosePixelFormat (simple) = {format}, LastError={Marshal.GetLastWin32Error()}");
            }

            if (format == 0) return;

            bool setOk = SetPixelFormat(hDC, format, ref pfd);
            _output.WriteLine($"SetPixelFormat({format}) = {setOk}, LastError={Marshal.GetLastWin32Error()}");

            // Describe the pixel format we got
            unsafe
            {
                var gotPfd = new PIXELFORMATDESCRIPTOR();
                bool desc = DescribePixelFormat(hDC, format, (uint)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(), &gotPfd);
                _output.WriteLine($"DescribePixelFormat: colorBits={gotPfd.cColorBits}, depthBits={gotPfd.cDepthBits}, flags=0x{gotPfd.dwFlags:X}");
            }

            IntPtr hRC = wglCreateContext(hDC);
            _output.WriteLine($"wglCreateContext = 0x{hRC:X}, LastError={Marshal.GetLastWin32Error()}");

            if (hRC != IntPtr.Zero)
            {
                bool makeOk = wglMakeCurrent(hDC, hRC);
                _output.WriteLine($"wglMakeCurrent = {makeOk}, LastError={Marshal.GetLastWin32Error()}");

                if (makeOk)
                {
                    IntPtr vendor = glGetString(0x1F00);
                    IntPtr renderer = glGetString(0x1F01);
                    IntPtr version = glGetString(0x1F02);
                    _output.WriteLine($"GL_VENDOR   = {Marshal.PtrToStringAnsi(vendor)}");
                    _output.WriteLine($"GL_RENDERER = {Marshal.PtrToStringAnsi(renderer)}");
                    _output.WriteLine($"GL_VERSION  = {Marshal.PtrToStringAnsi(version)}");
                }
            }
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, hDC);
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern int ChoosePixelFormat(IntPtr hdc, ref PIXELFORMATDESCRIPTOR pfd);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetPixelFormat(IntPtr hdc, int format, ref PIXELFORMATDESCRIPTOR ppfd);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern IntPtr wglCreateContext(IntPtr hdc);

    [DllImport("opengl32.dll", SetLastError = true)]
    private static extern bool wglMakeCurrent(IntPtr hdc, IntPtr hglrc);

    [DllImport("opengl32.dll")]
    private static extern IntPtr glGetString(uint name);

    [DllImport("gdi32.dll")]
    private static extern unsafe bool DescribePixelFormat(IntPtr hdc, int iPixelFormat, uint nBytes, PIXELFORMATDESCRIPTOR* ppfd);

    [StructLayout(LayoutKind.Sequential)]
    private struct PIXELFORMATDESCRIPTOR
    {
        public ushort nSize;
        public ushort nVersion;
        public uint dwFlags;
        public byte iPixelType;
        public byte cColorBits;
        public byte cRedBits, cRedShift, cGreenBits, cGreenShift, cBlueBits, cBlueShift;
        public byte cAlphaBits, cAlphaShift, cAccumBits, cAccumRedBits, cAccumGreenBits, cAccumBlueBits, cAccumAlphaBits;
        public byte cDepthBits, cStencilBits, cAuxBuffers;
        public sbyte iLayerType;
        public byte bReserved;
        public IntPtr dwLayerMask, dwVisibleMask, dwDamageMask;
    }
}
