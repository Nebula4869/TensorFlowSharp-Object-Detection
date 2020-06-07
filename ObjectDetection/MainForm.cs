using System;
using System.IO;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using TensorFlow;


namespace ObjectDetection
{
    public partial class MainForm : Form
    {
        private Capture capture = null;                         //EmguCV图像捕获器
        private Mat mat = new Mat();                            //EmguCV图像容器
        private int frameWidth = 1280;                         //摄像头帧宽
        private int frameHeight = 720;                         //摄像头帧高
        private int fps = 30;                                   //摄像头帧率
        private bool cameraFlag = false;

        static string modelFile = "models/ssd_mobilenet_v1_coco_11_06_2017.pb";
        static string labelsFile = "models/coco91.names";
        static string[] labels = File.ReadAllLines(labelsFile);

        TFGraph graph = null;
        TFSession session = null;

        private MCvScalar colorGreen = new MCvScalar(0, 255, 0);     //EmguCV用于绘图的颜色
        private MCvScalar colorRed = new MCvScalar(0, 0, 255);     //EmguCV用于绘图的颜色

        public MainForm()
        {
            InitializeComponent();
            // 恢复模型
            byte[] model = File.ReadAllBytes(modelFile);
            graph = new TFGraph();
            graph.Import(model, "");
        }
        private void buttonStart_Click(object sender, EventArgs e)
        {
            // 摄像头未开启则开启
            if (!cameraFlag)
            {
                cameraFlag = true;

                // 实例化VideoCapture对象
                capture = new Capture(1);
                capture.SetCaptureProperty(CapProp.Fps, fps);
                capture.SetCaptureProperty(CapProp.FrameWidth, frameWidth);
                capture.SetCaptureProperty(CapProp.FrameHeight, frameHeight);
                session = new TFSession(graph);
                // 开始读取视频
                capture.ImageGrabbed += ReadFrame;
                capture.Start();
                buttonStart.Text = "停止检测";
            }
            // 摄像头已开启则关闭
            else
            {
                cameraFlag = false;

                // 停止并回收VideoCapture对象
                capture.Stop();
                capture.Dispose();

                buttonStart.Text = "开始检测";
            }
        }
        private void ReadFrame(object sender, EventArgs arg)//捕获摄像头画面的事件
        {
            DateTime TimeStart = DateTime.Now;
            // VideoCapture捕获一帧图像
            try
            {
                capture.Retrieve(mat, 0);
            }
            catch { }

            // 创建Tensor作为网络输入
            TFTensor tensor = Mat2Tensor(mat);

            // 前向推理
            TFSession.Runner runner = session.GetRunner();
            runner.AddInput(graph["image_tensor"][0], tensor);
            runner.Fetch(graph["num_detections"][0]);
            runner.Fetch(graph["detection_scores"][0]);
            runner.Fetch(graph["detection_boxes"][0]);
            runner.Fetch(graph["detection_classes"][0]);
            TFTensor[] outputs = runner.Run();
            
            // 解析结果
            float num = ((float[])outputs[0].GetValue(jagged: true))[0];
            float[] scores = ((float[][])outputs[1].GetValue(jagged: true))[0];
            float[][] boxes = ((float[][][])outputs[2].GetValue(jagged: true))[0];
            float[] classes = ((float[][])outputs[3].GetValue(jagged: true))[0];
            
            // 显示检测框和类别
            for (int i = 0; i < (int)num; i++)
            {
                if (scores[i] > 0.8)
                {
                    int left = (int)(boxes[i][1] * frameWidth);
                    int top = (int)(boxes[i][0] * frameHeight);
                    int right = (int)(boxes[i][3] * frameWidth);
                    int bottom = (int)(boxes[i][2] * frameHeight);
                    CvInvoke.PutText(mat, labels[(int)classes[i]] + ": " + scores[i].ToString("0.00"), new Point(left, top), FontFace.HersheyDuplex, (right - left) / 200, colorGreen);
                    CvInvoke.Rectangle(mat, new Rectangle(left, top, right - left, bottom - top), colorRed, 2);
                }
            }

            // 在imageBox上显示经过缩放的图像
            Mat dst = new Mat();
            CvInvoke.Resize(mat, dst, new Size(imageBox.Width, imageBox.Height));
            imageBox.Image = cameraFlag ? dst : null;
            TimeSpan TimeCount = DateTime.Now - TimeStart;
            textFPS.Text = (1000 / TimeCount.TotalMilliseconds).ToString("0.00");
        }
        static TFTensor Mat2Tensor(Mat mat)
        {
            // 将图片读取为Tensor
            Bitmap bitmap = mat.Bitmap;
            byte[] contents = Bitmap2Bytes(bitmap);
            TFTensor tensor = TFTensor.CreateString(contents);

            TFGraph graph = new TFGraph();
            TFOutput input, output;
            input = graph.Placeholder(TFDataType.String);

            output = graph.DecodeJpeg(input, 3);
            output = graph.Cast(output, TFDataType.UInt8);
            output = graph.ExpandDims(output, graph.Const(0, "make_batch"));

            // 执行图
            using (var session = new TFSession(graph))
            {
                var normalized = session.Run(
                         inputs: new[] { input },
                         inputValues: new[] { tensor },
                         outputs: new[] { output });

                return normalized[0];
            }
        }
        public static byte[] Bitmap2Bytes(Bitmap bitmap)
        {
            using (MemoryStream stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Jpeg);
                byte[] data = new byte[stream.Length];
                stream.Seek(0, SeekOrigin.Begin);
                stream.Read(data, 0, Convert.ToInt32(stream.Length));
                return data;
            }
        }
    }
}
