# Downloads Tesseract language data needed for OCR.
# Run once from this folder:  powershell -File get-tessdata.ps1 [-Lang eng]
param(
    [string[]]$Lang = @('eng')
)

$ErrorActionPreference = 'Stop'
$dest = Join-Path $PSScriptRoot 'tessdata'
New-Item -ItemType Directory -Force -Path $dest | Out-Null

foreach ($l in $Lang) {
    $file = "$l.traineddata"
    $url = "https://github.com/tesseract-ocr/tessdata_fast/raw/main/$file"
    $out = Join-Path $dest $file
    if (Test-Path $out) {
        Write-Host "Already present: $file"
        continue
    }
    Write-Host "Downloading $file ..."
    Invoke-WebRequest -Uri $url -OutFile $out
    Write-Host "Saved $out"
}

Write-Host "Done. Rebuild (dotnet build) so tessdata is copied next to the exe."
