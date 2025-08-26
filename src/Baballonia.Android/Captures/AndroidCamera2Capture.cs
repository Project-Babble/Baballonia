using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Usb;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using Baballonia.Services.Inference;
using Java.Lang;
using OpenCvSharp;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Capture = Baballonia.SDK.Capture;
using Exception = System.Exception;

namespace Baballonia.Android.Captures;

/// <summary>
/// Android Camera implementation for Capture
/// Uses Android's Camera2 API
/// </summary>
public class AndroidCamera2Capture : Capture
{
    private readonly Context _context;
    private UsbDevice _usbDevice;
    private UsbDeviceConnection _usbConnection;
    private CameraManager _cameraManager;
    private CameraDevice _cameraDevice;
    private CameraCaptureSession _captureSession;
    private ImageReader _imageReader;
    private Handler _backgroundHandler;
    private HandlerThread _backgroundThread;

    private readonly object _frameLock = new();
    private bool _isCapturing;

    public AndroidCamera2Capture(string url) : base(url)
    {
        _context = Application.Context;
        _cameraManager = (CameraManager)_context.GetSystemService(Context.CameraService)!;
    }


    public override bool CanConnect(string connectionString)
    {
        return int.TryParse(connectionString, out _);
    }

    public override async Task<bool> StartCapture()
    {
        try
        {
            if (_isCapturing)
                return true;

            // Start background thread for camera operations
            StartBackgroundThread();

            // Setup camera capture
            if (!await SetupCameraCapture())
            {
                Log.Error("AndroidCameraClass", "Failed to setup camera capture");
                return false;
            }

            _isCapturing = true;
            IsReady = true;
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error starting capture: {ex.Message}");
            return false;
        }
    }

    public override async Task<bool> StopCapture()
    {
        try
        {
            _isCapturing = false;
            IsReady = false;

            // Close capture session
            _captureSession?.Close();
            _captureSession = null;

            // Close camera device
            _cameraDevice?.Close();
            _cameraDevice = null;

            // Close image reader
            _imageReader?.Close();
            _imageReader = null;

            // Close USB connection
            _usbConnection?.Close();
            _usbConnection = null;

            // Stop background thread
            StopBackgroundThread();

            return true;
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error stopping capture: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> SetupCameraCapture()
    {
        try
        {
            // Setup ImageReader for frame capture
            _imageReader = ImageReader.NewInstance(
                256,
                256,
                ImageFormatType.Yuv420888,
                2);

            _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), _backgroundHandler);

            var cameraIds = _cameraManager.GetCameraIdList();

            // Fallback to first available camera
            string targetCameraId = string.Empty;

            if (cameraIds.Length > 0)
            {
                if (int.TryParse(Source, out var index))
                {
                    var clampedIndex = System.Math.Clamp(index, 0, cameraIds.Length);
                    targetCameraId = cameraIds[clampedIndex];
                }
                else
                {
                    targetCameraId = cameraIds[0];
                }
            }

            if (string.IsNullOrEmpty(targetCameraId))
            {
                Log.Error("AndroidCameraClass", "No camera found");
                return false;
            }

            // Open camera
            var cameraStateCallback = new CameraStateCallback(this);
            _cameraManager.OpenCamera(targetCameraId, cameraStateCallback, _backgroundHandler);

            // Wait for camera to open (simplified - in practice use proper async/await)
            await Task.Delay(1000);

            return _cameraDevice != null;
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error setting up camera capture: {ex.Message}");
            return false;
        }
    }

    private void StartBackgroundThread()
    {
        _backgroundThread = new HandlerThread("CameraBackground");
        _backgroundThread.Start();
        _backgroundHandler = new Handler(_backgroundThread.Looper);
    }

    private void StopBackgroundThread()
    {
        _backgroundThread?.QuitSafely();
        try
        {
            _backgroundThread?.Join();
            _backgroundThread = null;
            _backgroundHandler = null;
        }
        catch (InterruptedException ex)
        {
            Log.Error("AndroidCameraClass", $"Error stopping background thread: {ex.Message}");
        }
    }

    private void OnCameraOpened(CameraDevice camera)
    {
        _cameraDevice = camera;
        CreateCaptureSession();
    }

    private void CreateCaptureSession()
    {
        try
        {
            var surface = _imageReader.Surface;
            var captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
            captureRequestBuilder.AddTarget(surface);

            List<Surface> list = new();
            var array = Java.Util.Arrays.AsList(surface);
            foreach (var item in array)
            {
                list.Add((Surface)item);
            }

            var sessionStateCallback = new CaptureSessionStateCallback(this);
            _cameraDevice.CreateCaptureSession(
                list,
                sessionStateCallback,
                _backgroundHandler);
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error creating capture session: {ex.Message}");
        }
    }

    private void OnCaptureSessionConfigured(CameraCaptureSession session)
    {
        _captureSession = session;

        try
        {
            var captureRequestBuilder = _cameraDevice.CreateCaptureRequest(CameraTemplate.Preview);
            captureRequestBuilder.AddTarget(_imageReader.Surface);

            var captureRequest = captureRequestBuilder.Build();
            _captureSession.SetRepeatingRequest(captureRequest, null, _backgroundHandler);
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error starting capture: {ex.Message}");
        }
    }

    private void ProcessImage(Image image)
    {
        try
        {
            // Convert Android Image to OpenCV Mat
            var mat = ConvertImageToMat(image);

            lock (_frameLock)
            {
                RawMat = mat;
            }
        }
        catch (Exception ex)
        {
            Log.Error("AndroidCameraClass", $"Error processing image: {ex.Message}");
        }
        finally
        {
            image.Close();
        }
    }

    private Mat ConvertImageToMat(Image image)
    {
        // Get Y, U, V planes from YUV_420_888 format
        var planes = image.GetPlanes();
        var yPlane = planes[0];
        var uPlane = planes[1];
        var vPlane = planes[2];

        var yBuffer = yPlane.Buffer;
        var uBuffer = uPlane.Buffer;
        var vBuffer = vPlane.Buffer;

        var ySize = yBuffer.Remaining();
        var uSize = uBuffer.Remaining();
        var vSize = vBuffer.Remaining();

        var yuvBytes = new byte[ySize + uSize + vSize];

        // Copy Y, U, V data
        yBuffer.Get(yuvBytes, 0, ySize);
        uBuffer.Get(yuvBytes, ySize, uSize);
        vBuffer.Get(yuvBytes, ySize + uSize, vSize);

        // Create Mat from YUV data
        var yuvMat = Mat.FromArray(yuvBytes);

        // Convert YUV to BGR
        var bgrMat = new Mat();
        Cv2.CvtColor(yuvMat, bgrMat, ColorConversionCodes.YUV2BGR_I420);

        yuvMat.Dispose();
        return bgrMat;
    }

    // Callback classes
    private class CameraStateCallback : CameraDevice.StateCallback
    {
        private readonly AndroidCamera2Capture _parent;

        public CameraStateCallback(AndroidCamera2Capture parent)
        {
            _parent = parent;
        }

        public override void OnOpened(CameraDevice camera)
        {
            _parent.OnCameraOpened(camera);
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            _parent._cameraDevice = null;
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            Log.Error("AndroidCameraClass", $"Camera error: {error}");
            camera.Close();
            _parent._cameraDevice = null;
        }
    }

    private class CaptureSessionStateCallback : CameraCaptureSession.StateCallback
    {
        private readonly AndroidCamera2Capture _parent;

        public CaptureSessionStateCallback(AndroidCamera2Capture parent)
        {
            _parent = parent;
        }

        public override void OnConfigured(CameraCaptureSession session)
        {
            _parent.OnCaptureSessionConfigured(session);
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            Log.Error("AndroidCameraClass", "Capture session configuration failed");
        }
    }

    private class ImageAvailableListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly AndroidCamera2Capture _parent;

        public ImageAvailableListener(AndroidCamera2Capture parent)
        {
            _parent = parent;
        }

        public void OnImageAvailable(ImageReader reader)
        {
            var image = reader?.AcquireLatestImage();
            if (image != null)
            {
                _parent.ProcessImage(image);
            }
        }
    }
}
