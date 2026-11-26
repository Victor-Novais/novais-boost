# NOVAIS boost - protótipo do banner (PowerShell 7+)
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$e = [char]27

$novais = @(
    '███╗   ██╗ ██████╗ ██╗   ██╗ █████╗ ██╗███████╗'
    '████╗  ██║██╔═══██╗██║   ██║██╔══██╗██║██╔════╝'
    '██╔██╗ ██║██║   ██║██║   ██║███████║██║███████╗'
    '██║╚██╗██║██║   ██║╚██╗ ██╔╝██╔══██║██║╚════██║'
    '██║ ╚████║╚██████╔╝ ╚████╔╝ ██║  ██║██║███████║'
    '╚═╝  ╚═══╝ ╚═════╝   ╚═══╝  ╚═╝  ╚═╝╚═╝╚══════╝'
)
$boost = @(
    '██████╗  ██████╗  ██████╗ ███████╗████████╗'
    '██╔══██╗██╔═══██╗██╔═══██╗██╔════╝╚══██╔══╝'
    '██████╔╝██║   ██║██║   ██║███████╗   ██║   '
    '██╔══██╗██║   ██║██║   ██║╚════██║   ██║   '
    '██████╔╝╚██████╔╝╚██████╔╝███████║   ██║   '
    '╚═════╝  ╚═════╝  ╚═════╝ ╚══════╝   ╚═╝   '
)

# Degradê roxo: #7C3AED -> #C084FC
$from = 124, 58, 237; $to = 192, 132, 252
Write-Host ''
for ($i = 0; $i -lt $novais.Count; $i++) {
    $t = $i / ($novais.Count - 1)
    $r = [int]($from[0] + ($to[0] - $from[0]) * $t)
    $g = [int]($from[1] + ($to[1] - $from[1]) * $t)
    $b = [int]($from[2] + ($to[2] - $from[2]) * $t)
    Write-Host "  $e[1;38;2;${r};${g};${b}m$($novais[$i])$e[0m"
}
foreach ($line in $boost) {
    Write-Host "       $e[1;38;2;255;255;255m$line$e[0m"
}
Write-Host ''
Write-Host "  $e[38;2;168;85;247m━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━$e[0m"
Write-Host "  $e[38;2;200;200;200m Max FPS · Low Input Lag · Windows 11 Gaming Optimizer$e[0m"
Write-Host "  $e[38;2;168;85;247m━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━$e[0m"
Write-Host ''
$menu = @(
    , @('1', 'Diagnóstico completo', '5', 'Process Guardian')
    , @('2', 'Otimização Universal', '6', 'Cronogramas')
    , @('3', 'Perfil do meu Hardware', '7', 'Benchmark antes/depois')
    , @('4', 'Jogos & Streaming', '8', 'Reverter / Backups')
)
$k = "$e[1;38;2;192;132;252m"; $x = "$e[0m"
foreach ($m in $menu) {
    Write-Host ("  {0}[{1}]{2} {3,-26}{0}[{4}]{2} {5}" -f $k, $m[0], $x, $m[1], $m[2], $m[3])
}
Write-Host ''
