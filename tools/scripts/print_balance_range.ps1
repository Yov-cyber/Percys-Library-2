param(
    [string]$path = "c:\Users\O11CE\OneDrive\Desktop\Percy Library\MainWindow.cs",
    [int]$start = 1,
    [int]$end = 2000
)
$lines = Get-Content $path
$bal = 0
for ($i = 1; $i -le $lines.Length; $i++) {
    $line = $lines[$i-1]
    $opens = ($line -split '{').Length - 1
    $closes = ($line -split '}').Length - 1
    $bal += $opens - $closes
    if ($i -ge $start -and $i -le $end) {
        Write-Host ("{0,5}: bal={1,4} opens={2,2} closes={3,2} | {4}" -f $i, $bal, $opens, $closes, $line)
    }
}
Write-Host "FinalBalance: $bal"