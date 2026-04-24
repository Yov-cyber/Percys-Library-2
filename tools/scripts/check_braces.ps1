$path = 'c:\Users\O11CE\OneDrive\Desktop\Percy Library\MainWindow.cs'
$lines = Get-Content $path
$bal = 0
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $opens = ($line -split '\{').Length - 1
    $closes = ($line -split '\}').Length - 1
    $bal += $opens - $closes
    if ($i -lt 1200 -and ($i % 50) -eq 0) {
        Write-Host "Line $($i+1): balance=$bal"
    }
    if ($bal -eq 0) {
        Write-Host "Balance zero at line $($i+1)"
        break
    }
}
Write-Host "Final balance: $bal"