# Captures the running WPF Inspect window on an interactive Windows desktop.
# Writes docs-bound PNG (1280x720) and an optional animated GIF under -OutDir.
param(
    [string]$RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path,
    [string]$OutDir = ""
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $RepoRoot "artifacts/ui-capture"
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$logPath = Join-Path $OutDir "capture.log"
function Write-Log([string]$Message) {
    $line = "{0} {1}" -f (Get-Date).ToString("o"), $Message
    Add-Content -Path $logPath -Value $line
    Write-Host $line
}

Write-Log ("Host: {0} {1}" -f $PSVersionTable.PSEdition, $PSVersionTable.PSVersion)
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @"
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

public static class ViewerCaptureNative {
    public const int SW_RESTORE = 9;
    public const int SW_SHOW = 5;
    public const int HWND_TOPMOST = -1;
    public const uint SWP_SHOWWINDOW = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder lpString, int nMaxCount);
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    public static string WindowText(IntPtr hWnd) {
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(hWnd, sb, sb.Capacity);
        return sb.ToString();
    }
}

public static class GifSequence {
    public static void Save(string path, Bitmap[] frames, int delayCs) {
        if (frames == null || frames.Length == 0) throw new ArgumentException("No frames.");
        ImageCodecInfo encoder = null;
        foreach (var candidate in ImageCodecInfo.GetImageEncoders()) {
            if (candidate.FormatID == ImageFormat.Gif.Guid) {
                encoder = candidate;
                break;
            }
        }
        if (encoder == null) throw new InvalidOperationException("GIF encoder is not available.");

        foreach (var frame in frames) {
            SetDelay(frame, delayCs);
        }

        var first = frames[0];
        var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.MultiFrame);
        first.Save(path, encoder, parameters);

        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.FrameDimensionTime);
        for (int i = 1; i < frames.Length; i++) {
            first.SaveAdd(frames[i], parameters);
        }

        parameters.Param[0] = new EncoderParameter(Encoder.SaveFlag, (long)EncoderValue.Flush);
        first.SaveAdd(parameters);
    }

    static void SetDelay(Bitmap frame, int delayCs) {
        try {
            using (var ms = new System.IO.MemoryStream()) {
                using (var dummy = new Bitmap(1, 1)) {
                    dummy.Save(ms, ImageFormat.Gif);
                }
                ms.Position = 0;
                using (var src = new Bitmap(ms)) {
                    PropertyItem item = src.GetPropertyItem(0x5100);
                    item.Value = new byte[] { (byte)(delayCs & 0xFF), (byte)((delayCs >> 8) & 0xFF), 0, 0 };
                    item.Len = 4;
                    frame.SetPropertyItem(item);
                }
            }
        }
        catch {
            // A GIF without an explicit delay still animates at the encoder default.
        }
    }
}
"@

[ViewerCaptureNative]::SetProcessDPIAware() | Out-Null

function Set-CaptureResolution {
    Add-Type @"
using System;
using System.Runtime.InteropServices;
public static class DisplayMode {
    const int ENUM_CURRENT_SETTINGS = -1;
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct DEVMODE {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public short dmSpecVersion;
        public short dmDriverVersion;
        public short dmSize;
        public short dmDriverExtra;
        public int dmFields;
        public int dmPositionX;
        public int dmPositionY;
        public int dmDisplayOrientation;
        public int dmDisplayFixedOutput;
        public short dmColor;
        public short dmDuplex;
        public short dmYResolution;
        public short dmTTOption;
        public short dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public short dmLogPixels;
        public int dmBitsPerPel;
        public int dmPelsWidth;
        public int dmPelsHeight;
        public int dmDisplayFlags;
        public int dmDisplayFrequency;
        public int dmICMMethod;
        public int dmICMIntent;
        public int dmMediaType;
        public int dmDitherType;
        public int dmReserved1;
        public int dmReserved2;
        public int dmPanningWidth;
        public int dmPanningHeight;
    }
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern bool EnumDisplaySettings(string deviceName, int modeNum, ref DEVMODE devMode);
    [DllImport("user32.dll", CharSet = CharSet.Ansi)]
    static extern int ChangeDisplaySettings(ref DEVMODE devMode, int flags);
    public static string Apply(int width, int height) {
        var dm = new DEVMODE();
        dm.dmSize = (short)Marshal.SizeOf(typeof(DEVMODE));
        if (!EnumDisplaySettings(null, ENUM_CURRENT_SETTINGS, ref dm)) return "EnumDisplaySettings failed";
        dm.dmPelsWidth = width;
        dm.dmPelsHeight = height;
        dm.dmFields = 0x00080000 | 0x00100000;
        int result = ChangeDisplaySettings(ref dm, 0);
        return "ChangeDisplaySettings=" + result + " from=" + dm.dmPelsWidth + "x" + dm.dmPelsHeight;
    }
}
"@
    try {
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        Write-Log ("Primary screen before resize: {0}x{1}" -f $bounds.Width, $bounds.Height)
        if ($bounds.Width -lt 1280 -or $bounds.Height -lt 720) {
            $message = [DisplayMode]::Apply(1920, 1080)
            Write-Log $message
            Start-Sleep -Milliseconds 500
        }
        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        Write-Log ("Primary screen now: {0}x{1}" -f $bounds.Width, $bounds.Height)
    }
    catch {
        Write-Log ("Display resize skipped: {0}" -f $_.Exception.Message)
    }
}

function Get-ViewerWindow([System.Diagnostics.Process]$Process) {
    $Process.Refresh()
    $handle = $Process.MainWindowHandle
    if ($handle -eq [IntPtr]::Zero) {
        return [IntPtr]::Zero
    }
    $title = [ViewerCaptureNative]::WindowText($handle)
    Write-Log ("Main window title: '{0}'" -f $title)
    if ([string]::IsNullOrWhiteSpace($title) -or $title -like "*PCB Inspection Viewer*") {
        return $handle
    }
    return [IntPtr]::Zero
}

function Copy-WindowPng([IntPtr]$Window, [string]$Path, [int]$Width, [int]$Height) {
    $rect = New-Object ViewerCaptureNative+RECT
    if (-not [ViewerCaptureNative]::GetWindowRect($Window, [ref]$rect)) {
        throw "GetWindowRect failed."
    }
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $left = [Math]::Max($rect.Left, $bounds.Left)
    $top = [Math]::Max($rect.Top, $bounds.Top)
    $right = [Math]::Min($rect.Right, $bounds.Right)
    $bottom = [Math]::Min($rect.Bottom, $bounds.Bottom)
    $srcW = $right - $left
    $srcH = $bottom - $top
    Write-Log ("Window rect: {0},{1}-{2},{3}; visible {4},{5} {6}x{7}; screen {8}x{9}" -f $rect.Left, $rect.Top, $rect.Right, $rect.Bottom, $left, $top, $srcW, $srcH, $bounds.Width, $bounds.Height)
    if ($srcW -le 50 -or $srcH -le 50) {
        throw "Visible window rectangle is too small to be the viewer."
    }

    $screen = New-Object System.Drawing.Bitmap $srcW, $srcH
    $graphics = [System.Drawing.Graphics]::FromImage($screen)
    try {
        $graphics.CopyFromScreen($left, $top, 0, 0, (New-Object System.Drawing.Size($srcW, $srcH)))
    }
    finally {
        $graphics.Dispose()
    }

    $output = New-Object System.Drawing.Bitmap $Width, $Height
    $scaled = [System.Drawing.Graphics]::FromImage($output)
    try {
        $scaled.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $scaled.DrawImage($screen, 0, 0, $Width, $Height)
    }
    finally {
        $scaled.Dispose()
        $screen.Dispose()
    }

    $output.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
    return $output
}

function Test-CaptureLooksReal([System.Drawing.Bitmap]$Bitmap) {
    $colors = New-Object "System.Collections.Generic.HashSet[string]"
    $nonExtreme = 0
    $samples = 0
    for ($y = 8; $y -lt $Bitmap.Height; $y += 24) {
        for ($x = 8; $x -lt $Bitmap.Width; $x += 24) {
            $pixel = $Bitmap.GetPixel($x, $y)
            $samples++
            [void]$colors.Add(("{0},{1},{2}" -f $pixel.R, $pixel.G, $pixel.B))
            if ($pixel.R -gt 12 -and $pixel.R -lt 248) { $nonExtreme++ }
            elseif ($pixel.G -gt 12 -and $pixel.G -lt 248) { $nonExtreme++ }
            elseif ($pixel.B -gt 12 -and $pixel.B -lt 248) { $nonExtreme++ }
        }
    }
    Write-Log ("Capture sample colors={0} midtoneSamples={1}/{2}" -f $colors.Count, $nonExtreme, $samples)
    return ($colors.Count -ge 12 -and $nonExtreme -ge 20)
}

Set-CaptureResolution
$sessionId = (Get-Process -Id $PID).SessionId
Write-Log ("PowerShell session id: {0}" -f $sessionId)

$publishDir = Join-Path $RepoRoot "artifacts/viewer-publish"
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
Write-Log "Publishing WPF app..."
dotnet publish (Join-Path $RepoRoot "src/PcbInspectionViewer.App/PcbInspectionViewer.App.csproj") -c Release -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $publishDir "PcbInspectionViewer.App.exe"
$fixture = Join-Path $RepoRoot "tests/fixtures/upload_triage_sample.json"
$readyFile = Join-Path $OutDir "ready.txt"
if (Test-Path $readyFile) { Remove-Item -Force $readyFile }
if (-not (Test-Path $exe)) { throw "Published executable was not found at $exe" }
if (-not (Test-Path $fixture)) { throw "Fixture was not found at $fixture" }

$argumentLine = "--fixture `"$fixture`" --window-size 1280x720 --ready-file `"$readyFile`" --cycle-layers"
Write-Log ("Starting: {0} {1}" -f $exe, $argumentLine)
$startInfo = New-Object System.Diagnostics.ProcessStartInfo
$startInfo.FileName = $exe
$startInfo.Arguments = $argumentLine
$startInfo.WorkingDirectory = $publishDir
$startInfo.UseShellExecute = $true
$proc = [System.Diagnostics.Process]::Start($startInfo)
if ($null -eq $proc) { throw "Process.Start returned no process for $exe" }
Write-Log ("Viewer pid {0}" -f $proc.Id)

$deadline = (Get-Date).AddSeconds(60)
while (-not (Test-Path $readyFile)) {
    if ($proc.HasExited) {
        Write-Log ("Viewer exited early with code {0}" -f $proc.ExitCode)
        $logDir = Join-Path $env:LOCALAPPDATA "PcbInspectionViewer\logs"
        if (Test-Path $logDir) {
            Get-ChildItem $logDir -Filter "*.log" | Sort-Object LastWriteTime -Descending | Select-Object -First 1 | ForEach-Object {
                Write-Log ("App log {0}" -f $_.FullName)
                Get-Content $_.FullName -Tail 80 | ForEach-Object { Write-Log $_ }
            }
        }
        throw "Viewer process exited before it signaled ready."
    }
    if ((Get-Date) -gt $deadline) {
        throw "Timed out waiting for the viewer ready file."
    }
    Start-Sleep -Milliseconds 250
}
Write-Log "Fixture applied; waiting for the window handle."

$hwnd = [IntPtr]::Zero
$handleDeadline = (Get-Date).AddSeconds(20)
while ($hwnd -eq [IntPtr]::Zero) {
    $hwnd = Get-ViewerWindow $proc
    if ($hwnd -ne [IntPtr]::Zero) { break }
    if ((Get-Date) -gt $handleDeadline) { throw "Timed out waiting for the PCB Inspection Viewer window." }
    Start-Sleep -Milliseconds 200
}
Write-Log ("Window handle {0} title '{1}'" -f $hwnd, [ViewerCaptureNative]::WindowText($hwnd))

$screenBounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
$placeW = [Math]::Min(1280, [Math]::Max(640, $screenBounds.Width))
$placeH = [Math]::Min(720, [Math]::Max(480, $screenBounds.Height))
[void][ViewerCaptureNative]::ShowWindow($hwnd, [ViewerCaptureNative]::SW_RESTORE)
[void][ViewerCaptureNative]::SetWindowPos($hwnd, [IntPtr]([ViewerCaptureNative]::HWND_TOPMOST), 0, 0, $placeW, $placeH, [ViewerCaptureNative]::SWP_SHOWWINDOW)
[void][ViewerCaptureNative]::SetForegroundWindow($hwnd)
Write-Log ("Placed window at 0,0 {0}x{1}" -f $placeW, $placeH)
Start-Sleep -Milliseconds 600

$pngPath = Join-Path $OutDir "viewer-inspect.png"
$still = Copy-WindowPng $hwnd $pngPath 1280 720
$stillReal = Test-CaptureLooksReal $still
if (-not $stillReal) {
    Write-Log "First capture looked blank. Waking the window and trying once more."
    [void][ViewerCaptureNative]::SetForegroundWindow($hwnd)
    [System.Windows.Forms.Cursor]::Position = New-Object System.Drawing.Point(200, 200)
    Start-Sleep -Milliseconds 800
    $still.Dispose()
    $still = Copy-WindowPng $hwnd $pngPath 1280 720
    $stillReal = Test-CaptureLooksReal $still
}

$frames = New-Object System.Collections.Generic.List[System.Drawing.Bitmap]
$frames.Add($still)
for ($i = 0; $i -lt 5; $i++) {
    Start-Sleep -Milliseconds 1000
    [void][ViewerCaptureNative]::SetForegroundWindow($hwnd)
    $framePath = Join-Path $OutDir ("frame-{0}.png" -f ($i + 1))
    $frame = Copy-WindowPng $hwnd $framePath 1280 720
    $frames.Add($frame)
}

$gifPath = Join-Path $OutDir "inspect-loop.gif"
try {
    $frameArray = [System.Drawing.Bitmap[]]$frames.ToArray()
    [GifSequence]::Save($gifPath, $frameArray, 100)
    Write-Log ("Wrote GIF {0} ({1} bytes)" -f $gifPath, (Get-Item $gifPath).Length)
}
catch {
    Write-Log ("GIF encoding failed: {0}" -f $_.Exception.Message)
}

foreach ($frame in $frames) { $frame.Dispose() }

try {
    if (-not $proc.HasExited) {
        Stop-Process -Id $proc.Id -Force
        Write-Log "Stopped viewer process."
    }
}
catch {
    Write-Log ("Could not stop viewer: {0}" -f $_.Exception.Message)
}

if (-not $stillReal) {
    throw "Screen capture did not contain a real rendered window (image is blank or nearly a single color). Refusing to treat it as a UI screenshot."
}

$png = Get-Item $pngPath
Write-Log ("Wrote PNG {0} ({1} bytes)" -f $png.FullName, $png.Length)
Write-Log "CAPTURE_OK"
