using System;
using System.Collections.Generic;

namespace Baballonia.Models;

/*
 * 0, 1, 2 - Corresponds to a numerical device entry
 * COMX - Corresponds to a serial port on Windows
 * /dev/ttyAVMX - Corresponds to a serial port on Windows
 * http - Corresponds to an IP cam (MJPEG)
 * /dev/videoX - Used for VFT on Linux
 */

public class VrCalibration
{
    public const string ModelName = "eyeModel.onnx";
    public string ModelSavePath { get; set; }
    public string CalibrationInstructions { get; set; }
    public float FOV { get; set; }
    public string LeftEyeMjpegSource { get; set; }
    public string RightEyeMjpegSource  { get; set; }
}
