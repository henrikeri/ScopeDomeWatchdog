Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

function Create-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    
    # Background circle (blue)
    $blueBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 120, 212))
    $bluePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(0, 90, 158), [Math]::Max(1, $size/64))
    $margin = $size * 0.03
    $g.FillEllipse($blueBrush, $margin, $margin, $size - 2*$margin, $size - 2*$margin)
    $g.DrawEllipse($bluePen, $margin, $margin, $size - 2*$margin, $size - 2*$margin)
    
    # Dome (white semicircle) - using integer coordinates
    $whiteBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::White)
    $domeX = [int]($size * 0.19)
    $domeY = [int]($size * 0.34)
    $domeW = [int]($size * 0.63)
    $domeH = [int]($size * 0.35)
    $domeRect = New-Object System.Drawing.Rectangle($domeX, $domeY, $domeW, $domeH)
    $g.FillPie($whiteBrush, $domeRect, 180, 180)
    $g.DrawArc($bluePen, $domeRect, 180, 180)
    
    # Dome base
    $baseY = [int]($size * 0.58)
    $baseH = [int]($size * 0.08)
    $g.FillRectangle($whiteBrush, $domeX, $baseY, $domeW, $baseH)
    $g.DrawLine($bluePen, $domeX, $baseY, $domeX + $domeW, $baseY)
    
    # Watchdog eyes (golden)
    $goldBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 215, 0))
    $darkBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(0, 90, 158))
    
    # Left eye
    $eyeSize = [int]($size * 0.09)
    $pupilSize = [int]($size * 0.05)
    $leftEyeX = [int]($size * 0.32)
    $leftEyeY = [int]($size * 0.50)
    $g.FillEllipse($darkBrush, $leftEyeX, $leftEyeY, $eyeSize, $eyeSize)
    $g.FillEllipse($goldBrush, $leftEyeX + ($eyeSize - $pupilSize)/2, $leftEyeY + ($eyeSize - $pupilSize)/2, $pupilSize, $pupilSize)
    
    # Right eye
    $rightEyeX = [int]($size * 0.59)
    $g.FillEllipse($darkBrush, $rightEyeX, $leftEyeY, $eyeSize, $eyeSize)
    $g.FillEllipse($goldBrush, $rightEyeX + ($eyeSize - $pupilSize)/2, $leftEyeY + ($eyeSize - $pupilSize)/2, $pupilSize, $pupilSize)
    
    # Alert shield (bottom) - simple triangle
    $shieldTop = [System.Drawing.PointF]::new($size * 0.50, $size * 0.70)
    $shieldLeft = [System.Drawing.PointF]::new($size * 0.42, $size * 0.88)
    $shieldRight = [System.Drawing.PointF]::new($size * 0.58, $size * 0.88)
    $shieldPoints = @($shieldTop, $shieldLeft, $shieldRight)
    $g.FillPolygon($goldBrush, $shieldPoints)
    
    # Exclamation mark
    if ($size -ge 32) {
        $exclamPen = New-Object System.Drawing.Pen($darkBrush, [Math]::Max(2, $size/32))
        $exclamPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
        $exclamPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $g.DrawLine($exclamPen, $size * 0.50, $size * 0.76, $size * 0.50, $size * 0.82)
        $dotSize = [Math]::Max(2, $size * 0.02)
        $g.FillEllipse($darkBrush, $size * 0.50 - $dotSize/2, $size * 0.84, $dotSize, $dotSize)
        $exclamPen.Dispose()
    }
    
    $g.Dispose()
    $blueBrush.Dispose()
    $bluePen.Dispose()
    $whiteBrush.Dispose()
    $goldBrush.Dispose()
    $darkBrush.Dispose()
    
    return $bmp
}

# Create bitmaps for different sizes
$sizes = @(16, 32, 48, 256)
$bitmaps = @{}
foreach ($size in $sizes) {
    Write-Host "Creating ${size}x${size} bitmap..."
    $bitmaps[$size] = Create-IconBitmap $size
}

# Save the 256x256 as PNG for reference
$bitmaps[256].Save("$PSScriptRoot\icon_preview.png", [System.Drawing.Imaging.ImageFormat]::Png)
Write-Host "Preview saved: icon_preview.png"

# Create ICO file manually
$icoPath = "$PSScriptRoot\app.ico"
$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICO header
$bw.Write([UInt16]0)  # Reserved
$bw.Write([UInt16]1)  # Type (1 = ICO)
$bw.Write([UInt16]$sizes.Count)  # Number of images

# Prepare PNG data for each size
$pngData = @{}
foreach ($size in $sizes) {
    $ms = New-Object System.IO.MemoryStream
    $bitmaps[$size].Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngData[$size] = $ms.ToArray()
    $ms.Dispose()
}

# Calculate offsets and write directory
$offset = 6 + ($sizes.Count * 16)
foreach ($size in $sizes) {
    $data = $pngData[$size]
    $sizeValue = if ($size -eq 256) { 0 } else { $size }
    $bw.Write([byte]$sizeValue)  # Width (0 means 256)
    $bw.Write([byte]$sizeValue)  # Height
    $bw.Write([byte]0)  # Color palette
    $bw.Write([byte]0)  # Reserved
    $bw.Write([UInt16]1)  # Color planes
    $bw.Write([UInt16]32)  # Bits per pixel
    $bw.Write([UInt32]$data.Length)  # Image data size
    $bw.Write([UInt32]$offset)  # Offset to image data
    $offset += $data.Length
}

# Write image data
foreach ($size in $sizes) {
    $bw.Write($pngData[$size])
}

$bw.Close()
$fs.Close()

# Cleanup
foreach ($bmp in $bitmaps.Values) {
    $bmp.Dispose()
}

Write-Host "SUCCESS: Icon created at $icoPath"
