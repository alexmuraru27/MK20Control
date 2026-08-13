# Filters every .pcapng in this directory in-place, keeping only packets where the MK20
# device (matched by known VID:PID pairs 0x1d6b:0x0104 or 0x1234:0x5678, per
# PROTOCOL_WAVESHARE_MK20.md) is the sender or receiver. Overwrites each file with the
# filtered result (no separate "_sanitized" file is created).
$tshark = "C:\Program Files\Wireshark\tshark.exe"
$dir = $PSScriptRoot
$files = Get-ChildItem -Path $dir -Filter *.pcapng

foreach ($f in $files) {
    Write-Host "=== $($f.Name) ($([math]::Round($f.Length/1MB,2)) MB) ==="
    $lines = & $tshark -r $f.FullName -Y "usb.idVendor" -T fields -e usb.device_address -e usb.idVendor -e usb.idProduct 2>$null
    $addr = $null
    foreach ($line in $lines) {
        $parts = $line -split "`t"
        if ($parts.Count -lt 3) { continue }
        if ((($parts[1] -match "1d6b") -and ($parts[2] -match "0104")) -or
            (($parts[1] -match "1234") -and ($parts[2] -match "5678"))) {
            $addr = $parts[0]
            break
        }
    }
    if (-not $addr) {
        Write-Host "  MK20 device address not found - skipping (left unmodified)."
        continue
    }
    Write-Host "  MK20 device address: $addr"
    $tmp = "$($f.FullName).tmp"
    & $tshark -r $f.FullName -Y "usb.device_address == $addr" -w $tmp 2>$null
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tmp)) {
        Write-Host "  tshark filter failed - leaving original untouched."
        Remove-Item $tmp -ErrorAction SilentlyContinue
        continue
    }
    $oldSize = $f.Length
    Move-Item -Force $tmp $f.FullName
    $newSize = (Get-Item $f.FullName).Length
    Write-Host "  $oldSize bytes -> $newSize bytes"
}
Write-Host "Done."
