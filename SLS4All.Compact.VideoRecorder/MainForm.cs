// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;
using SkiaSharp;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SLS4All.Compact.VideoRecorder
{
    public partial class MainForm : Form
    {
        private CancellationTokenSource? _cancelSource;
        private Task _task = Task.CompletedTask;

        public MainForm()
        {
            InitializeComponent();
        }

        private IEnumerable<IVideoFrame> CreateFrames(ChannelReader<(DateTime Time, long Iteration, TimeSpan Elapsed, byte[] Thermo, byte[] Video, string Status)> input)
        {
            using var textFont = new SKFont(SKTypeface.FromFamilyName("arial"));
            using var textPaint = new SKPaint(textFont)
            {
                Color = SKColors.Black,
                TextSize = 9,
            };
            using var textPaint2 = new SKPaint(textFont)
            {
                Color = SKColors.Yellow,
                TextSize = 9,
            };

            foreach (var item in input.ReadAllAsync().ToBlockingEnumerable())
            {
                using var thermoBitmap = SkiaHelpers.TryDecodeBitmap(item.Thermo, applyExifOrientation: true);
                using var videoBitmap = SkiaHelpers.TryDecodeBitmap(item.Video, applyExifOrientation: true);
                if (thermoBitmap == null ||
                    videoBitmap == null)
                    continue;

                var height = 512;
                var thermoWidth = thermoBitmap.Width * height / thermoBitmap.Height;
                var videoWidth = videoBitmap.Width * height / videoBitmap.Height;

                using var frame = new SKBitmap(thermoWidth + videoWidth, height);
                using var canvas = new SKCanvas(frame);

                canvas.DrawBitmap(thermoBitmap, SKRect.Create(0, 0, thermoBitmap.Width, thermoBitmap.Height), SKRect.Create(0, 0, thermoWidth, height));
                canvas.DrawBitmap(videoBitmap, SKRect.Create(0, 0, videoBitmap.Width, videoBitmap.Height), SKRect.Create(thermoWidth, 0, videoWidth, height));
                canvas.DrawText($"{item.Time} ({item.Elapsed.TotalSeconds:0.000}s)", thermoWidth + 1, height, textPaint);
                canvas.DrawText($"{item.Time} ({item.Elapsed.TotalSeconds:0.000}s)", thermoWidth + 0, height - 1, textPaint2);
                SkiaHelpers.DrawWrapLines(item.Status, videoWidth, canvas, textPaint, thermoWidth + 1, textPaint.TextSize + 1);
                SkiaHelpers.DrawWrapLines(item.Status, videoWidth, canvas, textPaint2, thermoWidth + 0, textPaint2.TextSize);
                canvas.Flush();

                using var videoFrame = new SKBitmapFrame(frame);
                yield return videoFrame;
            }
        }

        private async Task RecorderProc(string address, int fps, string filename, CancellationTokenSource cancelSource)
        {
            var cancel = cancelSource.Token;
            var wasCancelled = false;
            try
            {
                BeginInvoke(() =>
                {
                    _startStopButton.Text = "Stop";
                });
                await EnsureFfmpeg(cancel);
                var channel = System.Threading.Channels.Channel.CreateUnbounded<(DateTime Time, long Iteration, TimeSpan Elapsed, byte[] Thermo, byte[] Video, string Status)>(new UnboundedChannelOptions
                {
                    AllowSynchronousContinuations = false,
                    SingleReader = true,
                    SingleWriter = true,
                });
                var frames = CreateFrames(channel.Reader);
                var videoFramesSource = new RawVideoPipeSource(frames)
                {
                    FrameRate = fps,
                };
                var password = _passwordBox.Text;
                var stopwatch = Stopwatch.StartNew();
                var videoTask = Task.Run(() => FFMpegArguments
                    .FromPipeInput(videoFramesSource, options => options
                        .WithFramerate(fps))
                    .OutputToFile(filename, overwrite: true, options => options
                        .WithVideoCodec("libx264")
                        .WithConstantRateFactor(13)
                        .WithArgument(new MovFlagsArgument("faststart"))
                        .WithSpeedPreset(Speed.VeryFast))
                    .ProcessSynchronously());
                var downloadTask = Task.Run(async () =>
                {
                    try
                    {
                        var baseAddress = new Uri(address);
                        var thermoAddress = new Uri(baseAddress, $"api/BedMatrix/image/{Guid.NewGuid()}?cropped=true");
                        var videoAddress = new Uri(baseAddress, $"api/VideoCamera/image/{Guid.NewGuid()}");
                        var statusAddress = new Uri(baseAddress, $"api/PrintingService/status");
                        var client = new HttpClient();
                        var timer = new PeriodicTimer(TimeSpan.FromSeconds(1) / fps);
                        var counter = 0L;

                        var serverNonce = await client.GetStringAsync(new Uri(baseAddress, $"api/login/nonce/server"), cancel);
                        var clientNonce = await client.GetStringAsync(new Uri(baseAddress, $"api/login/nonce/client"), cancel);
                        var hash = GetPasswordHash(clientNonce, GetPasswordHash(serverNonce, password));
                        var res = await client.GetStringAsync(new Uri(baseAddress, $"api/login/validate/{clientNonce}/{hash}"), cancel);
                        if (!res.Equals("true", StringComparison.OrdinalIgnoreCase))
                            throw new ApplicationException("Failed to login");

                        do
                        {
                            try
                            {
                                var iteration = counter++;
                                var thermoDataAsync = client.GetByteArrayAsync(thermoAddress, cancel);
                                var videoDataAsync = client.GetByteArrayAsync(videoAddress, cancel);
                                var statusAsync = client.GetStringAsync(statusAddress, cancel);

                                var thermoImageAsync = Task.Run(async () =>
                                {
                                    using (var ms = new MemoryStream(await thermoDataAsync, false))
                                        return FixImage(Image.FromStream(ms));
                                });
                                var videoImageAsync = Task.Run(async () =>
                                {
                                    using (var ms = new MemoryStream(await videoDataAsync, false))
                                        return FixImage(Image.FromStream(ms));
                                });

                                await Task.WhenAll(thermoImageAsync, videoImageAsync, statusAsync);
                                var thermoImage = await thermoImageAsync;
                                var videoImage = await videoImageAsync;
                                var status = await statusAsync;

                                if ((thermoImage.RawFormat.Equals(ImageFormat.Png) || thermoImage.RawFormat.Equals(ImageFormat.Jpeg) || thermoImage.RawFormat.Equals(ImageFormat.MemoryBmp)) &&
                                    (videoImage.RawFormat.Equals(ImageFormat.Png) || videoImage.RawFormat.Equals(ImageFormat.Jpeg) || videoImage.RawFormat.Equals(ImageFormat.MemoryBmp)))
                                {
                                    await channel.Writer.WriteAsync((DateTime.Now, iteration, stopwatch.Elapsed, thermoDataAsync.Result, videoDataAsync.Result, status), cancel);
                                    BeginInvoke(() =>
                                    {
                                        _thermoPicture.Image = thermoImage;
                                        _videoPicture.Image = videoImage;
                                        _statusBox.Text = status ?? "";
                                    });
                                }
                            }
                            catch
                            {
                                // swallow
                            }
                        }
                        while (await timer.WaitForNextTickAsync(cancel));
                    }
                    finally
                    {
                        channel.Writer.TryComplete();
                    }
                });

                var task = await Task.WhenAny(videoTask, downloadTask);
                wasCancelled = cancel.IsCancellationRequested;
                cancelSource.Cancel();
                await task;
            }
            catch (Exception ex)
            {
                if (!wasCancelled)
                {
                    BeginInvoke(() =>
                    {
                        _statusBox.Text = "";
                        MessageBox.Show(this, ex.Message, "Error during recording", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    });
                }
                else
                {
                    BeginInvoke(() =>
                    {
                        _statusBox.Text = "";
                    });
                }
            }
            BeginInvoke(() =>
            {
                _startStopButton.Text = "Start";
            });
        }

        private static Image FixImage(Image img)
        {
            if (img is not Bitmap bmp)
                return img;
            var pi = bmp.PropertyItems.FirstOrDefault(x => x.Id == 0x0112);
            if (pi != null && pi.Value?.Length > 0)
            {
                switch (pi.Value[0])
                {
                    case 2: bmp.RotateFlip(RotateFlipType.RotateNoneFlipX); break;
                    case 3: bmp.RotateFlip(RotateFlipType.RotateNoneFlipXY); break;
                    case 4: bmp.RotateFlip(RotateFlipType.RotateNoneFlipY); break;
                    case 5: bmp.RotateFlip(RotateFlipType.Rotate90FlipX); break;
                    case 6: bmp.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 7: bmp.RotateFlip(RotateFlipType.Rotate90FlipY); break;
                }
            }
            return bmp;
        }

        private async Task EnsureFfmpeg(CancellationToken cancel)
        {
            if (!File.Exists("ffmpeg.exe"))
            {
                BeginInvoke(() =>
                {
                    _statusBox.Text = "Downloading FFMPEG...";
                });
                using (var client = new HttpClient())
                using (var ms = new MemoryStream())
                {
                    using (var source = await client.GetStreamAsync("https://tools.sls4all.com/ffmpeg-win.zip", cancel))
                        await source.CopyToAsync(ms, cancel);
                    ms.Position = 0;
                    ZipFile.ExtractToDirectory(ms, Path.GetDirectoryName(Process.GetCurrentProcess().MainModule!.FileName)!);
                }
            }
        }

        private static string GetPasswordHash(string salt, string? password)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{salt}:{password}")));

        private void _startStopButton_Click(object sender, EventArgs e)
        {
            if (!_task.IsCompleted)
            {
                _cancelSource?.Cancel();
                _task.GetAwaiter().GetResult();
            }
            else
            {
                _cancelSource = new();
                _task = Task.Run(() => RecorderProc(_addressBox.Text, (int)_fpsUpDown.Value, _filenameBox.Text, _cancelSource));
            }
        }

        private void MainForm_SizeChanged(object sender, EventArgs e)
        {
            var spaces = 998 - 475 * 2;
            var width = (Width - spaces) / 2;
            _videoPicture.Width = width;
            _thermoPicture.Width = width;
            _videoPicture.Location = _videoPicture.Location with { X = 495 + width - 475 };
        }

        private void _browseButton_Click(object sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                AddExtension = true,
                CheckFileExists = false,
                FileName = _filenameBox.Text,
                DefaultExt = ".mp4",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
                _filenameBox.Text = dialog.FileName;
        }
    }
}
