// The waifu2d page for Windows: a drawing in layers, as a Photoshop document, made to move.
// Nothing here runs a model -- a .psd out of img2layers (or anything layered the way
// See-through or Anime2.5DRig names layers) is opened and driven about.
//
// A port of waifu-app/swiftui's RigPage.swift + RigView.swift. Built code-only, against .NET
// Framework 4.8's own csc: no XAML, no SDK project.

using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SkiaSharp;
using Microsoft.Win32;

namespace Waifu2d
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            string logPath = Path.Combine(Path.GetTempPath(), "waifu2d_error.log");
            try
            {
                var app = new Application();
                app.DispatcherUnhandledException += (s, e) =>
                {
                    File.WriteAllText(logPath, e.Exception.ToString());
                    e.Handled = false;
                };
                var window = new Waifu2dWindow();
                if (args.Length > 0 && File.Exists(args[0]))
                    window.Loaded += (s, e) => window.OpenDocument(args[0]);
                // For trying it without the panel: waifu2d.exe x.psd mouth=1 smile=-0.8 twist=0.5
                window.Loaded += (s, e) =>
                {
                    foreach (var a in args.Skip(1))
                    {
                        var kv = a.Split('=');
                        if (kv.Length != 2 || !float.TryParse(kv[1], out float v)) continue;
                        window.SetPose(kv[0], v);
                    }
                };
                app.Run(window);
                return 0;
            }
            catch (Exception e)
            {
                File.WriteAllText(logPath, e.ToString());
                return 1;
            }
        }
    }

    sealed class Waifu2dWindow : Window
    {
        // The document on the page and how it moves.
        Rig rig;
        RigMotion motion;
        readonly RigRenderer renderer = new RigRenderer();
        readonly RigSwitches switches = new RigSwitches();
        double open = 1.0;
        double ease = 0.3;
        double nod = 1.0;
        double follow = 1.0;
        /// Where the pointer is over the canvas, -1 to 1 across it and down it.
        Vector2? pointer;
        DateTime lastFrame = DateTime.MinValue;
        string file;

        // Speech and expression: the mic, made-up talk, and where the sliders leave the face.
        MicLevel mic;
        bool micOn;
        bool autoTalk;
        /// Dragging a part: its index, the canvas point the press landed on, and the last point.
        int dragging = -1;
        Vector2 grabStart, grabNow;

        readonly Image picture = new Image { Stretch = Stretch.Uniform };
        readonly Grid stage = new Grid();
        readonly TextBlock status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock notes = new TextBlock
        {
            Foreground = Brushes.DimGray,
            TextWrapping = TextWrapping.Wrap,
        };
        WriteableBitmap bitmap;
        bool reading;

        public Waifu2dWindow()
        {
            Title = "waifu2d";
            Width = 1120;
            Height = 860;
            MinWidth = 700;
            MinHeight = 500;
            Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x16));
            Foreground = Brushes.White;
            FontSize = 13;
            AllowDrop = true;
            Drop += OnDrop;
            DragOver += (s, e) =>
            {
                e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
                    ? DragDropEffects.Copy : DragDropEffects.None;
                e.Handled = true;
            };

            // Top: the document to open, and what it made of it.
            var openButton = new Button { Content = "Open a .psd…", Padding = new Thickness(14, 6, 14, 6) };
            openButton.Click += (s, e) => ChooseFile();
            var blinkButton = new Button { Content = "Blink Now", Padding = new Thickness(14, 6, 14, 6) };
            blinkButton.Click += (s, e) => motion?.Blinker.BlinkNow();
            var top = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(16, 14, 16, 10),
            };
            top.Children.Add(openButton);
            top.Children.Add(Space(10));
            top.Children.Add(status);
            top.Children.Add(Space(10));
            top.Children.Add(blinkButton);

            // Middle: the drawing moving under the box.
            stage.Background = Checkerboard();
            stage.Children.Add(picture);
            stage.MouseMove += (s, e) =>
            {
                pointer = PointerAt(e.GetPosition(stage), stage);
                if (dragging >= 0) grabNow = CanvasAt(e.GetPosition(stage));
            };
            stage.MouseLeave += (s, e) => pointer = null;
            stage.MouseLeftButtonDown += (s, e) =>
            {
                if (rig == null || motion == null) return;
                var at = CanvasAt(e.GetPosition(stage));
                int hit = PickPart(at);
                if (hit < 0) return;
                dragging = hit;
                grabStart = grabNow = at;
                motion.Grab(hit, Vector2.Zero);
                stage.CaptureMouse();
            };
            stage.MouseLeftButtonUp += (s, e) =>
            {
                if (dragging < 0) return;
                dragging = -1;
                motion?.Grab(-1, Vector2.Zero);
                stage.ReleaseMouseCapture();
            };
            KeyDown += (s, e) => { if (e.Key == Key.Space) motion?.Jump(); };
            var middle = new Border
            {
                Child = stage,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x2a, 0x2a, 0x2e)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(16, 0, 0, 0),
            };

            // Right: how it moves.
            var settings = new StackPanel { Margin = new Thickness(16, 4, 16, 0), Width = 250 };

            settings.Children.Add(Header("Motion"));
            settings.Children.Add(Check("Follow the pointer", v => switches.FollowMouse = v, switches.FollowMouse));
            settings.Children.Add(Check("Sway", v => switches.Idle = v, switches.Idle));
            settings.Children.Add(Check("Look around", v => switches.LookAround = v, switches.LookAround));
            settings.Children.Add(Check("Hair swings", v => switches.Physics = v, switches.Physics));
            settings.Children.Add(Slider("Up and down", 0, 2, nod, v => nod = v));
            settings.Children.Add(Slider("Body follows", 0, 2, follow, v => follow = v));
            settings.Children.Add(Space(8));

            settings.Children.Add(Header("Blink"));
            settings.Children.Add(Check("On its own", v => switches.Blink = v, switches.Blink));
            settings.Children.Add(Slider("Eyes open", 0, 1, open, v => open = v));
            settings.Children.Add(Slider("Closes at", 0, 1, ease, v => ease = v));
            settings.Children.Add(Space(8));

            settings.Children.Add(Header("Talk"));
            settings.Children.Add(Check("Auto talk", v => autoTalk = v, false));
            settings.Children.Add(Check("Microphone", v => micOn = v, false));
            settings.Children.Add(Slider("Mouth open", 0, 1, 0, v => { if (motion != null) motion.MouthManual = (float)v; }));
            settings.Children.Add(Space(8));

            settings.Children.Add(Header("Expression"));
            settings.Children.Add(Slider("Smile", -1, 1, 0, v => { if (motion != null) motion.SmileTarget = (float)v; }));
            settings.Children.Add(Slider("Brows", -1, 1, 0, v => { if (motion != null) motion.BrowTarget = (float)v; }));
            settings.Children.Add(Check("Wink left", v => { if (motion != null) motion.WinkLTarget = v ? 1 : 0; }, false));
            settings.Children.Add(Check("Wink right", v => { if (motion != null) motion.WinkRTarget = v ? 1 : 0; }, false));
            settings.Children.Add(Slider("Twist", -1, 1, 0, v => { if (motion != null) motion.TwistTarget = (float)v; }));
            var playRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
            playRow.Children.Add(Act("Nod", () => motion?.NodOnce()));
            playRow.Children.Add(Space(6));
            playRow.Children.Add(Act("Shake", () => motion?.Shake()));
            playRow.Children.Add(Space(6));
            playRow.Children.Add(Act("Jump", () => motion?.Jump()));
            settings.Children.Add(playRow);
            settings.Children.Add(Space(8));

            settings.Children.Add(Header("World"));
            settings.Children.Add(Check("Wind", v => switches.Wind = v, switches.Wind));
            settings.Children.Add(Slider("Wind gusts", 0, 3, 1, v => { if (motion != null) motion.WindStrength = (float)v; }));
            var hint = new TextBlock
            {
                Text = "Drag the hair or skirt to pull it.\nSpace jumps.",
                Foreground = Brushes.DimGray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 6, 0, 0),
            };
            settings.Children.Add(hint);
            settings.Children.Add(Space(8));
            settings.Children.Add(notes);

            var columns = new Grid();
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(middle, 0);
            var settingsScroll = new ScrollViewer
            {
                Content = settings,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 12),
            };
            Grid.SetColumn(settingsScroll, 1);
            columns.Children.Add(middle);
            columns.Children.Add(settingsScroll);

            var page = new Grid();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(top, 0);
            Grid.SetRow(columns, 1);
            page.Children.Add(top);
            page.Children.Add(columns);

            Content = page;
            status.Text = "Drop or choose a .psd: layers named as See-through or Anime2.5DRig names them move.";

            CompositionTarget.Rendering += Frame;
            Closed += (s, e) => { renderer.Dispose(); mic?.Dispose(); };
        }

        void ChooseFile()
        {
            var dialog = new OpenFileDialog { Filter = "Photoshop documents (*.psd)|*.psd" };
            if (dialog.ShowDialog(this) == true) OpenDocument(dialog.FileName);
        }

        void OnDrop(object s, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0) OpenDocument(files[0]);
        }

        /// Sets one of the pose targets by name, as the command line's key=value does.
        public void SetPose(string key, float v)
        {
            poseArgs[key] = v;
            if (motion == null) return;
            ApplyPoseArgs();
        }

        readonly Dictionary<string, float> poseArgs = new Dictionary<string, float>();

        void ApplyPoseArgs()
        {
            foreach (var kv in poseArgs)
                switch (kv.Key)
                {
                    case "mouth": motion.MouthManual = kv.Value; break;
                    case "smile": motion.SmileTarget = kv.Value; break;
                    case "brow": motion.BrowTarget = kv.Value; break;
                    case "twist": motion.TwistTarget = kv.Value; break;
                    case "winkl": motion.WinkLTarget = kv.Value; break;
                    case "winkr": motion.WinkRTarget = kv.Value; break;
                    case "jump": motion.Jump(); break;
                    case "shake": motion.Shake(); break;
                    case "talk": autoTalk = kv.Value != 0; break;
                }
        }
        {
            reading = true;
            status.Text = $"Reading {Path.GetFileName(path)}…";
            try
            {
                var made = await Task.Run(() => Rig.Build(PsdDocument.Read(File.ReadAllBytes(path))));
                rig = made;
                file = path;
                motion = new RigMotion(rig);
                ApplyPoseArgs();
                renderer.Load(rig);
                status.Text = $"{Path.GetFileName(path)}: {rig.Parts.Count} layers, "
                    + $"{rig.Width} × {rig.Height}; {rig.Eyes.Count} of 2 eyes blink.";
                notes.Text = string.Join("\n", rig.Notes);
            }
            catch (Exception error)
            {
                status.Text = $"{Path.GetFileName(path)}: {error.Message}";
            }
            reading = false;
        }

        /// Where the pointer is over the drawing in the stage: -1 to 1 across it and down it, as
        /// far as 1.5 beyond it, or null when it is out.
        Vector2? PointerAt(System.Windows.Point p, Grid stage)
        {
            if (rig == null || bitmap == null) return null;
            var fitted = RigRenderer.Fit(rig.Width, rig.Height, (float)stage.ActualWidth, (float)stage.ActualHeight);
            if (fitted.Width <= 0 || fitted.Height <= 0) return null;
            double x = (p.X - fitted.Left) / fitted.Width * 2 - 1;
            // Down the drawing: the view's y is up.
            double y = (1 - (p.Y - fitted.Top) / fitted.Height) * 2 - 1;
            if (x < -1.5 || x > 1.5 || y < -1.5 || y > 1.5) return null;
            return new Vector2((float)Math.Min(Math.Max(x, -1.5), 1.5), (float)Math.Min(Math.Max(y, -1.5), 1.5));
        }

        /// A stage point in canvas pixels, the same space the rig's vertices live in.
        Vector2 CanvasAt(System.Windows.Point p)
        {
            var fitted = RigRenderer.Fit(rig.Width, rig.Height, (float)stage.ActualWidth, (float)stage.ActualHeight);
            return new Vector2(
                (float)((p.X - fitted.Left) / Math.Max(1, fitted.Width) * rig.Width),
                (float)((p.Y - fitted.Top) / Math.Max(1, fitted.Height) * rig.Height));
        }

        /// The part under a canvas point, topmost first: a swinging one if there is one under it.
        int PickPart(Vector2 at)
        {
            int any = -1;
            for (int i = rig.Parts.Count - 1; i >= 0; i--)
            {
                var part = rig.Parts[i];
                if (part.Mesh.Rest.Length == 0) continue;
                float mnx = float.MaxValue, mny = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue;
                foreach (var p in part.Mesh.Rest)
                {
                    mnx = Math.Min(mnx, p.X); mxx = Math.Max(mxx, p.X);
                    mny = Math.Min(mny, p.Y); mxy = Math.Max(mxy, p.Y);
                }
                if (at.X < mnx || at.X > mxx || at.Y < mny || at.Y > mxy) continue;
                if (part.Swing != null || part.Slot == "bottomwear") return i;
                if (any < 0) any = i;
            }
            return any;
        }

        /// Moves the rig on and draws it, each frame the screen composes.
        void Frame(object s, EventArgs e)
        {
            var now = DateTime.UtcNow;
            double dt = lastFrame == DateTime.MinValue ? 0 : (now - lastFrame).TotalSeconds;
            lastFrame = now;
            if (rig == null || motion == null || reading) return;

            // Draw at the stage's pixel size (DIP aware).
            var dpi = VisualTreeHelper.GetDpi(this);
            int w = Math.Max(1, (int)(stage.ActualWidth * dpi.DpiScaleX));
            int h = Math.Max(1, (int)(stage.ActualHeight * dpi.DpiScaleY));
            if (bitmap == null || bitmap.PixelWidth != w || bitmap.PixelHeight != h)
            {
                bitmap = new WriteableBitmap(w, h, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY,
                    PixelFormats.Pbgra32, null);
                picture.Source = bitmap;
            }

            // The talk level: the mic's loudness while it is on, or made-up babble, or both.
            float talk = 0;
            if (micOn)
            {
                if (mic == null) { try { mic = new MicLevel(); } catch { } }
                if (mic != null && mic.Open) { mic.Poll(); talk = mic.Level; }
            }
            if (autoTalk)
            {
                double bt = DateTime.UtcNow.TimeOfDay.TotalSeconds;
                // Bursts of babble with gaps, as speech comes and goes.
                double gate = Math.Max(0, Math.Sin(bt * 0.9) * 0.6 + Math.Sin(bt * 0.31 + 1) * 0.5);
                double babble = Math.Abs(Math.Sin(bt * 21) * 0.55 + Math.Sin(bt * 33 + 0.7) * 0.45);
                talk = Math.Max(talk, (float)(gate * babble));
            }
            motion.TalkLevel = talk;
            if (dragging >= 0) motion.Grab(dragging, grabNow - grabStart);

            motion.Step(dt, rig, switches, pointer, open, ease, nod, follow);
            var frame = motion.Frame(rig, switches.Physics);

            bitmap.Lock();
            var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
            using (var surface = SKSurface.Create(info, bitmap.BackBuffer, bitmap.BackBufferStride))
            {
                renderer.Encode(surface.Canvas, frame, w, h, new SKColor(0, 0, 0, 0));
            }
            bitmap.AddDirtyRect(new Int32Rect(0, 0, w, h));
            bitmap.Unlock();
        }

        // MARK: - small pieces of the page

        static UIElement Space(double width) => new Border { Width = width, Height = 1 };

        static Button Act(string label, Action action)
        {
            var button = new Button { Content = label, Padding = new Thickness(12, 4, 12, 4) };
            button.Click += (s, e) => action();
            return button;
        }

        static TextBlock Header(string text) => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 10, 0, 6),
        };

        CheckBox Check(string label, Action<bool> set, bool value)
        {
            var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 2, 0, 2) };
            box.Checked += (s, e) => set(true);
            box.Unchecked += (s, e) => set(false);
            return box;
        }

        UIElement Slider(string label, double min, double max, double value, Action<double> set)
        {
            var text = new TextBlock { Text = $"{label}: {value:0.00}", Margin = new Thickness(0, 4, 0, 0) };
            var slider = new System.Windows.Controls.Slider
            {
                Minimum = min,
                Maximum = max,
                Value = value,
                TickFrequency = 0.05,
            };
            slider.ValueChanged += (s, e) =>
            {
                set(e.NewValue);
                text.Text = $"{label}: {e.NewValue:0.00}";
            };
            var panel = new StackPanel();
            panel.Children.Add(text);
            panel.Children.Add(slider);
            return panel;
        }

        /// A checkerboard, as a drawing's transparency is shown.
        static DrawingBrush Checkerboard()
        {
            var group = new GeometryGroup();
            group.Children.Add(new RectangleGeometry(new System.Windows.Rect(0, 0, 8, 8)));
            group.Children.Add(new RectangleGeometry(new System.Windows.Rect(8, 8, 8, 8)));
            var drawing = new GeometryDrawing(
                new SolidColorBrush(Color.FromRgb(0x28, 0x28, 0x2c)), null, group);
            var brush = new DrawingBrush(new DrawingGroup
            {
                Children =
                {
                    new GeometryDrawing(new SolidColorBrush(Color.FromRgb(0x1e, 0x1e, 0x22)), null,
                        new RectangleGeometry(new System.Windows.Rect(0, 0, 16, 16))),
                    drawing,
                },
            })
            {
                TileMode = TileMode.Tile,
                Viewport = new System.Windows.Rect(0, 0, 16, 16),
                ViewportUnits = BrushMappingMode.Absolute,
            };
            brush.Freeze();
            return brush;
        }
    }
}
