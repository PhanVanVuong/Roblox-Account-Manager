using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Net;
using System.Net.Http;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RBX_Alt_Manager.Classes
{
    internal sealed class ChromeExtensionBridge : IDisposable
    {
        internal const string ExtensionId = "mjapekjkfljcmpclkafajgmenailmkcn";
        private readonly AccountManager owner;
        private readonly object gate = new object();
        private readonly Queue<Job> jobs = new Queue<Job>();
        private Job active;
        private string session, status = "Chưa có tài khoản trong hàng đợi.";
        private int saved;
        private bool stopped;
        private NamedPipeServerStream listener;
        private Form monitor;
        internal Func<string, Task<JObject>> ValidateCookie = FetchCookieIdentity;
        private sealed class Job
        {
            public string Id = Guid.NewGuid().ToString("N");
            public string Username, Password;
            public bool Manual;
            public string Birthday;
            public string Gender = "";
        }

        internal ChromeExtensionBridge(AccountManager owner)
        {
            this.owner = owner;
            RegisterHost();
            _ = Task.Run(Listen);
            owner.FormClosed += (s, e) => Dispose();
            try { EnsureChromeRunning(); } catch (Exception) { /* Do not interrupt startup with setup dialogs. */ }
        }

        internal static void RegisterHost()
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            string host = Path.Combine(root, "ChromeBridge", "RAM.ChromeBridge.exe");
            if (!File.Exists(host)) throw new FileNotFoundException("Thiếu thư mục ChromeBridge. Hãy chép đầy đủ bản ứng dụng mới.");
            string manifest = Path.Combine(root, "ChromeBridge", "com.ram.chrome_bridge.json");
            File.WriteAllText(manifest, JsonConvert.SerializeObject(new
            {
                name = "com.ram.chrome_bridge", description = "Roblox Account Manager Chrome bridge", path = host,
                type = "stdio", allowed_origins = new[] { "chrome-extension://" + ExtensionId + "/" }
            }, Formatting.Indented), new UTF8Encoding(false));
            using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Google\Chrome\NativeMessagingHosts\com.ram.chrome_bridge")) key.SetValue("", manifest);
        }

        internal void Enqueue(string combos, DateTime? birthday = null, string gender = "")
        {
            if (gender != "" && gender != "female" && gender != "male") throw new ArgumentException("Giới tính không hợp lệ.");
            if (birthday.HasValue && (birthday.Value.Date >= DateTime.Today || birthday.Value.Year < 1900))
                throw new ArgumentException("Ngày sinh không hợp lệ.");
            var parsed = new List<Job>();
            if (combos == null) parsed.Add(new Job { Username = "", Password = "", Manual = true });
            else
            {
                int lineNumber = 0;
                foreach (string line in combos.Split('\n'))
                {
                    lineNumber++;
                    string text = line.TrimEnd('\r');
                    if (string.IsNullOrWhiteSpace(text)) continue;
                    int index = text.IndexOf(':');
                    if (index <= 0 || index == text.Length - 1 || string.IsNullOrWhiteSpace(text.Substring(0, index)))
                        throw new ArgumentException("Dòng " + lineNumber + " không đúng định dạng user:pass.");
                    parsed.Add(new Job { Username = text.Substring(0, index).Trim(), Password = text.Substring(index + 1) });
                }
            }
            if (birthday.HasValue)
            {
                if (parsed.Count == 0 || parsed.Exists(job => job.Manual)) throw new ArgumentException("Nhập ít nhất một dòng user:pass để đăng ký.");
                foreach (var job in parsed)
                {
                    job.Birthday = birthday.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    job.Gender = gender;
                }
            }
            lock (gate)
            {
                foreach (var job in parsed) jobs.Enqueue(job);
                status = "Đang chờ tiện ích Chrome kết nối. Nếu chưa cài, xem hướng dẫn bên dưới.";
            }
            EnsureChromeRunning();
        }

        private static void EnsureChromeRunning()
        {
            var processes = Process.GetProcessesByName("chrome");
            bool running = processes.Length > 0;
            foreach (var process in processes) process.Dispose();
            if (!running)
                Process.Start(new ProcessStartInfo(AccountBrowser.FindChromeExecutable(), "--no-startup-window")
                { UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden });
        }

        private void OpenExtension()
        {
            Process.Start(new ProcessStartInfo(AccountBrowser.FindChromeExecutable(), "--new-tab chrome-extension://" + ExtensionId + "/popup.html") { UseShellExecute = true });
        }

        internal void ShowMonitor()
        {
            if (monitor != null && !monitor.IsDisposed) { monitor.Show(); monitor.BringToFront(); return; }
            monitor = new Form { Text = "Chrome Extension", ClientSize = new Size(650, 330), StartPosition = FormStartPosition.CenterParent };
            var info = new Label { Dock = DockStyle.Top, Height = 155, Padding = new Padding(12), Text =
                "Cài một lần trên Chrome bạn đang dùng:\r\n1. Mở chrome://extensions, bật Chế độ dành cho nhà phát triển.\r\n2. Chọn Tải tiện ích đã giải nén (Load unpacked), chọn thư mục ChromeExtension cạnh file .exe.\r\n3. Mở Chi tiết của RAM Login Helper, bật Cho phép ở chế độ ẩn danh.\r\n4. Bấm Kết nối tiện ích bên dưới.\r\n\r\nTiện ích xử lý lần lượt. Đóng các cửa sổ ẩn danh khác trước khi bắt đầu. CAPTCHA/2FA cần bạn xử lý trong Chrome." };
            var progress = new Label { Dock = DockStyle.Fill, Padding = new Padding(12), AutoEllipsis = true };
            var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 45 };
            var connect = new Button { Text = "Kết nối tiện ích", AutoSize = true };
            var folder = new Button { Text = "Mở thư mục tiện ích", AutoSize = true };
            var cancel = new Button { Text = "Hủy hàng đợi", AutoSize = true };
            connect.Click += (s, e) => { try { OpenExtension(); } catch (Exception) { MessageBox.Show(monitor, "Không mở được Chrome."); } };
            folder.Click += (s, e) => Process.Start("explorer.exe", "\"" + Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ChromeExtension") + "\"");
            cancel.Click += (s, e) => { lock (gate) { jobs.Clear(); active = null; session = null; status = "Đã hủy hàng đợi."; } };
            actions.Controls.AddRange(new Control[] { connect, folder, cancel });
            monitor.Controls.Add(progress); monitor.Controls.Add(info); monitor.Controls.Add(actions);
            var timer = new System.Windows.Forms.Timer { Interval = 500 };
            timer.Tick += (s, e) => { lock (gate) progress.Text = status + "\r\nĐã lưu: " + saved + " | Đang chờ: " + jobs.Count; };
            monitor.FormClosed += (s, e) => timer.Dispose();
            timer.Start();
            monitor.Show(owner);
        }

        private async Task Listen()
        {
            while (!stopped)
            {
                try
                {
                    var security = new PipeSecurity();
                    security.SetAccessRuleProtection(true, false);
                    security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
                    using (var pipe = new NamedPipeServerStream("RAM.Chrome." + WindowsIdentity.GetCurrent().User.Value, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security))
                    {
                        listener = pipe;
                        await pipe.WaitForConnectionAsync();
                        using (var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true))
                        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true })
                        {
                            JObject request = null;
                            JObject response;
                            try
                            {
                                var text = new StringBuilder();
                                var buffer = new char[1];
                                while (await reader.ReadAsync(buffer, 0, 1) > 0 && buffer[0] != '\n')
                                { if (text.Length >= 262144) throw new InvalidDataException(); text.Append(buffer[0]); }
                                request = JObject.Parse(text.ToString());
                                response = await Handle(request);
                            }
                            catch (Exception) { response = new JObject { ["ok"] = false, ["error"] = "Không xử lý được yêu cầu. Kiểm tra kết nối và thử lại." }; }
                            response["requestId"] = request?["requestId"];
                            await writer.WriteLineAsync(response.ToString(Formatting.None));
                        }
                    }
                }
                catch (Exception) { if (!stopped) await Task.Delay(1000); }
            }
        }

        private async Task<JObject> Handle(JObject request)
        {
            string command = (string)request["command"], client = (string)request["session"];
            if (string.IsNullOrEmpty(client) || client.Length > 128) return Failure("Phiên kết nối không hợp lệ.");
            Job job;
            lock (gate)
            {
                if (command == "next")
                {
                    if (active == null && jobs.Count > 0) { active = jobs.Dequeue(); session = client; }
                    if (active == null) return new JObject { ["ok"] = true, ["job"] = null };
                    if (active.Birthday != null && (bool?)request["signupSupported"] != true)
                        return Failure("Hãy tải lại RAM Login Helper trong chrome://extensions để dùng tính năng tạo tài khoản.");
                    if (!string.IsNullOrEmpty(active.Gender) && (bool?)request["genderSupported"] != true)
                        return Failure("Hãy tải lại RAM Login Helper trong chrome://extensions để chọn giới tính.");
                    if (session != client) return Failure("Một phiên Chrome khác đang xử lý. Hủy hàng đợi trong ứng dụng nếu cần bắt đầu lại.");
                    return new JObject { ["ok"] = true, ["job"] = new JObject { ["id"] = active.Id, ["username"] = active.Username, ["password"] = active.Password, ["manual"] = active.Manual, ["signup"] = active.Birthday != null, ["birthday"] = active.Birthday, ["gender"] = active.Gender } };
                }
                if (active == null || session != client || (string)request["jobId"] != active.Id) return new JObject { ["ok"] = false, ["cancelled"] = true };
                job = active;
                if (command == "status") { status = ((string)request["message"] ?? "Đang xử lý.").Substring(0, Math.Min(250, ((string)request["message"] ?? "Đang xử lý.").Length)); return Success(); }
                if (command == "skip") { active = null; status = "Đã bỏ qua một tài khoản."; return Success(); }
                if (command == "credentials" && job.Manual)
                {
                    job.Password = ((string)request["password"] ?? "");
                    if (job.Password.Length > 5000) job.Password = "";
                    return Success();
                }
            }
            if (command != "complete") return Failure("Yêu cầu không được hỗ trợ.");
            string cookie = (string)request["cookie"];
            if (string.IsNullOrWhiteSpace(cookie) || cookie.Length > 32768) return Failure("Cookie không hợp lệ.");
            JObject identity = await ValidateCookie(cookie);
            if (identity == null) return Failure("Cookie chưa xác thực được. Hãy hoàn tất đăng nhập.");
            string username = (string)identity["name"];
            long id = (long?)identity["id"] ?? 0;
            if (id <= 0 || string.IsNullOrEmpty(username)) return Failure("Không xác định được tài khoản.");
            if (!job.Manual && !string.Equals(job.Username, username, StringComparison.OrdinalIgnoreCase)) return Failure("Cookie không thuộc username đang nhập. Hãy đăng nhập đúng tài khoản.");
            Task saveTask = (Task)owner.Invoke(new Func<Task>(() =>
            {
                lock (gate)
                {
                    if (active != job) return null;
                    var account = AccountManager.AddAccount(cookie, job.Password, new JObject { ["Name"] = username, ["UserId"] = id }.ToString(Formatting.None), job.Manual ? null : job.Username, true);
                    if (account == null) return null;
                    status = "Đang lưu tài khoản vào ổ đĩa…";
                    return AccountManager.SaveAccountsInBackground();
                }
            }));
            if (saveTask == null) return new JObject { ["ok"] = false, ["cancelled"] = true };
            try { await saveTask.ConfigureAwait(false); }
            catch (Exception error)
            {
                Program.Logger.Error("Browser account save failed: " + error.GetType().FullName);
                return Failure("Chưa lưu được AccountData.json. Kiểm tra quyền ghi và dung lượng ổ đĩa; giữ Chrome mở để thử lại.");
            }
            lock (gate)
            {
                if (active == job) { active = null; session = null; }
                saved++; status = "Đã lưu " + username + ".";
            }
            return Success();
        }
        private static JObject Success() => new JObject { ["ok"] = true };
        private static JObject Failure(string message) => new JObject { ["ok"] = false, ["error"] = message };
        private static async Task<JObject> FetchCookieIdentity(string cookie)
        {
            using (var handler = new HttpClientHandler { CookieContainer = new CookieContainer() })
            {
                handler.CookieContainer.Add(new Cookie(".ROBLOSECURITY", cookie, "/", ".roblox.com"));
                using (var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) })
                using (var result = await http.GetAsync("https://users.roblox.com/v1/users/authenticated"))
                    return result.IsSuccessStatusCode ? JObject.Parse(await result.Content.ReadAsStringAsync()) : null;
            }
        }
        public void Dispose()
        {
            stopped = true;
            try { listener?.Dispose(); } catch (Exception) { }
            lock (gate) { jobs.Clear(); active = null; session = null; }
        }
    }
}
