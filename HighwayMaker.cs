using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class HighwayMaker
{
    public const int W = 256, H = 512, DataOffset = 164;
    public const string PS4Dir = "/data/GoldHEN/RB4DX/ps4/dx/custom_textures/highways/";

    public static string CleanName(string s)
    {
        s = Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9 ()_-]", "").Trim();
        return s.Length == 0 ? "my highway" : s;
    }

    static Bitmap LoadUpright(string path)
    {
        using (var im = Image.FromFile(path))
        {
            foreach (var pi in im.PropertyItems)
            {
                if (pi.Id != 0x0112 || pi.Value == null || pi.Value.Length < 1) continue;
                switch (pi.Value[0])
                {
                    case 3: im.RotateFlip(RotateFlipType.Rotate180FlipNone); break;
                    case 6: im.RotateFlip(RotateFlipType.Rotate90FlipNone); break;
                    case 8: im.RotateFlip(RotateFlipType.Rotate270FlipNone); break;
                }
                break;
            }
            double sc = Math.Min(1.0, 1200.0 / Math.Max(im.Width, im.Height));
            int w = Math.Max(1, (int)(im.Width * sc)), h = Math.Max(1, (int)(im.Height * sc));
            var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bm))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(im, 0, 0, w, h);
            }
            return bm;
        }
    }

    static Rectangle CropRect(Bitmap b, double aspect, double yBias)
    {
        int cw = b.Width, ch = (int)(cw / aspect);
        if (ch > b.Height) { ch = b.Height; cw = (int)(ch * aspect); }
        return new Rectangle((b.Width - cw) / 2, (int)((b.Height - ch) * yBias), cw, ch);
    }

    static Bitmap Feather(Bitmap src, Rectangle crop, int w, int h, int margin, float opacity)
    {
        var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bm))
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, new Rectangle(0, 0, w, h), crop.X, crop.Y, crop.Width, crop.Height, GraphicsUnit.Pixel);
        }
        var d = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadWrite, PixelFormat.Format32bppArgb);
        var buf = new byte[d.Stride * h];
        System.Runtime.InteropServices.Marshal.Copy(d.Scan0, buf, 0, buf.Length);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                double ex = Math.Min(x, w - 1 - x) / (double)Math.Max(1, margin), ey = Math.Min(y, h - 1 - y) / (double)Math.Max(1, margin);
                double f = Math.Min(1.0, Math.Min(ex, ey)); if (f < 0) f = 0; f = f * f * (3 - 2 * f);
                int i = y * d.Stride + x * 4 + 3; buf[i] = (byte)(buf[i] * f * opacity);
            }
        System.Runtime.InteropServices.Marshal.Copy(buf, 0, d.Scan0, buf.Length);
        bm.UnlockBits(d);
        return bm;
    }

    // mode 0: three large photos, 1: collage of up to 21 small photos, 2: one photo as a full-track backdrop
    public static Bitmap Render(List<string> files, int mode, Color acc)
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            Color d1 = Color.FromArgb(acc.R / 9 + 6, acc.G / 9 + 6, acc.B / 9 + 6);
            Color d2 = Color.FromArgb(acc.R / 6 + 8, acc.G / 6 + 8, acc.B / 6 + 8);
            using (var br = new LinearGradientBrush(new Rectangle(0, 0, W, H), d1, d2, 90f)) g.FillRectangle(br, 0, 0, W, H);
            int[] edges = { 0, 51, 102, 153, 205, 256 };
            for (int i = 0; i < 5; i++)
                using (var b = new SolidBrush(Color.FromArgb(i % 2 == 0 ? 22 : 8, acc)))
                    g.FillRectangle(b, edges[i], 0, edges[i + 1] - edges[i], H);

            if (files.Count > 0)
            {
                if (mode == 0)
                {
                    int n = Math.Min(files.Count, 3); int slotH = H / n;
                    for (int k = 0; k < n; k++)
                        using (var im = LoadUpright(files[k]))
                        {
                            int iw = 214, ih = 160;
                            using (var piece = Feather(im, CropRect(im, 4.0 / 3.0, 0.35), iw, ih, 14, 0.92f))
                                g.DrawImage(piece, (W - iw) / 2, k * slotH + (slotH - ih) / 2);
                        }
                }
                else if (mode == 1)
                {
                    const int cell = 72, cols = 3, rows = 7;
                    int ox = (W - cols * cell) / 2, oy = (H - rows * cell) / 2;
                    var cache = new Dictionary<int, Bitmap>();
                    try
                    {
                        for (int k = 0; k < cols * rows; k++)
                        {
                            int fi = k % files.Count; Bitmap im;
                            if (!cache.TryGetValue(fi, out im)) { im = LoadUpright(files[fi]); cache[fi] = im; }
                            using (var piece = Feather(im, CropRect(im, 1.0, 0.2), cell, cell, 7, 0.9f))
                                g.DrawImage(piece, ox + (k % cols) * cell, oy + (k / cols) * cell);
                        }
                    }
                    finally { foreach (var b in cache.Values) b.Dispose(); }
                }
                else
                {
                    using (var im = LoadUpright(files[0]))
                    using (var piece = Feather(im, CropRect(im, (double)W / H, 0.3), W, H, 1, 0.55f))
                        g.DrawImage(piece, 0, 0);
                }
            }

            foreach (int x in new[] { 51, 102, 153, 205 })
            {
                using (var p = new Pen(Color.FromArgb(40, acc), 5)) g.DrawLine(p, x, 0, x, H);
                using (var p = new Pen(Color.FromArgb(200, Math.Min(255, acc.R + 90), Math.Min(255, acc.G + 90), Math.Min(255, acc.B + 90)), 1)) g.DrawLine(p, x, 0, x, H);
            }
            foreach (int x in new[] { 1, 254 })
            {
                using (var p = new Pen(Color.FromArgb(90, acc), 6)) g.DrawLine(p, x, 0, x, H);
                using (var p = new Pen(Color.FromArgb(255, Math.Min(255, acc.R + 50), Math.Min(255, acc.G + 50), Math.Min(255, acc.B + 50)), 2)) g.DrawLine(p, x, 0, x, H);
            }
        }
        return bmp;
    }

    static object Member(object o, string name)
    {
        var t = o.GetType();
        var p = t.GetProperty(name); if (p != null) return p.GetValue(o, null);
        var f = t.GetField(name); if (f != null) return f.GetValue(o);
        throw new Exception("LibForge has no member " + name);
    }

    // Converts the image with LibForge and writes its pixels into a copy of a known-good highway texture.
    public static byte[] Pack(Bitmap bmp, string root)
    {
        string dll = Path.Combine(root, "highway_lib", "LibForge.dll");
        string tpl = Path.Combine(root, "highway_template.png_ps4");
        if (!File.Exists(dll) || !File.Exists(tpl)) throw new Exception("highway_template.png_ps4 or highway_lib\\LibForge.dll is missing.");
        var asm = System.Reflection.Assembly.LoadFrom(dll);
        var conv = asm.GetType("LibForge.Texture.TextureConverter", true);
        Bitmap flat = new Bitmap(bmp.Width, bmp.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(flat)) { g.Clear(Color.FromArgb(14, 11, 8)); g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height); }
        object tex = conv.GetMethod("ToTexture").Invoke(null, new object[] { flat });
        flat.Dispose();
        var mips = (IList)Member(tex, "Mipmaps");
        byte[] bytes = File.ReadAllBytes(tpl);
        byte[] pix = (byte[])Member(mips[0], "Data");
        if (pix.Length > bytes.Length - DataOffset) throw new Exception("Unexpected texture size (" + pix.Length + ").");
        Array.Copy(pix, 0, bytes, DataOffset, pix.Length);
        return bytes;
    }

    public const string BgDir = "/data/GoldHEN/RB4DX/ps4/dx/custom_textures/backgrounds/";

    public static byte[] PackBackground(Bitmap bmp, string root)
    {
        string dll = Path.Combine(root, "highway_lib", "LibForge.dll");
        string tpl = Path.Combine(root, "background_template.bin");
        if (!File.Exists(dll) || !File.Exists(tpl)) throw new Exception("background_template.bin or highway_lib\\LibForge.dll is missing.");
        if (bmp.Width != 1920 || bmp.Height != 1080) throw new Exception("Background must be 1920x1080.");
        var asm = System.Reflection.Assembly.LoadFrom(dll);
        var conv = asm.GetType("LibForge.Texture.TextureConverter", true);
        Bitmap flat = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(flat)) { g.Clear(Color.Black); g.DrawImage(bmp, 0, 0, 1920, 1080); }
        object tex = conv.GetMethod("ToTexture").Invoke(null, new object[] { flat });
        flat.Dispose();
        byte[] pix = (byte[])Member(((IList)Member(tex, "Mipmaps"))[0], "Data");
        if (pix.Length != 1920 * 1080 * 4) throw new Exception("Unexpected texture size (" + pix.Length + ").");
        byte[] t = File.ReadAllBytes(tpl);
        byte[] outb = new byte[DataOffset + pix.Length + 28];
        Array.Copy(t, 0, outb, 0, DataOffset);
        Array.Copy(pix, 0, outb, DataOffset, pix.Length);
        Array.Copy(t, DataOffset, outb, DataOffset + pix.Length, 28);
        return outb;
    }

    // Fills 1920x1080 with the photo (cropped to fit), then darkens it by dim (0..0.8).
    public static Bitmap RenderBackground(string file, double dim)
    {
        var bmp = new Bitmap(1920, 1080, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.Clear(Color.Black);
            if (file != null)
            {
                using (var src = Image.FromFile(file))
                {
                    foreach (var pi in src.PropertyItems)
                    {
                        if (pi.Id != 0x0112 || pi.Value == null || pi.Value.Length < 1) continue;
                        if (pi.Value[0] == 3) src.RotateFlip(RotateFlipType.Rotate180FlipNone);
                        else if (pi.Value[0] == 6) src.RotateFlip(RotateFlipType.Rotate90FlipNone);
                        else if (pi.Value[0] == 8) src.RotateFlip(RotateFlipType.Rotate270FlipNone);
                        break;
                    }
                    double sc = Math.Max(1920.0 / src.Width, 1080.0 / src.Height);
                    int w = (int)Math.Ceiling(src.Width * sc), h = (int)Math.Ceiling(src.Height * sc);
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(src, (1920 - w) / 2, (1080 - h) / 2, w, h);
                }
            }
            if (dim > 0) using (var b = new SolidBrush(Color.FromArgb((int)(dim * 255), 0, 0, 0))) g.FillRectangle(b, 0, 0, 1920, 1080);
        }
        return bmp;
    }

    public static List<string> ListRemote(string host) { return ListRemote(host, PS4Dir); }
    public static List<string> ListRemote(string host, string dir)
    {
        var req = (FtpWebRequest)WebRequest.Create("ftp://" + host + ":2121" + dir);
        req.Method = WebRequestMethods.Ftp.ListDirectory; req.KeepAlive = false; req.Timeout = 15000;
        req.Credentials = new NetworkCredential("anonymous", "anonymous@");
        var names = new List<string>();
        using (var resp = req.GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream()))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                line = line.Trim();
                if (line.EndsWith(".png_ps4", StringComparison.OrdinalIgnoreCase)) names.Add(line.Substring(0, line.Length - 8));
            }
        }
        names.Sort(StringComparer.OrdinalIgnoreCase);
        return names;
    }

    public static void DeleteRemote(string host, string name) { DeleteRemote(host, PS4Dir, name); }
    public static void DeleteRemote(string host, string dir, string name)
    {
        var req = (FtpWebRequest)WebRequest.Create("ftp://" + host + ":2121" + dir + name + ".png_ps4");
        req.Method = WebRequestMethods.Ftp.DeleteFile; req.KeepAlive = false; req.Timeout = 15000;
        req.Credentials = new NetworkCredential("anonymous", "anonymous@");
        using (req.GetResponse()) { }
    }

    // Checks the PS4 is reachable and asks before replacing a file with the same name.
    public static bool ConfirmSend(IWin32Window owner, string host, string dir, string name, out string error)
    {
        error = null;
        if (string.IsNullOrEmpty(host)) { error = "Enter the PS4 IP on the SONGS page first."; return false; }
        List<string> existing;
        try { existing = ListRemote(host, dir); }
        catch (Exception)
        {
            error = "Can't reach the PS4 at " + host + ". Check the IP, that the console is on and GoldHEN FTP is running (port 2121).";
            return false;
        }
        foreach (var n in existing)
            if (string.Equals(n, name, StringComparison.OrdinalIgnoreCase))
                return MessageBox.Show(owner, "\"" + name + "\" already exists on the PS4. Replace it?", "Replace file?",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        return true;
    }

    public static void Upload(string host, string name, byte[] data) { Upload(host, PS4Dir, name, data); }
    public static void Upload(string host, string dir, string name, byte[] data)
    {
        string url = "ftp://" + host + ":2121" + dir + name + ".png_ps4";
        var req = (FtpWebRequest)WebRequest.Create(url);
        req.Method = WebRequestMethods.Ftp.UploadFile; req.UseBinary = true; req.KeepAlive = false;
        req.Timeout = 15000; req.ReadWriteTimeout = 60000;
        req.Credentials = new NetworkCredential("anonymous", "anonymous@");
        using (var rs = req.GetRequestStream()) rs.Write(data, 0, data.Length);
        using (req.GetResponse()) { }
    }
}

class HighwayForm : Form
{
    readonly string root; readonly Func<string> getIp;
    TextBox nameBox; ComboBox layout; ListBox list; PictureBox pic; Label status; Button colorBtn;
    Color accent = Color.FromArgb(255, 190, 90);
    readonly List<string> files = new List<string>();

    public HighwayForm(string root, Func<string> getIp)
    {
        this.root = root; this.getIp = getIp;
        Text = "Highway Maker"; StartPosition = FormStartPosition.CenterParent; FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ClientSize = new Size(700, 600);
        BackColor = Color.FromArgb(24, 24, 32); ForeColor = Color.White; Font = new Font("Segoe UI", 9);

        Controls.Add(new Label { Text = "Make a custom Rock Band 4 highway from your photos and send it to the PS4.", Left = 16, Top = 12, Width = 370, Height = 34 });
        Controls.Add(new Label { Text = "Highway name", Left = 16, Top = 54, Width = 100 });
        nameBox = new TextBox { Text = "my highway", Left = 16, Top = 74, Width = 260, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(nameBox);
        Controls.Add(new Label { Text = "Layout", Left = 16, Top = 108, Width = 100 });
        layout = new ComboBox { Left = 16, Top = 128, Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        layout.Items.AddRange(new object[] { "3 large photos (easiest to see)", "Collage (up to 21 small photos)", "One photo as the whole backdrop" });
        layout.SelectedIndex = 0; layout.SelectedIndexChanged += delegate { Preview(); };
        Controls.Add(layout);

        Controls.Add(new Label { Text = "Photos (in order, top of the track first)", Left = 16, Top = 162, Width = 330 });
        list = new ListBox { Left = 16, Top = 182, Width = 330, Height = 190, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.MultiExtended };
        Controls.Add(list);
        var add = Mk("ADD PHOTOS", 16, 380, 106); add.Click += delegate { AddPhotos(); };
        var rem = Mk("REMOVE", 128, 380, 100); rem.Click += delegate { RemoveSel(); };
        var clr = Mk("CLEAR", 234, 380, 112); clr.Click += delegate { files.Clear(); list.Items.Clear(); Preview(); };
        var up = Mk("MOVE UP", 16, 416, 106); up.Click += delegate { MovePick(-1); };
        var dn = Mk("MOVE DOWN", 128, 416, 100); dn.Click += delegate { MovePick(1); };
        colorBtn = Mk("ACCENT COLOR", 234, 416, 112); colorBtn.BackColor = accent; colorBtn.ForeColor = Color.Black;
        colorBtn.Click += delegate
        {
            using (var cd = new ColorDialog { Color = accent, FullOpen = true })
                if (cd.ShowDialog(this) == DialogResult.OK) { accent = cd.Color; colorBtn.BackColor = accent; Preview(); }
        };
        Controls.AddRange(new Control[] { add, rem, clr, up, dn, colorBtn });

        var send = Mk("SEND TO PS4", 16, 468, 160); send.Height = 40; send.BackColor = Color.FromArgb(230, 100, 20);
        send.Click += delegate { Build(true); };
        var save = Mk("SAVE FILE...", 186, 468, 160); save.Height = 40; save.BackColor = Color.FromArgb(70, 70, 90);
        save.Click += delegate { Build(false); };
        Controls.Add(send); Controls.Add(save);
        var manage = Mk("MANAGE HIGHWAYS ON PS4...", 16, 516, 330); manage.BackColor = Color.FromArgb(120, 60, 150);
        manage.Click += delegate { ShowManager(this, getIp(), HighwayMaker.PS4Dir, "Highways"); };
        Controls.Add(manage);
        status = new Label { Left = 16, Top = 556, Width = 330, Height = 44, ForeColor = Color.FromArgb(200, 200, 215),
            Text = "Add photos, pick a layout, then SEND TO PS4. Fully restart Rock Band 4, then choose the name under Highway in settings." };
        Controls.Add(status);

        pic = new PictureBox { Left = 400, Top = 40, Width = 256, Height = 512, SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(pic);
        Controls.Add(new Label { Text = "Preview", Left = 400, Top = 18, Width = 100 });
        Preview();
    }

    public static Button Mk(string text, int x, int y, int w)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(60, 60, 80),
            ForeColor = Color.White, Font = new Font("Segoe UI", 9, FontStyle.Bold), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderSize = 0; return b;
    }

    public static void ShowManager(Form owner, string ip, string dir, string what)
    {
        using (var dlg = new Form { Text = what + " on your PS4", ClientSize = new Size(420, 470), StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
            BackColor = Color.FromArgb(24, 24, 32), ForeColor = Color.White, Font = new Font("Segoe UI", 9) })
        {
            var lb = new ListBox { Left = 12, Top = 12, Width = 396, Height = 390, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle, SelectionMode = SelectionMode.MultiExtended };
            var msg = new Label { Left = 12, Top = 444, Width = 396, Height = 20, ForeColor = Color.FromArgb(200, 200, 215) };
            var refresh = Mk("REFRESH", 12, 408, 120);
            var del = Mk("DELETE SELECTED", 140, 408, 160); del.BackColor = Color.FromArgb(190, 40, 40);
            var close = Mk("CLOSE", 308, 408, 100);
            Action load = delegate
            {
                try
                {
                    msg.Text = "Loading..."; dlg.Refresh();
                    lb.Items.Clear();
                    foreach (var n in HighwayMaker.ListRemote(ip, dir)) lb.Items.Add(n);
                    msg.Text = lb.Items.Count + " " + what.ToLower() + " on " + ip;
                }
                catch (Exception ex) { msg.Text = "Failed: " + ex.Message; }
            };
            refresh.Click += delegate { load(); };
            close.Click += delegate { dlg.Close(); };
            del.Click += delegate
            {
                var sel = lb.SelectedItems.Cast<string>().ToList();
                if (sel.Count == 0) { msg.Text = "Select one or more highways first."; return; }
                if (MessageBox.Show(dlg, "Delete " + sel.Count + " highway(s) from the PS4?\n\n" + string.Join("\n", sel.Take(8).ToArray()), "Delete",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
                int ok = 0;
                foreach (var n in sel) { try { HighwayMaker.DeleteRemote(ip, dir, n); ok++; } catch (Exception ex) { msg.Text = "Failed on " + n + ": " + ex.Message; } }
                load(); if (ok > 0) msg.Text = "Deleted " + ok + ". " + msg.Text;
            };
            dlg.Controls.AddRange(new Control[] { lb, refresh, del, close, msg });
            dlg.Shown += delegate { load(); };
            dlg.ShowDialog(owner);
        }
    }

    void AddPhotos()
    {
        using (var dlg = new OpenFileDialog { Multiselect = true, Title = "Choose photos", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.jfif|All files|*.*" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            foreach (var f in dlg.FileNames.OrderBy(x => x, StringComparer.OrdinalIgnoreCase)) { files.Add(f); list.Items.Add(Path.GetFileName(f)); }
        }
        Preview();
    }

    void RemoveSel()
    {
        foreach (int i in list.SelectedIndices.Cast<int>().OrderByDescending(x => x).ToList()) { files.RemoveAt(i); list.Items.RemoveAt(i); }
        Preview();
    }

    void MovePick(int dir)
    {
        if (list.SelectedIndices.Count != 1) return;
        int i = list.SelectedIndex, j = i + dir;
        if (j < 0 || j >= files.Count) return;
        string f = files[i]; files[i] = files[j]; files[j] = f;
        object t = list.Items[i]; list.Items[i] = list.Items[j]; list.Items[j] = t;
        list.ClearSelected(); list.SelectedIndex = j; Preview();
    }

    Bitmap Draw()
    {
        return HighwayMaker.Render(files, layout.SelectedIndex, accent);
    }

    void Preview()
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            var old = pic.Image; pic.Image = Draw(); if (old != null) old.Dispose();
        }
        catch (Exception ex) { status.Text = "Preview failed: " + ex.Message; }
        finally { Cursor = Cursors.Default; }
    }

    void Build(bool send)
    {
        string name = HighwayMaker.CleanName(nameBox.Text); nameBox.Text = name;
        if (files.Count == 0) { status.Text = "Add at least one photo first."; return; }
        try
        {
            Cursor = Cursors.WaitCursor;
            byte[] data;
            using (var bmp = Draw()) data = HighwayMaker.Pack(bmp, root);
            if (send)
            {
                string ip = getIp(), err;
                if (!HighwayMaker.ConfirmSend(this, ip, HighwayMaker.PS4Dir, name, out err)) { status.Text = err ?? "Cancelled."; return; }
                HighwayMaker.Upload(ip, name, data);
                status.Text = "Sent \"" + name + "\" to " + ip + ". Fully restart Rock Band 4, then pick it under Highway in settings.";
            }
            else
            {
                using (var sd = new SaveFileDialog { FileName = name + ".png_ps4", Filter = "Highway texture|*.png_ps4" })
                {
                    if (sd.ShowDialog(this) != DialogResult.OK) return;
                    File.WriteAllBytes(sd.FileName, data);
                    status.Text = "Saved " + sd.FileName;
                }
            }
        }
        catch (Exception ex) { status.Text = "Failed: " + ex.Message + (send ? "  (Check the PS4 IP and that GoldHEN FTP is running.)" : ""); }
        finally { Cursor = Cursors.Default; }
    }
}

class BackgroundForm : Form
{
    readonly string root; readonly Func<string> getIp;
    TextBox nameBox; Label fileLbl, status; TrackBar dimBar; PictureBox pic; string file;

    public BackgroundForm(string root, Func<string> getIp)
    {
        this.root = root; this.getIp = getIp;
        Text = "Background Maker"; ClientSize = new Size(700, 600);
        BackColor = Color.FromArgb(24, 24, 32); ForeColor = Color.White; Font = new Font("Segoe UI", 9);
        Controls.Add(new Label { Text = "Make a custom 1920x1080 Rock Band 4 background from one photo and send it to the PS4.", Left = 16, Top = 12, Width = 660, Height = 20 });
        Controls.Add(new Label { Text = "Background name", Left = 16, Top = 44, Width = 200 });
        nameBox = new TextBox { Text = "my background", Left = 16, Top = 64, Width = 300, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(nameBox);
        var pick = HighwayForm.Mk("CHOOSE PHOTO...", 16, 100, 300); pick.Click += delegate { Pick(); };
        Controls.Add(pick);
        fileLbl = new Label { Text = "No photo chosen", Left = 16, Top = 136, Width = 300, Height = 34, ForeColor = Color.FromArgb(200, 200, 215) };
        Controls.Add(fileLbl);
        Controls.Add(new Label { Text = "Darken (so the notes stay readable)", Left = 16, Top = 180, Width = 300 });
        dimBar = new TrackBar { Left = 12, Top = 200, Width = 310, Minimum = 0, Maximum = 80, TickFrequency = 10, Value = 30 };
        dimBar.ValueChanged += delegate { Preview(); };
        Controls.Add(dimBar);
        var send = HighwayForm.Mk("SEND TO PS4", 16, 270, 146); send.Height = 40; send.BackColor = Color.FromArgb(230, 100, 20);
        send.Click += delegate { Build(true); };
        var save = HighwayForm.Mk("SAVE FILE...", 170, 270, 146); save.Height = 40; save.BackColor = Color.FromArgb(70, 70, 90);
        save.Click += delegate { Build(false); };
        var manage = HighwayForm.Mk("MANAGE BACKGROUNDS ON PS4...", 16, 322, 300); manage.BackColor = Color.FromArgb(120, 60, 150);
        manage.Click += delegate { HighwayForm.ShowManager(this, getIp(), HighwayMaker.BgDir, "Backgrounds"); };
        Controls.AddRange(new Control[] { send, save, manage });
        status = new Label { Left = 16, Top = 370, Width = 300, Height = 120, ForeColor = Color.FromArgb(200, 200, 215),
            Text = "Choose a photo, then SEND TO PS4. Fully restart Rock Band 4, then pick the name where backgrounds are chosen in the RB4DX settings." };
        Controls.Add(status);
        pic = new PictureBox { Left = 340, Top = 64, Width = 346, Height = 195, SizeMode = PictureBoxSizeMode.StretchImage, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(pic);
        Controls.Add(new Label { Text = "Preview", Left = 340, Top = 44, Width = 100 });
        Preview();
    }

    void Pick()
    {
        using (var dlg = new OpenFileDialog { Title = "Choose a photo", Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.jfif|All files|*.*" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            file = dlg.FileName; fileLbl.Text = Path.GetFileName(file);
        }
        Preview();
    }

    void Preview()
    {
        try
        {
            using (var b = HighwayMaker.RenderBackground(file, dimBar.Value / 100.0))
            {
                var old = pic.Image; pic.Image = new Bitmap(b, 346, 195); if (old != null) old.Dispose();
            }
        }
        catch (Exception ex) { status.Text = "Preview failed: " + ex.Message; }
    }

    void Build(bool send)
    {
        string name = HighwayMaker.CleanName(nameBox.Text); if (name == "my highway") name = "my background"; nameBox.Text = name;
        if (file == null) { status.Text = "Choose a photo first."; return; }
        try
        {
            status.Text = "Working..."; Refresh();
            using (var bmp = HighwayMaker.RenderBackground(file, dimBar.Value / 100.0))
            {
                byte[] data = HighwayMaker.PackBackground(bmp, root);
                if (send)
                {
                    string ip = getIp(), err;
                    if (!HighwayMaker.ConfirmSend(this, ip, HighwayMaker.BgDir, name, out err)) { status.Text = err ?? "Cancelled."; return; }
                    HighwayMaker.Upload(ip, HighwayMaker.BgDir, name, data);
                    status.Text = "Sent \"" + name + "\" to " + ip + ". Fully restart Rock Band 4, then pick it in the RB4DX background setting.";
                }
                else
                    using (var sd = new SaveFileDialog { FileName = name + ".png_ps4", Filter = "PS4 texture|*.png_ps4" })
                    {
                        if (sd.ShowDialog(this) != DialogResult.OK) return;
                        File.WriteAllBytes(sd.FileName, data); status.Text = "Saved " + sd.FileName;
                    }
            }
        }
        catch (Exception ex) { status.Text = "Failed: " + ex.Message + (send ? "  (Check the PS4 IP and that GoldHEN FTP is running.)" : ""); }
    }
}