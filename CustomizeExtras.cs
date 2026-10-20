using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Windows.Forms;

static class Ps4Ftp
{
    public const string TexRoot = "/data/GoldHEN/RB4DX/ps4/dx/custom_textures/";

    public static FtpWebRequest Req(string host, string path, string method)
    {
        var r = (FtpWebRequest)WebRequest.Create("ftp://" + host + ":2121" + path);
        r.Method = method; r.KeepAlive = false; r.UseBinary = true; r.Timeout = 20000; r.ReadWriteTimeout = 60000;
        r.Credentials = new NetworkCredential("anonymous", "anonymous@");
        return r;
    }

    public static byte[] Download(string host, string path)
    {
        using (var resp = Req(host, path, WebRequestMethods.Ftp.DownloadFile).GetResponse())
        using (var s = resp.GetResponseStream())
        using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
    }

    public static void Upload(string host, string path, byte[] data)
    {
        var r = Req(host, path, WebRequestMethods.Ftp.UploadFile);
        using (var rs = r.GetRequestStream()) rs.Write(data, 0, data.Length);
        using (r.GetResponse()) { }
    }

    public static void MakeDir(string host, string path)
    {
        try { using (Req(host, path, WebRequestMethods.Ftp.MakeDirectory).GetResponse()) { } } catch { }
    }

    // Returns (name, isDirectory) pairs for a remote folder.
    public static List<KeyValuePair<string, bool>> List(string host, string dir)
    {
        var res = new List<KeyValuePair<string, bool>>();
        using (var resp = Req(host, dir, WebRequestMethods.Ftp.ListDirectoryDetails).GetResponse())
        using (var sr = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
        {
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                if (line.Length < 10) continue;
                bool isDir = line[0] == 'd';
                // Unix listing: 8 columns before the name
                int pos = 0;
                for (int col = 0; col < 8; col++)
                {
                    while (pos < line.Length && line[pos] == ' ') pos++;
                    while (pos < line.Length && line[pos] != ' ') pos++;
                }
                while (pos < line.Length && line[pos] == ' ') pos++;
                if (pos >= line.Length) continue;
                string name = line.Substring(pos).TrimEnd('\r');
                if (name == "." || name == "..") continue;
                res.Add(new KeyValuePair<string, bool>(name, isDir));
            }
        }
        return res;
    }
}

// Raw RGBA .png_ps4 textures: 164-byte header, w*h*4 pixels, 28-byte tail.
static class RawTex
{
    public static bool Parse(byte[] x, out int w, out int h)
    {
        w = h = 0;
        if (x == null || x.Length < 192) return false;
        w = x[136] | (x[137] << 8); h = x[140] | (x[141] << 8);
        int len = x[160] | (x[161] << 8) | (x[162] << 16);
        return w > 0 && h > 0 && len == w * h * 4 && x.Length == 164 + len + 28;
    }

    public static Bitmap Decode(byte[] x, int w, int h)
    {
        var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        var bd = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        byte[] row = new byte[w * 4];
        for (int y = 0; y < h; y++)
        {
            for (int i = 0; i < w; i++)
            {
                int s = 164 + (y * w + i) * 4;
                row[i * 4] = x[s + 2]; row[i * 4 + 1] = x[s + 1]; row[i * 4 + 2] = x[s]; row[i * 4 + 3] = x[s + 3];
            }
            System.Runtime.InteropServices.Marshal.Copy(row, 0, new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), row.Length);
        }
        bm.UnlockBits(bd);
        return bm;
    }

    // Stretches any image to w x h and writes it into a copy of the reference file's header/tail.
    public static byte[] Encode(Image src, byte[] reference, int w, int h)
    {
        using (var bm = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        {
            using (var g = Graphics.FromImage(bm))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            var bd = bm.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            byte[] bgra = new byte[w * h * 4];
            for (int y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(new IntPtr(bd.Scan0.ToInt64() + (long)y * bd.Stride), bgra, y * w * 4, w * 4);
            bm.UnlockBits(bd);
            byte[] o = (byte[])reference.Clone();
            for (int i = 0; i < w * h; i++)
            {
                o[164 + i * 4] = bgra[i * 4 + 2]; o[164 + i * 4 + 1] = bgra[i * 4 + 1]; o[164 + i * 4 + 2] = bgra[i * 4]; o[164 + i * 4 + 3] = bgra[i * 4 + 3];
            }
            return o;
        }
    }
}

class SkinForm : Form
{
    static readonly string[] Cats = { "crowd meter", "score", "solo box" };
    readonly Func<string> getIp;
    ComboBox cat; ListBox skins; PictureBox refPic, newPic; TextBox nameBox; Label info, status;
    byte[] refBytes; int rw, rh; string imgFile;

    string Dir { get { return Ps4Ftp.TexRoot + (string)cat.SelectedItem + "/"; } }

    public SkinForm(string root, Func<string> getIp)
    {
        this.getIp = getIp;
        Text = "Skin Maker"; ClientSize = new Size(700, 600);
        BackColor = Color.FromArgb(24, 24, 32); ForeColor = Color.White; Font = new Font("Segoe UI", 9);
        Controls.Add(new Label { Text = "Make your own crowd meter, score or solo box skin. Pick an existing skin as the shape to follow, EXPORT it, edit it in any image editor, then IMPORT it back.", Left = 16, Top = 8, Width = 660, Height = 34 });
        Controls.Add(new Label { Text = "Type", Left = 16, Top = 48, Width = 100 });
        cat = new ComboBox { Left = 16, Top = 66, Width = 330, DropDownStyle = ComboBoxStyle.DropDownList };
        cat.Items.AddRange(Cats); cat.SelectedIndex = 0; cat.SelectedIndexChanged += delegate { LoadList(); };
        Controls.Add(cat);
        skins = new ListBox { Left = 16, Top = 98, Width = 330, Height = 150, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        skins.SelectedIndexChanged += delegate { LoadRef(); };
        Controls.Add(skins);
        var refresh = HighwayForm.Mk("REFRESH", 16, 254, 100); refresh.Click += delegate { LoadList(); };
        var export = HighwayForm.Mk("EXPORT AS PNG...", 122, 254, 224); export.Click += delegate { Export(); };
        Controls.AddRange(new Control[] { refresh, export });
        info = new Label { Left = 16, Top = 290, Width = 330, Height = 34, ForeColor = Color.FromArgb(200, 200, 215), Text = "Select a skin above." };
        Controls.Add(info);
        Controls.Add(new Label { Text = "Name for your new skin", Left = 16, Top = 332, Width = 200 });
        nameBox = new TextBox { Text = "my skin", Left = 16, Top = 352, Width = 330, BackColor = Color.FromArgb(40, 40, 54), ForeColor = Color.White, BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(nameBox);
        var pick = HighwayForm.Mk("IMPORT IMAGE (PNG / JPG)...", 16, 384, 330); pick.Click += delegate { Pick(); };
        Controls.Add(pick);
        var send = HighwayForm.Mk("SEND TO PS4", 16, 428, 160); send.Height = 40; send.BackColor = Color.FromArgb(230, 100, 20); send.Click += delegate { Build(true); };
        var save = HighwayForm.Mk("SAVE FILE...", 186, 428, 160); save.Height = 40; save.BackColor = Color.FromArgb(70, 70, 90); save.Click += delegate { Build(false); };
        var manage = HighwayForm.Mk("MANAGE ON PS4...", 16, 476, 330); manage.BackColor = Color.FromArgb(120, 60, 150);
        manage.Click += delegate { HighwayForm.ShowManager(this, getIp(), Dir, (string)cat.SelectedItem); };
        Controls.AddRange(new Control[] { send, save, manage });
        status = new Label { Left = 16, Top = 520, Width = 330, Height = 70, ForeColor = Color.FromArgb(200, 200, 215),
            Text = "Fully restart Rock Band 4 after sending, then pick the skin name in the RB4DX settings." };
        Controls.Add(status);

        Controls.Add(new Label { Text = "Selected skin", Left = 370, Top = 48, Width = 200 });
        refPic = new PictureBox { Left = 370, Top = 66, Width = 310, Height = 230, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(60, 60, 70), BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(refPic);
        Controls.Add(new Label { Text = "Your new skin", Left = 370, Top = 306, Width = 200 });
        newPic = new PictureBox { Left = 370, Top = 324, Width = 310, Height = 230, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.FromArgb(60, 60, 70), BorderStyle = BorderStyle.FixedSingle };
        Controls.Add(newPic);
        Shown += delegate { LoadList(); };
    }

    void LoadList()
    {
        try
        {
            status.Text = "Loading..."; Refresh();
            skins.Items.Clear(); refBytes = null; refPic.Image = null;
            foreach (var n in HighwayMaker.ListRemote(getIp(), Dir)) skins.Items.Add(n);
            status.Text = skins.Items.Count + " skins on the PS4.";
        }
        catch (Exception ex) { status.Text = "Failed to list: " + ex.Message; }
    }

    void LoadRef()
    {
        if (skins.SelectedItem == null) return;
        try
        {
            status.Text = "Downloading..."; Refresh();
            refBytes = Ps4Ftp.Download(getIp(), Dir + skins.SelectedItem + ".png_ps4");
            if (!RawTex.Parse(refBytes, out rw, out rh)) { refBytes = null; info.Text = "This skin uses a compressed format the tool can't edit. Pick another."; status.Text = ""; return; }
            var old = refPic.Image; refPic.Image = RawTex.Decode(refBytes, rw, rh); if (old != null) old.Dispose();
            info.Text = "Size: " + rw + " x " + rh + " pixels. Your image is stretched to this size.";
            status.Text = "";
            UpdateNew();
        }
        catch (Exception ex) { status.Text = "Failed: " + ex.Message; }
    }

    void Export()
    {
        if (refBytes == null) { status.Text = "Select an editable skin first."; return; }
        using (var sd = new SaveFileDialog { FileName = skins.SelectedItem + ".png", Filter = "PNG image|*.png" })
        {
            if (sd.ShowDialog(this) != DialogResult.OK) return;
            using (var bm = RawTex.Decode(refBytes, rw, rh)) bm.Save(sd.FileName, ImageFormat.Png);
            status.Text = "Exported " + sd.FileName + " (" + rw + "x" + rh + "). Edit it, keep the size, then IMPORT it.";
        }
    }

    void Pick()
    {
        using (var dlg = new OpenFileDialog { Title = "Choose an image", Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*" })
        {
            if (dlg.ShowDialog(this) != DialogResult.OK) return;
            imgFile = dlg.FileName;
        }
        UpdateNew();
    }

    void UpdateNew()
    {
        if (imgFile == null || refBytes == null) return;
        try
        {
            using (var im = Image.FromFile(imgFile))
            {
                byte[] data = RawTex.Encode(im, refBytes, rw, rh);
                var old = newPic.Image; newPic.Image = RawTex.Decode(data, rw, rh); if (old != null) old.Dispose();
            }
        }
        catch (Exception ex) { status.Text = "Image failed: " + ex.Message; }
    }

    void Build(bool send)
    {
        if (refBytes == null) { status.Text = "Select an editable skin first (it sets the size)."; return; }
        if (imgFile == null) { status.Text = "Import an image first."; return; }
        string name = HighwayMaker.CleanName(nameBox.Text); if (name == "my highway") name = "my skin"; nameBox.Text = name;
        try
        {
            byte[] data;
            using (var im = Image.FromFile(imgFile)) data = RawTex.Encode(im, refBytes, rw, rh);
            if (send)
            {
                string err;
                if (!HighwayMaker.ConfirmSend(this, getIp(), Dir, name, out err)) { status.Text = err ?? "Cancelled."; return; }
                HighwayMaker.Upload(getIp(), Dir, name, data);
                status.Text = "Sent \"" + name + "\" to " + (string)cat.SelectedItem + ". Fully restart Rock Band 4, then pick it in the RB4DX settings.";
            }
            else using (var sd = new SaveFileDialog { FileName = name + ".png_ps4", Filter = "PS4 texture|*.png_ps4" })
            {
                if (sd.ShowDialog(this) != DialogResult.OK) return;
                File.WriteAllBytes(sd.FileName, data); status.Text = "Saved " + sd.FileName;
            }
        }
        catch (Exception ex) { status.Text = "Failed: " + ex.Message; }
    }
}

class BackupForm : Form
{
    readonly Func<string> getIp; TextBox log; Button backBtn, restBtn; volatile bool busy;

    public BackupForm(string root, Func<string> getIp)
    {
        this.getIp = getIp;
        Text = "Backup"; ClientSize = new Size(700, 600);
        BackColor = Color.FromArgb(24, 24, 32); ForeColor = Color.White; Font = new Font("Segoe UI", 9);
        Controls.Add(new Label { Text = "Back up every custom texture on your PS4 (highways, backgrounds, skins) to a folder on this PC, or put a backup back.", Left = 16, Top = 12, Width = 660, Height = 34 });
        backBtn = HighwayForm.Mk("BACK UP PS4 TEXTURES TO PC...", 16, 52, 320); backBtn.Click += delegate { Backup(); };
        restBtn = HighwayForm.Mk("RESTORE BACKUP TO PS4...", 346, 52, 320); restBtn.BackColor = Color.FromArgb(230, 100, 20); restBtn.Click += delegate { Restore(); };
        Controls.AddRange(new Control[] { backBtn, restBtn });
        log = new TextBox { Left = 16, Top = 96, Width = 650, Height = 480, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(10, 10, 12), ForeColor = Color.FromArgb(170, 220, 170), Font = new Font("Consolas", 9), BorderStyle = BorderStyle.None };
        Controls.Add(log);
    }

    void Say(string s) { try { BeginInvoke((MethodInvoker)delegate { log.AppendText(s + "\r\n"); }); } catch { } }
    void Busy(bool b) { try { BeginInvoke((MethodInvoker)delegate { busy = b; backBtn.Enabled = !b; restBtn.Enabled = !b; }); } catch { } }

    void Backup()
    {
        if (busy) return;
        using (var fb = new FolderBrowserDialog { Description = "Choose where to save the backup" })
        {
            if (fb.ShowDialog(this) != DialogResult.OK) return;
            string dest = Path.Combine(fb.SelectedPath, "RB4DX-textures-" + DateTime.Now.ToString("yyyyMMdd-HHmm"));
            string host = getIp(); busy = true; Busy(true); log.Clear(); Say("Backing up to " + dest);
            new Thread(() =>
            {
                int n = 0, bad = 0;
                try { Walk(host, Ps4Ftp.TexRoot, dest, ref n, ref bad); Say("Done: " + n + " files saved" + (bad > 0 ? ", " + bad + " failed." : ".")); }
                catch (Exception ex) { Say("Failed: " + ex.Message); }
                Busy(false);
            }) { IsBackground = true }.Start();
        }
    }

    void Walk(string host, string remote, string local, ref int n, ref int bad)
    {
        Directory.CreateDirectory(local);
        foreach (var e in Ps4Ftp.List(host, remote))
        {
            if (e.Value) Walk(host, remote + e.Key + "/", Path.Combine(local, e.Key), ref n, ref bad);
            else
            {
                try { File.WriteAllBytes(Path.Combine(local, e.Key), Ps4Ftp.Download(host, remote + e.Key)); n++; if (n % 10 == 0) Say(n + " files..."); }
                catch (Exception ex) { bad++; Say("Skipped " + e.Key + ": " + ex.Message); }
            }
        }
    }

    void Restore()
    {
        if (busy) return;
        using (var fb = new FolderBrowserDialog { Description = "Choose a backup folder (the one that contains highways, backgrounds, ...)" })
        {
            if (fb.ShowDialog(this) != DialogResult.OK) return;
            string src = fb.SelectedPath;
            if (MessageBox.Show(this, "Copy everything in this folder onto the PS4's custom_textures? Files with the same name are overwritten.\r\n\r\n" + src,
                "Restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            string host = getIp(); Busy(true); log.Clear(); Say("Restoring from " + src);
            new Thread(() =>
            {
                int n = 0, bad = 0;
                try { Put(host, src, Ps4Ftp.TexRoot, ref n, ref bad); Say("Done: " + n + " files sent" + (bad > 0 ? ", " + bad + " failed." : ".")); }
                catch (Exception ex) { Say("Failed: " + ex.Message); }
                Busy(false);
            }) { IsBackground = true }.Start();
        }
    }

    void Put(string host, string local, string remote, ref int n, ref int bad)
    {
        Ps4Ftp.MakeDir(host, remote);
        foreach (var d in Directory.GetDirectories(local)) Put(host, d, remote + Path.GetFileName(d) + "/", ref n, ref bad);
        foreach (var f in Directory.GetFiles(local))
        {
            try { Ps4Ftp.Upload(host, remote + Path.GetFileName(f), File.ReadAllBytes(f)); n++; if (n % 10 == 0) Say(n + " files..."); }
            catch (Exception ex) { bad++; Say("Failed " + Path.GetFileName(f) + ": " + ex.Message); }
        }
    }
}
