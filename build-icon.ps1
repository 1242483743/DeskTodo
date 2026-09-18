param([string]$InputImage, [string]$TargetIcon)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName PresentationCore, WindowsBase
if (-not $InputImage -or -not $TargetIcon) { throw 'InputImage and TargetIcon are required.' }
# ICO export only: keep the original PNG unchanged, fit its main artwork rather
# than its transparent gutters, and supply native sizes used by Windows shells.
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.IO;
public static class DeskTodoIconExport {
    public static RectangleF ArtworkBounds(Bitmap image) {
        int[] rows = new int[image.Height], columns = new int[image.Width];
        for (int y = 0; y < image.Height; y++)
            for (int x = 0; x < image.Width; x++)
                if (image.GetPixel(x, y).A >= 128) { rows[y]++; columns[x]++; }
        int left = image.Width, top = image.Height, right = -1, bottom = -1;
        // Ignore isolated export specks in the transparent gutters.
        for (int y = 0; y < rows.Length; y++)
            if (rows[y] >= image.Width / 5) { top = Math.Min(top, y); bottom = y; }
        for (int x = 0; x < columns.Length; x++)
            if (columns[x] >= image.Height / 5) { left = Math.Min(left, x); right = x; }
        if (right < left || bottom < top) throw new InvalidDataException("No main artwork found.");
        float padding = Math.Max(right - left + 1, bottom - top + 1) * .008f;
        return RectangleF.FromLTRB(Math.Max(0, left - padding), Math.Max(0, top - padding),
            Math.Min(image.Width, right + 1 + padding), Math.Min(image.Height, bottom + 1 + padding));
    }
    public static byte[] Dib(Bitmap bitmap) {
        int size = bitmap.Width, maskStride = ((size + 31) / 32) * 4;
        using (MemoryStream stream = new MemoryStream())
        using (BinaryWriter writer = new BinaryWriter(stream)) {
            writer.Write(40); writer.Write(size); writer.Write(size * 2);
            writer.Write((ushort)1); writer.Write((ushort)32); writer.Write(0);
            writer.Write(size * size * 4 + maskStride * size);
            writer.Write(0); writer.Write(0); writer.Write(0); writer.Write(0);
            for (int y = size - 1; y >= 0; y--)
                for (int x = 0; x < size; x++) {
                    Color color = bitmap.GetPixel(x, y);
                    writer.Write(color.A == 0 ? (byte)0 : color.B);
                    writer.Write(color.A == 0 ? (byte)0 : color.G);
                    writer.Write(color.A == 0 ? (byte)0 : color.R); writer.Write(color.A);
                }
            for (int y = size - 1; y >= 0; y--) {
                byte[] mask = new byte[maskStride];
                for (int x = 0; x < size; x++)
                    if (bitmap.GetPixel(x, y).A < 128) mask[x / 8] |= (byte)(128 >> (x % 8));
                writer.Write(mask);
            }
            writer.Flush(); return stream.ToArray();
        }
    }
}
'@
$taskImage = [System.Drawing.Bitmap]::new((Resolve-Path -LiteralPath $InputImage).Path)
$taskFrames = [System.Collections.Generic.List[byte[]]]::new()
$taskSizes = @(16,20,24,32,40,48,64,96,128,256)
try {
    foreach ($taskPoint in @(@(0,0),@(($taskImage.Width-1),0),@(0,($taskImage.Height-1)),@(($taskImage.Width-1),($taskImage.Height-1)))) {
        if ($taskImage.GetPixel($taskPoint[0],$taskPoint[1]).A -ne 0) { throw 'The PNG does not have transparent corners.' }
    }
    $taskSource = [DeskTodoIconExport]::ArtworkBounds($taskImage)
    Write-Output ('ICO artwork bounds: ' + $taskSource)
    foreach ($taskSize in $taskSizes) {
        $taskBitmap = [System.Drawing.Bitmap]::new($taskSize,$taskSize,[System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $taskGraphics = [System.Drawing.Graphics]::FromImage($taskBitmap)
        $taskBytes = [System.IO.MemoryStream]::new()
        try {
            $taskGraphics.Clear([System.Drawing.Color]::Transparent)
            $taskGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $taskGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $taskGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $taskScale = ($taskSize * .98) / [Math]::Max($taskSource.Width,$taskSource.Height)
            $taskWidth = $taskSource.Width * $taskScale; $taskHeight = $taskSource.Height * $taskScale
            $taskDestination = [System.Drawing.RectangleF]::new(($taskSize-$taskWidth)/2,($taskSize-$taskHeight)/2,$taskWidth,$taskHeight)
            $taskGraphics.DrawImage($taskImage,$taskDestination,$taskSource,[System.Drawing.GraphicsUnit]::Pixel)
            # Small frames use 32-bit DIB + AND mask; only 256px is PNG-compressed.
            if ($taskSize -eq 256) {
                $taskBitmap.Save($taskBytes,[System.Drawing.Imaging.ImageFormat]::Png)
                $taskFrames.Add($taskBytes.ToArray())
            } else { $taskFrames.Add([DeskTodoIconExport]::Dib($taskBitmap)) }
            if ($taskSize -in @(32,40,48,256)) {
                $taskBitmap.Save((Join-Path $PSScriptRoot ('icon-preview-' + $taskSize + '.png')),[System.Drawing.Imaging.ImageFormat]::Png)
            }
        } finally { $taskBytes.Dispose(); $taskGraphics.Dispose(); $taskBitmap.Dispose() }
    }
} finally { $taskImage.Dispose() }
$taskFile = [System.IO.File]::Create([System.IO.Path]::GetFullPath($TargetIcon))
$taskWriter = [System.IO.BinaryWriter]::new($taskFile)
try {
    $taskWriter.Write([uint16]0); $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]$taskSizes.Count)
    $taskOffset = 6 + 16 * $taskSizes.Count
    for ($taskIndex=0; $taskIndex -lt $taskSizes.Count; $taskIndex++) {
        $taskDimension = if ($taskSizes[$taskIndex] -eq 256) { 0 } else { $taskSizes[$taskIndex] }
        $taskWriter.Write([byte]$taskDimension); $taskWriter.Write([byte]$taskDimension)
        $taskWriter.Write([byte]0); $taskWriter.Write([byte]0)
        $taskWriter.Write([uint16]1); $taskWriter.Write([uint16]32)
        $taskWriter.Write([uint32]$taskFrames[$taskIndex].Length); $taskWriter.Write([uint32]$taskOffset)
        $taskOffset += $taskFrames[$taskIndex].Length
    }
    foreach ($taskFrame in $taskFrames) { $taskWriter.Write($taskFrame) }
} finally { $taskWriter.Dispose() }
$taskRead = [System.IO.File]::OpenRead([System.IO.Path]::GetFullPath($TargetIcon))
try {
    $taskDecoder = [System.Windows.Media.Imaging.IconBitmapDecoder]::new($taskRead,[System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,[System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
    if ($taskDecoder.Frames.Count -ne $taskSizes.Count) { throw 'ICO frame count mismatch.' }
    foreach ($taskFrame in $taskDecoder.Frames) {
        $taskPixels = [byte[]]::new($taskFrame.PixelWidth * $taskFrame.PixelHeight * 4)
        $taskBgra = [System.Windows.Media.Imaging.FormatConvertedBitmap]::new($taskFrame,[System.Windows.Media.PixelFormats]::Bgra32,$null,0)
        $taskBgra.CopyPixels($taskPixels,$taskFrame.PixelWidth*4,0)
        $taskLastX = $taskFrame.PixelWidth-1; $taskLastY = $taskFrame.PixelHeight-1
        foreach ($taskCorner in @(3,($taskLastX*4+3),($taskLastY*$taskFrame.PixelWidth*4+3),($taskPixels.Length-1))) {
            if ($taskPixels[$taskCorner] -ne 0) { throw 'Nontransparent ICO corner.' }
        }
        $taskOpaqueAcross = 0; $taskCenterY = [int][Math]::Floor($taskFrame.PixelHeight/2)
        for ($taskX=0; $taskX -lt $taskFrame.PixelWidth; $taskX++) {
            if ($taskPixels[($taskCenterY*$taskFrame.PixelWidth+$taskX)*4+3] -ge 128) { $taskOpaqueAcross++ }
        }
        if ($taskOpaqueAcross -lt $taskFrame.PixelWidth*.9) { throw 'ICO artwork has excessive transparent gutters.' }
        Write-Output ('PASS: ' + $taskFrame.PixelWidth + 'px ICO: transparent corners, artwork ' + $taskOpaqueAcross + 'px wide.')
    }
} finally { $taskRead.Dispose() }
