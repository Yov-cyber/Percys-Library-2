param(
    [string]$path = "c:\Users\O11CE\OneDrive\Desktop\Percy Library\MainWindow.cs",
    [int]$start = 1,
    [int]$end = 200
)
$lines = Get-Content $path
for ($i = $start; $i -le $end; $i++) {
    $ln = $lines[$i-1]
    Write-Host ("{0,5}: {1}" -f $i, $ln)
}