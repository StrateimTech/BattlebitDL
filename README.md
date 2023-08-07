# BattleBitDL (BattleBit Deep learning)
Attempts to mark/identify objects(players) both enemies and teammates using YoloV8's object detection on a custom dataset.

# Model
YoloV8 small model trained on 1.25k images at 1920x1088 (Original's 1920x1080).
![Model Results](results.png)

# Requirements
- Windows Only
- Nvidia GPU
- CUDA 11.X, cuDNN 11.X, zlib 1.2.3 installed / linked to path
- Knowledge

# NuGet Dependencies
- [YoloV8.NET](https://github.com/sstainba/Yolov8.Net/)
- [Emgu.CV](https://github.com/emgucv/emgucv)
  - Emgu.CV.Bitmap
  - Emgu.CV.Runtime.Windows
- Microsoft.ML.OnnxRuntime.GPU
- System.Drawing.common