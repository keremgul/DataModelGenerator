# Uctan uca akis: kural gir -> model uret -> soru cevapla -> sohbetten alan adi degistir.
# tools/mock-llm.mjs calisiyor olmali.
param(
    [string]$Exe = "DataModelGenerator.App\bin\Debug\net8.0-windows\DataModelGenerator.exe",
    [string]$OutDir = "tools\shots"
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes, System.Windows.Forms, System.Drawing

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$proc = Start-Process -FilePath $Exe -PassThru
Start-Sleep -Seconds 4

function Get-Window {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::ProcessIdProperty, $proc.Id)
    return $AE::RootElement.FindFirst($TS::Children, $cond)
}

function Find-By([string]$name) {
    $cond = New-Object System.Windows.Automation.PropertyCondition($AE::NameProperty, $name)
    return (Get-Window).FindFirst($TS::Descendants, $cond)
}

function Find-All-Buttons {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Button)
    return (Get-Window).FindAll($TS::Descendants, $cond)
}

function Invoke-Named([string]$name) {
    $el = Find-By $name
    if ($null -eq $el) { throw "Bulunamadi: $name" }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1200
}

function Set-Text($el, [string]$text) {
    if ($null -eq $el) { throw "Metin kutusu bulunamadi" }
    $el.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).SetValue($text)
    Start-Sleep -Milliseconds 300
}

function Save-Shot([string]$name) {
    $win = Get-Window
    $r = $win.Current.BoundingRectangle
    $bmp = New-Object System.Drawing.Bitmap([int]$r.Width, [int]$r.Height)
    $gfx = [System.Drawing.Graphics]::FromImage($bmp)
    $gfx.CopyFromScreen([int]$r.X, [int]$r.Y, 0, 0, $bmp.Size)
    $bmp.Save((Join-Path (Resolve-Path $OutDir) "$name.png"))
    $gfx.Dispose(); $bmp.Dispose()
}

function Status {
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)
    $texts = (Get-Window).FindAll($TS::Descendants, $cond)
    $out = @()
    foreach ($t in $texts) { $out += $t.Current.Name }
    return $out
}

function Click-Step([int]$index) {
    $buttons = @()
    foreach ($b in Find-All-Buttons) { if ($b.Current.Name -match '^\d\.\s') { $buttons += $b } }
    $buttons = $buttons | Sort-Object { $_.Current.Name }
    $buttons[$index].GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 1000
}

# --- 1. Baglanti: kayitli mock profili kullan -------------------------------
Click-Step 0
$use = Find-By "Bu Profili Kullan"
if ($null -eq $use) {
    foreach ($b in Find-All-Buttons) { if ($b.Current.Name -like "*Kullan*") { $use = $b; break } }
}
$use.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
Start-Sleep -Seconds 1
Write-Host "1) baglanti profili secildi"

# --- 2. Girdi: kurallari yaz ------------------------------------------------
Click-Step 1
$cond = New-Object System.Windows.Automation.PropertyCondition(
    $AE::ControlTypeProperty, [System.Windows.Automation.ControlType]::Edit)
$edits = (Get-Window).FindAll($TS::Descendants, $cond)
Set-Text $edits[0] "Her musterinin tekil bir musteri numarasi ve unvani vardir.`nBir musteri birden cok siparis verebilir ve her siparisin tutari tutulur."
Invoke-Named "Ekle"
Save-Shot "flow-1-girdi"
Write-Host "2) kurallar eklendi"

Invoke-Named "Modeli Üret →"
Start-Sleep -Seconds 1

# --- 3. Uretim --------------------------------------------------------------
Invoke-Named "Modeli Üret"
Start-Sleep -Seconds 6
Save-Shot "flow-2-uretim"
Write-Host "3) uretim: $((Status) -match 'Üretim tamamland' -join ' ')"

# Ozet sekmesi (izlenebilirlik tablosu)
$tab = Find-By "Özet"
if ($null -ne $tab) {
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1200
    Save-Shot "flow-3-izlenebilirlik"
}

$tab = Find-By "Beklemede"
if ($null -ne $tab) {
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1000
    Save-Shot "flow-4-beklemede"
}

$tab = Find-By "Öneriler"
if ($null -ne $tab) {
    $tab.GetCurrentPattern([System.Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 1000
    Save-Shot "flow-4b-oneriler"
}

# --- 4. Sorular -------------------------------------------------------------
Click-Step 3
Start-Sleep -Milliseconds 800
Save-Shot "flow-5-sorular"

$kullan = $null
foreach ($b in Find-All-Buttons) { if ($b.Current.Name -eq "Bu cevabı kullan") { $kullan = $b; break } }
if ($null -ne $kullan) {
    $kullan.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 600
    Save-Shot "flow-6-cevap-kullan"
    Write-Host "4) 'Bu cevabi kullan' calisti"
} else {
    Write-Host "4) UYARI: 'Bu cevabi kullan' butonu bulunamadi"
}

Invoke-Named "Cevapları Uygula (kısmi güncelleme)"
Start-Sleep -Seconds 5
Save-Shot "flow-7-cevap-uygulandi"

# --- 5. Sohbet --------------------------------------------------------------
Click-Step 4
Start-Sleep -Milliseconds 800
$msg = Find-By "SohbetMesaji"
Set-Text $msg "Siparis tablosunun Tutar alaninin adini NetTutar yap"
Invoke-Named "Gönder"
Start-Sleep -Seconds 5
Save-Shot "flow-8-sohbet"

# Sohbet modelin güncel halini biliyor mu: alan ekle, sonra aynı alanı kaldır.
Set-Text (Find-By "SohbetMesaji") "Musteri tablosuna vergi numarasi alani ekle"
Invoke-Named "Gönder"
Start-Sleep -Seconds 5
Save-Shot "flow-8b-alan-eklendi"

Set-Text (Find-By "SohbetMesaji") "Musteri tablosundan VergiNo alanini kaldir"
Invoke-Named "Gönder"
Start-Sleep -Seconds 5
Save-Shot "flow-8c-alan-kaldirildi"

# --- 6. Adim gostergesi -----------------------------------------------------
Click-Step 5
Start-Sleep -Milliseconds 800
Save-Shot "flow-9-disa-aktar"

Write-Host "--- durum satirlari ---"
Status | Where-Object { $_ -match 'Uygulan|değişiklik|güncel|tamamland|cevap' } | Select-Object -First 10

$proc.CloseMainWindow() | Out-Null
Start-Sleep -Seconds 2
if (-not $proc.HasExited) { $proc.Kill() }
Write-Host "AKIS TAMAM"
