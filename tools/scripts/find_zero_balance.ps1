$path = 'c:\Users\O11CE\OneDrive\Desktop\Percy Library\MainWindow.cs'
$lines = Get-Content $path
$bal = 0
$first = $null
for ($i = 0; $i -lt $lines.Length; $i++) {
    $line = $lines[$i]
    $opens = ($line -split '\{').Length - 1
    $closes = ($line -split '\}').Length - 1
    $bal += $opens - $closes
    if ($null -eq $first -and $bal -eq 0 -and $i -gt 0) { $first = $i+1; break }
}
if ($null -ne $first) { Write-Host "First zero balance at line $first" } else { Write-Host "No zero balance found; final balance=$bal" }