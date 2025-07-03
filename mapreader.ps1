# Launch the MapReader tool with the specified arguments
$ScriptDir = Split-Path -Path $MyInvocation.MyCommand.Definition -Parent
Set-Location $ScriptDir
[Environment]::CurrentDirectory = $ScriptDir

$Arguments = $args
& "$ScriptDir\bin\OpenRA.MapReader.exe" @Arguments

# Pause if not running in a terminal or if the user is double-clicking the PS1 file
if ([Environment]::UserInteractive -and -not $psISE -and [Environment]::CurrentDirectory -eq $ScriptDir) {
    Write-Output "Press any key to continue..."
    [Console]::ReadKey() | Out-Null
}
