using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace DbdClockViewer
{
    public class ViewerWindow : Window
    {
        private const string BaseUrl = "https://dbd-clock-maps.cloudflare-hankering577.workers.dev";

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private const int GwlExstyle = -20;
        private const int WsExTransparent = 0x00000020;
        private const uint ModControl = 0x0002;
        private const uint ModAlt = 0x0001;
        private const uint ModWin = 0x0008;
        private const int WmHotkey = 0x0312;
        private const int HkToggleVisible = 1;
        private const int HkToggleMode = 2;
        private const int HkOpacityUp = 3;
        private const int HkOpacityDown = 4;
        private const int HkScaleDown = 5;
        private const int HkScaleUp = 6;

        private readonly string _configDir;
        private readonly string _cacheDir;
        private readonly string _configFile;
        private readonly string _clientId;

        private IntPtr _hwnd;
        private bool _clickThrough;
        private int _opacityPercent;
        private string _room;
        private string _currentPath;

        private volatile bool _running;
        private int _syncGeneration;

        private Border _topBar;
        private TextBox _roomBox;
        private TextBlock _status;
        private Image _image;
        private TextBlock _placeholder;
        private Grid _contentGrid;
        private double _scale;
        private int _imgW;
        private int _imgH;

        [STAThread]
        public static void Main()
        {
            try
            {
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("DbdClockWeb"))
                {
                    try { p.Kill(); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | SecurityProtocolType.Tls12;
            Application app = new Application();
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            ViewerWindow win = new ViewerWindow();
            app.MainWindow = win;
            app.Run(win);
        }

        public ViewerWindow()
        {
            _opacityPercent = 90;
            _room = "";
            _currentPath = "";
            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbdClockViewer");
            _cacheDir = Path.Combine(_configDir, "cache");
            _configFile = Path.Combine(_configDir, "config.txt");
            _clientId = Guid.NewGuid().ToString("N");
            try { Directory.CreateDirectory(_cacheDir); } catch (Exception) { }

            Title = "DBD Clock Viewer";
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            Topmost = true;
            ShowInTaskbar = true;
            Background = new SolidColorBrush(Color.FromRgb(16, 16, 16));
            Width = 420;
            Height = 460;
            MinWidth = 180;
            MinHeight = 180;

            BuildUi();
            LoadConfig();
            ApplyOpacity();
            ClampToScreen();

            SourceInitialized += OnSourceInitialized;
            Closing += OnClosingWindow;
            MouseLeftButtonDown += OnDragArea;
            SizeChanged += OnSizeChanged;
        }

        private void BuildUi()
        {
            DockPanel root = new DockPanel();

            _topBar = new Border();
            _topBar.Background = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            _topBar.Padding = new Thickness(6);
            DockPanel.SetDock(_topBar, Dock.Top);

            StackPanel bar = new StackPanel();
            bar.Orientation = Orientation.Horizontal;

            _roomBox = new TextBox();
            _roomBox.Width = 90;
            _roomBox.MaxLength = 8;
            _roomBox.CharacterCasing = CharacterCasing.Upper;
            _roomBox.VerticalContentAlignment = VerticalAlignment.Center;
            _roomBox.KeyDown += OnRoomKeyDown;

            Button connect = new Button();
            connect.Content = "Connect";
            connect.Margin = new Thickness(6, 0, 6, 0);
            connect.Padding = new Thickness(10, 2, 10, 2);
            connect.Click += OnConnectClick;

            _status = new TextBlock();
            _status.Foreground = Brushes.Gainsboro;
            _status.VerticalAlignment = VerticalAlignment.Center;
            _status.Text = "Enter room code";

            bar.Children.Add(_roomBox);
            bar.Children.Add(connect);
            bar.Children.Add(_status);
            _topBar.Child = bar;

            Grid content = new Grid();
            _contentGrid = content;
            _image = new Image();
            _image.Stretch = Stretch.Uniform;
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);

            _placeholder = new TextBlock();
            _placeholder.Text = "Waiting for host...";
            _placeholder.Foreground = Brushes.Gray;
            _placeholder.FontSize = 16;
            _placeholder.TextAlignment = TextAlignment.Center;
            _placeholder.HorizontalAlignment = HorizontalAlignment.Center;
            _placeholder.VerticalAlignment = VerticalAlignment.Center;

            content.Children.Add(_image);
            content.Children.Add(_placeholder);

            root.Children.Add(_topBar);
            root.Children.Add(content);
            Content = root;
        }

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            HwndSource source = HwndSource.FromHwnd(_hwnd);
            if (source != null) source.AddHook(WndProc);

            bool ok = true;
            ok = RegisterHotKey(_hwnd, HkToggleVisible, ModControl | ModWin | ModAlt, 0x4D) && ok;
            ok = RegisterHotKey(_hwnd, HkToggleMode, ModControl | ModWin | ModAlt, 0x56) && ok;
            ok = RegisterHotKey(_hwnd, HkOpacityUp, ModControl | ModWin | ModAlt, 0x26) && ok;
            ok = RegisterHotKey(_hwnd, HkOpacityDown, ModControl | ModWin | ModAlt, 0x28) && ok;
            ok = RegisterHotKey(_hwnd, HkScaleDown, ModControl | ModWin | ModAlt, 0x25) && ok;
            ok = RegisterHotKey(_hwnd, HkScaleUp, ModControl | ModWin | ModAlt, 0x27) && ok;
            if (!ok) SetStatus("Hotkey conflict, some hotkeys unavailable");

            if (_room.Length > 0)
            {
                _roomBox.Text = _room;
                StartSync();
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey)
            {
                int id = wParam.ToInt32();
                if (id == HkToggleVisible) { ToggleVisible(); handled = true; }
                else if (id == HkToggleMode) { ToggleMode(); handled = true; }
                else if (id == HkOpacityUp) { ChangeOpacity(10); handled = true; }
                else if (id == HkOpacityDown) { ChangeOpacity(-10); handled = true; }
                else if (id == HkScaleUp) { ScaleStep(1.05); handled = true; }
                else if (id == HkScaleDown) { ScaleStep(1.0 / 1.05); handled = true; }
            }
            return IntPtr.Zero;
        }

        private void ToggleVisible()
        {
            if (IsVisible) Hide();
            else Show();
        }

        private void ToggleMode()
        {
            _clickThrough = !_clickThrough;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (_hwnd == IntPtr.Zero) return;
            int style = GetWindowLong(_hwnd, GwlExstyle);
            if (_clickThrough)
            {
                SetWindowLong(_hwnd, GwlExstyle, style | WsExTransparent);
                _topBar.Visibility = Visibility.Collapsed;
                ResizeMode = ResizeMode.NoResize;
            }
            else
            {
                SetWindowLong(_hwnd, GwlExstyle, style & ~WsExTransparent);
                _topBar.Visibility = Visibility.Visible;
                ResizeMode = ResizeMode.CanResizeWithGrip;
            }
            ScheduleRecordScale();
        }

        private void ChangeOpacity(int delta)
        {
            int next = _opacityPercent + delta;
            if (next < 20) next = 20;
            if (next > 100) next = 100;
            _opacityPercent = next;
            ApplyOpacity();
        }

        private void ApplyOpacity()
        {
            Opacity = _opacityPercent / 100.0;
        }

        private void ScaleStep(double factor)
        {
            double barH = (_topBar.Visibility == Visibility.Visible) ? _topBar.ActualHeight : 0.0;
            double cw = Width * factor;
            double ch = (Height - barH) * factor;
            double maxW;
            double maxH;
            GrowthCap(out maxW, out maxH);
            if (cw > maxW) cw = maxW;
            if (ch + barH > maxH) ch = maxH - barH;
            if (cw < MinWidth) cw = MinWidth;
            if (ch + barH < MinHeight) ch = MinHeight - barH;
            Width = cw;
            Height = ch + barH;
        }

        private void OnDragArea(object sender, MouseButtonEventArgs e)
        {
            if (_clickThrough) return;
            if (e.LeftButton != MouseButtonState.Pressed) return;
            if (e.OriginalSource == _roomBox) return;
            try { DragMove(); } catch (Exception) { }
        }

        private void OnRoomKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) ConnectFromBox();
        }

        private void OnConnectClick(object sender, RoutedEventArgs e)
        {
            ConnectFromBox();
        }

        private void ConnectFromBox()
        {
            string code = SanitizeRoom(_roomBox.Text);
            if (code.Length == 0)
            {
                SetStatus("Invalid room code");
                return;
            }
            _roomBox.Text = code;
            _room = code;
            SaveConfig();
            StartSync();
        }

        private static string SanitizeRoom(string value)
        {
            if (value == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in value.ToUpperInvariant())
            {
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')) sb.Append(c);
                if (sb.Length == 8) break;
            }
            return sb.ToString();
        }

        private void StartSync()
        {
            _syncGeneration++;
            int gen = _syncGeneration;
            string room = _room;
            _running = true;
            SetStatus("Connecting to " + room + "...");
            Thread t = new Thread(delegate() { SyncLoop(gen, room); });
            t.IsBackground = true;
            t.Start();
        }

        private void SyncLoop(int gen, string room)
        {
            while (_running && gen == _syncGeneration)
            {
                try
                {
                    string url = BaseUrl + "/api/rooms/" + room + "/events?client=" + _clientId;
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                    req.Method = "GET";
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 70000;
                    req.Accept = "text/event-stream";
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (StreamReader reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                    {
                        SetStatus("Connected: " + room);
                        string eventName = null;
                        StringBuilder data = new StringBuilder();
                        string line;

                        while (_running && gen == _syncGeneration && (line = reader.ReadLine()) != null)
                        {
                            if (line.Length == 0)
                            {
                                if (eventName != null) HandleEvent(gen, eventName, data.ToString());
                                eventName = null;
                                data.Length = 0;
                                continue;
                            }
                            if (line.StartsWith("event:", StringComparison.Ordinal))
                            {
                                eventName = line.Substring(6).Trim();
                            }
                            else if (line.StartsWith("data:", StringComparison.Ordinal))
                            {
                                if (data.Length > 0) data.Append('\n');
                                data.Append(line.Substring(5).TrimStart());
                            }
                        }
                    }
                }
                catch (Exception) { }
                if (!_running || gen != _syncGeneration) break;
                SetStatus("Reconnecting to " + room + "...");
                Thread.Sleep(1500);
            }
        }

        private void HandleEvent(int gen, string eventName, string data)
        {
            if (eventName != "state") return;
            string mapPath = JsonString(data, "map");
            string name = JsonString(data, "name");
            if (gen != _syncGeneration) return;
            if (mapPath.Length == 0)
            {
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _placeholder.Text = "Waiting for host...";
                    _placeholder.Visibility = Visibility.Visible;
                }));
                return;
            }
            LoadMap(gen, mapPath, name);
        }

        private void LoadMap(int gen, string mapPath, string name)
        {
            if (mapPath == _currentPath) return;
            _currentPath = mapPath;
            SetStatus("Loading " + name + "...");

            try
            {
                string file = EnsureCached(mapPath);
                BitmapFrame frame;
                using (FileStream fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    BitmapDecoder decoder = BitmapDecoder.Create(fs, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                    frame = decoder.Frames[0];
                }
                if (frame.CanFreeze) frame.Freeze();
                if (gen != _syncGeneration) return;
                BitmapFrame captured = frame;
                string capturedName = name;
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    ApplyImage(captured, capturedName);
                }));
            }
            catch (NotSupportedException)
            {
                SetStatus("Cannot decode image, WebP codec missing");
                Dispatcher.BeginInvoke(new Action(delegate()
                {
                    _placeholder.Text = "WebP codec missing.\nInstall 'WebP Image Extensions'\nfrom the Microsoft Store.";
                    _placeholder.Visibility = Visibility.Visible;
                }));
                _currentPath = "";
            }
            catch (Exception ex)
            {
                SetStatus("Load failed: " + ex.Message);
                _currentPath = "";
            }
        }

        private string EnsureCached(string mapPath)
        {
            string ext = Path.GetExtension(mapPath);
            if (ext == null || ext.Length == 0 || ext.Length > 6) ext = ".img";
            string file = Path.Combine(_cacheDir, Sha1Hex(mapPath) + ext);
            string etagFile = file + ".etag";
            FileInfo info = new FileInfo(file);
            bool hasCache = info.Exists && info.Length > 0;
            string etag = null;
            if (hasCache && File.Exists(etagFile))
            {
                try { etag = File.ReadAllText(etagFile).Trim(); } catch (Exception) { }
                if (etag != null && etag.Length == 0) etag = null;
            }

            string url = BaseUrl + "/" + EscapePath(mapPath);
            string temp = file + ".part";
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET";
            req.Timeout = 30000;
            req.ReadWriteTimeout = 60000;
            if (etag != null) req.Headers[HttpRequestHeader.IfNoneMatch] = etag;
            string newTag = null;
            try
            {
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    newTag = resp.Headers[HttpResponseHeader.ETag];
                    using (Stream input = resp.GetResponseStream())
                    using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        byte[] buffer = new byte[65536];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            output.Write(buffer, 0, read);
                        }
                    }
                }
            }
            catch (WebException wex)
            {
                // 304 Not Modified lands here, as do network failures.
                // Either way the cached copy is the best we have.
                if (wex.Response != null) wex.Response.Close();
                try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { }
                if (hasCache) return file;
                throw;
            }
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
            try
            {
                if (newTag != null && newTag.Length > 0) File.WriteAllText(etagFile, newTag);
                else if (File.Exists(etagFile)) File.Delete(etagFile);
            }
            catch (Exception) { }
            return file;
        }

        private static string EscapePath(string path)
        {
            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = Uri.EscapeDataString(parts[i]);
            }
            return string.Join("/", parts);
        }

        private static string Sha1Hex(string value)
        {
            using (SHA1 sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
                StringBuilder sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        private void SetStatus(string text)
        {
            Dispatcher.BeginInvoke(new Action(delegate() { _status.Text = text; }));
        }

        private void ApplyImage(BitmapFrame frame, string name)
        {
            int iw = frame.PixelWidth;
            int ih = frame.PixelHeight;
            if (iw > 0 && ih > 0)
            {
                double barH = (_topBar.Visibility == Visibility.Visible) ? _topBar.ActualHeight : 0.0;
                double curW = _contentGrid.ActualWidth;
                double curH = _contentGrid.ActualHeight;
                if (curW <= 0) curW = Width;
                if (curH <= 0) curH = Height - barH;
                double sNew = 0;
                if (_scale > 0 && _imgW > 0 && _imgH > 0)
                {
                    double area = (_scale * _imgW) * (_scale * _imgH);
                    double sArea = Math.Sqrt(area / ((double)iw * ih));
                    sNew = Math.Sqrt(_scale * sArea);
                    double maxArea = area * 1.3;
                    double minArea = area / 1.3;
                    double newArea = (sNew * iw) * (sNew * ih);
                    if (newArea > maxArea) sNew = Math.Sqrt(maxArea / ((double)iw * ih));
                    else if (newArea < minArea) sNew = Math.Sqrt(minArea / ((double)iw * ih));
                }
                else if (curW > 0 && curH > 0)
                {
                    sNew = Math.Sqrt((curW * curH) / ((double)iw * ih));
                }
                if (sNew > 0)
                {
                    double cw = iw * sNew;
                    double ch = ih * sNew;
                    double maxW;
                    double maxH;
                    GrowthCap(out maxW, out maxH);
                    double fit = 1.0;
                    if (cw > maxW) fit = maxW / cw;
                    if (ch + barH > maxH)
                    {
                        double f2 = (maxH - barH) / ch;
                        if (f2 < fit) fit = f2;
                    }
                    if (fit < 1.0 && fit > 0)
                    {
                        cw = cw * fit;
                        ch = ch * fit;
                    }
                    if (cw < MinWidth) cw = MinWidth;
                    if (ch + barH < MinHeight) ch = MinHeight - barH;
                    Width = cw;
                    Height = ch + barH;
                }
            }
            _imgW = iw;
            _imgH = ih;
            _image.Source = frame;
            _placeholder.Visibility = Visibility.Collapsed;
            _status.Text = name;
            ScheduleRecordScale();
        }

        private void GrowthCap(out double maxW, out double maxH)
        {
            maxW = 10000;
            maxH = 10000;
            try
            {
                System.Drawing.Point p = new System.Drawing.Point((int)(Left + 30), (int)(Top + 30));
                System.Windows.Forms.Screen s = System.Windows.Forms.Screen.FromPoint(p);
                maxW = s.WorkingArea.Right - Left - 10;
                maxH = s.WorkingArea.Bottom - Top - 10;
            }
            catch (Exception) { }
            if (maxW < MinWidth) maxW = MinWidth;
            if (maxH < MinHeight) maxH = MinHeight;
        }

        private void ScheduleRecordScale()
        {
            Dispatcher.BeginInvoke(new Action(RecordScale), DispatcherPriority.Loaded);
        }

        private void RecordScale()
        {
            if (_imgW <= 0 || _imgH <= 0) return;
            double cw = _contentGrid.ActualWidth;
            double ch = _contentGrid.ActualHeight;
            if (cw <= 0 || ch <= 0) return;
            double s = Math.Min(cw / _imgW, ch / _imgH);
            if (s > 0) _scale = s;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            ScheduleRecordScale();
        }

        private static string JsonString(string json, string key)
        {
            string needle = "\"" + key + "\"";
            int i = json.IndexOf(needle, StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf(':', i + needle.Length);
            if (i < 0) return "";
            i++;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length || json[i] != '"') return "";
            i++;
            StringBuilder sb = new StringBuilder();
            while (i < json.Length)
            {
                char c = json[i];
                if (c == '"') break;
                if (c == '\\' && i + 1 < json.Length)
                {
                    char n = json[i + 1];
                    if (n == 'u' && i + 5 < json.Length)
                    {
                        int code;
                        if (int.TryParse(json.Substring(i + 2, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out code)) sb.Append((char)code);
                        i += 6;
                        continue;
                    }

                    if (n == 'n') sb.Append('\n');
                    else if (n == 't') sb.Append('\t');
                    else if (n == 'r') sb.Append('\r');
                    else sb.Append(n);
                    i += 2;
                    continue;
                }
                sb.Append(c);
                i++;
            }
            return sb.ToString();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configFile)) return;
                foreach (string raw in File.ReadAllLines(_configFile))
                {
                    int idx = raw.IndexOf('=');
                    if (idx <= 0) continue;
                    string key = raw.Substring(0, idx).Trim();
                    string value = raw.Substring(idx + 1).Trim();
                    double d;

                    if (key == "room") _room = SanitizeRoom(value);
                    else if (key == "left" && TryParse(value, out d)) Left = d;
                    else if (key == "top" && TryParse(value, out d)) Top = d;
                    else if (key == "width" && TryParse(value, out d) && d >= MinWidth) Width = d;
                    else if (key == "height" && TryParse(value, out d) && d >= MinHeight) Height = d;
                    else if (key == "opacity")
                    {
                        int p;
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out p) && p >= 20 && p <= 100) _opacityPercent = p;
                    }
                }
            }
            catch (Exception) { }
        }

        private static bool TryParse(string value, out double result)
        {
            return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("room=" + _room);
                sb.AppendLine("left=" + Left.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("top=" + Top.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("width=" + Width.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("height=" + Height.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("opacity=" + _opacityPercent.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(_configFile, sb.ToString());
            }
            catch (Exception) { }
        }

        private void ClampToScreen()
        {
            Rect wa = SystemParameters.WorkArea;
            bool bad = double.IsNaN(Left) || double.IsNaN(Top) || !OnAnyScreen(Left, Top);
            if (bad)
            {
                Left = wa.Left + 80;
                Top = wa.Top + 80;
            }
        }

        private static bool OnAnyScreen(double left, double top)
        {
            System.Drawing.Point p = new System.Drawing.Point((int)(left + 30), (int)(top + 30));
            foreach (System.Windows.Forms.Screen s in System.Windows.Forms.Screen.AllScreens)
            {
                if (s.Bounds.Contains(p)) return true;
            }
            return false;
        }

        private void OnClosingWindow(object sender, CancelEventArgs e)
        {
            _running = false;
            _syncGeneration++;
            SaveConfig();
            if (_hwnd != IntPtr.Zero)
            {
                UnregisterHotKey(_hwnd, HkToggleVisible);
                UnregisterHotKey(_hwnd, HkToggleMode);
                UnregisterHotKey(_hwnd, HkOpacityUp);
                UnregisterHotKey(_hwnd, HkOpacityDown);
                UnregisterHotKey(_hwnd, HkScaleDown);
                UnregisterHotKey(_hwnd, HkScaleUp);
            }
        }
    }
}
