using System.Drawing;
using System.Drawing.Imaging;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Yolov8Net;
using Image = SixLabors.ImageSharp.Image;
using Point = System.Drawing.Point;
using Rectangle = System.Drawing.Rectangle;
using Size = System.Drawing.Size;

namespace BattlebitDL;

#pragma warning disable CA1416
class Program
{
    // TODO: Replace this to either the absolute path or place inside bin when running!
    private const string ModelPath = @"./Model.onnx";
    private static readonly (int width, int height) DesktopResolution = (1920, 1080);
    
    private static Image<Bgr, byte> _dataImage = new(1280, 720);
    
    private static IDetectionResult? _predictions;

    [SupportedOSPlatform("windows")]
    public static void Main(string[] args)
    {
        Socket s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        IPAddress broadcast = IPAddress.Parse("192.168.0.181");
        IPEndPoint ep = new IPEndPoint(broadcast, 7483);
        
        Console.WriteLine("Loading yolov8 predictor... this will take a moment");
        using var predictor = new YoloV8(ModelPath, new YoloV8Metadata("", "", "", YoloV8Task.Detect, -1, new SixLabors.ImageSharp.Size(1920, 1088), new []{new YoloV8Class(0, "Teammate"), new YoloV8Class(1, "Enemy")}), SessionOptions.MakeSessionOptionWithCudaProvider());
        Console.WriteLine($"Loaded predictor into memory starting program! (Width: {predictor.Metadata.ImageSize.Width}, Height: {predictor.Metadata.ImageSize.Height})");
        Console.WriteLine($"Maintain Aspect: {predictor.Parameters.KeepOriginalAspectRatio}, IoU: {predictor.Parameters.IoU}, Confidence: {predictor.Parameters.Confidence}");
        
        new Task(() =>
        {
            while (true)
            {
                var localImage = _dataImage;

                if (_predictions != null)
                {
                    foreach (var prediction in _predictions.Boxes)
                    {
                        var x = prediction.Bounds.X;
                        var y = prediction.Bounds.Y;
                        var width = prediction.Bounds.Width;
                        var height = prediction.Bounds.Height;
                
                        var centerX = x + width / 2;
                        var centerY = y + height / 2;
                
                        var color = prediction.Class.Id is 1 ? new MCvScalar(0, 150, 255) : new MCvScalar(238, 75, 43);
                
                        var rect = new Rectangle(new Point(x, y), new Size(width, height));
                        CvInvoke.Rectangle(localImage, rect, color, 2, LineType.AntiAlias);
                        CvInvoke.Circle(localImage, new Point(centerX, centerY), 8, color);
                        CvInvoke.PutText(localImage, $"{Math.Round(prediction.Confidence * 1000) / 1000} {prediction.Class.Name}", new Point(x, y - 6),
                            FontFace.HersheyDuplex, 0.75, new MCvScalar(255, 234, 0), 1, LineType.AntiAlias);
                    }
                    
                    _predictions = null;
                }
        
                using var resizedImage = localImage.Resize(1280.0 / DesktopResolution.width, Inter.Nearest);
                CvInvoke.Imshow("View", resizedImage);
                CvInvoke.WaitKey(1);
            }
        }).Start();

        IntPtr hDc = Win32Api.GetDC(Win32Api.GetDesktopWindow());
        IntPtr hMemDc = Win32Api.CreateCompatibleDC(hDc);
        var mHBitmap = Win32Api.CreateCompatibleBitmap(hDc, DesktopResolution.width, DesktopResolution.height);
        
        
        while (true)
        {
            if (mHBitmap != IntPtr.Zero)
            {
                IntPtr hOld = Win32Api.SelectObject(hMemDc, mHBitmap);
                Win32Api.BitBlt(hMemDc, 0, 0, DesktopResolution.width, DesktopResolution.height, hDc, 0, 0, Win32Api.Srccopy | Win32Api.Captureblt);
                Win32Api.SelectObject(hMemDc, hOld);
                
                var xHbitmap = System.Drawing.Image.FromHbitmap(mHBitmap);

                _dataImage = xHbitmap.ToImage<Bgr, byte>();
                
                var memoryStream = new MemoryStream();
                xHbitmap.Save(memoryStream, ImageFormat.Png);
                var desktopImage = Image.Load(memoryStream.ToArray());
                
                HandleImage(predictor, desktopImage, ep, s);
            }
            else
            {
                break;
            }
        }
    }


    private static void HandleImage(YoloV8 predictor, Image desktopImage, IPEndPoint endPoint, Socket socket)
    {
        var prediction = predictor.Detect(desktopImage);
        // Console.WriteLine($"({prediction.Speed.Inference.TotalMilliseconds} {prediction.Speed.Postprocess.TotalMilliseconds} {prediction.Speed.Preprocess.TotalMilliseconds}) -> ({(prediction.Speed.Inference.TotalMilliseconds + prediction.Speed.Postprocess.TotalMilliseconds + prediction.Speed.Preprocess.TotalMilliseconds)}ms)");
                
        if (prediction.Boxes.Count <= 0)
        {
            return;
        }
                
        _predictions = prediction;

        HandlePrediction(prediction, endPoint, socket);
    }

    private static void HandlePrediction(IDetectionResult detection, IPEndPoint endPoint, Socket socket)
    {
        var enemies = detection.Boxes.Where(x => x.Class.Id == 1 && x.Confidence > 0.50).ToList();

        if (enemies.Count <= 0)
        {
            return;
        }

        var closestEnumerable = enemies.OrderBy(e =>
        {
            var centerX = e.Bounds.X + (e.Bounds.Width / 2);
            var mouseX = DesktopResolution.width / 2;
            var deltaX = centerX - mouseX;
            
            return deltaX;
        }).ThenBy(e =>
        {
            var centerY = e.Bounds.Y + (e.Bounds.Height / 2);
            var mouseY = DesktopResolution.height / 2;
            var deltaY = centerY - mouseY;
            
            return deltaY;
        }).ThenBy(e => e.Confidence);
        
        var closestList = closestEnumerable.ToList();
        var closest = closestList[0];
        
        var centerX = closest.Bounds.X + (closest.Bounds.Width / 2);
        var centerY = closest.Bounds.Y + (closest.Bounds.Height / 2);
        
        // Assuming mouse is dead center
        var mouseX = DesktopResolution.width / 2;
        var mouseY = DesktopResolution.height / 2;
        
        // TODO: Send these movements to the mouse (TCP/UDP Server externally or Directly with Win32 Mouse Events)
        var deltaX = centerX - mouseX;
        var deltaY = centerY - mouseY;
        
        var data = $"{deltaX},{deltaY},false";
        socket.SendTo(Encoding.UTF8.GetBytes(data), endPoint);
        
        Console.WriteLine($"Enemy found! (X: {centerX}, Y: {centerY}, Score: {closest.Confidence}) (DeltaX: {deltaX}, DeltaY: {deltaY})");
    }

    private static class Win32Api
    {
        public const int Srccopy = 13369376;
        public const int Captureblt = 1073741824;

        [DllImport("gdi32.dll", EntryPoint = "BitBlt")]
        public static extern bool BitBlt(IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest, IntPtr hdcSource, int xSrc, int ySrc, int rasterOp);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleBitmap")]
        public static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int nWidth, int nHeight);

        [DllImport("gdi32.dll", EntryPoint = "CreateCompatibleDC")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);

        [DllImport("gdi32.dll", EntryPoint = "SelectObject")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr bmp);

        [DllImport("user32.dll", EntryPoint = "GetDesktopWindow")]
        public static extern IntPtr GetDesktopWindow();

        [DllImport("user32.dll", EntryPoint = "GetDC")]
        public static extern IntPtr GetDC(IntPtr ptr);
    }
}
#pragma warning restore CA1416