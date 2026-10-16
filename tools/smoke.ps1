# Her ekrani sirayla yukleyerek XAML ayristirma ve binding hatalarini yakalar.
# LLM gerekmez: adim gostergesindeki butonlarla gezinilir.
param(
    [string]$Exe = "DataModelGenerator.App\bin\Debug\net8.0-windows\DataModelGenerator.exe",
    [string]$OutDir = "tools\shots"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 4

if ($proc.HasExited) {
    Write-Host "HATA: uygulama acilisla birlikte kapandi (cikis kodu $($proc.ExitCode))."
    exit 1
}

function Get-Window {
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $proc.Id)
    return $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
}

function Save-Shot([string]$name) {
    $win = Get-Window
    if ($null -eq $win) { return }
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path (Resolve-Path $OutDir) "$name.png"))
    $g.Dispose(); $bmp.Dispose()
}

# Adim basliklari Turkce karakter icerdigi icin sabit metinle aranmaz;
# "1. ", "2. " ile baslayan butonlar agactan okunur.
function Get-StepButtons {
    $win = Get-Window
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $all = $win.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)

    $found = @()
    foreach ($el in $all) {
        if ($el.Current.Name -match '^\d\.\s') { $found += $el }
    }
    return $found | Sort-Object { $_.Current.Name }
}

$buttons = Get-StepButtons
if ($buttons.Count -lt 6) {
    Write-Host "HATA: adim gostergesinde 6 buton beklenirken $($buttons.Count) bulundu."
    exit 1
}

$index = 0
foreach ($btn in $buttons) {
    $name = $btn.Current.Name
    $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1200

    if ($proc.HasExited) {
        Write-Host "HATA: '$name' adiminda uygulama cokti (cikis kodu $($proc.ExitCode))."
        exit 1
    }

    Save-Shot ("step{0}" -f $index)
    Write-Host "YUKLENDI: $name"
    $index++
}

Write-Host "TAMAM: butun ekranlar yuklendi, uygulama ayakta."
$proc.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited) { $proc.Kill() }
