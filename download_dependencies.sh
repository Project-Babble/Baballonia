#!/bin/bash

os=$(uname -s)
arch=$(uname -m)

if [[ $os == "Darwin" ]]; then
  if [[ $arch != "arm64" ]]; then
    echo "$arch not supported for macOS." >&2
    exit 1
  fi

  espflashDownloadPath="https://github.com/esp-rs/espflash/releases/latest/download/espflash-aarch64-apple-darwin.zip"

  curl -L $espflashDownloadPath -o "src/Baballonia/Firmware/MacOS/espflash.zip"

  unzip "src/Baballonia/Firmware/MacOS/espflash.zip" -d "src/Baballonia/Firmware/MacOS"
  rm "src/Baballonia/Firmware/MacOS/espflash.zip"
elif [[ $os == "Linux" ]]; then
  calibrationDownloadPath="https://github.com/Project-Babble/BabbleCalibration/releases/latest/download/Linux.zip"
  trainerDownloadPath="https://github.com/Project-Babble/BabbleTrainer/releases/latest/download/BabbleTrainer-x64"
  espflashDownloadPath="https://github.com/esp-rs/espflash/releases/latest/download/espflash-x86_64-unknown-linux-gnu.zip"

  if [[ $arch == *"arm"* || $arch == "aarch64" ]]; then
    calibrationDownloadPath="https://github.com/Project-Babble/BabbleCalibration/releases/latest/download/Linux-ARM.zip"
    trainerDownloadPath="https://github.com/Project-Babble/BabbleTrainer/releases/latest/download/BabbleTrainer-arm64"
    espflashDownloadPath="https://github.com/esp-rs/espflash/releases/latest/download/espflash-aarch64-unknown-linux-gnu.zip"
  fi

  curl -Z -L $trainerDownloadPath -L $calibrationDownloadPath -L $espflashDownloadPath -o "src/Baballonia.Desktop/Calibration/Linux/Trainer/BabbleTrainer" -o "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip" -o "src/Baballonia/Firmware/Linux/espflash.zip"

  unzip "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip" -d "src/Baballonia.Desktop/Calibration/Linux/Overlay"
  rm "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip"

  unzip "src/Baballonia/Firmware/Linux/espflash.zip" -d "src/Baballonia/Firmware/Linux"
  rm "src/Baballonia/Firmware/Linux/espflash.zip"
else
  echo "Unsupported OS: $os"
  exit 1
fi
