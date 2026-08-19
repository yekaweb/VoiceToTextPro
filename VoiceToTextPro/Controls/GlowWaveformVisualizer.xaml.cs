using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace VoiceToTextPro.Controls
{
    public partial class GlowWaveformVisualizer : UserControl
    {
        private readonly DispatcherTimer _animationTimer = new();
        private double _phase = 0;
        private double _targetAmplitude = 2.0; // Calm low amplitude during silence
        private double _currentAmplitude = 2.0;
        private bool _isActive = false;

        public GlowWaveformVisualizer()
        {
            InitializeComponent();

            _animationTimer.Interval = TimeSpan.FromMilliseconds(22); // ~45 FPS for fluid rendering
            _animationTimer.Tick += AnimationTimer_Tick;

            Loaded += GlowWaveformVisualizer_Loaded;
            Unloaded += GlowWaveformVisualizer_Unloaded;
        }

        private void GlowWaveformVisualizer_Loaded(object sender, RoutedEventArgs e)
        {
            _isActive = true;
            _animationTimer.Start();
        }

        private void GlowWaveformVisualizer_Unloaded(object sender, RoutedEventArgs e)
        {
            _isActive = false;
            _animationTimer.Stop();
        }

        /// <summary>
        /// Updates real-time waveform amplitude directly from live microphone audio peak volume (0.0 to 1.0).
        /// </summary>
        public void UpdateAudioLevel(float level)
        {
            if (level < 0.02f)
            {
                // Silence: calm down to near-flat line (2.0px)
                _targetAmplitude = 2.0;
            }
            else
            {
                // Attack: Dynamic scaling for live voice (0.05..1.0 -> 7.0..58.0 amplitude)
                _targetAmplitude = Math.Clamp(level * 65.0, 6.0, 58.0);
            }
        }

        private void AnimationTimer_Tick(object? sender, EventArgs e)
        {
            if (!_isActive || WaveCanvas.ActualWidth <= 0 || WaveCanvas.ActualHeight <= 0) return;

            // Attack / Release smoothing interpolation (0.22 factor for immediate voice response without lag)
            _currentAmplitude += (_targetAmplitude - _currentAmplitude) * 0.22;
            _phase += 0.16;

            DrawWaveform();
        }

        private void DrawWaveform()
        {
            WaveCanvas.Children.Clear();

            double width = WaveCanvas.ActualWidth;
            double height = WaveCanvas.ActualHeight;
            double midY = height / 2;

            // Wave 1: Luminous Neon Pink/Magenta Wave (Reference Image 1 Core)
            Polyline wave1 = CreatePolyline("#FF52D9", 3.0, 0.95, 16);
            PointCollection points1 = new();

            // Wave 2: Neon Violet / Purple Ambient Wave
            Polyline wave2 = CreatePolyline("#A855F7", 2.2, 0.80, 12);
            PointCollection points2 = new();

            // Wave 3: Cyan / Electric Blue Accent Wave
            Polyline wave3 = CreatePolyline("#06B6D4", 1.5, 0.70, 8);
            PointCollection points3 = new();

            int resolution = (int)(width / 3.5);

            for (int i = 0; i <= resolution; i++)
            {
                double x = i * (width / resolution);
                double normX = (x / width) * Math.PI * 4.5;

                // Tapering envelope at left and right edges for smooth connection
                double envelope = Math.Sin((x / width) * Math.PI);

                double y1 = midY + Math.Sin(normX + _phase) * _currentAmplitude * envelope;
                double y2 = midY + Math.Cos(normX * 1.25 - _phase * 0.85) * (_currentAmplitude * 0.75) * envelope;
                double y3 = midY + Math.Sin(normX * 1.5 + _phase * 1.1) * (_currentAmplitude * 0.45) * envelope;

                points1.Add(new Point(x, y1));
                points2.Add(new Point(x, y2));
                points3.Add(new Point(x, y3));
            }

            wave1.Points = points1;
            wave2.Points = points2;
            wave3.Points = points3;

            WaveCanvas.Children.Add(wave3);
            WaveCanvas.Children.Add(wave2);
            WaveCanvas.Children.Add(wave1);
        }

        private static Polyline CreatePolyline(string colorHex, double thickness, double opacity, double blurRadius)
        {
            Color color = (Color)ColorConverter.ConvertFromString(colorHex);
            return new Polyline
            {
                Stroke = new SolidColorBrush(color) { Opacity = opacity },
                StrokeThickness = thickness,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = color,
                    BlurRadius = blurRadius,
                    ShadowDepth = 0,
                    Opacity = 0.90
                }
            };
        }
    }
}
