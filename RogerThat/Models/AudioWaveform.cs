using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace RogerThat.Models
{
    public class AudioWaveform
    {
        private readonly Canvas _canvas;
        private readonly System.Windows.Shapes.Rectangle _progressRect;
        private readonly List<Path> _waveformPaths = new();
        private Path _playedWaveformPath;
        public double Duration { get; private set; }

        public AudioWaveform(Canvas canvas, System.Windows.Shapes.Rectangle progressRect)
        {
            _canvas = canvas;
            _progressRect = progressRect;
            _canvas.Children.Clear();
        }

        public void DrawWaveform(string audioFilePath)
        {
            ClearWaveform();

            using var reader = new AudioFileReader(audioFilePath);
            Duration = reader.TotalTime.TotalSeconds;
            var samples = new float[reader.Length / 4];
            reader.Read(samples, 0, samples.Length);

            var points = ProcessSamples(samples);
            DrawWaveformPath(points);
        }

        private void ClearWaveform()
        {
            foreach (var path in _waveformPaths)
            {
                _canvas.Children.Remove(path);
            }
            _waveformPaths.Clear();
            _progressRect.Width = 0;
            _canvas.Children.Clear();
        }

        private List<System.Windows.Point> ProcessSamples(float[] samples)
        {
            var points = new List<System.Windows.Point>();
            var blockSize = samples.Length / _canvas.ActualWidth;
            
            for (int i = 0; i < _canvas.ActualWidth; i++)
            {
                var start = (int)(i * blockSize);
                var end = (int)((i + 1) * blockSize);
                var max = 0f;
                
                for (int j = start; j < end && j < samples.Length; j++)
                {
                    var abs = Math.Abs(samples[j]);
                    if (abs > max) max = abs;
                }

                var height = max * _canvas.ActualHeight / 2;
                points.Add(new System.Windows.Point(i, _canvas.ActualHeight / 2 - height));
                points.Add(new System.Windows.Point(i, _canvas.ActualHeight / 2 + height));
            }

            return points;
        }

        private void DrawWaveformPath(List<System.Windows.Point> points)
        {
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(points[0], true, true);
                for (int i = 1; i < points.Count; i++)
                {
                    context.LineTo(points[i], true, false);
                }
            }
            geometry.Freeze();

            // 波形背景
            var path = new Path
            {
                Data = geometry,
                Style = _canvas.Resources["WaveformPathStyle"] as Style,
                StrokeThickness = 1,
                Opacity = 0.5
            };

            // 波形前景
            _playedWaveformPath = new Path
            {
                Data = geometry,
                Style = _canvas.Resources["WaveformPathStyle"] as Style,
                StrokeThickness = 1.5,
                Opacity = 0.9,
                Clip = new RectangleGeometry()
            };

            _canvas.Children.Add(path);
            _canvas.Children.Add(_playedWaveformPath);
            _waveformPaths.Add(path);
            _waveformPaths.Add(_playedWaveformPath);
        }

        public void UpdateProgress(TimeSpan position)
        {
            if (Duration <= 0) return;
            
            var progress = position.TotalSeconds / Duration;
            _progressRect.Width = _canvas.ActualWidth * progress;

            if (_playedWaveformPath != null)
            {
                var clipRect = new RectangleGeometry(
                    new Rect(0, 0, _canvas.ActualWidth * progress, _canvas.ActualHeight)
                );
                _playedWaveformPath.Clip = clipRect;
            }
        }
    }
} 