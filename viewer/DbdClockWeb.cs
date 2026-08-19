using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace DbdClockWeb
{
    public class WebForm : Form
    {
        private const string BaseUrl = "https://dbd-clock-maps.cloudflare-hankering577.workers.dev";

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private const int GwlExstyle = -20;
        private const int WsExLayered = 0x00080000;
        private const int WsExTransparent = 0x00000020;

        private const uint ModControl = 0x0002;
        private const uint ModAlt = 0x0001;
        private const uint ModWin = 0x0008;
        private const int WmHotkey = 0x0312;
        private const int WmNclbuttondown = 0x00A1;
        private const int HtCaption = 2;
        private const int HtBottomRight = 17;
        private const int HkToggleVisible = 1;
        private const int HkToggleClean = 2;
        private const int HkOpacityUp = 3;
        private const int HkOpacityDown = 4;
        private const int HkScaleDown = 5;
        private const int HkScaleUp = 6;
        private const int HkSnap = 7;

        private readonly string _configDir;
        private readonly string _configFile;

        private Panel _strip;
        private Panel _grip;
        private WebView2 _web;
        private PictureBox _still;
        private Timer _capTimer;
        private int _opacityPercent;
        private bool _display;
        private bool _cleanBefore;
        private Rectangle _boundsBefore;
        private double _displayZoom;

        [STAThread]
        public static void Main()
        {
            try
            {
                int self = System.Diagnostics.Process.GetCurrentProcess().Id;
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("DbdClockViewer"))
                {
                    try { p.Kill(); } catch (Exception) { }
                }
                foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcessesByName("DbdClockWeb"))
                {
                    try { if (p.Id != self) p.Kill(); } catch (Exception) { }
                }
            }
            catch (Exception) { }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new WebForm());
        }

        public WebForm()
        {
            _opacityPercent = 100;
            _configDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DbdClockWeb");
            _configFile = Path.Combine(_configDir, "config.txt");
            try { Directory.CreateDirectory(_configDir); } catch (Exception) { }

            Text = "DBD Clock Web";
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = true;
            BackColor = Color.FromArgb(16, 16, 16);
            Width = 900;
            Height = 700;
            MinimumSize = new Size(240, 200);
            Left = 120;
            Top = 120;

            BuildUi();
            LoadConfig();
            ClampToScreen();
            ApplyOpacity();

            Load += OnLoadForm;
            FormClosing += OnClosingForm;
        }

        private void BuildUi()
        {
            _strip = new Panel();
            _strip.Dock = DockStyle.Top;
            _strip.Height = 28;
            _strip.BackColor = Color.FromArgb(30, 30, 30);
            _strip.MouseDown += OnStripMouseDown;

            Label title = new Label();
            title.Text = "DBD Clock Web  (drag here)";
            title.ForeColor = Color.Gainsboro;
            title.AutoSize = false;
            title.Dock = DockStyle.Fill;
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Padding = new Padding(8, 0, 0, 0);
            title.MouseDown += OnStripMouseDown;

            Button close = new Button();
            close.Text = "X";
            close.Dock = DockStyle.Right;
            close.Width = 36;
            close.FlatStyle = FlatStyle.Flat;
            close.ForeColor = Color.Gainsboro;
            close.FlatAppearance.BorderSize = 0;
            close.Click += delegate(object s, EventArgs e) { Close(); };

            _strip.Controls.Add(title);
            _strip.Controls.Add(close);

            _web = new WebView2();
            _web.Dock = DockStyle.Fill;

            _still = new PictureBox();
            _still.Dock = DockStyle.Fill;
            _still.SizeMode = PictureBoxSizeMode.Zoom;
            _still.BackColor = Color.FromArgb(16, 16, 16);
            _still.Visible = false;

            _capTimer = new Timer();
            _capTimer.Interval = 350;
            _capTimer.Tick += OnCapTimer;

            _grip = new Panel();
            _grip.Size = new Size(16, 16);
            _grip.BackColor = Color.FromArgb(70, 70, 70);
            _grip.Cursor = Cursors.SizeNWSE;
            _grip.MouseDown += OnGripMouseDown;

            Controls.Add(_web);
            Controls.Add(_still);
            Controls.Add(_strip);
            Controls.Add(_grip);
            PositionGrip();
            _grip.BringToFront();
            Resize += delegate(object s, EventArgs e) { PositionGrip(); };
        }

        private void PositionGrip()
        {
            _grip.Location = new Point(ClientSize.Width - _grip.Width, ClientSize.Height - _grip.Height);
        }

        private void OnStripMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WmNclbuttondown, (IntPtr)HtCaption, IntPtr.Zero);
            }
        }

        private void OnGripMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                ReleaseCapture();
                SendMessage(Handle, WmNclbuttondown, (IntPtr)HtBottomRight, IntPtr.Zero);
            }
        }

        private async void OnLoadForm(object sender, EventArgs e)
        {
            RegisterHotkeys();
            try
            {
                CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(_configDir, "wv2"), null);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.WebMessageReceived += OnWebMessage;
                await _web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync("window.addEventListener('keydown', function(e){ var t = e.target; var ed = t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable); if (!ed) return; var k = e.key || ''; if (k === 'ArrowLeft' || k === 'ArrowRight' || k.toLowerCase() === 'h') { e.stopImmediatePropagation(); } }, true); document.addEventListener('DOMContentLoaded', function(){ try { var img = document.getElementById('mapImage'); if (img) { var post = function(){ try { window.chrome.webview.postMessage('map'); } catch (err) {} }; img.addEventListener('load', post); new MutationObserver(post).observe(img, { attributes: true, attributeFilter: ['src'] }); } } catch (err) {} });");
                _web.CoreWebView2.Navigate(BaseUrl);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "WebView2 init failed: " + ex.Message, "DBD Clock Web");
            }
        }

        private void RegisterHotkeys()
        {
            bool ok = true;
            ok = RegisterHotKey(Handle, HkToggleVisible, ModControl | ModWin | ModAlt, 0x4D) && ok;
            ok = RegisterHotKey(Handle, HkToggleClean, ModControl | ModWin | ModAlt, 0x58) && ok;
            ok = RegisterHotKey(Handle, HkSnap, ModControl | ModWin | ModAlt, 0x56) && ok;
            ok = RegisterHotKey(Handle, HkOpacityUp, ModControl | ModWin | ModAlt, 0x26) && ok;
            ok = RegisterHotKey(Handle, HkOpacityDown, ModControl | ModWin | ModAlt, 0x28) && ok;
            ok = RegisterHotKey(Handle, HkScaleDown, ModControl | ModWin | ModAlt, 0x25) && ok;
            ok = RegisterHotKey(Handle, HkScaleUp, ModControl | ModWin | ModAlt, 0x27) && ok;
            if (!ok) Text = "DBD Clock Web (hotkey conflict)";
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmHotkey)
            {
                int id = m.WParam.ToInt32();
                if (id == HkToggleVisible) ToggleVisible();
                else if (id == HkToggleClean) SwapMode();
                else if (id == HkSnap) ToggleDisplay();
                else if (id == HkOpacityUp) ChangeOpacity(10);
                else if (id == HkOpacityDown) ChangeOpacity(-10);
                else if (id == HkScaleUp) ScaleStep(1.05);
                else if (id == HkScaleDown) ScaleStep(1.0 / 1.05);
                return;
            }
            base.WndProc(ref m);
        }

        private void ToggleVisible()
        {
            if (Visible) Hide();
            else Show();
        }

        private async void SwapMode()
        {
            try
            {
                if (_web.CoreWebView2 == null) return;
                if (_display) await ExitDisplay();
                bool clean = await GetSiteClean();
                await SetSiteClean(!clean);
            }
            catch (Exception) { }
        }

        private async void ToggleDisplay()
        {
            try
            {
                if (_display) await ExitDisplay();
                else await EnterDisplay();
            }
            catch (Exception) { }
        }

        private async Task EnterDisplay()
        {
            if (_web.CoreWebView2 == null) return;
            _cleanBefore = await GetSiteClean();
            _boundsBefore = Bounds;
            _displayZoom = 1.0;
            _web.Dock = DockStyle.None;
            await SetSiteClean(true);
            await Task.Delay(400);
            bool ok = await CaptureToStill();
            if (!ok)
            {
                _web.Dock = DockStyle.Fill;
                await SetSiteClean(_cleanBefore);
                return;
            }
            _display = true;
            _strip.Visible = false;
            _grip.Visible = false;
            _still.Visible = true;
            _still.BringToFront();
            SetClickThrough(true);
        }

        private async Task ExitDisplay()
        {
            _display = false;
            SetClickThrough(false);
            _still.Visible = false;
            _web.Dock = DockStyle.Fill;
            Bounds = _boundsBefore;
            _strip.Visible = true;
            _grip.Visible = true;
            _grip.BringToFront();
            try { await SetSiteClean(_cleanBefore); } catch (Exception) { }
        }

        private void SetClickThrough(bool on)
        {
            int style = GetWindowLong(Handle, GwlExstyle);
            if (on) SetWindowLong(Handle, GwlExstyle, style | WsExLayered | WsExTransparent);
            else SetWindowLong(Handle, GwlExstyle, style & ~WsExTransparent);
        }

        private async Task<bool> GetSiteClean()
        {
            string raw = await _web.CoreWebView2.ExecuteScriptAsync("(function(){var b=document.getElementById('cleanButton'); return b ? b.textContent : '';})();");
            return StripQuotes(raw) == "UI";
        }

        private async Task SetSiteClean(bool want)
        {
            bool cur = await GetSiteClean();
            if (cur != want)
            {
                await _web.CoreWebView2.ExecuteScriptAsync("(function(){var b=document.getElementById('cleanButton'); if(b){ b.click(); } })();");
            }
        }

        private static string StripQuotes(string raw)
        {
            if (raw == null) return "";
            string s = raw.Trim();
            if (s.StartsWith("\"", StringComparison.Ordinal)) s = s.Substring(1);
            if (s.EndsWith("\"", StringComparison.Ordinal)) s = s.Substring(0, s.Length - 1);
            return s;
        }

        private async Task<bool> CaptureToStill()
        {
            string raw = await _web.CoreWebView2.ExecuteScriptAsync("(function(){var el=document.getElementById('mapImage'); if(!el||!el.naturalWidth) return ''; var r=el.getBoundingClientRect(); return r.left+'|'+r.top+'|'+r.width+'|'+r.height+'|'+el.naturalWidth+'|'+el.naturalHeight+'|'+window.devicePixelRatio;})();");
            string s = StripQuotes(raw);
            if (s.Length == 0) return false;
            string[] parts = s.Split('|');
            if (parts.Length != 7) return false;
            double rl = ParseD(parts[0]);
            double rt = ParseD(parts[1]);
            double rw = ParseD(parts[2]);
            double rh = ParseD(parts[3]);
            double nw = ParseD(parts[4]);
            double nh = ParseD(parts[5]);
            double dpr = ParseD(parts[6]);
            if (rw <= 0 || rh <= 0 || nw <= 0 || nh <= 0) return false;
            if (dpr <= 0) dpr = 1.0;
            double sc = Math.Min(rw / nw, rh / nh);
            double iw = nw * sc;
            double ih = nh * sc;
            double ox = rl + (rw - iw) / 2.0;
            double oy = rt + (rh - ih) / 2.0;
            MemoryStream ms = new MemoryStream();
            try
            {
                await _web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
                ms.Position = 0;
                Bitmap full = new Bitmap(ms);
                Rectangle crop = new Rectangle((int)Math.Round(ox * dpr), (int)Math.Round(oy * dpr), (int)Math.Round(iw * dpr), (int)Math.Round(ih * dpr));
                Rectangle bounds = new Rectangle(0, 0, full.Width, full.Height);
                crop.Intersect(bounds);
                if (crop.Width <= 0 || crop.Height <= 0)
                {
                    full.Dispose();
                    return false;
                }
                Bitmap cut = full.Clone(crop, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                full.Dispose();
                Image old = _still.Image;
                _still.Image = cut;
                if (old != null) old.Dispose();
                SizeToStill(cut.Width, cut.Height);
            }
            finally
            {
                ms.Dispose();
            }
            return true;
        }

        private void SizeToStill(int pw, int ph)
        {
            if (pw <= 0 || ph <= 0) return;
            double zoom = _displayZoom;
            if (zoom <= 0) zoom = 1.0;
            int w = (int)Math.Round(pw * zoom);
            int h = (int)Math.Round(ph * zoom);
            int maxW;
            int maxH;
            GrowthCap(out maxW, out maxH);
            double fit = 1.0;
            if (w > maxW) fit = (double)maxW / w;
            if (h > maxH)
            {
                double f2 = (double)maxH / h;
                if (f2 < fit) fit = f2;
            }
            if (fit < 1.0)
            {
                w = (int)Math.Round(w * fit);
                h = (int)Math.Round(h * fit);
            }
            if (w < MinimumSize.Width) w = MinimumSize.Width;
            if (h < MinimumSize.Height) h = MinimumSize.Height;
            ClientSize = new Size(w, h);
        }

        private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string msg = null;
            try { msg = e.TryGetWebMessageAsString(); } catch (Exception) { }
            if (msg != "map") return;
            if (!_display) return;
            _capTimer.Stop();
            _capTimer.Start();
        }

        private async void OnCapTimer(object sender, EventArgs e)
        {
            _capTimer.Stop();
            if (!_display) return;
            try { await CaptureToStill(); } catch (Exception) { }
        }

        private static double ParseD(string value)
        {
            double d;
            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            return 0;
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
            int strip = _strip.Visible ? _strip.Height : 0;
            double cw = ClientSize.Width * factor;
            double chc = (ClientSize.Height - strip) * factor;
            int maxW;
            int maxH;
            GrowthCap(out maxW, out maxH);
            int w = (int)Math.Round(cw);
            int h = (int)Math.Round(chc) + strip;
            if (w > maxW) w = maxW;
            if (h > maxH) h = maxH;
            if (w < MinimumSize.Width) w = MinimumSize.Width;
            if (h < MinimumSize.Height) h = MinimumSize.Height;
            ClientSize = new Size(w, h);
            if (_display && _still.Image != null && _still.Image.Width > 0)
            {
                _displayZoom = (double)ClientSize.Width / _still.Image.Width;
            }
        }

        private void GrowthCap(out int maxW, out int maxH)
        {
            maxW = 10000;
            maxH = 10000;
            try
            {
                Screen s = Screen.FromPoint(new Point(Left + 30, Top + 30));
                maxW = s.WorkingArea.Right - Left - 10;
                maxH = s.WorkingArea.Bottom - Top - 10;
            }
            catch (Exception) { }
            if (maxW < MinimumSize.Width) maxW = MinimumSize.Width;
            if (maxH < MinimumSize.Height) maxH = MinimumSize.Height;
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
                    int n;
                    if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) continue;
                    if (key == "left") Left = n;
                    else if (key == "top") Top = n;
                    else if (key == "width" && n >= MinimumSize.Width) Width = n;
                    else if (key == "height" && n >= MinimumSize.Height) Height = n;
                    else if (key == "opacity" && n >= 20 && n <= 100) _opacityPercent = n;
                }
            }
            catch (Exception) { }
        }

        private void SaveConfig()
        {
            try
            {
                Directory.CreateDirectory(_configDir);
                StringBuilder sb = new StringBuilder();
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
            bool onScreen = false;
            Point p = new Point(Left + 30, Top + 30);
            foreach (Screen s in Screen.AllScreens)
            {
                if (s.Bounds.Contains(p)) { onScreen = true; break; }
            }
            if (!onScreen)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Left = wa.Left + 80;
                Top = wa.Top + 80;
            }
        }

        private void OnClosingForm(object sender, FormClosingEventArgs e)
        {
            SaveConfig();
            UnregisterHotKey(Handle, HkToggleVisible);
            UnregisterHotKey(Handle, HkToggleClean);
            UnregisterHotKey(Handle, HkSnap);
            UnregisterHotKey(Handle, HkOpacityUp);
            UnregisterHotKey(Handle, HkOpacityDown);
            UnregisterHotKey(Handle, HkScaleDown);
            UnregisterHotKey(Handle, HkScaleUp);
        }
    }
}
