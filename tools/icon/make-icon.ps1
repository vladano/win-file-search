<#
.SYNOPSIS
    Generates the File Search application icon.

.DESCRIPTION
    Draws the icon at every size Windows uses (16 to 256 px) and writes:
      src/FileSearch.App/Assets/FileSearch.ico   multi-size icon used by the exe and the windows
      docs/icon.png                              256 px PNG for the README and other artwork

    Each size is drawn separately. Below 32 px the text lines are left out so the shape stays
    readable in Explorer lists and on the taskbar. Sizes up to 48 px are stored as 32-bit BMP
    entries and larger sizes as PNG entries, which every Windows version and WPF can read.

    Run from the repository root:
      pwsh -File tools/icon/make-icon.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot
)

$ErrorActionPreference = 'Stop'
# $PSScriptRoot is not available in param defaults on Windows PowerShell 5.1, so resolve it here.
if (-not $RepoRoot) { $RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path }

# The drawing code is compiled with Add-Type against System.Drawing from .NET Framework. PowerShell 7
# splits System.Drawing across several assemblies, which Add-Type cannot resolve reliably, so under
# PowerShell 7 run this script in Windows PowerShell 5.1 (part of every Windows 10 and 11).
if ($PSVersionTable.PSEdition -eq 'Core') {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $PSCommandPath -RepoRoot $RepoRoot
    exit $LASTEXITCODE
}

Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

public static class FileSearchIcon
{
    // All shapes are designed on a 256 x 256 grid and scaled to the target size.
    static GraphicsPath RoundedRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        float d = r * 2;
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static Bitmap Draw(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.CompositingQuality = CompositingQuality.HighQuality;
            g.Clear(Color.Transparent);
            g.ScaleTransform(size / 256f, size / 256f);

            bool small = size < 32;
            bool tiny = size < 20;

            var yellow = Color.FromArgb(255, 255, 204, 51);
            var navy = Color.FromArgb(255, 15, 39, 68);

            // Tile
            float inset = tiny ? 4 : 12;
            using (var tile = RoundedRect(inset, inset, 256 - 2 * inset, 256 - 2 * inset, tiny ? 40 : 52))
            using (var fill = new LinearGradientBrush(new PointF(0, 0), new PointF(256, 256),
                       Color.FromArgb(255, 52, 120, 196), Color.FromArgb(255, 20, 52, 92)))
            {
                g.FillPath(fill, tile);
                if (!small)
                    using (var edge = new Pen(Color.FromArgb(70, 255, 255, 255), 3))
                        g.DrawPath(edge, tile);
            }

            // Document
            float dx = small ? 46 : 52, dy = small ? 36 : 40, dw = small ? 124 : 116, dh = small ? 156 : 150;
            using (var shadow = RoundedRect(dx + 5, dy + 7, dw, dh, 12))
            using (var sb = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
                g.FillPath(sb, shadow);
            using (var doc = RoundedRect(dx, dy, dw, dh, 12))
                g.FillPath(Brushes.White, doc);

            // Text lines with one highlighted line (the "hit")
            var lineColor = Color.FromArgb(255, 160, 178, 204);
            float lx = dx + 18;
            if (!small)
            {
                using (var lb = new SolidBrush(lineColor))
                {
                    float[] widths = { 78, 60, 0, 70, 44 };
                    for (int i = 0; i < widths.Length; i++)
                    {
                        if (widths[i] == 0) continue;
                        using (var line = RoundedRect(lx, dy + 22 + i * 24, widths[i], 10, 5))
                            g.FillPath(lb, line);
                    }
                }
            }
            using (var hb = new SolidBrush(yellow))
            using (var hit = RoundedRect(lx - 6, small ? dy + 28 : dy + 64, small ? 100 : 86, small ? 34 : 18, small ? 10 : 6))
                g.FillPath(hb, hit);

            // Magnifying glass: navy outline, white ring, light lens, yellow handle
            // At small sizes the lens moves up and left so the handle has room and stays visible.
            float cx = small ? 146 : 160, cy = small ? 144 : 158, r = small ? 50 : 46;
            float ring = small ? 24 : 16;
            float hx = small ? 226 : 222;

            using (var handleOutline = new Pen(navy, small ? 62 : 38) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(handleOutline, cx + r * 0.72f, cy + r * 0.72f, hx, hx);
            using (var handle = new Pen(yellow, small ? 40 : 24) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                g.DrawLine(handle, cx + r * 0.80f, cy + r * 0.80f, hx - 2, hx - 2);

            using (var lens = new SolidBrush(Color.FromArgb(small ? 255 : 215, 225, 238, 252)))
                g.FillEllipse(lens, cx - r, cy - r, 2 * r, 2 * r);
            if (!small)
            {
                // Magnified highlight inside the lens
                using (var hb = new SolidBrush(yellow))
                using (var hit = RoundedRect(cx - 30, cy - 9, 60, 18, 6))
                    g.FillPath(hb, hit);
            }
            using (var outline = new Pen(navy, ring + (small ? 12 : 10)))
                g.DrawEllipse(outline, cx - r, cy - r, 2 * r, 2 * r);
            using (var white = new Pen(Color.White, ring))
                g.DrawEllipse(white, cx - r, cy - r, 2 * r, 2 * r);

            if (!small)
                using (var glint = new Pen(Color.FromArgb(200, 255, 255, 255), 7) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(glint, cx - r + 18, cy - r + 18, 2 * r - 36, 2 * r - 36, 200, 60);
        }
        return bmp;
    }

    static byte[] Png(Bitmap b)
    {
        using (var ms = new MemoryStream()) { b.Save(ms, ImageFormat.Png); return ms.ToArray(); }
    }

    // 32-bit DIB with an empty AND mask (transparency comes from the alpha channel).
    static byte[] Dib(Bitmap b)
    {
        int w = b.Width, h = b.Height;
        int maskStride = ((w + 31) / 32) * 4;
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(40); bw.Write(w); bw.Write(h * 2); bw.Write((short)1); bw.Write((short)32);
            bw.Write(0); bw.Write(w * h * 4 + maskStride * h); bw.Write(0); bw.Write(0); bw.Write(0); bw.Write(0);
            for (int y = h - 1; y >= 0; y--)
                for (int x = 0; x < w; x++)
                {
                    var c = b.GetPixel(x, y);
                    bw.Write(c.B); bw.Write(c.G); bw.Write(c.R); bw.Write(c.A);
                }
            bw.Write(new byte[maskStride * h]);
            return ms.ToArray();
        }
    }

    public static void WriteIco(string path, int[] sizes)
    {
        var images = new List<byte[]>();
        foreach (var s in sizes)
            using (var b = Draw(s))
                images.Add(s >= 64 ? Png(b) : Dib(b));

        using (var fs = File.Create(path))
        using (var bw = new BinaryWriter(fs))
        {
            bw.Write((short)0); bw.Write((short)1); bw.Write((short)sizes.Length);
            int offset = 6 + 16 * sizes.Length;
            for (int i = 0; i < sizes.Length; i++)
            {
                int s = sizes[i];
                bw.Write((byte)(s >= 256 ? 0 : s)); bw.Write((byte)(s >= 256 ? 0 : s));
                bw.Write((byte)0); bw.Write((byte)0);
                bw.Write((short)1); bw.Write((short)32);
                bw.Write(images[i].Length); bw.Write(offset);
                offset += images[i].Length;
            }
            foreach (var img in images) bw.Write(img);
        }
    }

    public static void WritePng(string path, int size)
    {
        using (var b = Draw(size)) b.Save(path, ImageFormat.Png);
    }
}
'@

$ico = Join-Path $RepoRoot 'src\FileSearch.App\Assets\FileSearch.ico'
$png = Join-Path $RepoRoot 'docs\icon.png'
New-Item -ItemType Directory -Force (Split-Path $ico), (Split-Path $png) | Out-Null

[FileSearchIcon]::WriteIco($ico, [int[]](16, 20, 24, 32, 40, 48, 64, 128, 256))
[FileSearchIcon]::WritePng($png, 256)

Write-Host "Wrote $ico ($((Get-Item $ico).Length) bytes)"
Write-Host "Wrote $png ($((Get-Item $png).Length) bytes)"
