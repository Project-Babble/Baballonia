namespace AvaloniaMiaDev.Services.Inference.Models;

public class CameraSettings
{
    public Enums.Camera Camera { get; set; }
    public int RoiX;
    public int RoiY;
    public int RoiWidth;
    public int RoiHeight;
    public float RotationRadians;
    public bool UseRedChannel;
    public bool UseHorizontalFlip;
    public bool UseVerticalFlip;
    public float Brightness = 1f;
}
