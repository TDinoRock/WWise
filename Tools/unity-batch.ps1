<#
Runs Unity headless (batch mode) on this project and prints only the useful lines.

  .\Tools\unity-batch.ps1                              # compile check: imports + compiles, reports errors
  .\Tools\unity-batch.ps1 -Method Namespace.Class.Method   # run a static editor method, then quit
  .\Tools\unity-batch.ps1 -Tests EditMode              # run Unity Test Framework tests (EditMode or PlayMode)

Unity refuses to open a project that is already open, so CLOSE THE UNITY EDITOR FIRST.
The full log is kept in Logs\batch.log.
#>
param(
    [string]$Method = "",
    [ValidateSet("", "EditMode", "PlayMode")] [string]$Tests = "",
    [int]$TimeoutMinutes = 20
)

$project = Split-Path $PSScriptRoot -Parent
$version = (Select-String -Path "$project\ProjectSettings\ProjectVersion.txt" -Pattern "m_EditorVersion: (.+)").Matches[0].Groups[1].Value.Trim()
$unity = "C:\Program Files\Unity\Hub\Editor\$version\Editor\Unity.exe"
if (-not (Test-Path $unity)) { Write-Error "Unity $version not found at $unity"; exit 2 }

# Is the editor open on this project? Check running Unity processes for our project path,
# then the lock file the editor holds while the project is loaded.
$open = Get-CimInstance Win32_Process -Filter "Name='Unity.exe'" |
    Where-Object { $_.CommandLine -and $_.CommandLine.ToLower().Contains($project.ToLower()) }
if ($open) { Write-Error "The project is open in the Unity Editor (PID $($open[0].ProcessId)). Close it first."; exit 3 }
$lock = "$project\Temp\UnityLockfile"
if (Test-Path $lock) {
    try { [IO.File]::Open($lock, 'Open', 'ReadWrite', 'None').Close() }
    catch { Write-Error "The project is open in the Unity Editor. Close it first."; exit 3 }
}

New-Item -ItemType Directory -Force "$project\Logs" | Out-Null
$log = "$project\Logs\batch.log"
$results = "$project\Logs\test-results.xml"
$unityArgs = @("-batchmode", "-nographics", "-projectPath", "`"$project`"", "-logFile", "`"$log`"")

if ($Tests) {
    # -runTests quits on its own when the tests finish.
    $unityArgs += @("-runTests", "-testPlatform", $Tests, "-testResults", "`"$results`"")
} else {
    if ($Method) { $unityArgs += @("-executeMethod", $Method) }
    $unityArgs += "-quit"
}

Write-Host "Unity $version batch mode: $($unityArgs -join ' ')"
$proc = Start-Process -FilePath $unity -ArgumentList $unityArgs -PassThru -WindowStyle Hidden
if (-not $proc.WaitForExit($TimeoutMinutes * 60 * 1000)) { $proc.Kill(); Write-Error "Timed out after $TimeoutMinutes min"; exit 4 }

# Short summary instead of the whole (very long) log.
$errors = Select-String -Path $log -Pattern "error CS\d+|Exception:|Scripts have compiler errors|executeMethod.*(failed|could not)" | Select-Object -ExpandProperty Line -Unique
if ($errors) { Write-Host "`n--- Errors ---"; $errors | Select-Object -First 30 | ForEach-Object { Write-Host $_ } }

if ($Tests -and (Test-Path $results)) {
    [xml]$x = Get-Content $results
    $r = $x.'test-run'
    Write-Host "`n--- Tests ($Tests) --- total $($r.total), passed $($r.passed), failed $($r.failed), skipped $($r.skipped)"
    $x.SelectNodes("//test-case[@result='Failed']") | ForEach-Object { Write-Host "FAILED: $($_.fullname)`n  $($_.failure.message.'#cdata-section')" }
}

Write-Host "`nExit code $($proc.ExitCode). Full log: $log"
exit $proc.ExitCode
