# Launches RimWorld with a separate test profile (own mod list, settings and saves),
# so the real profile is never touched.
#   -Quick   skip the menus: generate a small Crashlanded world and open the creator directly
#   -Tour    -Quick, then click through every tab, press Play and screenshot each step
#            (screenshots go to the test profile's Screenshots folder)
#   -Menu    open New Colony from the main menu, screenshot the popup, pick Normal, screenshot the vanilla page
#   -Explore -Quick, then walk every feature through its edge cases with checks and a layout lint, then start
#            the game (results are "[CAC explore]" lines in the log, screenshots cac_explore_*)
#   -Full    use the second test profile, which has a copy of the full everyday mod list
param([switch]$Quick, [switch]$Tour, [switch]$Explore, [switch]$Menu, [switch]$Full)
$game = "C:\Program Files (x86)\Steam\steamapps\common\RimWorld\RimWorld.exe"
# No spaces allowed in this path: RimWorld splits its command line on spaces.
$profile = Join-Path $env:USERPROFILE ("AppData\LocalLow\CreateAColonist-TestProfile" + $(if ($Full) { "Full" } else { "" }))
$gameArgs = "-savedatafolder=`"$profile`""
# The test flags need the dev build. Remember to run build.ps1 (release) again before publishing.
if ($Quick -or $Tour -or $Explore -or $Menu) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "build.ps1") -Dev
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
if ($Quick -or $Tour -or $Explore) { $gameArgs += " -cacTest" }
if ($Explore) { $gameArgs += " -cacExplore" }
if ($Tour) { $gameArgs += " -cacTour" }
if ($Menu) { $gameArgs += " -cacMenu" }
Start-Process -FilePath $game -ArgumentList $gameArgs -WorkingDirectory (Split-Path $game)
