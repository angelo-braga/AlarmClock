using Microsoft.ProjectOxford.Face;
using Microsoft.ProjectOxford.Face.Contract;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using System.Timers;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage.Streams;
using Windows.UI;
using Windows.UI.Core;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;
using Microsoft.Toolkit.Uwp.Helpers;

// The Blank Page item template is documented at https://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace AlarmClock
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        private List<string> labels;

        private string expectedEmotion;
        private string returnDetection = string.Empty;

        private string detectedEmotion = string.Empty;
        private string detectedGender = string.Empty;
        private string detectedAge = string.Empty;
        private string detectedGlasses = string.Empty;
        private string detectedHair = string.Empty;
        private string detectedFacialHair = string.Empty;
        
        private VideoFrame lastFrame;
        private Timer timer = new Timer(5000);
        private DispatcherTimer clockTimer;
        private bool alarmOn = true;
        private SolidColorBrush red = new SolidColorBrush(Color.FromArgb(255, 255, 0, 0));
        private SolidColorBrush white = new SolidColorBrush(Color.FromArgb(255, 255, 255, 255));

        public MainPage()
        {
            this.InitializeComponent();
        }

        protected async override void OnNavigatedTo(NavigationEventArgs e)
        {
            labels = new List<string>()
            {
                "Neutral",
                "Happiness",
                "Surprise",
                "Sadness",
                "Anger",
                "Disgust",
                "Fear",
                "Contempt"
             };

            await camera.StartAsync();
            camera.CameraHelper.FrameArrived += Preview_FrameArrived;

            timer.Elapsed += Timer_Elapsed;
            timer.Start();

            // Choose Happiness as expected emotion
            expectedEmotion = labels[1];
            EmotionText.Text = $"Show {expectedEmotion} to Dismiss";

            clockTimer = new DispatcherTimer();
            clockTimer.Interval = TimeSpan.FromMilliseconds(300);
            clockTimer.Tick += Timer_Tick;
            clockTimer.Start();
        }

        private void Timer_Tick(object sender, object e)
        {
            TimeText.Text = DateTime.Now.ToString("HH:mm:ss");

            if (alarmOn)
            {
                TimeText.Foreground = TimeText.Foreground == white ? red : white;
                AlarmText.Text = "Alarm ON";
            }
            else
            {
                TimeText.Foreground = white;
                AlarmText.Text = "Alarm OFF";
            }
        }

        private async Task ProcessEmotion(string returnDetection)
        {
            if (returnDetection == "OK" && (expectedEmotion.Equals(detectedEmotion, StringComparison.CurrentCultureIgnoreCase)))
            {
                alarmOn = false;
            }
        }

        private async void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            await AnalyzeFrame();

            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(CoreDispatcherPriority.Normal,
            () =>
            {
                DetectedEmotion.Text = string.Format("Detected {0} at {1}", detectedEmotion, DateTime.Now.ToLongTimeString());
                DetectedData.Text = string.Format("Gender: {0} Age: {1} Glasses: {2} Hair: {3} Facial Hair: {4}", detectedGender, detectedAge, detectedGlasses, detectedHair, detectedFacialHair);
            }
            );
        }

        private async void Preview_FrameArrived(object sender, FrameEventArgs e)
        {
            lastFrame = e.VideoFrame;
        }

        private async Task AnalyzeFrame()
        {
            if (!alarmOn)
                return;

            // Analyze the last frame
            try
            {
                returnDetection = await DetectEmotionWithCognitiveServices();
                await ProcessEmotion(returnDetection);
            }
            catch
            {
                return;
            }
        }

        private async Task<string> DetectEmotionWithCognitiveServices()
        {
            var originalBitmap = lastFrame.SoftwareBitmap;
            if (originalBitmap == null)
                return "No frame captured";

            // Set correct subscriptionKey and API Url.
            string subscriptionKey = "ce17eb6651cb4abf995991ff301aee2f";
            string apiBaseUrl = "https://westcentralus.api.cognitive.microsoft.com/face/v1.0";

            using (InMemoryRandomAccessStream imageStream = new InMemoryRandomAccessStream())
            {
                SoftwareBitmap bitmap = SoftwareBitmap.Convert(originalBitmap, BitmapPixelFormat.Rgba16);
                BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, imageStream);
                encoder.SetSoftwareBitmap(bitmap);
                var ratio = bitmap.PixelHeight / 200;
                encoder.BitmapTransform.ScaledHeight = (uint)Math.Round((double)bitmap.PixelHeight / ratio);
                encoder.BitmapTransform.ScaledWidth = (uint)Math.Round((double)bitmap.PixelWidth / ratio);
                await encoder.FlushAsync();

                imageStream.Seek(0);

                var faceServiceClient = new FaceServiceClient(subscriptionKey, apiBaseUrl);
                detectedEmotion = string.Empty;
                detectedGender = string.Empty;
                detectedAge = string.Empty;
                detectedGlasses = string.Empty;
                detectedHair = string.Empty;
                detectedFacialHair = string.Empty;

                try
                {
                    Face[] faces = await faceServiceClient.DetectAsync(imageStream.AsStream(), false, true, new FaceAttributeType[] { FaceAttributeType.Emotion, FaceAttributeType.Age, FaceAttributeType.Gender, FaceAttributeType.Glasses, FaceAttributeType.Hair, FaceAttributeType.FacialHair });
                    var detectedFace = faces?.FirstOrDefault();
                    detectedEmotion = detectedFace == null ? "Nothing" : detectedFace.FaceAttributes.Emotion.ToRankedList().FirstOrDefault().Key;
                    detectedGender = detectedFace == null ? "Nothing" : detectedFace.FaceAttributes.Gender.ToString();
                    detectedAge = detectedFace == null ? "Nothing" : detectedFace.FaceAttributes.Age.ToString();
                    detectedGlasses = detectedFace == null ? "Nothing" : detectedFace.FaceAttributes.Glasses.ToString();
                    detectedHair = detectedFace == null ? "Nothing" : (detectedFace.FaceAttributes.Hair.Bald * 100).ToString();
                    detectedFacialHair = detectedFace == null ? "Nothing" : (detectedFace.FaceAttributes.FacialHair.Beard * 100).ToString();
                }
                catch (FaceAPIException e)
                {
                    return "API error. Check the values of subscriptionKey and apiBaseUrl";
                }

                return "OK";
            }
        }
    }
}
