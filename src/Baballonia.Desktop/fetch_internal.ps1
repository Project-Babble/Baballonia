param(
    [string]$Url = "http://217.154.52.44:7771/builds/trainer/1.0.0.0.zip",
    [string]$Output = "_internal.zip",
    [string]$ExpectedHash = "02695FE8E209CA2534BDD3285C0CF15CF9E55F84C4F6142B8C33336E430ECC31"
)

Write-Host "Downloading file from $Url ..."

# Download using Invoke-WebRequest (built-in)
Invoke-WebRequest -Uri $Url -OutFile $Output -UseBasicParsing

Write-Host "Download complete. Verifying SHA256..."

# Compute SHA256
$FileHash = (Get-FileHash -Path $Output -Algorithm SHA256).Hash.ToLower()

if ($FileHash -eq $ExpectedHash.ToLower()) {
    Write-Host "✅ File verified. SHA256 matches."
    exit 0
} else {
    Write-Host "❌ SHA256 mismatch!"
    Write-Host "Expected: $ExpectedHash"
    Write-Host "Actual:   $FileHash"
    Remove-Item -Path $Output -Force
    exit 1
}
