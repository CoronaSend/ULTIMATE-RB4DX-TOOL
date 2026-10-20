using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;

class SongTool : Form
{
    TextBox log; Label drop; string root; CheckBox bundle; TextBox packName;
    ComboBox ipBox; CheckBox sendBox; Button sendBtn; bool sendOn; string ip;
    ListView list; RockBar bar; Label status; Label pct; ComboBox densityBox; CheckBox euBox; Button cancelBtn; Process cur; volatile bool cancelled; Dictionary<string, string> covers = new Dictionary<string, string>();
    int totalSteps, doneSteps;

    static readonly Color BG = Color.FromArgb(18, 18, 24), PANEL = Color.FromArgb(31, 32, 42),
        RED = Color.FromArgb(226, 28, 44), ORANGE = Color.FromArgb(255, 150, 0), TEXT = Color.FromArgb(232, 232, 232),
        DIM = Color.FromArgb(140, 140, 150), GREEN = Color.FromArgb(60, 210, 110);

    class RockBar : Control
    {
        public int Maximum = 1, Value = 0;
        public RockBar() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true); }
        public void Set(int v, int max) { Value = v; Maximum = Math.Max(1, max); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BG);
            int rad = (Height - 1) / 2;
            using (var p = RR(new Rectangle(0, 0, Width - 1, Height - 1), rad))
            using (var b = new SolidBrush(Color.FromArgb(44, 45, 58))) g.FillPath(b, p);
            int w = (int)((long)(Width - 1) * Value / Maximum);
            if (w > 2)
                using (var p = RR(new Rectangle(0, 0, w, Height - 1), rad))
                using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, Math.Max(2, w), Height), RED, ORANGE, 0f))
                    g.FillPath(b, p);
        }
    }

    class DropZone : Label
    {
        public bool Hot;
        public DropZone() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true); }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics; g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.Clear(BG);
            var r = new Rectangle(10, 6, Width - 21, Height - 13);
            using (var p = RR(r, 18))
            {
                using (var b = new SolidBrush(Hot ? Color.FromArgb(52, 30, 36) : PANEL)) g.FillPath(b, p);
                using (var pen = new Pen(Hot ? ORANGE : RED, 2) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dash }) g.DrawPath(pen, p);
            }
            var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
            using (var b = new SolidBrush(Enabled ? TEXT : ORANGE)) g.DrawString(Text, Font, b, r, sf);
        }
    }

    static System.Drawing.Drawing2D.GraphicsPath RR(Rectangle r, int rad)
    {
        var p = new System.Drawing.Drawing2D.GraphicsPath(); int d = Math.Max(1, rad * 2);
        p.AddArc(r.X, r.Y, d, d, 180, 90); p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure(); return p;
    }

    static void Round(Control c, int rad)
    {
        EventHandler f = (s, e) =>
        {
            if (c.Width < 4 || c.Height < 4) return;
            var old = c.Region;
            using (var p = RR(new Rectangle(0, 0, c.Width, c.Height), Math.Min(rad, Math.Min(c.Width, c.Height) / 2)))
                c.Region = new Region(p);
            if (old != null) old.Dispose();
        };
        c.SizeChanged += f; f(c, EventArgs.Empty);
    }

    static Color Shade(Color c, int d)
    {
        Func<int, int> k = v => Math.Max(0, Math.Min(255, v + d));
        return Color.FromArgb(k(c.R), k(c.G), k(c.B));
    }

    // Rounds buttons/inputs and adds hover effects to everything under root.
    static void Style(Control root)
    {
        root.BackColor = root is Form ? BG : root.BackColor;
        foreach (Control c in root.Controls.Cast<Control>().ToArray())
        {
            var b = c as Button;
            if (b != null)
            {
                b.FlatStyle = FlatStyle.Flat; b.FlatAppearance.BorderSize = 0; b.Cursor = Cursors.Hand;
                if (b.BackColor == SystemColors.Control || b.BackColor.A == 0 || (b.Parent != null && b.BackColor == b.Parent.BackColor)) b.BackColor = Color.FromArgb(52, 54, 68);
                b.FlatAppearance.MouseOverBackColor = Shade(b.BackColor, 22);
                b.FlatAppearance.MouseDownBackColor = Shade(b.BackColor, -22);
                Round(b, 9);
            }
            else if (c is TextBox && !((TextBox)c).Multiline && c.Parent != null && !(c.Parent is Panel && c.Parent.Tag as string == "tbhost"))
            {
                var tb = (TextBox)c; var par = tb.Parent;
                var host = new Panel { Bounds = tb.Bounds, BackColor = tb.BackColor, Anchor = tb.Anchor, Tag = "tbhost" };
                par.Controls.Add(host); par.Controls.SetChildIndex(host, par.Controls.GetChildIndex(tb));
                tb.BorderStyle = BorderStyle.None; tb.Left = 8; tb.Width = host.Width - 16;
                tb.Top = Math.Max(0, (host.Height - tb.Height) / 2); tb.Anchor = AnchorStyles.Left | AnchorStyles.Right;
                host.Controls.Add(tb); Round(host, 7);
            }
            else if (c is ComboBox) { }
            if (c.HasChildren) Style(c);
        }
    }

    static TextBox FirstTextBox(Control r)
    {
        foreach (Control c in r.Controls)
        {
            if (c is TextBox) return (TextBox)c;
            var t = FirstTextBox(c); if (t != null) return t;
        }
        return null;
    }

    static Button Btn(string text, Color back, int left, int width)
    {
        var b = new Button { Text = text, Left = left, Top = 14, Width = width, Height = 30, FlatStyle = FlatStyle.Flat,
            BackColor = back, ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand,
            Anchor = AnchorStyles.Top | AnchorStyles.Right };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    const string OK = "\u2714 Done", WAIT = "Waiting", FAIL = "\u2716 Failed";

    [STAThread]
    static void Main() { Application.EnableVisualStyles(); Application.Run(new SongTool()); }

    SongTool()
    {
        root = AppDomain.CurrentDomain.BaseDirectory;
        AutoScaleMode = AutoScaleMode.None;
        Text = "ULTIMATE RB4DX TOOL v" + VERSION + " - made by CoronaSend";                 ClientSize = new Size(890, 800);
                MinimumSize = Size; BackColor = BG; ForeColor = TEXT; StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        drop = new DropZone { Text = "\u266B  DROP YOUR MP3s HERE  \u266B", Dock = DockStyle.Top, Height = 110,
            Font = new Font("Impact", 22), AllowDrop = true };

        var opts = new Panel { Dock = DockStyle.Top, Height = 40, BackColor = BG };
        bundle = new CheckBox { Text = "BUNDLE all songs into ONE package:", Left = 14, Top = 10, Width = 270, ForeColor = TEXT,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
        packName = new TextBox { Text = "My Song Pack", Left = 290, Top = 9, Width = 250, BackColor = PANEL, ForeColor = ORANGE,
            BorderStyle = BorderStyle.FixedSingle };
        opts.Controls.Add(bundle); opts.Controls.Add(packName);
        var find = Btn("BROWSE...", ORANGE, 544, 80);
        find.Anchor = AnchorStyles.Top | AnchorStyles.Left; find.Top = 5; find.ForeColor = Color.Black;
        find.Click += (s, e) => Browse();
        opts.Controls.Add(find);
        var web = Btn("WEB SEARCH", Color.FromArgb(0, 150, 200), 628, 104);
        web.Anchor = AnchorStyles.Top | AnchorStyles.Left; web.Top = 5;
        web.Click += (s, e) => WebSearch();
        opts.Controls.Add(web);
<<<<<<< HEAD
=======
        var ytmp3 = Btn("MP3 SEARCH", Color.FromArgb(200, 50, 50), 736, 90);
        ytmp3.Anchor = AnchorStyles.Top | AnchorStyles.Left; ytmp3.Top = 5; ytmp3.ForeColor = Color.White;
        ytmp3.Click += (s, e) => Mp3Search();
        opts.Controls.Add(ytmp3);
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
        drop.Cursor = Cursors.Hand;
        drop.Click += (s, e) => Browse();
        KeyPreview = true;
        KeyDown += (s, e) => { if (e.Control && e.KeyCode == Keys.O) Browse(); };

        list = new ListView { Dock = DockStyle.Top, Height = 160, View = View.Details, FullRowSelect = true, OwnerDraw = true,
            BackColor = PANEL, ForeColor = TEXT, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10) };
        list.Columns.Add("SONG", 244); list.Columns.Add("1. CHART + AUDIO", 150); list.Columns.Add("2. PS4 PKG", 150); list.Columns.Add("3. SEND TO PS4", 150);
        list.DrawColumnHeader += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(Color.FromArgb(44, 20, 24)), e.Bounds);
            TextRenderer.DrawText(e.Graphics, e.Header.Text, new Font("Segoe UI", 9, FontStyle.Bold), e.Bounds, ORANGE,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };
        list.DrawSubItem += (s, e) =>
        {
            e.Graphics.FillRectangle(new SolidBrush(e.ItemIndex % 2 == 0 ? PANEL : Color.FromArgb(34, 34, 40)), e.Bounds);
            string t = e.SubItem.Text; Color c = TEXT;
            if (e.ColumnIndex > 0)
                c = t.StartsWith("\u2714") ? GREEN : t.StartsWith("\u2716") ? RED : t.EndsWith("...") ? ORANGE : DIM;
            TextRenderer.DrawText(e.Graphics, t, list.Font, e.Bounds, c,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        };

        var prog = new Panel { Dock = DockStyle.Top, Height = 62, Width = 740, BackColor = BG };
        bar = new RockBar { Left = 12, Top = 10, Width = 616, Height = 20, Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        pct = new Label { Left = 636, Top = 9, Width = 92, Height = 22, Text = "0%", ForeColor = ORANGE, TextAlign = ContentAlignment.MiddleRight,
            Font = new Font("Impact", 12), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        status = new Label { Left = 12, Top = 36, Width = 610, Height = 20, Text = "Idle - waiting for a track", ForeColor = DIM,
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right };
        prog.Controls.Add(bar); prog.Controls.Add(pct); prog.Controls.Add(status);
        cancelBtn = Btn("CANCEL", RED, 740 - 12 - 90, 90); cancelBtn.Top = 33; cancelBtn.Height = 24; cancelBtn.Visible = false;
        cancelBtn.Click += (s, e) => CancelRun();
        prog.Controls.Add(cancelBtn);

        log = new TextBox { Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9), BackColor = Color.FromArgb(10, 10, 12), ForeColor = Color.FromArgb(170, 220, 170),
            BorderStyle = BorderStyle.None };

        var header = new Panel { Dock = DockStyle.Top, Height = 62, Width = 740, BackColor = Color.FromArgb(8, 8, 10) };
        var title = new Label { Text = "TOOL MADE BY CORONASEND", ForeColor = Color.White, AutoSize = false,
            Left = 12, Top = 4, Width = 390, Height = 36, TextAlign = ContentAlignment.MiddleLeft, Font = new Font("Impact", 20) };
        var sub = new Label { Text = "ULTIMATE RB4DX TOOL:  MP3  \u2192  CHART  \u2192  PS4 PKG", ForeColor = DIM, AutoSize = false,
            Left = 14, Top = 38, Width = 430, Height = 20, Font = new Font("Segoe UI", 8, FontStyle.Bold) };
        header.Controls.Add(title); header.Controls.Add(sub);
        var stripe = new Panel { Dock = DockStyle.Top, Height = 4, BackColor = RED };
        stripe.Paint += (s, e) =>
        {
            using (var b = new System.Drawing.Drawing2D.LinearGradientBrush(stripe.ClientRectangle, RED, ORANGE, 0f))
                e.Graphics.FillRectangle(b, stripe.ClientRectangle);
        };
        double hue = 0;
        var tm = new System.Windows.Forms.Timer { Interval = 40 };
        tm.Tick += (s, e) => { hue = (hue + 0.6) % 360; title.ForeColor = FromHsv(hue, 0.7, 1.0); };
        tm.Start();
        var donate = Btn("DONATE (CASH APP)", Color.FromArgb(0, 170, 70), 400, 160);
        var disc = Btn("JOIN DISCORD", Color.FromArgb(88, 101, 242), 570, 140);
        donate.Left = 740 - 12 - 140 - 10 - 160; disc.Left = 740 - 12 - 140;
        donate.Click += (s, e) => Process.Start("https://cash.app/$CoronaSend");
        disc.Click += (s, e) =>
        {
            string f = Path.Combine(root, "discord.txt");
            string url = File.Exists(f) ? File.ReadAllText(f).Trim() : "";
            if (url.StartsWith("http")) Process.Start(url);
            else MessageBox.Show("Discord invite not set. Put the invite link in discord.txt next to this program.");
        };
        header.Controls.Add(donate); header.Controls.Add(disc);

        var ps4row = new Panel { Dock = DockStyle.Top, Height = 40, Width = 740, BackColor = BG };
        ps4row.Controls.Add(new Label { Text = "PS4 IP:", Left = 14, Top = 12, Width = 56, ForeColor = TEXT, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        string ipFile = Path.Combine(root, "ps4_ip.txt");
        ipBox = new ComboBox { Text = File.Exists(ipFile) ? File.ReadAllText(ipFile).Trim() : "192.168.1.27", Left = 72, Top = 8, Width = 130,
                    BackColor = PANEL, ForeColor = ORANGE, FlatStyle = FlatStyle.Flat };
        sendBox = new CheckBox { Text = "Send to PS4 after building (FTP 2121)", Left = 216, Top = 10, Width = 300, ForeColor = TEXT,
            Font = new Font("Segoe UI", 9, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
        sendBtn = Btn("SEND PKG TO PS4...", RED, 740 - 12 - 180, 180);
        sendBtn.Top = 5; sendBtn.Height = 28;
        sendBtn.Click += (s, e) => PickAndSend();
        ps4row.Controls.Add(ipBox); ps4row.Controls.Add(sendBox); ps4row.Controls.Add(sendBtn);

        var toolsRow = new Panel { Dock = DockStyle.Top, Height = 40, Width = 740, BackColor = BG };
        Func<string, Color, int, int, Action, Button> TB = (text, col, left, w, act) =>
        {
            var b = Btn(text, col, left, w); b.Anchor = AnchorStyles.Top | AnchorStyles.Left; b.Top = 5; b.Height = 28;
            b.Click += (s, e) => act(); toolsRow.Controls.Add(b); return b;
        };
        TB("TEST PS4", Color.FromArgb(70, 70, 90), 12, 80, TestPs4);
        TB("PS4 SONGS", Color.FromArgb(70, 70, 90), 96, 86, Ps4Songs);
        TB("COPY LOG", Color.FromArgb(70, 70, 90), 186, 80, CopyLog);
        TB("CLEAN UP", Color.FromArgb(70, 70, 90), 270, 80, CleanAll);
        TB("HELP / REPORT BUG", Color.FromArgb(88, 101, 242), 596, 132, OpenDiscord);
<<<<<<< HEAD
=======
        TB("MP3 SEARCH", Color.FromArgb(200, 50, 50), 732, 80, Mp3Search);
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
        toolsRow.Controls.Add(new Label { Text = "Chart:", Left = 366, Top = 12, Width = 44, ForeColor = TEXT, Font = new Font("Segoe UI", 9, FontStyle.Bold) });
        densityBox = new ComboBox { Left = 410, Top = 8, Width = 86, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = PANEL, ForeColor = ORANGE, FlatStyle = FlatStyle.Flat };
        densityBox.Items.AddRange(new object[] { "Easier", "Normal", "Busier" }); densityBox.SelectedIndex = 1;
        euBox = new CheckBox { Text = "EU PS4", Left = 512, Top = 10, Width = 76, ForeColor = TEXT, Font = new Font("Segoe UI", 9, FontStyle.Bold), FlatStyle = FlatStyle.Flat };
        toolsRow.Controls.Add(densityBox); toolsRow.Controls.Add(euBox);

        var listHost = new Panel { Dock = DockStyle.Top, Height = 176, BackColor = BG, Padding = new Padding(12, 4, 12, 8) };
        list.Dock = DockStyle.Fill; listHost.Controls.Add(list); Round(list, 12);
        var logHost = new Panel { Dock = DockStyle.Fill, BackColor = BG, Padding = new Padding(12, 0, 12, 12) };
        log.Dock = DockStyle.Fill; logHost.Controls.Add(log); Round(log, 12);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = 40, BackColor = BG };
        var creditsBtn = Btn("CREDITS", Color.FromArgb(70, 70, 90), 12, 100); creditsBtn.Top = 4; creditsBtn.Height = 28;
        creditsBtn.Click += (s, e) => ShowCredits();
        footer.Controls.Add(creditsBtn);
        var songsPage = new Panel { Dock = DockStyle.Fill, BackColor = BG };
        songsPage.Controls.Add(logHost); songsPage.Controls.Add(footer); songsPage.Controls.Add(prog); songsPage.Controls.Add(listHost);
        songsPage.Controls.Add(toolsRow); songsPage.Controls.Add(ps4row); songsPage.Controls.Add(opts); songsPage.Controls.Add(drop);
        var customPage = new Panel { Dock = DockStyle.Fill, BackColor = BG, Visible = false };
        var nav = new Panel { Dock = DockStyle.Left, Width = 150, BackColor = Color.FromArgb(14, 14, 18) };
        var navSongs = Btn("SONGS", ORANGE, 10, 130); navSongs.Top = 14; navSongs.Height = 38; navSongs.Anchor = AnchorStyles.Top | AnchorStyles.Left; navSongs.ForeColor = Color.Black;
        var navCust = Btn("GAME\nCUSTOMIZATION", Color.FromArgb(70, 70, 90), 10, 130); navCust.Top = 60; navCust.Height = 46; navCust.Anchor = AnchorStyles.Top | AnchorStyles.Left;
        nav.Controls.Add(navSongs); nav.Controls.Add(navCust);
        Action<bool> showSongs = on =>
        {
            songsPage.Visible = on; customPage.Visible = !on;
            navSongs.BackColor = on ? ORANGE : Color.FromArgb(70, 70, 90); navSongs.ForeColor = on ? Color.Black : Color.White;
            navCust.BackColor = on ? Color.FromArgb(70, 70, 90) : Color.FromArgb(120, 60, 150); navCust.ForeColor = Color.White;
            navSongs.FlatAppearance.MouseOverBackColor = Shade(navSongs.BackColor, 22); navCust.FlatAppearance.MouseOverBackColor = Shade(navCust.BackColor, 22);
        };
        navSongs.Click += (s, e) => showSongs(true);
        navCust.Click += (s, e) => showSongs(false);
        Controls.Add(songsPage); Controls.Add(customPage); Controls.Add(nav); Controls.Add(stripe); Controls.Add(header);
        Style(this);
        var hwForm = new HighwayForm(root, () => ipBox.Text.Trim()) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
        var subForms = new Form[] { hwForm,
            new BackgroundForm(root, () => ipBox.Text.Trim()),
            new SkinForm(root, () => ipBox.Text.Trim()),
            new BackupForm(root, () => ipBox.Text.Trim()) };
        string[] subNames = { "HIGHWAYS", "BACKGROUNDS", "SKINS", "BACKUP" };
        var subNav = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = BG };
        var subBtns = new Button[subForms.Length];
        Action<int> showSub = idx =>
        {
            for (int i = 0; i < subForms.Length; i++)
            {
                subForms[i].Visible = i == idx;
                subBtns[i].BackColor = i == idx ? Color.FromArgb(120, 60, 150) : Color.FromArgb(70, 70, 90);
                subBtns[i].FlatAppearance.MouseOverBackColor = Shade(subBtns[i].BackColor, 22);
            }
        };
        for (int i = 0; i < subForms.Length; i++)
        {
            int idx = i;
            subBtns[i] = Btn(subNames[i], Color.FromArgb(70, 70, 90), 12 + i * 138, 130); subBtns[i].Top = 8; subBtns[i].Height = 30; subBtns[i].Anchor = AnchorStyles.Top | AnchorStyles.Left;
            subBtns[i].Click += (s, e) => showSub(idx);
            subNav.Controls.Add(subBtns[i]);
            var sf = subForms[i]; sf.TopLevel = false; sf.FormBorderStyle = FormBorderStyle.None; sf.Dock = DockStyle.Fill;
        }
        Style(subNav);
        foreach (var sf in subForms) customPage.Controls.Add(sf);
        customPage.Controls.Add(subNav);
        foreach (var sf in subForms) sf.Show();
        showSub(0);
                drop.DragEnter += (s, e) => { ((DropZone)drop).Hot = true; drop.Invalidate(); };
                drop.DragLeave += (s, e) => { ((DropZone)drop).Hot = false; drop.Invalidate(); };
                drop.DragDrop += (s, e) => { ((DropZone)drop).Hot = false; drop.Invalidate(); };
                DragEnter += OnEnter; drop.DragEnter += OnEnter;
        DragDrop += OnDrop; drop.DragDrop += OnDrop;
        Say("Drop one or more .mp3 files. Finished .pkg files go in:\r\n" + Path.Combine(root, "PS4 Packages"));
        Shown += (s, e) => { var wa = Screen.FromControl(this).WorkingArea; if (Height > wa.Height) { Height = wa.Height; Top = wa.Top; } };
        Shown += (s, e) => CheckUpdate();
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        LoadSettings();
        FormClosing += (s, e) => SaveSettings();
        var cmd = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (cmd.Length > 0)
        {
            if (cmd.Contains("--bundle")) bundle.Checked = true;
            if (cmd.Contains("--send")) sendBox.Checked = true;
            var mp3s = cmd.Where(a => !a.StartsWith("--")).ToArray();
            Shown += (s, e) => Start(mp3s);
        }
    }

    static Color FromHsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = v - c, r = 0, g = 0, b = 0;
        if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
        else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
        return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
    }

    string PyOut(string args)
    {
        var psi = new ProcessStartInfo(PyExe(), PyPre() + "-X utf8 web_search.py " + args)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
            return o;
        }
    }

    void WebSearch()
    {
        if (!drop.Enabled) return;
        var dlg = new Form { Text = "Web search - free / Creative Commons music (Internet Archive, Jamendo)",
            ClientSize = new Size(760, 440), StartPosition = FormStartPosition.CenterParent, BackColor = BG, ForeColor = TEXT };
        var q = new TextBox { Left = 12, Top = 12, Width = 560, BackColor = PANEL, ForeColor = ORANGE, BorderStyle = BorderStyle.FixedSingle };
        var go = new Button { Text = "SEARCH", Left = 582, Top = 10, Width = 166, Height = 26, FlatStyle = FlatStyle.Flat, BackColor = ORANGE, ForeColor = Color.Black };
        var res = new ListView { Left = 12, Top = 48, Width = 736, Height = 330, View = View.Details, FullRowSelect = true,
            MultiSelect = true, BackColor = PANEL, ForeColor = TEXT, BorderStyle = BorderStyle.None };
        res.Columns.Add("SOURCE", 80); res.Columns.Add("ARTIST", 200); res.Columns.Add("TITLE", 280); res.Columns.Add("LICENSE", 170);
        var info = new Label { Left = 12, Top = 388, Width = 560, Height = 40, Text = "Only free-to-download music is listed. Jamendo needs a free client id in jamendo_key.txt." };
        var add = new Button { Text = "DOWNLOAD + CONVERT", Left = 582, Top = 390, Width = 166, Height = 32, FlatStyle = FlatStyle.Flat, BackColor = RED, ForeColor = Color.White };
        dlg.Controls.AddRange(new Control[] { q, go, res, info, add });
        dlg.AcceptButton = go;
        go.Click += (s, e) =>
        {
            go.Enabled = false; info.Text = "Searching...";
            string term = q.Text.Replace("\"", "");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string o = PyOut("search \"" + term + "\"");
                dlg.BeginInvoke((MethodInvoker)delegate
                {
                    res.Items.Clear();
                    foreach (var line in o.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        var f = line.TrimEnd('\r').Split('\t');
                        if (f.Length < 5) continue;
                        var it = new ListViewItem(new[] { f[0], f[2], f[3], f[4] }) { Tag = f[1] };
                        res.Items.Add(it);
                    }
                    info.Text = res.Items.Count + " result(s).";
                    go.Enabled = true;
                });
            });
        };
        add.Click += (s, e) =>
        {
            var picks = res.SelectedItems.Cast<ListViewItem>().ToList();
            if (picks.Count == 0) { info.Text = "Select one or more results first."; return; }
            add.Enabled = false; go.Enabled = false; info.Text = "Downloading...";
            string outDir = Path.Combine(root, "Downloads");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var got = new List<string>();
                foreach (var it in picks)
                {
                    string o = PyOut("get " + it.SubItems[0].Text + " \"" + it.Tag + "\" \"" + outDir + "\" \"" +
                        it.SubItems[1].Text.Replace("\"", "") + "\" \"" + it.SubItems[2].Text.Replace("\"", "") + "\"").Trim();
                    string path = o.Split('\n').Last().Trim();
                    if (path.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) got.Add(path);
                }
                dlg.BeginInvoke((MethodInvoker)delegate
                {
                    dlg.Close();
                    if (got.Count > 0) Start(got.ToArray()); else Say("Download failed.");
                });
            });
        };
        Style(dlg);
        dlg.ShowDialog(this);
    }

<<<<<<< HEAD
=======
    string FindFfmpeg()
    {
        try
        {
            foreach (var f in Directory.GetFiles(Path.Combine(root, "runtime"), "ffmpeg*.exe", SearchOption.AllDirectories))
                return f;
        }
        catch { }
        return null;
    }

    string PrepareFfmpegDir()
    {
        try
        {
            string src = FindFfmpeg();
            if (src == null) return null;
            string dir = Path.Combine(root, "runtime", "ffmpeg_bin");
            Directory.CreateDirectory(dir);
            string dst = Path.Combine(dir, "ffmpeg.exe");
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length)
                File.Copy(src, dst, true);
            return dir;
        }
        catch { return null; }
    }

    // One pending MP3 download for the search dialog.
    class DlJob { public int Row; public string Id; public string Title; }

    // Runs yt-dlp and returns everything it printed to stdout.
    // Both streams are drained on their own threads: the old code read stdout
    // fully and then stderr, which deadlocks whenever yt-dlp writes more than
    // a pipe buffer of progress output (the app would sit on "WAIT..." forever).
    string RunYtdlp(string args, Action<string> onStderrLine, out int exitCode)
    {
        string ytdlp = Path.Combine(root, "yt-dlp.exe");
        var psi = new ProcessStartInfo(ytdlp, args)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardOutput = true, RedirectStandardError = true,
          StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        var stdout = new StringBuilder();
        using (var p = Process.Start(psi))
        {
            var tOut = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = p.StandardOutput.ReadLine()) != null)
                        lock (stdout) stdout.AppendLine(line);
                }
                catch { } // stream closed
            }) { IsBackground = true };
            var tErr = new Thread(() =>
            {
                try
                {
                    string line;
                    while ((line = p.StandardError.ReadLine()) != null)
                        if (onStderrLine != null) onStderrLine(line);
                }
                catch { } // stream closed / callback failed
            }) { IsBackground = true };
            tOut.Start(); tErr.Start();
            p.WaitForExit();
            tOut.Join(3000); tErr.Join(3000);
            exitCode = p.ExitCode;
        }
        lock (stdout) return stdout.ToString();
    }

    // Downloads a YouTube video as an MP3 into the Downloads folder.
    // yt-dlp prints the final file path itself (--print after_move:filepath),
    // so we get the exact file instead of guessing "newest mp3 in the folder",
    // which mixed up songs when several downloads ran close together.
    // onPct gets 0-100 while downloading, or -1 while converting to mp3.
    string DownloadMp3(string videoId, Action<int> onPct, out string err)
    {
        err = null;
        string outDir = Path.Combine(root, "Downloads");
        Directory.CreateDirectory(outDir);
        string ffd = PrepareFfmpegDir();
        string url = "https://www.youtube.com/watch?v=" + videoId;
        string args = "\"" + url + "\" --extract-audio --audio-format mp3 --no-playlist --no-warnings " +
            "--retries 3 --fragment-retries 3 --force-overwrites --no-part " +
            "--print \"after_move:filepath\" " +
            (ffd != null ? "--ffmpeg-location \"" + ffd + "\" " : "") +
            "-o \"" + Path.Combine(outDir, "%(title)s.%(ext)s") + "\"";
        int code;
        string stdout = RunYtdlp(args, line =>
        {
            if (onPct == null) return;
            if (line.StartsWith("[ExtractAudio]")) { onPct(-1); return; }
            var m = Regex.Match(line, @"^\[download\]\s+(\d+(?:\.\d+)?)%");
            if (!m.Success) return;
            double d;
            if (double.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out d))
                onPct((int)d);
        }, out code);
        string path = null;
        foreach (var l in stdout.Split('\n'))
        {
            var t = l.Trim();
            if (t.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(t)) path = t;
        }
        if (path == null && code == 0)
        {
            // fallback for yt-dlp builds without after_move support: newest mp3
            var mp3s = Directory.GetFiles(outDir, "*.mp3").OrderByDescending(File.GetLastWriteTime).ToArray();
            if (mp3s.Length > 0 && File.GetLastWriteTime(mp3s[0]) > DateTime.Now.AddMinutes(-2)) path = mp3s[0];
        }
        if (path == null)
            err = "Download failed" + (code != 0 ? " (yt-dlp exit code " + code + ")" : "") +
                  ". Check your internet connection and retry.";
        return path;
    }

    void Mp3Search()
    {
        using (var dlg = new Form { Text = "MP3 Song Search (YouTube via yt-dlp)", Width = 780, Height = 500,
            StartPosition = FormStartPosition.CenterParent, BackColor = BG, ForeColor = TEXT })
        {
            var info = new Label { Left = 12, Top = 12, Width = 750, Height = 30, ForeColor = DIM,
                Text = "Search YouTube for songs by title / artist. Click DOWNLOAD on a row, or select several rows (Ctrl+click / Shift+click) and use DOWNLOAD SELECTED - they queue up and fetch one after another." };
            var q = new TextBox { Left = 12, Top = 48, Width = 560, BackColor = PANEL, ForeColor = ORANGE, BorderStyle = BorderStyle.FixedSingle };
            var searchBtn = new Button { Text = "SEARCH", Left = 580, Top = 46, Width = 90, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = ORANGE, ForeColor = Color.Black };
            var status = new Label { Left = 12, Top = 82, Width = 750, Height = 20, ForeColor = DIM, Text = "Idle." };
            var grid = new DataGridView
            {
                Left = 12, Top = 108, Width = 750, Height = 320, BackColor = PANEL, ForeColor = TEXT,
                BorderStyle = BorderStyle.None, RowHeadersVisible = false, AllowUserToAddRows = false,
                AllowUserToDeleteRows = false, ReadOnly = true, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                MultiSelect = true, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                BackgroundColor = PANEL, GridColor = Color.FromArgb(52, 54, 68),
                Font = new Font("Segoe UI", 10), RowTemplate = { Height = 28 },
                ColumnHeadersDefaultCellStyle = { Font = new Font("Segoe UI", 10, FontStyle.Bold), BackColor = Color.FromArgb(44, 20, 24), ForeColor = ORANGE },
                DefaultCellStyle = { ForeColor = TEXT, BackColor = PANEL, SelectionBackColor = Color.FromArgb(60, 30, 36), SelectionForeColor = Color.White }
            };
            grid.Columns.Add("Title", "TITLE");
            grid.Columns.Add("Uploader", "ARTIST / UPLOADER");
            grid.Columns.Add("Duration", "DURATION");
            var dlCol = new DataGridViewButtonColumn { Name = "Download", HeaderText = "", Text = "DOWNLOAD", UseColumnTextForButtonValue = true, Width = 100 };
            grid.Columns.Add(dlCol);
            grid.Columns.Add("Id", "ID");
            grid.Columns["Id"].Visible = false;
            grid.Columns["Title"].FillWeight = 46;
            grid.Columns["Uploader"].FillWeight = 30;
            grid.Columns["Duration"].FillWeight = 14;
            grid.Columns["Download"].FillWeight = 10;
            var downloadedFiles = new List<string>();
            object dlLock = new object();
            var dlQueue = new Queue<DlJob>();
            bool dlRunning = false;
            var buildBtn = new Button { Text = "BUILD DOWNLOADED (0)", Left = 424, Top = 440, Width = 224, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = RED, ForeColor = Color.White, Enabled = false };
            var dlSelBtn = new Button { Text = "DOWNLOAD SELECTED", Left = 214, Top = 440, Width = 200, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(200, 50, 50), ForeColor = Color.White };
            var folderBtn = new Button { Text = "OPEN FOLDER", Left = 12, Top = 440, Width = 190, Height = 28, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 62, 76), ForeColor = Color.White };
            var close = new Button { Text = "CLOSE", Left = 660, Top = 440, Width = 100, Height = 28, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            dlg.Controls.AddRange(new Control[] { info, q, searchBtn, status, grid, folderBtn, dlSelBtn, buildBtn, close });
            dlg.CancelButton = close;
            dlg.FormClosed += (s, e) => { lock (dlLock) dlQueue.Clear(); };
            Style(dlg);

            Action<string> RunSearch = null;
            RunSearch = (query) =>
            {
                query = (query ?? "").Trim();
                if (query.Length == 0) { status.Text = "Enter a search query first."; return; }
                searchBtn.Enabled = false; status.Text = "Searching YouTube..."; grid.Rows.Clear();
                string qcapture = query.Replace("\"", "");
                new Thread(() =>
                {
                    var rows = new List<string[]>();
                    string err = null;
                    try
                    {
                        int code;
                        string o = RunYtdlp(
                            "\"ytsearch25:" + qcapture + "\" --flat-playlist --skip-download --no-warnings --print \"%(id)s\t%(title)s\t%(uploader)s\t%(duration_string)s\"",
                            null, out code);
                        foreach (var line in o.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var f = line.TrimEnd('\r').Split('\t');
                            if (f.Length < 4) continue;
                            rows.Add(new[] { f[0], f[1], f[2], f[3] });
                        }
                        if (rows.Count == 0)
                            err = "No results found" + (code != 0 ? " (yt-dlp exit code " + code + ")" : "") + ".";
                    }
                    catch (Exception ex) { err = ex.Message; }
                    dlg.BeginInvoke((MethodInvoker)delegate
                    {
                        searchBtn.Enabled = true;
                        if (err != null) { status.Text = err; return; }
                        foreach (var r in rows)
                            grid.Rows.Add(r[1], r[2], r[3], "DOWNLOAD", r[0]);
                        status.Text = rows.Count + " result(s). Click DOWNLOAD on a row, or select several and use DOWNLOAD SELECTED.";
                    });
                }) { IsBackground = true }.Start();
            };

            searchBtn.Click += (s, e) => RunSearch(q.Text);
            q.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { searchBtn.PerformClick(); e.SuppressKeyPress = true; } };

            // --- Download queue -------------------------------------------------
            // Downloads run strictly one at a time. Before, every DOWNLOAD click
            // spawned its own yt-dlp process and then guessed which mp3 was the
            // newest in the folder, so grabbing several songs quickly mixed them
            // up or reported failures. Now jobs queue up and each result is the
            // exact file yt-dlp reports back (see DownloadMp3).
            Action EnsureWorker = () =>
            {
                lock (dlLock)
                {
                    if (dlRunning) return;
                    dlRunning = true;
                }
                new Thread(() =>
                {
                    while (true)
                    {
                        DlJob job = null;
                        lock (dlLock)
                        {
                            if (dlQueue.Count > 0) job = dlQueue.Dequeue();
                            else dlRunning = false;
                        }
                        if (job == null) break;
                        string got = null, err = null;
                        try
                        {
                            got = DownloadMp3(job.Id, pct =>
                            {
                                try
                                {
                                    dlg.BeginInvoke((MethodInvoker)delegate
                                    {
                                        if (job.Row >= grid.Rows.Count) return;
                                        // ignore if a new search replaced the rows meanwhile
                                        if (System.Convert.ToString(grid.Rows[job.Row].Cells["Id"].Value) != job.Id) return;
                                        string st = System.Convert.ToString(grid.Rows[job.Row].Cells["Download"].Value);
                                        if (st == "QUEUED" || st.EndsWith("%") || st == "CONV...")
                                            grid.Rows[job.Row].Cells["Download"].Value =
                                                pct < 0 ? "CONV..." : (pct >= 100 ? "SAVING" : pct + "%");
                                    });
                                }
                                catch { } // dialog closed mid-download
                            }, out err);
                        }
                        catch (Exception ex) { err = ex.Message; }
                        string fileGot = got, fileErr = err;
                        try
                        {
                            dlg.BeginInvoke((MethodInvoker)delegate
                            {
                                bool same = job.Row < grid.Rows.Count &&
                                    System.Convert.ToString(grid.Rows[job.Row].Cells["Id"].Value) == job.Id;
                                if (same)
                                    grid.Rows[job.Row].Cells["Download"].Value = fileGot != null ? "ADDED" : "RETRY";
                                if (fileGot != null)
                                {
                                    if (!downloadedFiles.Contains(fileGot)) downloadedFiles.Add(fileGot);
                                    buildBtn.Enabled = true;
                                    buildBtn.Text = "BUILD DOWNLOADED (" + downloadedFiles.Count + ")";
                                    status.Text = "Downloaded: " + Path.GetFileName(fileGot);
                                }
                                else
                                {
                                    status.Text = "Failed: " + job.Title + " - " + fileErr + " (click the row's RETRY button)";
                                }
                            });
                        }
                        catch { } // dialog closed mid-download
                    }
                    try
                    {
                        dlg.BeginInvoke((MethodInvoker)delegate
                        {
                            int n; lock (dlLock) n = dlQueue.Count;
                            if (n == 0)
                                status.Text = downloadedFiles.Count + " song(s) ready - click BUILD DOWNLOADED to convert.";
                        });
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            };

            Func<int, bool> QueueRow = rowIndex =>
            {
                if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return false;
                string id = System.Convert.ToString(grid.Rows[rowIndex].Cells["Id"].Value);
                string title = System.Convert.ToString(grid.Rows[rowIndex].Cells["Title"].Value);
                if (string.IsNullOrWhiteSpace(id)) return false;
                string st = System.Convert.ToString(grid.Rows[rowIndex].Cells["Download"].Value);
                if (st != "DOWNLOAD" && st != "RETRY") return false; // already queued/running/done
                grid.Rows[rowIndex].Cells["Download"].Value = "QUEUED";
                lock (dlLock) dlQueue.Enqueue(new DlJob { Row = rowIndex, Id = id, Title = title });
                EnsureWorker();
                return true;
            };

            grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0) return;
                if (grid.Columns[e.ColumnIndex].Name != "Download") return;
                if (QueueRow(e.RowIndex))
                    status.Text = "Queued: " + System.Convert.ToString(grid.Rows[e.RowIndex].Cells["Title"].Value);
            };
            grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) QueueRow(e.RowIndex); };

            dlSelBtn.Click += (s, e) =>
            {
                var idx = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).OrderBy(i => i).ToList();
                if (idx.Count == 0)
                {
                    status.Text = "Select one or more rows first (Ctrl+click or Shift+click), then press DOWNLOAD SELECTED.";
                    return;
                }
                int n = idx.Count(i => QueueRow(i));
                status.Text = n > 0
                    ? n + " song(s) queued - downloads run one after another."
                    : "Those rows are already queued or downloaded.";
            };

            folderBtn.Click += (s, e) =>
            {
                try
                {
                    string outDir = Path.Combine(root, "Downloads");
                    Directory.CreateDirectory(outDir);
                    Process.Start(new ProcessStartInfo(outDir) { UseShellExecute = true });
                }
                catch (Exception ex) { status.Text = "Could not open the Downloads folder: " + ex.Message; }
            };

            buildBtn.Click += (s, e) =>
            {
                if (downloadedFiles.Count == 0) return;
                var files = downloadedFiles.ToArray();
                dlg.Close();
                Start(files);
            };

            dlg.ShowDialog(this);
        }
    }

>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
    void Browse()
    {
        if (!drop.Enabled) return;
        string dl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        using (var dlg = new OpenFileDialog { Filter = "MP3 files (*.mp3)|*.mp3", Multiselect = true,
            Title = "Choose MP3 file(s) to convert", InitialDirectory = Directory.Exists(dl) ? dl : "" })
        {
            if (dlg.ShowDialog(this) == DialogResult.OK) Start(dlg.FileNames);
        }
    }

    void OnEnter(object s, DragEventArgs e)
    { e.Effect = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; }

    void OnDrop(object s, DragEventArgs e)
    {
        Start(((string[])e.Data.GetData(DataFormats.FileDrop)));
    }

<<<<<<< HEAD
    const string VERSION = "1.1.3";
=======
    const string VERSION = "1.1.4";
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)

    StringBuilder tail = new StringBuilder();
    double density = 1.0; bool eu;

    string Friendly(string extra, string fallback)
    {
        string t; lock (tail) t = tail.ToString() + "\n" + extra;
        if (Regex.IsMatch(t, "Unable to connect|actively refused|No connection could be made|timed out|remote name could not")) return "Can't reach the PS4 at " + ip + ":2121. Check it is on, GoldHEN is loaded and the IP is right (use TEST PS4).";
        if (t.Contains("No module named")) return "A Python library is missing. Keep the 'runtime' folder next to the tool (re-extract the whole tool folder).";
        if (t.Contains("simultaneous")) return "The auto-chart put too many drum hits at once. Set Chart to 'Easier' and try again.";
        if (Regex.IsMatch(t, "MIDI Compiler[^\n]*(ERROR|Error)")) return "Rock Band's chart validator rejected the generated chart. Try Chart = 'Easier', or report it with COPY LOG.";
        if (Regex.IsMatch(t, "NoBackendError|Error decoding|audioread|Invalid data found")) return "The MP3 could not be read. It may be corrupt - try another copy.";
        if (Regex.IsMatch(t, "cannot find the file|system cannot find")) return "A bundled tool is missing. Re-extract the whole tool folder (onyx, forge_official and runtime must stay next to the tool).";
        if (Regex.IsMatch(t, "being used by another process|Access to the path")) return "A file is locked or access was denied. Close anything using the song files and retry.";
        return fallback;
    }

    void ShowCredits()
    {
<<<<<<< HEAD
        MessageBox.Show(
            "ULTIMATE RB4DX TOOL v" + VERSION + " - made by CoronaSend\r\n\r\n" +
            "Built on the work of:\r\n" +
            "  - Onyx - Michael Tolly (mtolly) - GPL v3\r\n" +
            "  - LibForge / ForgeTool - Maxton; fork by LlysiX and mtolly - LGPL/GPL\r\n" +
            "  - RB4DX and the Rock Band modding community\r\n" +
            "  - librosa, numpy, FFmpeg and other open-source libraries\r\n" +
            "  - iTunes, Internet Archive and Jamendo (cover art / free music search)\r\n\r\n" +
            "Full licenses: THIRD-PARTY-NOTICES.md and README.md",
            "Credits", MessageBoxButtons.OK, MessageBoxIcon.Information);
=======
        using (var dlg = new Form
        {
            Text = "Credits & Acknowledgements",
            Width = 640,
            Height = 520,
            StartPosition = FormStartPosition.CenterParent,
            BackColor = BG,
            ForeColor = TEXT,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false
        })
        {
            var top = new Label
            {
                Left = 16,
                Top = 14,
                Width = 600,
                Height = 46,
                ForeColor = ORANGE,
                Font = new Font("Segoe UI", 12, FontStyle.Bold),
                Text = "ULTIMATE RB4DX TOOL v" + VERSION + "\r\nMade by CoronaSend"
            };
            var sub = new Label
            {
                Left = 16,
                Top = 62,
                Width = 600,
                Height = 18,
                ForeColor = DIM,
                Text = "This tool would not be possible without the following projects and people:"
            };
            var box = new TextBox
            {
                Left = 16,
                Top = 86,
                Width = 596,
                Height = 344,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                BackColor = PANEL,
                ForeColor = TEXT,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 9),
                Text =
                    "CORE TOOLS\r\n" +
                    "  • Onyx Music Game Toolkit - Michael Tolly (mtolly) [GPL v3] - https://github.com/mtolly/onyx\r\n" +
                    "  • LibForge / ForgeTool - Maxton; fork maintained by LlysiX and mtolly [LGPL/GPL] - https://github.com/mtolly/LibForge\r\n" +
                    "  • highway_lib (LibForge / DtxCS / MidiCS / GameArchives) used by Highway Maker [LGPL/GPL]\r\n\r\n" +
                    "AUDIO / CHARTING\r\n" +
                    "  • librosa - Brian McFee et al. [ISC] - https://librosa.org\r\n" +
                    "  • NumPy - NumPy Developers [BSD] - https://numpy.org\r\n" +
                    "  • SciPy - SciPy Developers [BSD] - https://scipy.org\r\n" +
                    "  • scikit-learn / numba / llvmlite [BSD/Apache-2.0]\r\n" +
                    "  • soundfile / PySoundFile, soxr, mido [BSD/MIT]\r\n" +
                    "  • FFmpeg - FFmpeg team [LGPL/GPL] - https://ffmpeg.org\r\n" +
                    "  • imageio-ffmpeg [BSD]\r\n\r\n" +
                    "UTILITIES\r\n" +
                    "  • Python 3 - Python Software Foundation [PSF] - https://www.python.org\r\n" +
                    "  • yt-dlp - yt-dlp contributors [Unlicense/Public Domain] - https://github.com/yt-dlp/yt-dlp\r\n" +
                    "  • Pillow (PIL Fork) - Jeffrey A. Clark et al. [HPND] - https://python-pillow.org\r\n" +
                    "  • Requests - Kenneth Reitz et al. [Apache-2.0] - https://requests.readthedocs.io\r\n\r\n" +
                    "ART / DATA SOURCES\r\n" +
                    "  • iTunes Search API - Apple Inc. (cover art & song lookup)\r\n" +
                    "  • Internet Archive - archive.org (free/CC music search)\r\n" +
                    "  • Jamendo - jamendo.com (free/CC music search)\r\n\r\n" +
                    "COMMUNITY\r\n" +
                    "  • RB4DX team & the entire Rock Band modding community\r\n" +
                    "  • Everyone who tested, reported bugs and shared feedback\r\n\r\n" +
                    "LEGAL\r\n" +
                    "  Rock Band, Rock Band 4 and RB4DX are trademarks of their respective owners.\r\n" +
                    "  This project is not affiliated with or endorsed by Harmonix, Sony or the RB4DX team.\r\n" +
                    "  See THIRD-PARTY-NOTICES.md for full license details."
            };
            var close = new Button
            {
                Text = "CLOSE",
                Left = 512,
                Top = 442,
                Width = 100,
                Height = 28,
                FlatStyle = FlatStyle.Flat,
                BackColor = RED,
                ForeColor = Color.White,
                DialogResult = DialogResult.OK
            };
            dlg.Controls.AddRange(new Control[] { top, sub, box, close });
            dlg.AcceptButton = close;
            dlg.CancelButton = close;
            Style(dlg);
            dlg.ShowDialog(this);
        }
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
    }

    void OpenDiscord()
    {
        string f = Path.Combine(root, "discord.txt");
        string url = File.Exists(f) ? File.ReadAllText(f).Trim() : "";
        if (url.StartsWith("http")) { Say("Opening Discord - tip: press COPY LOG first and paste it with your bug report."); Process.Start(url); }
        else MessageBox.Show("Discord invite not set. Put the invite link in discord.txt next to this program.");
    }

    List<string> ExpandMp3Core(IEnumerable<string> paths)
    {
        var res = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) { try { res.AddRange(Directory.EnumerateFiles(p, "*.mp3", SearchOption.AllDirectories).OrderBy(x => x)); } catch { } }
            else if (p.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) && File.Exists(p)) res.Add(p);
        }
        return res.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    string[] ExpandMp3(string[] paths)
    {
        var all = ExpandMp3Core(paths);
        if (all.Count > 40) { Say("Found " + all.Count + " MP3s - only the first 40 are used per run."); all = all.Take(40).ToList(); }
        return all.ToArray();
    }

    void CheckUpdate()
    {
        new Thread(() =>
        {
            try
            {
                ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072 | SecurityProtocolType.Tls;
                var wc = new WebClient(); wc.Headers["User-Agent"] = "ULTIMATE-RB4DX-TOOL";
                string json = wc.DownloadString("https://api.github.com/repos/CoronaSend/ULTIMATE-RB4DX-TOOL/releases/latest");
                var m = System.Text.RegularExpressions.Regex.Match(json, "\"tag_name\"\\s*:\\s*\"v?([0-9.]+)\"");
                var u = System.Text.RegularExpressions.Regex.Match(json, "\"html_url\"\\s*:\\s*\"(https://github.com/[^\"]+/releases/tag/[^\"]+)\"");
                Version a, b;
                if (!m.Success || !Version.TryParse(m.Groups[1].Value, out a) || !Version.TryParse(VERSION, out b) || a <= b) return;
                string page = u.Success ? u.Groups[1].Value : "https://github.com/CoronaSend/ULTIMATE-RB4DX-TOOL/releases/latest";
                Say("A newer version (" + a + ") is available: " + page);
                BeginInvoke((MethodInvoker)delegate
                {
                    if (MessageBox.Show(this, "Version " + a + " is available (you have " + VERSION + ").\r\n\r\nOpen the download page?", "Update available",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes) Process.Start(page);
                });
            }
            catch { }
        }) { IsBackground = true }.Start();
    }

    void CopyLog()
    {
        try { Clipboard.SetText("ULTIMATE RB4DX TOOL v" + VERSION + " log\r\nPython: " + PyExe() + "\r\nPS4: " + ipBox.Text + "\r\n\r\n" + log.Text); Say("Log copied to the clipboard - paste it in Discord."); }
        catch (Exception ex) { Say("Could not copy: " + ex.Message); }
    }

    string SettingsPath { get { return Path.Combine(root, "settings.ini"); } }

    void LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return;
            foreach (var line in File.ReadAllLines(SettingsPath))
            {
                int k = line.IndexOf('='); if (k < 1) continue;
                string key = line.Substring(0, k).Trim(), v = line.Substring(k + 1).Trim();
                if (key == "ip" && v.Length > 0) ipBox.Text = v;
                else if (key == "ips") { foreach (var p in v.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)) if (!ipBox.Items.Contains(p)) ipBox.Items.Add(p); }
                else if (key == "send") sendBox.Checked = v == "1";
                else if (key == "bundle") bundle.Checked = v == "1";
                else if (key == "pack" && v.Length > 0) packName.Text = v;
                else if (key == "eu") euBox.Checked = v == "1";
                else if (key == "density") { int d; if (int.TryParse(v, out d) && d >= 0 && d < densityBox.Items.Count) densityBox.SelectedIndex = d; }
            }
        }
        catch { }
    }

    void SaveSettings()
    {
        try
        {
            string cur = ipBox.Text.Trim(); if (cur.Length > 0 && !ipBox.Items.Contains(cur)) ipBox.Items.Add(cur);
            var ipl = new List<string>(); foreach (var o in ipBox.Items) ipl.Add(o.ToString());
            File.WriteAllLines(SettingsPath, new[] { "ip=" + cur, "ips=" + string.Join(",", ipl.ToArray()), "send=" + (sendBox.Checked ? "1" : "0"),
                "bundle=" + (bundle.Checked ? "1" : "0"), "pack=" + packName.Text.Trim(), "density=" + densityBox.SelectedIndex,
                "eu=" + (euBox.Checked ? "1" : "0") });
        }
        catch { }
    }

    bool Ps4Open(string host)
    {
        try
        {
            using (var c = new System.Net.Sockets.TcpClient())
            {
                var r = c.BeginConnect(host, 2121, null, null);
                if (!r.AsyncWaitHandle.WaitOne(2500)) return false;
                c.EndConnect(r); return true;
            }
        }
        catch { return false; }
    }

    FtpWebRequest Ftp(string host, string path, string method)
    {
        var req = (FtpWebRequest)WebRequest.Create("ftp://" + host + ":2121" + path);
        req.Method = method; req.Timeout = 8000; req.KeepAlive = false;
        req.Credentials = new NetworkCredential("anonymous", "anonymous@");
        return req;
    }

    List<string> FtpNames(string host, string path)
    {
        var names = new List<string>();
        using (var resp = Ftp(host, path, WebRequestMethods.Ftp.ListDirectory).GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream()))
        { string l; while ((l = sr.ReadLine()) != null) { l = l.Trim(); if (l.Length > 0) names.Add(Path.GetFileName(l.TrimEnd('/'))); } }
        return names;
    }

    // level: 0 ok, 1 warning, 2 PS4 unreachable
    string CheckPs4(string host, out int level)
    {
        level = 0;
        if (!Ps4Open(host)) { level = 2; return "Can't reach the PS4 at " + host + ":2121. Check it is on, GoldHEN is loaded (FTP uses port 2121) and the IP is right."; }
        try
        {
            var apps = FtpNames(host, "/user/app/");
            if (eu) return "PS4 reachable (EU mode - game version can't be verified).";
            if (!apps.Any(a => a.StartsWith("CUSA02084")))
            {
                level = 1;
                return "PS4 reachable, but Rock Band 4 (CUSA02084, US version) was not found in /user/app. If you have the EU version, tick 'EU PS4'.";
            }
            return "PS4 reachable and Rock Band 4 (CUSA02084) is installed.";
        }
        catch (Exception ex) { level = 1; return "PS4 reachable, but the game could not be verified (" + ex.Message + ")."; }
    }

    void TestPs4()
    {
        string host = ipBox.Text.Trim(); eu = euBox.Checked; Say("Testing PS4 " + host + " ...");
        new Thread(() => { int lv; string m = CheckPs4(host, out lv); Say((lv == 0 ? "PS4 OK: " : lv == 1 ? "PS4 WARNING: " : "PS4 PROBLEM: ") + m); }) { IsBackground = true }.Start();
    }

    class PkgItem { public string Name, Text; public override string ToString() { return Text; } }

    void Ps4Songs()
    {
        string host = ipBox.Text.Trim();
        using (var dlg = new Form { Text = "Packages on the PS4 (/data/pkg)", Width = 640, Height = 500, StartPosition = FormStartPosition.CenterParent,
            BackColor = BG, ForeColor = TEXT, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false })
        {
            var lb = new ListBox { Left = 12, Top = 12, Width = 600, Height = 340, SelectionMode = SelectionMode.MultiExtended, BackColor = PANEL, ForeColor = TEXT, Font = new Font("Consolas", 9) };
            var info = new Label { Left = 12, Top = 358, Width = 600, Height = 48, ForeColor = DIM, Text = "Loading..." };
            var refresh = new Button { Text = "REFRESH", Left = 12, Top = 414, Width = 100, Height = 30, FlatStyle = FlatStyle.Flat };
            var del = new Button { Text = "DELETE SELECTED", Left = 120, Top = 414, Width = 160, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = RED, ForeColor = Color.White };
            var close = new Button { Text = "CLOSE", Left = 512, Top = 414, Width = 100, Height = 30, FlatStyle = FlatStyle.Flat, DialogResult = DialogResult.Cancel };
            Action load = () =>
            {
                info.Text = "Loading..."; lb.Items.Clear();
                new Thread(() =>
                {
                    var items = new List<PkgItem>(); string err = null;
                    try
                    {
                        foreach (var n in FtpNames(host, "/data/pkg/").Where(n => n.EndsWith(".pkg", StringComparison.OrdinalIgnoreCase)))
                        {
                            string sz = "";
                            try { using (var r = Ftp(host, "/data/pkg/" + n, WebRequestMethods.Ftp.GetFileSize).GetResponse()) sz = "   (" + (r.ContentLength / 1048576.0).ToString("0.0") + " MB)"; } catch { }
                            items.Add(new PkgItem { Name = n, Text = n + sz });
                        }
                    }
                    catch (Exception ex) { err = ex.Message; }
                    try
                    {
                        dlg.BeginInvoke((MethodInvoker)delegate
                        {
                            foreach (var i in items) lb.Items.Add(i);
                            info.Text = err != null ? "Could not list the PS4: " + err + "\r\nCheck the PS4 is on and the IP is right."
                                : items.Count + " package(s). Deleting removes only the .pkg install file from /data/pkg; songs already installed stay in the game until you remove them on the PS4.";
                        });
                    }
                    catch { }
                }) { IsBackground = true }.Start();
            };
            refresh.Click += (s, e) => load();
            del.Click += (s, e) =>
            {
                var sel = lb.SelectedItems.Cast<PkgItem>().ToList();
                if (sel.Count == 0) return;
                if (MessageBox.Show("Delete " + sel.Count + " file(s) from the PS4's /data/pkg?", "Delete", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                var failed = new List<string>();
                foreach (var it in sel)
                {
                    try { Ftp(host, "/data/pkg/" + it.Name, WebRequestMethods.Ftp.DeleteFile).GetResponse().Close(); Say("Deleted from PS4: " + it.Name); }
                    catch (Exception ex) { failed.Add(it.Name + ": " + ex.Message); }
                }
                if (failed.Count > 0) MessageBox.Show("Some deletes failed:\r\n" + string.Join("\r\n", failed.ToArray()));
                load();
            };
            dlg.Controls.AddRange(new Control[] { lb, info, refresh, del, close }); dlg.CancelButton = close;
            foreach (var b in new[] { refresh, close }) b.ForeColor = TEXT;
            dlg.Shown += (s, e) => load();
            Style(dlg);
            dlg.ShowDialog(this);
        }
    }

    // removes per-song scratch folders (kept: .con files, so bundles can still be made)
    void CleanWork()
    {
        try
        {
            string b = Path.Combine(root, "build"); if (!Directory.Exists(b)) return;
            foreach (var d in Directory.GetDirectories(b))
                foreach (var sub in new[] { "onyx_work", "onyx_src" })
                { string q = Path.Combine(d, sub); if (Directory.Exists(q)) Directory.Delete(q, true); }
        }
        catch { }
    }

    void CleanAll()
    {
        if (!drop.Enabled) return;
        string b = Path.Combine(root, "build");
        if (!Directory.Exists(b)) { Say("Nothing to clean."); return; }
        long bytes = 0; try { bytes = new DirectoryInfo(b).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); } catch { }
        string mb = (bytes / 1048576.0).ToString("0.0");
        if (MessageBox.Show("Delete all temporary build files (" + mb + " MB)?\r\nFinished .pkg files in 'PS4 Packages' are NOT touched.", "Clean up", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { Directory.Delete(b, true); Say("Build folder cleaned (" + mb + " MB freed)."); }
        catch (Exception ex) { Say("Clean failed: " + ex.Message); }
    }

    string CoverArg(string mp3)
    {
        string c; return covers.TryGetValue(mp3, out c) && File.Exists(c) ? " --cover \"" + c + "\"" : "";
    }

    void CancelRun()
    {
        cancelled = true; Say("Cancelling...");
        try
        {
            var p = cur;
            if (p != null && !p.HasExited)
                Process.Start(new ProcessStartInfo("taskkill", "/PID " + p.Id + " /T /F") { CreateNoWindow = true, UseShellExecute = false });
        }
        catch { }
    }

    string PyExe()
    {
        string r = Path.Combine(root, "runtime", "python.exe");
        return File.Exists(r) ? r : "py";
    }
    string PyPre() { return File.Exists(Path.Combine(root, "runtime", "python.exe")) ? "" : "-3.14 "; }

    void GetNames(string f, out string artist, out string title)
    {
        SongInfo v;
        if (infos.TryGetValue(f, out v)) { artist = v.Artist; title = v.Title; }
        else Names(f, out artist, out title);
    }

    class SongInfo { public string Artist, Title, Genre = "rock"; public int Year = 2000; public double Preview; public int Level = 1; }
    Dictionary<string, SongInfo> infos = new Dictionary<string, SongInfo>();
    static readonly string[] GENRES = { "rock", "pop", "metal", "punk", "alternative", "country", "blues", "jazz", "hiphop", "reggae", "electronic", "folk", "other" };

    static double Dens(int level) { return level == 0 ? 0.65 : level == 2 ? 1.4 : 1.0; }
    static string Inv(double d) { return d.ToString(System.Globalization.CultureInfo.InvariantCulture); }

    SongInfo Info(string f)
    {
        SongInfo v;
        if (infos.TryGetValue(f, out v)) return v;
        v = new SongInfo(); Names(f, out v.Artist, out v.Title); v.Level = densityBox.SelectedIndex; return v;
    }

    [System.Runtime.InteropServices.DllImport("winmm.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    static extern int mciSendString(string cmd, StringBuilder ret, int len, IntPtr cb);

    Button playing;
    void StopPlay() { mciSendString("close song", null, 0, IntPtr.Zero); if (playing != null) { playing.Text = "\u25B6 PLAY"; playing = null; } }
    void StartPlay(string path, double startSec, Button b)
    {
        StopPlay();
        mciSendString("open \"" + path + "\" type mpegvideo alias song", null, 0, IntPtr.Zero);
        mciSendString("play song from " + (int)(startSec * 1000), null, 0, IntPtr.Zero);
        playing = b; b.Text = "\u25A0 STOP";
    }

    string PyScript(string script, string args)
    {
        var psi = new ProcessStartInfo(PyExe(), PyPre() + "-X utf8 " + script + " " + args)
        { WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
          RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        using (var p = Process.Start(psi))
        {
            string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit();
            return o;
        }
    }

    static string Quality(string o)
    {
        var p = o.Split('\t'); double dur, br;
        if (p.Length < 2 || !double.TryParse(p[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out dur)
            || !double.TryParse(p[1], out br)) return "";
        var w = new List<string>();
        if (dur <= 0) w.Add("could not read the length - the file may be damaged");
        else if (dur > 600) w.Add("very long (" + (dur / 60).ToString("0.0") + " min) - will be slow and the chart may be huge");
        else if (dur < 30) w.Add("very short (" + (int)dur + " s) - the build will probably fail");
        else if (dur < 60) w.Add("very short (" + (int)dur + " s)");
        if (br > 0 && br < 96) w.Add("low bitrate (" + br + " kbps) - audio quality will be poor");
        return w.Count == 0 ? "" : "\u26A0 " + string.Join("; ", w.ToArray());
    }

    bool EditNames(string[] files)
    {
        const int ROW = 84;
        using (var dlg = new Form { Text = "Check song details", Width = 840, Height = Math.Min(130 + files.Length * ROW + 60, 700),
            StartPosition = FormStartPosition.CenterParent, BackColor = BG, ForeColor = TEXT, FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false, MinimizeBox = false })
        {
            var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = BG };
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 52, BackColor = BG };
            var hint = new Label { Dock = DockStyle.Top, Height = 40, Padding = new Padding(12, 8, 12, 0),
                Text = "These show up in Rock Band and drive the cover-art search. Edit anything that is wrong, or press LOOK UP to fill from iTunes." };
            dlg.Controls.Add(host); dlg.Controls.Add(bottom); dlg.Controls.Add(hint);

            Func<string, int, int, int, Label> Lb = (t, x, y, w) => { var l = new Label { Text = t, Left = x, Top = y + 4, Width = w, ForeColor = DIM }; host.Controls.Add(l); return l; };
            var ab = new List<TextBox>(); var tb = new List<TextBox>(); var gb = new List<ComboBox>(); var yb = new List<TextBox>();
            var pb = new List<TextBox>(); var lv = new List<ComboBox>();
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i]; int y = 8 + i * ROW; var inf = Info(file);
                var A = new TextBox { Left = 14, Top = y, Width = 250, Text = inf.Artist };
                var T = new TextBox { Left = 272, Top = y, Width = 310, Text = inf.Title };
                var L = new Button { Left = 590, Top = y - 2, Width = 96, Height = 26, Text = "LOOK UP", FlatStyle = FlatStyle.Flat, ForeColor = ORANGE };
                var AR = new Button { Left = 692, Top = y - 2, Width = 100, Height = 26, Text = covers.ContainsKey(file) ? "ART \u2714" : "ART...", FlatStyle = FlatStyle.Flat, ForeColor = TEXT };
                Lb("Genre", 14, y + 30, 44);
                var G = new ComboBox { Left = 58, Top = y + 30, Width = 110, BackColor = PANEL, ForeColor = ORANGE, FlatStyle = FlatStyle.Flat };
                G.Items.AddRange(GENRES); G.Text = inf.Genre;
                Lb("Year", 180, y + 30, 32);
                var Y = new TextBox { Left = 214, Top = y + 30, Width = 54, Text = inf.Year.ToString() };
                Lb("Preview (s)", 280, y + 30, 66);
                var P = new TextBox { Left = 348, Top = y + 30, Width = 50, Text = inf.Preview > 0 ? Inv(inf.Preview) : "" };
                var PL = new Button { Left = 404, Top = y + 28, Width = 84, Height = 26, Text = "\u25B6 PLAY", FlatStyle = FlatStyle.Flat, ForeColor = TEXT };
                Lb("Chart", 500, y + 30, 40);
                var C = new ComboBox { Left = 542, Top = y + 30, Width = 90, DropDownStyle = ComboBoxStyle.DropDownList, BackColor = PANEL, ForeColor = ORANGE, FlatStyle = FlatStyle.Flat };
                C.Items.AddRange(new object[] { "Easier", "Normal", "Busier" }); C.SelectedIndex = inf.Level;
                var W = new Label { Left = 14, Top = y + 58, Width = 780, Height = 18, ForeColor = ORANGE, Text = "" };
                host.Controls.AddRange(new Control[] { A, T, L, AR, G, Y, P, PL, C, W });
                ab.Add(A); tb.Add(T); gb.Add(G); yb.Add(Y); pb.Add(P); lv.Add(C);

                L.Click += (s, e) =>
                {
                    L.Enabled = false; L.Text = "...";
                    string q = (A.Text + " " + T.Text).Trim();
                    new Thread(() =>
                    {
                        string o = PyOut("meta \"" + q.Replace("\"", "") + "\"");
                        try { dlg.BeginInvoke((MethodInvoker)delegate
                        {
                            var p = o.Split('\t');
                            if (p.Length >= 2 && p[0].Length > 0)
                            {
                                A.Text = p[0]; T.Text = p[1]; L.Text = "LOOK UP";
                                if (p.Length >= 3 && p[2].Length == 4) Y.Text = p[2];
                                if (p.Length >= 4) { string g = GENRES.FirstOrDefault(x => p[3].ToLower().Contains(x)); if (g != null) G.Text = g; }
                            }
                            else L.Text = "no match";
                            L.Enabled = true;
                        }); } catch { }
                    }) { IsBackground = true }.Start();
                };
                AR.Click += (s, e) =>
                {
                    using (var od = new OpenFileDialog { Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp", Title = "Choose album art" })
                        if (od.ShowDialog(dlg) == DialogResult.OK) { covers[file] = od.FileName; AR.Text = "ART \u2714"; }
                };
                PL.Click += (s, e) =>
                {
                    if (playing == PL) { StopPlay(); return; }
                    double st; double.TryParse(P.Text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out st);
                    StartPlay(file, st, PL);
                };
                new Thread(() =>
                {
                    string q = Quality(PyScript("audio_info.py", "\"" + file + "\""));
                    if (q.Length > 0) try { dlg.BeginInvoke((MethodInvoker)delegate { W.Text = q; }); } catch { }
                }) { IsBackground = true }.Start();
            }
            var ok = new Button { Text = "CONVERT", Left = 580, Top = 8, Width = 100, Height = 32, DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = RED, ForeColor = Color.White };
            var cancel = new Button { Text = "CANCEL", Left = 690, Top = 8, Width = 96, Height = 32, DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, ForeColor = TEXT };
            bottom.Controls.Add(ok); bottom.Controls.Add(cancel); dlg.AcceptButton = ok; dlg.CancelButton = cancel;
            foreach (Control c in host.Controls) if (c is TextBox) { c.BackColor = PANEL; c.ForeColor = TEXT; }
            Style(dlg);
            dlg.Shown += (s, e) => { var t = FirstTextBox(dlg); if (t != null) { t.Focus(); t.SelectAll(); } };
            var res = dlg.ShowDialog(this);
            StopPlay();
            if (res != DialogResult.OK) return false;
            for (int i = 0; i < files.Length; i++)
            {
                var inf = new SongInfo();
                inf.Artist = ab[i].Text.Trim(); inf.Title = tb[i].Text.Trim();
                if (inf.Artist.Length == 0) inf.Artist = "Unknown";
                if (inf.Title.Length == 0) inf.Title = "Untitled";
                inf.Artist = inf.Artist.Replace('"', '\''); inf.Title = inf.Title.Replace('"', '\'');
                inf.Genre = Regex.Replace(gb[i].Text.Trim().ToLower(), "[^a-z]", ""); if (inf.Genre.Length == 0) inf.Genre = "rock";
                int yr; inf.Year = int.TryParse(yb[i].Text.Trim(), out yr) && yr >= 1900 && yr <= 2100 ? yr : 2000;
                double pv; double.TryParse(pb[i].Text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out pv);
                inf.Preview = pv > 0 ? pv : 0; inf.Level = lv[i].SelectedIndex;
                infos[files[i]] = inf;
            }
            return true;
        }
    }

    void Start(string[] dropped)
    {
        if (!drop.Enabled) return;
        var files = ExpandMp3(dropped);
        if (files.Length == 0) { Say("Only .mp3 files please."); return; }
        if (!EditNames(files)) return;
        bool pack = bundle.Checked && files.Length > 1;
        string name = packName.Text.Trim();
        if (name.Length == 0) name = "My Song Pack";

        sendOn = sendBox.Checked; ip = ipBox.Text.Trim();
        if (sendOn) File.WriteAllText(Path.Combine(root, "ps4_ip.txt"), ip);
        density = densityBox.SelectedIndex == 0 ? 0.65 : densityBox.SelectedIndex == 2 ? 1.4 : 1.0; eu = euBox.Checked; SaveSettings();
        if (sendOn)
        {
            int lv; string pm = CheckPs4(ip, out lv);
            if (lv > 0) Say("PS4 check: " + pm);
            if (lv == 2 && MessageBox.Show(pm + "\r\n\r\nConvert anyway and skip sending?", "PS4 not reachable", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            if (lv == 2) sendOn = false;
        }
        string sendCell = sendOn ? WAIT : "(off)";

        drop.Enabled = false; drop.Text = "\u266B  SHREDDING... PLEASE WAIT  \u266B"; drop.Invalidate();
        list.Items.Clear();
        cancelled = false; cancelBtn.Visible = true; cancelBtn.BringToFront();
        var rows = new List<ListViewItem>();
        foreach (var f in files)
        {
            string a, t; GetNames(f, out a, out t);
            var it = new ListViewItem(new[] { a + " - " + t, WAIT, pack ? "(in bundle)" : WAIT, pack ? "(in bundle)" : sendCell });
            list.Items.Add(it); rows.Add(it);
        }
        ListViewItem packRow = null;
        if (pack)
        {
            packRow = new ListViewItem(new[] { "BUNDLE: " + name, "(merge)", WAIT, sendCell });
            list.Items.Add(packRow);
        }
        int send = sendOn ? 1 : 0;
        totalSteps = pack ? files.Length + 2 + send : files.Length * (2 + send);
        doneSteps = 0; SetProgress("Starting...");

        new Thread(() =>
        {
            var cons = new List<string>();
            for (int i = 0; i < files.Length; i++)
            {
                if (cancelled) { Cell(rows[i], 1, "Cancelled"); continue; }
                string con = Convert(files[i], rows[i], !pack, i + 1, files.Length);
                if (con != null) cons.Add(con);
            }
            if (pack && cancelled) Cell(packRow, 1, "Cancelled");
            else if (pack && cons.Count > 0) MakePack(cons, name, packRow);
            else if (pack) { Cell(packRow, 2, FAIL); Cell(packRow, 3, "Skipped"); }
            CleanWork();
            SetProgress("Finished. See the list above for results.", true);
            Invoke((Action)(() => { cancelBtn.Visible = false; drop.Enabled = true; drop.Text = "\u266B  DROP YOUR MP3s HERE  \u266B"; drop.Invalidate(); }));
        }) { IsBackground = true }.Start();
    }

    void Cell(ListViewItem it, int col, string text)
    {
        Invoke((Action)(() => { it.SubItems[col].Text = text; list.Refresh(); }));
    }

    void SetProgress(string text, bool final)
    {
        Invoke((Action)(() =>
        {
            bar.Set(Math.Min(Math.Max(1, totalSteps), doneSteps), totalSteps);
            int p = totalSteps == 0 ? 0 : (doneSteps * 100 / totalSteps);
            pct.Text = p + "%";
            status.Text = text;
        }));
    }
    void SetProgress(string text) { SetProgress(text, false); }
    void Step(string text) { doneSteps++; SetProgress(text); }

    void Say(string m)
    {
        if (InvokeRequired) { Invoke((Action)(() => Say(m))); return; }
        log.AppendText(m + "\r\n");
    }

    int Run(string exe, string args, string cwd)
    {
        var psi = new ProcessStartInfo(exe, args) { WorkingDirectory = cwd, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 };
        psi.EnvironmentVariables["PYTHONUTF8"] = "1";
        using (var p = Process.Start(psi))
        {
            p.OutputDataReceived += (s, e) => { if (e.Data != null && e.Data.Trim().Length > 0 && !e.Data.StartsWith(" ") && !e.Data.StartsWith("}") && !e.Data.StartsWith("{")) Say("  " + e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null && e.Data.Trim().Length > 0) Say("  " + e.Data); };
            cur = p; DataReceivedEventHandler keep = (s, e) => { if (e.Data != null) lock (tail) { tail.AppendLine(e.Data); if (tail.Length > 30000) tail.Remove(0, 10000); } };
            p.OutputDataReceived += keep; p.ErrorDataReceived += keep;
            p.BeginOutputReadLine(); p.BeginErrorReadLine();
            p.WaitForExit();
            return p.ExitCode;
        }
    }

    static void Names(string mp3, out string artist, out string title)
    {
        string stem = Path.GetFileNameWithoutExtension(mp3);
<<<<<<< HEAD
        stem = Regex.Replace(stem, @"\(mp3\.pm\)|\[.*?\]", "").Trim(' ', '_', '-');
        artist = "Unknown"; title = stem;
        var parts = stem.Split(new[] { "_-_", " - " }, 2, StringSplitOptions.None);
        if (parts.Length == 2) { artist = parts[0].Replace('_', ' ').Trim(); title = parts[1].Replace('_', ' ').Trim(); }
=======
        stem = Regex.Replace(stem, @"\(mp3\.pm\)|\[.*?\]|\(.*?\)", "").Trim(' ', '_', '-');
        // collapse multiple spaces left behind by stripping
        stem = Regex.Replace(stem, @"\s{2,}", " ").Trim();
        artist = "Unknown"; title = stem;
        var parts = stem.Split(new[] { "_-_", " - " }, 2, StringSplitOptions.None);
        if (parts.Length == 2) { artist = parts[0].Replace('_', ' ').Trim(); title = parts[1].Replace('_', ' ').Trim(); }
        // strip leftover bracketed/quoted junk from the title only
        title = Regex.Replace(title, @"[\[\]＂＂\""]", "").Trim();
        title = Regex.Replace(title, @"\s{2,}", " ").Trim();
        if (artist.Length == 0) artist = "Unknown";
        if (title.Length == 0) title = "Untitled";
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
    }

    string Convert(string mp3, ListViewItem row, bool makePkg, int n, int count)
    {
        try
        {
            var inf = Info(mp3); string artist = inf.Artist, title = inf.Title;
            string tag = "Song " + n + "/" + count + " (" + title + "): ";
            Say("\r\n=== " + artist + " - " + title + " ===");
            lock (tail) tail.Clear();

            Cell(row, 1, "Working...");
            SetProgress(tag + "making chart, audio and Rock Band package (about a minute)...");
            int rc = Run(PyExe(), PyPre() + "-X utf8 build_song.py \"" + mp3 + "\" --title \"" + title + "\" --artist \"" + artist + "\" --density " + Inv(Dens(inf.Level)) + " --genre \"" + inf.Genre + "\" --year " + inf.Year + " --preview " + Inv(inf.Preview) + CoverArg(mp3), root);
            if (cancelled)
            {
                Say("Cancelled."); Cell(row, 1, "Cancelled"); if (makePkg) { Cell(row, 2, "Skipped"); Cell(row, 3, "Skipped"); }
                doneSteps += makePkg ? 2 + (sendOn ? 1 : 0) : 1; SetProgress(tag + "cancelled");
                return null;
            }
<<<<<<< HEAD
            string bstem = Path.GetFileNameWithoutExtension(mp3).Replace("(mp3.pm)", "").Trim('_', '-', ' ');
            if (bstem.Length > 40)
            {
                using (var md5 = System.Security.Cryptography.MD5.Create())
                {
                    string hex = BitConverter.ToString(md5.ComputeHash(Encoding.UTF8.GetBytes(bstem))).Replace("-", "").ToLowerInvariant();
                    bstem = bstem.Substring(0, 30).TrimEnd('.', '_', ' ') + "_" + hex.Substring(0, 8);
                }
            }
            string buildDir = Path.Combine(root, "build", bstem);
            string con = Directory.Exists(buildDir) ? Directory.GetFiles(buildDir, "*.con")
                .OrderByDescending(File.GetLastWriteTime).FirstOrDefault() : null;
=======
            DateTime buildStart = DateTime.Now;
            // build_song.py now uses a safe ASCII slug for its folder, so locate the
            // freshly produced .con anywhere under build\ instead of guessing the name.
            string con = Directory.GetFiles(Path.Combine(root, "build"), "*.con", SearchOption.AllDirectories)
                .Where(f => File.GetLastWriteTime(f) >= buildStart.AddSeconds(-5))
                .OrderByDescending(File.GetLastWriteTime)
                .FirstOrDefault();
>>>>>>> cd46517 (v1.1.4 - MP3 SEARCH overhaul + credits popup)
            if (rc != 0 || con == null)
            {
                Say("FAILED: chart step - " + Friendly("", "The chart or audio step failed. Press COPY LOG and paste it in Discord."));
                Cell(row, 1, FAIL); if (makePkg) { Cell(row, 2, "Skipped"); Cell(row, 3, "Skipped"); }
                doneSteps += makePkg ? 2 + (sendOn ? 1 : 0) : 1; SetProgress(tag + "failed");
                return null;
            }
            Cell(row, 1, OK); Step(tag + "chart done");

            if (makePkg)
            {
                Cell(row, 2, "Working...");
                SetProgress(tag + "building PS4 .pkg...");
                string outDir = Path.Combine(root, "PS4 Packages", Path.GetFileNameWithoutExtension(con));
                string pkg = MakePkg(con, title, outDir);
                Cell(row, 2, pkg != null ? OK : FAIL); Step(tag + (pkg != null ? "pkg done" : "pkg failed"));
                if (sendOn) SendStep(pkg, row, tag);
            }
            return con;
        }
        catch (Exception ex) { Say("ERROR: " + Friendly(ex.Message, ex.Message)); Cell(row, 1, FAIL); doneSteps += makePkg ? 2 + (sendOn ? 1 : 0) : 1; return null; }
    }

    void SendStep(string pkg, ListViewItem row, string tag)
    {
        if (pkg == null) { Cell(row, 3, "Skipped"); Step(tag + "send skipped"); return; }
        bool ok = Upload(pkg, row, tag);
        Step(tag + (ok ? "sent to PS4" : "send failed"));
    }

    bool Upload(string file, ListViewItem row, string tag)
    {
        try
        {
            string baseUrl = "ftp://" + ip + ":2121/data/pkg";
            Say("Sending " + Path.GetFileName(file) + " to " + ip + ":2121 /data/pkg ...");
            Cell(row, 3, "Connecting...");
            try
            {
                var m = (FtpWebRequest)WebRequest.Create(baseUrl);
                m.Method = WebRequestMethods.Ftp.MakeDirectory; m.Timeout = 8000; m.KeepAlive = false;
                m.GetResponse().Close();
            }
            catch (WebException) { }
            var req = (FtpWebRequest)WebRequest.Create(baseUrl + "/" + Path.GetFileName(file));
            req.Method = WebRequestMethods.Ftp.UploadFile; req.UseBinary = true; req.KeepAlive = false;
            req.Timeout = 15000; req.ReadWriteTimeout = 60000;
            req.Credentials = new NetworkCredential("anonymous", "anonymous@");
            long total = new FileInfo(file).Length, sent = 0; int last = -1;
            using (var fs = File.OpenRead(file))
            using (var rs = req.GetRequestStream())
            {
                var buf = new byte[65536]; int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (cancelled) throw new Exception("Cancelled"); rs.Write(buf, 0, n); sent += n;
                    int p = (int)(sent * 100 / total);
                    if (p != last && p % 2 == 0) { last = p; Cell(row, 3, "Sending " + p + "%..."); SetProgress(tag + "sending to PS4 " + p + "%"); }
                }
            }
            using (req.GetResponse()) { }
            Cell(row, 3, OK); Say("Sent: /data/pkg/" + Path.GetFileName(file));
            return true;
        }
        catch (Exception ex)
        {
            Say("SEND FAILED: " + Friendly(ex.Message, "The PS4 refused the upload (" + ex.Message + "). Check free disk space and that GoldHEN FTP is running."));
            Cell(row, 3, FAIL); return false;
        }
    }

    void PickAndSend()
    {
        if (!drop.Enabled) return;
        using (var dlg = new OpenFileDialog { Filter = "PS4 packages (*.pkg)|*.pkg", Multiselect = true, Title = "Choose .pkg file(s) to send to the PS4",
            InitialDirectory = Directory.Exists(Path.Combine(root, "PS4 Packages")) ? Path.Combine(root, "PS4 Packages") : root })
        {
            if (dlg.ShowDialog() != DialogResult.OK) return;
            ip = ipBox.Text.Trim(); File.WriteAllText(Path.Combine(root, "ps4_ip.txt"), ip);
            var files = dlg.FileNames;
            drop.Enabled = false; drop.Text = "\u266B  SENDING TO PS4... \u266B"; drop.Invalidate();
            list.Items.Clear();
            var rows = files.Select(f => { var it = new ListViewItem(new[] { Path.GetFileName(f), "(existing)", "(existing)", WAIT }); list.Items.Add(it); return it; }).ToList();
            totalSteps = files.Length; doneSteps = 0; SetProgress("Starting...");
            new Thread(() =>
            {
                for (int i = 0; i < files.Length; i++) { bool ok = Upload(files[i], rows[i], "File " + (i + 1) + "/" + files.Length + ": "); Step(ok ? "sent" : "send failed"); }
                SetProgress("Finished. See the list above for results.", true);
                Invoke((Action)(() => { drop.Enabled = true; drop.Text = "\u266B  DROP YOUR MP3s HERE  \u266B"; drop.Invalidate(); }));
            }) { IsBackground = true }.Start();
        }
    }

    void MakePack(List<string> cons, string name, ListViewItem row)
    {
        Say("\r\n=== Bundling " + cons.Count + " songs: " + name + " ===");
        string safe = Regex.Replace(name, "[^A-Za-z0-9 _-]", "").Trim();
        if (safe.Length == 0) safe = "SongPack";
        string merged = Path.Combine(root, "build", safe + "_pack.con");
        lock (tail) tail.Clear();
        Cell(row, 1, "Merging...");
        SetProgress("Merging " + cons.Count + " songs into one package...");
        var args = PyPre() + "-X utf8 merge_cons.py \"" + merged + "\" \"" + name + "\" " + string.Join(" ", cons.Select(c => "\"" + c + "\"").ToArray());
        if (Run(PyExe(), args, root) != 0 || !File.Exists(merged))
        {
            Say("FAILED: bundling step - " + Friendly("", "Merging the songs failed (duplicate song titles can not be bundled). Press COPY LOG for details.")); Cell(row, 1, FAIL); Cell(row, 2, "Skipped"); Cell(row, 3, "Skipped");
            doneSteps += 2 + (sendOn ? 1 : 0); SetProgress("Bundling failed"); return;
        }
        Cell(row, 1, OK); Step("Songs merged");
        Cell(row, 2, "Working...");
        SetProgress("Building PS4 .pkg for the bundle...");
        string pkg = MakePkg(merged, name, Path.Combine(root, "PS4 Packages", safe));
        Cell(row, 2, pkg != null ? OK : FAIL);
        Step(pkg != null ? "Bundle pkg done" : "Bundle pkg failed");
        if (sendOn) SendStep(pkg, row, "Bundle: ");
    }

    int NextPkgNumber()
    {
        lock (tail)
        {
            string f = Path.Combine(root, "pkg_counter.txt"); int n = 0;
            try { if (File.Exists(f)) int.TryParse(File.ReadAllText(f).Trim(), out n); } catch { }
            n = (n + 1) % 1000000;
            try { File.WriteAllText(f, n.ToString()); } catch { }
            return n;
        }
    }

    string MakePkg(string con, string title, string outDir)
    {
        string id = Regex.Replace(title.ToUpper(), "[^A-Z0-9]", "");
        id = (id + "0000000000").Substring(0, 10) + NextPkgNumber().ToString("D6");
        Directory.CreateDirectory(outDir);
        lock (tail) tail.Clear();
        Say("Making PS4 .pkg...");
        Run(Path.Combine(root, "forge_official", "ForgeTool.exe"),
            "con2pkg " + (eu ? "--scee " : "") + "--id " + id + " --desc \"" + title + "\" \"" + con + "\" \"" + outDir + "\"", root);
        var pkg = Directory.GetFiles(outDir, "*.pkg").OrderByDescending(File.GetLastWriteTime).FirstOrDefault();
        Say(pkg == null ? "FAILED: pkg step - " + Friendly("", "Making the PS4 package failed. The CON may be invalid; press COPY LOG and paste it in Discord.") : "DONE: " + pkg);
        return pkg;
    }
}

