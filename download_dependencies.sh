#!/bin/bash

curl -Z -L "https://github.com/Project-Babble/BabbleTrainer/releases/latest/download/BabbleTrainer" -L "https://github.com/Project-Babble/BabbleCalibration/releases/latest/download/Linux.zip" -L "https://github.com/esp-rs/espflash/releases/latest/download/espflash-x86_64-unknown-linux-gnu.zip" -o "src/Baballonia.Desktop/Calibration/Linux/Trainer/BabbleTrainer" -o "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip" -o "src/Baballonia/Firmware/Linux/espflash-x86_64-unknown-linux-gnu.zip"

unzip "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip" -d "src/Baballonia.Desktop/Calibration/Linux/Overlay"
rm "src/Baballonia.Desktop/Calibration/Linux/Overlay/Linux.zip"

unzip "src/Baballonia/Firmware/Linux/espflash-x86_64-unknown-linux-gnu.zip" -d "src/Baballonia/Firmware/Linux"
rm "src/Baballonia/Firmware/Linux/espflash-x86_64-unknown-linux-gnu.zip"
