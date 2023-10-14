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
    private const int Width = 1920;
    private const int Height = 1080;

    private static Image<Bgr, byte> _dataImage = new(1280, 720);
    private static Image? _desktopImage;
    private static bool _new;

    private static readonly List<Prediction> Predictions = new();

    [SupportedOSPlatform("windows")]
    static void Main(string[] args)
    {
        // using TcpClient client = new("192.168.0.182", 7483);
        // using NetworkStream stream = client.GetStream();
        // if (!client.Connected)
        // {
        //     Console.WriteLine("Failed to connect to remote server!");
        //     return;
        // }
        // Console.WriteLine("Connected to remote server!");
        
        StartModelInstance(null);

        new Task(() =>
        {
            int frames = 0;
            while (true)
            {
                frames++;
                var localImage = _dataImage;

                foreach (var prediction in Predictions)
                {
                    var x = (int) prediction.Rectangle.X;
                    var y = (int) prediction.Rectangle.Y;
                    var width = (int) prediction.Rectangle.Width;
                    var height = (int) prediction.Rectangle.Height;

                    var centerX = x + width / 2;
                    var centerY = y + height / 2;

                    var color = prediction.Label is {Id: 1} ? new MCvScalar(0, 150, 255) : new MCvScalar(238, 75, 43);

                    var rect = new Rectangle(new Point(x, y), new Size(width, height));
                    CvInvoke.Rectangle(localImage, rect, color, 2, LineType.AntiAlias);
                    CvInvoke.Circle(localImage, new Point(centerX, centerY), 8, color);
                    CvInvoke.PutText(localImage, $"{Math.Round(prediction.Score * 1000) / 1000} {prediction.Label?.Name}", new Point(x, y - 6),
                        FontFace.HersheyDuplex, 0.75, new MCvScalar(255, 234, 0), 1, LineType.AntiAlias);
                }

                if (frames >= 30)
                {
                    Predictions.Clear();
                    frames = 0;
                }

                using var resizedImage = localImage.Resize(1280.0 / Width, Inter.Nearest);
                CvInvoke.Imshow("View", resizedImage);
                CvInvoke.WaitKey(1);
            }
        }).Start();

        IntPtr hDc = Win32Api.GetDC(Win32Api.GetDesktopWindow());
        IntPtr hMemDc = Win32Api.CreateCompatibleDC(hDc);
        var mHBitmap = Win32Api.CreateCompatibleBitmap(hDc, Width, Height);

        while (true)
        {
            if (mHBitmap != IntPtr.Zero)
            {
                IntPtr hOld = Win32Api.SelectObject(hMemDc, mHBitmap);
                Win32Api.BitBlt(hMemDc, 0, 0, Width, Height, hDc, 0, 0, Win32Api.Srccopy | Win32Api.Captureblt);
                Win32Api.SelectObject(hMemDc, hOld);
                var xHbitmap = System.Drawing.Image.FromHbitmap(mHBitmap);

                _dataImage = xHbitmap.ToImage<Bgr, byte>();
                
                var memoryStream = new MemoryStream();
                xHbitmap.Save(memoryStream, ImageFormat.Png);
                _desktopImage = Image.Load(memoryStream.ToArray());
                _new = true;
            }
            else
            {
                break;
            }
        }
    }

    private static void StartModelInstance(NetworkStream? stream = null)
    {
        var modelInstance = new Thread(() =>
        {
            using var yolo = YoloV8Predictor.Create(ModelPath, new[] {"Teammate", "Enemy"}, true);
            Console.WriteLine("Predictor loading into GPU Memory...  This might take a few seconds!");
            
            while (true)
            {
                if (_desktopImage == null || !_new)
                {
                    Thread.Sleep(1);
                    continue;
                }
    
                var predictions = yolo.Predict(_desktopImage);
                _new = false;
                
                if (predictions.Length <= 0)
                {
                    continue;
                }
                
                HandlePredictions(predictions, stream);
            }
        })
        {
            IsBackground = true
        };

        modelInstance.Start();
    }

    private static void HandlePredictions(Prediction[] predictions, NetworkStream? stream = null)
    {
        foreach (var prediction in predictions)
        {
            Predictions.Add(prediction);
        }

        var enemies = predictions.Where(x => x.Label?.Id == 1);
        foreach (var enemy in enemies)
        {
            if (enemy.Score > 0.65)
            {
                var centerX = enemy.Rectangle.X + (enemy.Rectangle.Width / 2);
                var centerY = enemy.Rectangle.Y + (enemy.Rectangle.Height / 2);

                // Assuming mouse is dead center
                var mouseX = Width / 2;
                var mouseY = Height / 2;

                // TODO: Send these movements to the mouse (TCP/UDP Server externally or Directly with Win32 Mouse Events)
                var deltaX = centerX - mouseX;
                var deltaY = centerY - mouseY;

                // var data = $"{deltaX},{deltaY}";
                // if (stream != null)
                // {
                //     stream.Write(Encoding.UTF8.GetBytes(data));
                // }

                Console.WriteLine($"Enemy found! (X: {centerX}, Y: {centerY}, Score: {enemy.Score}) (DeltaX: {deltaX}, DeltaY: {deltaY})");
                break;
            }
        }
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