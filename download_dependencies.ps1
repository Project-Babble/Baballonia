Invoke-WebRequest -Uri "https://github.com/Project-Babble/BabbleTrainer/releases/latest/download/BabbleTrainer-x64.exe" -OutFile "src/Baballonia.Desktop/Calibration/Windows/Trainer/BabbleTrainer.exe"
Invoke-WebRequest -Uri "https://github.com/Project-Babble/BabbleCalibration/releases/latest/download/Windows.zip" -OutFile "src/Baballonia.Desktop/Calibration/Windows/Overlay/Windows.zip"
Invoke-WebRequest -Uri "https://github.com/esp-rs/espflash/releases/latest/download/espflash-x86_64-pc-windows-msvc.zip" -OutFile "src/Baballonia/Firmware/Windows/espflash.zip"

Expand-Archive -Path "src/Baballonia.Desktop/Calibration/Windows/Overlay/Windows.zip" -DestinationPath "src/Baballonia.Desktop/Calibration/Windows/Overlay"

Remove-Item "src/Baballonia.Desktop/Calibration/Windows/Overlay/Windows.zip"

Expand-Archive -Path "src/Baballonia/Firmware/Windows/espflash.zip" -DestinationPath "src/Baballonia/Firmware/Windows"

Remove-Item "src/Baballonia/Firmware/Windows/espflash.zip"
