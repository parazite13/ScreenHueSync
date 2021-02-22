using DesktopDuplication;
using Q42.HueApi.ColorConverters;
using Q42.HueApi.Streaming.Extensions;
using Q42.HueApi.Streaming.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ScreenHueSync
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public bool Loading
        {
            get => ProgressBar.Visibility == Visibility.Visible;
            set => ProgressBar.Visibility = value ? Visibility.Visible : Visibility.Hidden;
        }

        private bool active;
        public bool Active
        {
            get => active;
            set
            {
                active = value;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        ToggleButton.IsChecked = active;
                    });
                }
                catch (Exception) { }
            }
        }

        public double LightIntensity { get; private set; } = 0.5;

        private long refreshTimeLeft = 0L;
        private long RefreshTimeLeft
        {
            get => refreshTimeLeft;
            set
            {
                refreshTimeLeft = value;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        RefreshTimeLeftText.Text = refreshTimeLeft == -1 ? string.Empty : $"{refreshTimeLeft} ms";
                    });
                }
                catch (Exception) { }
            }
        }

        private long refreshTimeRight = 0L;
        private long RefreshTimeRight
        {
            get => refreshTimeRight;
            set
            {
                refreshTimeRight = value;
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        RefreshTimeRightText.Text = refreshTimeRight == -1 ? string.Empty : $"{refreshTimeRight} ms";
                    });
                }
                catch (Exception) { }
            }
        }

        private CancellationTokenSource cancellationTokenSource;

        private readonly DesktopDuplicator leftDesktop;
        private readonly DesktopDuplicator rightDesktop;

        public MainWindow()
        {
            InitializeComponent();

            // Primary screen is at index 0
            leftDesktop = new DesktopDuplicator(0);
            rightDesktop = new DesktopDuplicator(1);

            // Sync at start
            SetActive(true);

            // Hide the window in the system tray
            Hide();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            Hide();
        }

        private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            LightIntensity = e.NewValue;
        }

        private async void SetActive(bool state)
        {
            if (state == Active) return;

            if (state)
            {
                Loading = true;

                cancellationTokenSource = new CancellationTokenSource();

                await Hue.Setup(cancellationTokenSource.Token);

                _ = Task.Run(() => SyncScreen(cancellationTokenSource.Token, leftDesktop, Hue.BaseLayer.GetLeft(), time => RefreshTimeLeft = time));
                _ = Task.Run(() => SyncScreen(cancellationTokenSource.Token, rightDesktop, Hue.BaseLayer.GetRight(), time => RefreshTimeRight = time));

                Loading = false;
                Active = true;
            }
            else
            {
                cancellationTokenSource.Cancel();
                Hue.Dispose();

                Active = false;
            }
        }

        private void ToggleButton_Click(object sender, RoutedEventArgs e)
        {
            SetActive((sender as ToggleButton).IsChecked ?? false);
        }

        private void SyncScreen(CancellationToken ct, DesktopDuplicator screen, IEnumerable<EntertainmentLight> lights, Action<long> refreshTimeCallback = null)
        {
            var timer = new Stopwatch();
            try
            {
                while (true)
                {
                    if (ct.IsCancellationRequested)
                    {
                        ct.ThrowIfCancellationRequested();
                    }

                    timer.Start();
                    try
                    {
                        var frame = screen.GetLatestFrame();
                        if (frame?.DesktopImage != null)
                        {
                            using (var image = new Bitmap(frame.DesktopImage, new System.Drawing.Size(32, 32)))
                            {
                                var color = DominantColor(image);
                                lights.SetState(ct, color, LightIntensity);
                            }
                            refreshTimeCallback?.Invoke(timer.ElapsedMilliseconds);
                            timer.Reset();
                        }
                    }
                    catch(DesktopDuplicationException)
                    {
                        screen.Destroy();
                        screen = new DesktopDuplicator(screen.OutputDeviceIndex);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                refreshTimeCallback?.Invoke(-1);
            }
        }

        private static RGBColor DominantColor(Bitmap image)
        {
            int r = 0, g = 0, b = 0;
            var total = 0;

            for (var i = 0; i < image.Width; i++)
            {
                for (var j = 0; j < image.Height; j++)
                {
                    var pixel = image.GetPixel(i, j);
                    b += pixel.B;
                    g += pixel.G;
                    r += pixel.R;
                    total++;
                }
            }

            r /= total;
            g /= total;
            b /= total;

            return new RGBColor(r, g, b);
        }

        private void TrayIconExit_Click(object sender, RoutedEventArgs e)
        {
            if(Active)
            {
                Hue.Dispose();
            }
            Application.Current.Shutdown();
        }

        private void TrayIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            Show();
        }
    }
}
