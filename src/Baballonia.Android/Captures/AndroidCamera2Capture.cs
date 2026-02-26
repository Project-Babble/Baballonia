using Android.App;
using Android.Content;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.Hardware.Usb;
using Android.Media;
using Android.OS;
using Android.Util;
using Android.Views;
using Java.Lang;
using Microsoft.Extensions.Logging;
using OpenCvSharp;
using System.Collections.Generic;
using System.Threading.Tasks;
using Java.Nio;
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

    private bool _isCapturing;

    public AndroidCamera2Capture(string url, ILogger logger) : base(url, logger)
    {
        _context = Application.Context;
        _cameraManager = (CameraManager)_context.GetSystemService(Context.CameraService)!;
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
                ImageFormatType.Jpeg,
                2);

            _imageReader.SetOnImageAvailableListener(new ImageAvailableListener(this), _backgroundHandler);

            var cameraIds = _cameraManager.GetCameraIdList();

            // Fallback to first available camera
            var targetCameraId = string.Empty;

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

            List<Surface> list = [];
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
            SetRawMat(mat);
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

    private byte[] _imageBuffer = [];

    private Mat ConvertImageToMat(Image image)
    {
        var bb = image.GetPlanes()[0].Buffer;
        var size = bb.Remaining();

        if (_imageBuffer.Length != size)
            _imageBuffer = new byte[size];

        bb.Get(_imageBuffer);
        return Mat.FromImageData(_imageBuffer);
    }

    // Callback classes
    private class CameraStateCallback(AndroidCamera2Capture parent) : CameraDevice.StateCallback
    {
        public override void OnOpened(CameraDevice camera)
        {
            parent.OnCameraOpened(camera);
        }

        public override void OnDisconnected(CameraDevice camera)
        {
            camera.Close();
            parent._cameraDevice = null;
        }

        public override void OnError(CameraDevice camera, CameraError error)
        {
            Log.Error("AndroidCameraClass", $"Camera error: {error}");
            camera.Close();
            parent._cameraDevice = null;
        }
    }

    private class CaptureSessionStateCallback(AndroidCamera2Capture parent) : CameraCaptureSession.StateCallback
    {
        public override void OnConfigured(CameraCaptureSession session)
        {
            parent.OnCaptureSessionConfigured(session);
        }

        public override void OnConfigureFailed(CameraCaptureSession session)
        {
            Log.Error("AndroidCameraClass", "Capture session configuration failed");
        }
    }

    private class ImageAvailableListener(AndroidCamera2Capture parent)
        : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        public void OnImageAvailable(ImageReader reader)
        {
            var image = reader?.AcquireLatestImage();
            if (image != null)
            {
                parent.ProcessImage(image);
            }
        }
    }
}
