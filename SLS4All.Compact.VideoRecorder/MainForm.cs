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
using System.Threading;
using System.Threading.Channels;

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

        private void DrawWrapLines(string text, float width, SKCanvas canvas, SKPaint paint, float x, float y)
        {
            var wrappedLines = new List<string>();
            var lineLength = 0f;
            var line = "";
            foreach (var word in text.Split(','))
            {
                var wordWithSpace = word + ",";
                var wordWithSpaceLength = paint.MeasureText(wordWithSpace);
                if (lineLength + wordWithSpaceLength > width)
                {
                    wrappedLines.Add(line);
                    line = wordWithSpace;
                    lineLength = wordWithSpaceLength;
                }
                else
                {
                    line += wordWithSpace;
                    lineLength += wordWithSpaceLength;
                }
            }
            if (line.Length != 0)
                wrappedLines.Add(line);
            foreach (var wrappedLine in wrappedLines)
            {
                canvas.DrawText(wrappedLine, x, y, paint);
                y += paint.FontSpacing;
            }
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
                using var thermoBitmap = DecodeFixRotation(item.Thermo);
                using var videoBitmap = DecodeFixRotation(item.Video);

                var height = 512;
                var thermoWidth = thermoBitmap.Width * height / thermoBitmap.Height;
                var videoWidth = videoBitmap.Width * height / videoBitmap.Height;

                using var frame = new SKBitmap(thermoWidth + videoWidth, height);
                using var canvas = new SKCanvas(frame);

                canvas.DrawBitmap(thermoBitmap, SKRect.Create(0, 0, thermoBitmap.Width, thermoBitmap.Height), SKRect.Create(0, 0, thermoWidth, height));
                canvas.DrawBitmap(videoBitmap, SKRect.Create(0, 0, videoBitmap.Width, videoBitmap.Height), SKRect.Create(thermoWidth, 0, videoWidth, height));
                canvas.DrawText($"{item.Time} ({item.Elapsed.TotalSeconds:0.000}s)", thermoWidth + 1, height, textPaint);
                canvas.DrawText($"{item.Time} ({item.Elapsed.TotalSeconds:0.000}s)", thermoWidth + 0, height - 1, textPaint2);
                DrawWrapLines(item.Status, videoWidth, canvas, textPaint, thermoWidth + 1, textPaint.TextSize + 1);
                DrawWrapLines(item.Status, videoWidth, canvas, textPaint2, thermoWidth + 0, textPaint2.TextSize);
                canvas.Flush();

                using var videoFrame = new SKBitmapFrame(frame);
                yield return videoFrame;
            }
        }

        private SKBitmap DecodeFixRotation(byte[] video)
        {
            using (var inputStream = new MemoryStream(video))
            {
                using (var codec = SKCodec.Create(inputStream))
                {
                    using (var original = SKBitmap.Decode(codec))
                    {
                        var useWidth = original.Width;
                        var useHeight = original.Height;
                        Action<SKCanvas> transform = canvas => { };
                        switch (codec.EncodedOrigin)
                        {
                            case SKEncodedOrigin.TopLeft:
                                break;
                            case SKEncodedOrigin.TopRight:
                                // flip along the x-axis
                                transform = canvas => canvas.Scale(-1, 1, useWidth / 2, useHeight / 2);
                                break;
                            case SKEncodedOrigin.BottomRight:
                                transform = canvas => canvas.RotateDegrees(180, useWidth / 2, useHeight / 2);
                                break;
                            case SKEncodedOrigin.BottomLeft:
                                // flip along the y-axis
                                transform = canvas => canvas.Scale(1, -1, useWidth / 2, useHeight / 2);
                                break;
                            case SKEncodedOrigin.LeftTop:
                                useWidth = original.Height;
                                useHeight = original.Width;
                                transform = canvas =>
                                {
                                    // Rotate 90
                                    canvas.RotateDegrees(90, useWidth / 2, useHeight / 2);
                                    canvas.Scale(useHeight * 1.0f / useWidth, -useWidth * 1.0f / useHeight, useWidth / 2, useHeight / 2);
                                };
                                break;
                            case SKEncodedOrigin.RightTop:
                                useWidth = original.Height;
                                useHeight = original.Width;
                                transform = canvas =>
                                {
                                    // Rotate 90
                                    canvas.RotateDegrees(90, useWidth / 2, useHeight / 2);
                                    canvas.Scale(useHeight * 1.0f / useWidth, useWidth * 1.0f / useHeight, useWidth / 2, useHeight / 2);
                                };
                                break;
                            case SKEncodedOrigin.RightBottom:
                                useWidth = original.Height;
                                useHeight = original.Width;
                                transform = canvas =>
                                {
                                    // Rotate 90
                                    canvas.RotateDegrees(90, useWidth / 2, useHeight / 2);
                                    canvas.Scale(-useHeight * 1.0f / useWidth, useWidth * 1.0f / useHeight, useWidth / 2, useHeight / 2);
                                };
                                break;
                            case SKEncodedOrigin.LeftBottom:
                                useWidth = original.Height;
                                useHeight = original.Width;
                                transform = canvas =>
                                {
                                    // Rotate 90
                                    canvas.RotateDegrees(90, useWidth / 2, useHeight / 2);
                                    canvas.Scale(-useHeight * 1.0f / useWidth, -useWidth * 1.0f / useHeight, useWidth / 2, useHeight / 2);
                                };
                                break;
                            default:
                                break;
                        }
                        var target = new SKBitmap(useWidth, useHeight, original.ColorType, original.AlphaType);
                        using (var canvas = new SKCanvas(target))
                        {
                            using (var paint = new SKPaint())
                            {
                                // high quality with antialiasing
                                paint.IsAntialias = true;
                                paint.FilterQuality = SKFilterQuality.High;

                                // rotate according to origin
                                transform.Invoke(canvas);

                                // draw the bitmap to fill the surface
                                canvas.DrawBitmap(original, 0, 0, paint);
                                canvas.Flush();

                                return target;
                            }
                        }
                    }
                }
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
