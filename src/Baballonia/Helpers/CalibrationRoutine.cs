using System;
using System.Collections.Generic;
using System.Linq;
using Tmds.DBus.Protocol;

namespace Baballonia.Helpers;

public static class CalibrationRoutine
{
    public enum Routines
    {
        BasicCalibration,
        BasicCalibrationNoTutorial,
        GazeOnly,
        BlinkOnly,
    }
    public static readonly Dictionary<string, Routines> Map = Enum.GetValues<Routines>().ToDictionary(i => i.ToString(), i => i);
    /*
    public static readonly Dictionary<string, string> Map = new()
    {
        { "BasicCalibration", "0" },
        { "ExtendedCalibration", "1" },
        { "HorizontalSweep", "2" },
        { "VerticalSweep", "3" },
        { "DiagonalPattern1", "4" },
        { "DiagonalPattern2", "5" },
        { "CircularPattern", "6" },
        { "SpiralPattern", "7" },
        { "SaccadeMovements", "8" },
        { "SmoothHorizontal", "9" },
        { "SmoothVertical", "10" },
        { "SmoothCircle", "11" },
        { "Comprehensive", "12" },
        { "PeripheralVision", "13" },
        { "QuickCalibration", "14" },
        { "CentralFineGrained", "15" },
        { "DynamicRange", "16" },
        { "MicroSaccades", "17" },
        { "ReadingPattern", "18" },
        { "ZPattern", "19" },
        { "Figure8Pattern", "20" },
        { "DepthSimulation", "21" },
        { "QuickCalibrationNoTutorial", "22" },
    };
    */
}
