// Передача by mysik — файлы и тексты между компьютером и телефоном по Wi-Fi через QR-код.
// Свой HTTP-сервер на TcpListener (не нужны права администратора) и свой генератор QR (QrCode.cs).

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Mysik;
using IOPath = System.IO.Path;
using Shapes = System.Windows.Shapes;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Передача")]
[assembly: AssemblyDescription("Передача by mysik — файлы между компьютером и телефоном по QR-коду")]
[assembly: AssemblyCompany("mysik")]
[assembly: AssemblyProduct("Передача by mysik")]
[assembly: AssemblyCopyright("by mysik")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace Peredacha
{
    static class Program
    {
        [STAThread]
        static void Main() { Boot.Run("Передача", "PeredachaByMysik_SingleInstance", () => new MainWindow()); }
    }

    class SharedFile
    {
        public int Id;
        public string Path, Name;
        public long Size;
    }

    // ------------------------------------------------------------------ HTTP-сервер

    class Server
    {
        TcpListener listener;
        public int Port;
        public readonly string Token;
        public DateTime LastSeen = DateTime.MinValue;
        public string Folder;
        public Func<List<SharedFile>> Files;
        public Func<List<string>> Texts;
        public event Action<string, long> FileReceived;
        public event Action<string> TextReceived;
        string page;

        public Server()
        {
            var b = new byte[9];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(b);
            Token = Convert.ToBase64String(b).Replace('+', 'x').Replace('/', 'y').Replace("=", "");
            using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("phone.html"))
            using (var r = new StreamReader(s, Encoding.UTF8)) page = r.ReadToEnd();
        }

        public void Start()
        {
            foreach (int p in new[] { 8765, 8766, 8780, 8790, 9876, 0 })
            {
                try
                {
                    listener = new TcpListener(IPAddress.Any, p);
                    listener.Start();
                    Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    break;
                }
                catch (SocketException) { listener = null; }
            }
            if (listener == null) throw new Exception("Не удалось открыть порт для подключения");
            Task.Run(() => Loop());
        }

        async Task Loop()
        {
            while (true)
            {
                TcpClient c;
                try { c = await listener.AcceptTcpClientAsync(); }
                catch { return; }
                var client = c;
                var t = Task.Run(() => Handle(client));
            }
        }

        public void Stop() { try { listener.Stop(); } catch { } }

        void Handle(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = 30000;
                    client.SendTimeout = 60000;
                    var ns = client.GetStream();
                    byte[] leftover;
                    var head = ReadHead(ns, out leftover);
                    if (head == null) return;
                    var lines = head.Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var first = lines[0].Split(' ');
                    if (first.Length < 2) return;
                    string method = first[0], target = first[1];
                    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var l in lines.Skip(1))
                    {
                        int i = l.IndexOf(':');
                        if (i > 0) headers[l.Substring(0, i).Trim()] = l.Substring(i + 1).Trim();
                    }
                    string path = target, query = "";
                    int qi = target.IndexOf('?');
                    if (qi >= 0) { path = target.Substring(0, qi); query = target.Substring(qi + 1); }
                    var qs = ParseQuery(query);

                    string k;
                    if (!qs.TryGetValue("k", out k) || k != Token)
                    {
                        if (path == "/favicon.ico") { Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("")); return; }
                        Send(ns, 403, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(
                            "<meta name=viewport content='width=device-width'><body style='background:#14161b;color:#e8eaee;font:16px sans-serif;padding:24px'>" +
                            "<h3>Нужен новый QR-код</h3><p>Открой «Передачу» на компьютере и отсканируй код камерой ещё раз.</p></body>"));
                        return;
                    }
                    LastSeen = DateTime.Now;

                    if (method == "GET" && (path == "/" || path == "/index.html"))
                        Send(ns, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(page));
                    else if (method == "GET" && path == "/api/state")
                        Send(ns, 200, "application/json; charset=utf-8", Encoding.UTF8.GetBytes(StateJson()));
                    else if (method == "GET" && path.StartsWith("/d/"))
                        SendFile(ns, path.Substring(3));
                    else if (method == "POST" && path == "/u")
                    {
                        string name;
                        qs.TryGetValue("name", out name);
                        long len = headers.ContainsKey("Content-Length") ? long.Parse(headers["Content-Length"]) : -1;
                        if (len < 0) { Send(ns, 411, "text/plain", Encoding.UTF8.GetBytes("length required")); return; }
                        string saved = SaveUpload(ns, leftover, len, name);
                        Send(ns, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                        if (FileReceived != null) FileReceived(saved, len);
                    }
                    else if (method == "POST" && path == "/t")
                    {
                        long len = headers.ContainsKey("Content-Length") ? long.Parse(headers["Content-Length"]) : 0;
                        if (len > 1024 * 1024) { Send(ns, 413, "text/plain", Encoding.UTF8.GetBytes("too long")); return; }
                        var ms = new MemoryStream();
                        ms.Write(leftover, 0, leftover.Length);
                        Copy(ns, ms, len - leftover.Length);
                        string text = Encoding.UTF8.GetString(ms.ToArray()).Trim();
                        Send(ns, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                        if (text.Length > 0 && TextReceived != null) TextReceived(text);
                    }
                    else Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("not found"));
                }
                catch { }
            }
        }

        // Читает заголовки до пустой строки; то, что пришло после, — начало тела.
        static string ReadHead(NetworkStream ns, out byte[] leftover)
        {
            var buf = new byte[8192];
            var acc = new MemoryStream();
            leftover = new byte[0];
            while (acc.Length < 64 * 1024)
            {
                int n = ns.Read(buf, 0, buf.Length);
                if (n <= 0) return null;
                acc.Write(buf, 0, n);
                var all = acc.ToArray();
                for (int i = 3; i < all.Length; i++)
                {
                    if (all[i - 3] == 13 && all[i - 2] == 10 && all[i - 1] == 13 && all[i] == 10)
                    {
                        leftover = all.Skip(i + 1).ToArray();
                        return Encoding.UTF8.GetString(all, 0, i - 3);
                    }
                }
            }
            return null;
        }

        static Dictionary<string, string> ParseQuery(string q)
        {
            var d = new Dictionary<string, string>();
            foreach (var part in q.Split('&'))
            {
                if (part.Length == 0) continue;
                int i = part.IndexOf('=');
                string key = i >= 0 ? part.Substring(0, i) : part, val = i >= 0 ? part.Substring(i + 1) : "";
                try { d[Uri.UnescapeDataString(key)] = Uri.UnescapeDataString(val.Replace('+', ' ')); } catch { }
            }
            return d;
        }

        static void Copy(Stream from, Stream to, long count)
        {
            var buf = new byte[81920];
            while (count > 0)
            {
                int n = from.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) throw new IOException("связь прервалась");
                to.Write(buf, 0, n);
                count -= n;
            }
        }

        string SaveUpload(NetworkStream ns, byte[] leftover, long len, string name)
        {
            Directory.CreateDirectory(Folder);
            name = SafeName(string.IsNullOrWhiteSpace(name) ? "файл с телефона" : name);
            string target = Unique(IOPath.Combine(Folder, name));
            string part = target + ".part";
            try
            {
                using (var fs = File.Create(part))
                {
                    int first = (int)Math.Min(leftover.Length, len);
                    fs.Write(leftover, 0, first);
                    Copy(ns, fs, len - first);
                }
                File.Move(part, target);
                return target;
            }
            catch
            {
                try { File.Delete(part); } catch { }
                throw;
            }
        }

        static string SafeName(string n)
        {
            n = IOPath.GetFileName(n.Replace('\\', '/').Split('/').Last());
            foreach (char c in IOPath.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            if (n.Length > 150) n = n.Substring(n.Length - 150);
            return n.Length == 0 ? "файл" : n;
        }

        static string Unique(string p)
        {
            if (!File.Exists(p)) return p;
            string dir = IOPath.GetDirectoryName(p), name = IOPath.GetFileNameWithoutExtension(p), ext = IOPath.GetExtension(p);
            for (int i = 2; ; i++)
            {
                string c = IOPath.Combine(dir, name + " (" + i + ")" + ext);
                if (!File.Exists(c)) return c;
            }
        }

        void SendFile(NetworkStream ns, string idStr)
        {
            int id;
            var f = int.TryParse(idStr, out id) ? Files().FirstOrDefault(x => x.Id == id) : null;
            if (f == null || !File.Exists(f.Path)) { Send(ns, 404, "text/plain", Encoding.UTF8.GetBytes("файл больше не доступен")); return; }
            using (var fs = File.OpenRead(f.Path))
            {
                string enc = Uri.EscapeDataString(f.Name);
                string head = "HTTP/1.1 200 OK\r\nContent-Type: " + Mime(f.Name) + "\r\nContent-Length: " + fs.Length +
                              "\r\nContent-Disposition: attachment; filename=\"" + Ascii(f.Name) + "\"; filename*=UTF-8''" + enc +
                              "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
                var hb = Encoding.UTF8.GetBytes(head);
                ns.Write(hb, 0, hb.Length);
                fs.CopyTo(ns, 81920);
            }
        }

        static string Ascii(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s) sb.Append(c >= 32 && c < 127 && c != '"' && c != '\\' ? c : '_');
            return sb.ToString();
        }

        static string Mime(string name)
        {
            switch (IOPath.GetExtension(name).ToLowerInvariant())
            {
                case ".jpg": case ".jpeg": return "image/jpeg";
                case ".png": return "image/png";
                case ".gif": return "image/gif";
                case ".mp4": return "video/mp4";
                case ".mp3": return "audio/mpeg";
                case ".pdf": return "application/pdf";
                default: return "application/octet-stream";
            }
        }

        static void Send(NetworkStream ns, int code, string type, byte[] body)
        {
            string status = code == 200 ? "OK" : code == 403 ? "Forbidden" : code == 404 ? "Not Found" : "Error";
            string head = "HTTP/1.1 " + code + " " + status + "\r\nContent-Type: " + type + "\r\nContent-Length: " + body.Length +
                          "\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            var hb = Encoding.UTF8.GetBytes(head);
            ns.Write(hb, 0, hb.Length);
            ns.Write(body, 0, body.Length);
        }

        string StateJson()
        {
            var sb = new StringBuilder("{\"pc\":");
            sb.Append(Json(Environment.MachineName)).Append(",\"files\":[");
            var files = Files();
            for (int i = 0; i < files.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(files[i].Id).Append(",\"name\":").Append(Json(files[i].Name)).Append(",\"size\":").Append(files[i].Size).Append('}');
            }
            sb.Append("],\"texts\":[");
            var texts = Texts();
            for (int i = 0; i < texts.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(Json(texts[i]));
            }
            sb.Append("]}");
            return sb.ToString();
        }

        static string Json(string s)
        {
            var sb = new StringBuilder("\"");
            foreach (char c in s ?? "")
            {
                if (c == '"') sb.Append("\\\"");
                else if (c == '\\') sb.Append("\\\\");
                else if (c == '\n') sb.Append("\\n");
                else if (c == '\r') sb.Append("\\r");
                else if (c == '\t') sb.Append("\\t");
                else if (c < 32) sb.AppendFormat("\\u{0:x4}", (int)c);
                else sb.Append(c);
            }
            return sb.Append('"').ToString();
        }
    }

    // ------------------------------------------------------------------ окно

    class MainWindow : ShellWindow
    {
        readonly Server server = new Server();
        readonly List<SharedFile> shared = new List<SharedFile>();
        readonly List<string> texts = new List<string>();
        readonly object sync = new object();
        int nextId = 1;
        string ip;
        DispatcherTimer timer;

        System.Windows.Controls.Image qrImage;
        Shapes.Ellipse linkDot;
        TextBlock linkStatus, addressText, sharedEmpty, receivedEmpty;
        WrapPanel ipPanel;
        StackPanel sharedList, receivedList;
        Border dropZone;
        Button clipBtn, addBtn, folderBtn;

        public MainWindow()
        {
            LoadUi("Передача by mysik", 440, 800);
            qrImage = F<System.Windows.Controls.Image>("QrImage");
            linkDot = F<Shapes.Ellipse>("LinkDot");
            linkStatus = F<TextBlock>("LinkStatus");
            addressText = F<TextBlock>("AddressText");
            sharedEmpty = F<TextBlock>("SharedEmpty");
            receivedEmpty = F<TextBlock>("ReceivedEmpty");
            ipPanel = F<WrapPanel>("IpPanel");
            sharedList = F<StackPanel>("SharedList");
            receivedList = F<StackPanel>("ReceivedList");
            dropZone = F<Border>("DropZone");
            clipBtn = F<Button>("ClipBtn");
            addBtn = F<Button>("AddBtn");
            folderBtn = F<Button>("FolderBtn");

            server.Folder = IOPath.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Передача by mysik");
            server.Files = () => { lock (sync) return shared.ToList(); };
            server.Texts = () => { lock (sync) return texts.ToList(); };
            server.FileReceived += (p, len) => UI(() => AddReceivedFile(p, len));
            server.TextReceived += t => UI(() => AddReceivedText(t));

            AllowDrop = true;
            Drop += (s, e) =>
            {
                var files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null) AddShared(files);
            };
            DragOver += (s, e) => { e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None; e.Handled = true; };
            addBtn.Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Что отправить на телефон?" };
                if (dlg.ShowDialog(this) == true) AddShared(dlg.FileNames);
            };
            clipBtn.Click += (s, e) =>
            {
                string t = null;
                try { if (Clipboard.ContainsText()) t = Clipboard.GetText(); } catch { }
                if (string.IsNullOrWhiteSpace(t)) { Toast("В буфере нет текста"); return; }
                lock (sync) texts.Insert(0, t.Length > 20000 ? t.Substring(0, 20000) : t);
                RenderShared();
                Toast("Текст появится на телефоне");
            };
            folderBtn.Click += (s, e) =>
            {
                try { Directory.CreateDirectory(server.Folder); Process.Start("explorer.exe", "\"" + server.Folder + "\""); } catch { }
            };
            addressText.MouseLeftButtonUp += (s, e) =>
            {
                try { Clipboard.SetText(Url()); Toast("Адрес скопирован — можно открыть в браузере телефона"); } catch { }
            };
        }

        public override void Init()
        {
            try { server.Start(); }
            catch (Exception ex)
            {
                Ask("Не получилось запустить передачу", ex.Message, "Понятно", null);
                return;
            }
            var ips = LocalIps();
            if (ips.Count == 0)
            {
                linkStatus.Text = "Нет подключения к сети";
                linkDot.Fill = Br(CRed);
                addressText.Text = "Подключись к Wi-Fi и перезапусти программу";
                return;
            }
            ip = ips[0];
            if (ips.Count > 1)
            {
                var pill = S("Pill");
                foreach (var a in ips)
                {
                    var addr = a;
                    var rb = new RadioButton { Style = pill, GroupName = "ip", IsChecked = a == ip, Content = new TextBlock { Text = a, FontSize = 11.5 } };
                    rb.Checked += (s, e) => { ip = addr; DrawQr(); };
                    ipPanel.Children.Add(rb);
                }
            }
            DrawQr();
            RenderShared();
            RenderReceived();

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, e) =>
            {
                bool on = (DateTime.Now - server.LastSeen).TotalSeconds < 6;
                linkDot.Fill = Br(on ? CGreen : CAmber);
                linkStatus.Text = on ? "Телефон подключён" : "Наведи камеру телефона на код";
            };
            timer.Start();
        }

        string Url() { return "http://" + ip + ":" + server.Port + "/?k=" + server.Token; }

        void DrawQr()
        {
            string url = Url();
            addressText.Text = "http://" + ip + ":" + server.Port;
            bool[,] m = QrCode.Encode(url);
            int n = m.GetLength(0), quiet = 2;
            var g = new GeometryGroup();
            for (int r = 0; r < n; r++)
                for (int c = 0; c < n; c++)
                    if (m[r, c]) g.Children.Add(new RectangleGeometry(new Rect(c + quiet, r + quiet, 1.02, 1.02)));
            var dg = new DrawingGroup();
            dg.Children.Add(new GeometryDrawing(Brushes.White, null, new RectangleGeometry(new Rect(0, 0, n + 2 * quiet, n + 2 * quiet))));
            dg.Children.Add(new GeometryDrawing(new SolidColorBrush(Color.FromRgb(20, 22, 27)), null, g));
            var img = new DrawingImage(dg);
            img.Freeze();
            qrImage.Source = img;
        }

        static List<string> LocalIps()
        {
            var res = new List<KeyValuePair<int, string>>();
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    string d = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    if (d.Contains("virtual") || d.Contains("vmware") || d.Contains("hyper-v") || d.Contains("vpn") || d.Contains("wsl")) continue;
                    var props = ni.GetIPProperties();
                    bool gw = props.GatewayAddresses.Any(x => x.Address.AddressFamily == AddressFamily.InterNetwork && !x.Address.Equals(IPAddress.Any));
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var b = ua.Address.GetAddressBytes();
                        bool priv = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        if (!priv) continue;
                        int score = (gw ? 0 : 10) + (ni.NetworkInterfaceType == NetworkInterfaceType.Wireless80211 ? 0 : 1);
                        res.Add(new KeyValuePair<int, string>(score, ua.Address.ToString()));
                    }
                }
                catch { }
            }
            return res.OrderBy(x => x.Key).Select(x => x.Value).Distinct().ToList();
        }

        // -------------------------------------------------- списки

        void AddShared(IEnumerable<string> paths)
        {
            int added = 0;
            lock (sync)
                foreach (var p in paths)
                {
                    if (!File.Exists(p) || shared.Any(f => f.Path.Equals(p, StringComparison.OrdinalIgnoreCase))) continue;
                    shared.Add(new SharedFile { Id = nextId++, Path = p, Name = IOPath.GetFileName(p), Size = new FileInfo(p).Length });
                    added++;
                }
            RenderShared();
            if (added > 0) Toast(added == 1 ? "Файл появится на телефоне" : Plural(added, "файл появится", "файла появятся", "файлов появятся") + " на телефоне");
            else Toast("Папки не передаются — выбери файлы");
        }

        void RenderShared()
        {
            sharedList.Children.Clear();
            List<SharedFile> files;
            List<string> ts;
            lock (sync) { files = shared.ToList(); ts = texts.ToList(); }
            foreach (var f in files)
            {
                var file = f;
                sharedList.Children.Add(Row(ExtLabel(f.Name), f.Name, FmtBytes(f.Size), "", "Убрать", () =>
                {
                    lock (sync) shared.Remove(file);
                    RenderShared();
                }));
            }
            foreach (var t in ts)
            {
                var text = t;
                sharedList.Children.Add(Row("Аа", OneLine(t), "текст", "", "Убрать", () =>
                {
                    lock (sync) texts.Remove(text);
                    RenderShared();
                }));
            }
            sharedEmpty.Visibility = files.Count + ts.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        readonly List<UIElement> received = new List<UIElement>();

        void AddReceivedFile(string path, long len)
        {
            var row = Row(ExtLabel(path), IOPath.GetFileName(path), FmtBytes(len) + " · " + DateTime.Now.ToString("HH:mm"), "", "Показать в папке", () =>
            {
                try { Process.Start("explorer.exe", "/select,\"" + path + "\""); } catch { }
            });
            received.Insert(0, row);
            RenderReceived();
            Toast("С телефона: " + IOPath.GetFileName(path));
        }

        void AddReceivedText(string text)
        {
            bool isLink = text.StartsWith("http://") || text.StartsWith("https://");
            var row = Row(isLink ? "🔗" : "Аа", OneLine(text), "текст · " + DateTime.Now.ToString("HH:mm"), "", "Скопировать", () =>
            {
                try { Clipboard.SetText(text); Toast("Скопировано"); } catch { }
            });
            received.Insert(0, row);
            RenderReceived();
            try { Clipboard.SetText(text); Toast("Текст с телефона уже в буфере обмена"); } catch { Toast("Пришёл текст с телефона"); }
        }

        void RenderReceived()
        {
            receivedList.Children.Clear();
            foreach (var r in received.Take(30)) receivedList.Children.Add(r);
            receivedEmpty.Visibility = received.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        UIElement Row(string badge, string title, string sub, string glyph, string tip, Action action)
        {
            var g = Columns(Auto, Star, Auto);
            g.Margin = new Thickness(0, 8, 0, 8);
            var ic = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(10), Background = Br(CCard2), Margin = new Thickness(0, 0, 12, 0) };
            ic.Child = new TextBlock { Text = badge, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Br(CSub), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            g.Children.Add(ic);
            var mid = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mid.Children.Add(Txt(title, 13, CText, true));
            mid.Children.Add(Txt(sub, 11.5, CSub, false));
            g.Children.Add(At(mid, 1));
            var btn = new Button { Style = S("IconBtn"), Content = Glyph(glyph, 12, CSub), ToolTip = tip, VerticalAlignment = VerticalAlignment.Center };
            btn.Click += (s, e) => action();
            g.Children.Add(At(btn, 2));
            return g;
        }

        static string ExtLabel(string name)
        {
            string e = IOPath.GetExtension(name).TrimStart('.').ToUpperInvariant();
            return e.Length == 0 ? "—" : (e.Length > 4 ? e.Substring(0, 4) : e);
        }

        static string OneLine(string t)
        {
            t = t.Replace("\r", " ").Replace("\n", " ").Trim();
            return t.Length > 80 ? t.Substring(0, 77) + "…" : t;
        }

        protected override void OnClosed(EventArgs e)
        {
            server.Stop();
            base.OnClosed(e);
        }
    }
}
