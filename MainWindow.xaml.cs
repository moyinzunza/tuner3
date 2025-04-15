using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using NAudio.Wave;

namespace Tuner3
{
    public partial class MainWindow : Window
    {
        private WaveInEvent waveIn;
        private const int sampleRate = 44100;
        private readonly string[] noteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
        private int selectedDeviceIndex = 0;
        private const int maxPoints = 200;
        private DispatcherTimer graphTimer;

        public MainWindow()
        {
            InitializeComponent();
            LoadMicDevices();
            InitMic();
            StartGraphTimer();
        }

        private void StartGraphTimer()
        {
            graphTimer = new DispatcherTimer();
            graphTimer.Interval = TimeSpan.FromMilliseconds(30);
            graphTimer.Tick += (s, e) =>
            {
                if (frequencyCanvas.Children.Count == 0) return;

                Polyline graph = (Polyline)frequencyCanvas.Children[0];

                for (int i = 0; i < graph.Points.Count; i++)
                {
                    Point p = graph.Points[i];
                    p.X -= 1;
                    graph.Points[i] = p;
                }

                while (graph.Points.Count > 0 && graph.Points[0].X < 0)
                {
                    graph.Points.RemoveAt(0);
                }
            };

            graphTimer.Start();
        }

        private void LoadMicDevices()
        {
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var deviceInfo = WaveIn.GetCapabilities(i);
                micSelector.Items.Add($"{i}: {deviceInfo.ProductName}");
            }

            micSelector.SelectedIndex = 0;
        }

        private void MicSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            selectedDeviceIndex = micSelector.SelectedIndex;

            if (waveIn != null)
            {
                waveIn.StopRecording();
                waveIn.Dispose();
            }

            InitMic();
        }

        private void InitMic()
        {
            waveIn = new WaveInEvent
            {
                DeviceNumber = selectedDeviceIndex,
                BufferMilliseconds = 30 // Más rápido: 30ms te da una mejor respuesta que 50ms
            };

            waveIn.WaveFormat = new WaveFormat(sampleRate, 2); // Estéreo
            waveIn.DataAvailable += OnDataAvailable;
            waveIn.StartRecording();

            micNameLabel.Text = $"Mic: {WaveIn.GetCapabilities(waveIn.DeviceNumber).ProductName}";
        }

        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            float[] buffer = new float[e.BytesRecorded / 4];
            float sum = 0;

            for (int i = 0; i < buffer.Length; i++)
            {
                short left = BitConverter.ToInt16(e.Buffer, i * 4);
                short right = BitConverter.ToInt16(e.Buffer, i * 4 + 2);
                float sample = (left + right) / 2f / 32768f;

                buffer[i] = sample;
                sum += Math.Abs(sample);
            }

            float averageAmplitude = sum / buffer.Length;
            float amplitudeThreshold = 0.01f;

            if (averageAmplitude > amplitudeThreshold)
            {
                float freq = DetectFrequency(buffer, sampleRate);
                if (freq > 50 && freq < 2000)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var (note, noteFreq) = GetClosestNote(freq);
                        labelFrequency.Text = $"Frecuencia: {freq:F2} Hz (≈ {note})";
                        labelNote.Text = $"Nota: {note} ({noteFreq:F1} Hz)";
                        labelStatus.Text = Math.Abs(freq - noteFreq) <= 2 ? "✅ In tune" : "❌ Out of tune";

                        UpdateFrequencyGraph(freq);
                    });
                }
            }
        }

        private void UpdateFrequencyGraph(float freq)
        {
            var point = new Point(frequencyCanvas.ActualWidth, frequencyCanvas.ActualHeight - (freq * frequencyCanvas.ActualHeight / 2000));

            if (frequencyCanvas.Children.Count == 0)
            {
                Polyline polyline = new Polyline
                {
                    Stroke = Brushes.Green,
                    StrokeThickness = 2
                };
                frequencyCanvas.Children.Add(polyline);
            }

            Polyline graph = (Polyline)frequencyCanvas.Children[0];
            graph.Points.Add(point);

            if (graph.Points.Count > maxPoints)
            {
                graph.Points.RemoveAt(0);
            }

            for (int i = 0; i < graph.Points.Count; i++)
            {
                Point p = graph.Points[i];
                p.X -= 1;
                graph.Points[i] = p;
            }
        }

        private float DetectFrequency(float[] samples, int sampleRate)
        {
            int bestOffset = 0;
            float bestCorrelation = 0;
            int minLag = sampleRate / 2000;
            int maxLag = sampleRate / 50;

            for (int lag = minLag; lag <= maxLag; lag++)
            {
                float correlation = 0;
                for (int i = 0; i < samples.Length - lag; i++)
                {
                    correlation += samples[i] * samples[i + lag];
                }

                if (correlation > bestCorrelation)
                {
                    bestCorrelation = correlation;
                    bestOffset = lag;
                }
            }

            return bestOffset == 0 ? 0 : sampleRate / (float)bestOffset;
        }

        private (string note, float freq) GetClosestNote(float freq)
        {
            double a4 = 440.0;
            double c0 = a4 * Math.Pow(2, -4.75);
            int halfSteps = (int)Math.Round(12 * Math.Log2(freq / c0));
            int octave = halfSteps / 12;
            int noteIndex = ((halfSteps % 12) + 12) % 12;
            double noteFreq = c0 * Math.Pow(2, halfSteps / 12.0);
            string noteName = noteNames[noteIndex] + octave;
            return (noteName, (float)noteFreq);
        }
    }
}
