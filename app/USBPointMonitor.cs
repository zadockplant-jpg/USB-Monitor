using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace USBPointMonitorStable
{
    internal sealed class ProcResult
    {
        public int ExitCode;
        public string StdOut = "";
        public string StdErr = "";
        public bool TimedOut;
    }

    internal sealed class CaptureRun
    {
        public int Slot;
        public string DeviceName;
        public Process Process;
        public string PcapPath;
        public DateTime Started;
        public bool StartedOk;
    }

    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        // Size the layout is designed for; FitToWorkingArea shrinks it on smaller screens.
        private static readonly Size DesignSize = new Size(1200, 800);
        private static readonly Size DesignMinimumSize = new Size(940, 650);

        private TextBox logBox;
        private TextBox testPad;
        private ComboBox slotCombo;
        private NumericUpDown maxSlotsBox;
        private CheckBox allSlots;
        private Label statusLabel;
        private Button refreshButton;
        private Button diagButton;
        private Button startButton;
        private Button stopButton;
        private Button killButton;
        private Button copyButton;
        private Button openButton;
        private Button clearButton;
        private System.Windows.Forms.Timer uiTimer;

        private readonly List<CaptureRun> captures = new List<CaptureRun>();
        private readonly object captureLock = new object();

        private string tsharkPath;
        private string usbpcapPath;
        private string sessionFolder;
        private bool captureActive;

        public MainForm()
        {
            BuildUi();
            uiTimer = new System.Windows.Forms.Timer();
            uiTimer.Interval = 1000;
            uiTimer.Tick += delegate { UpdateCaptureStatus(); };
            this.Load += delegate
            {
                FitToWorkingArea();
                AppendLog("USB POINT MONITOR stable v2 started.");
                AppendLog("Admin: " + IsAdmin());
                AppendLog("Capture engine: direct USBPcapCMD.exe, not tshark -D / extcap.");
                AppendLog("Reason: this avoids etwdump, Npcap, and duplicate extcap plugin failures.");
                RefreshStatus();
            };
            this.FormClosing += delegate { TryStopCaptures(false); };
        }

        private void BuildUi()
        {
            Text = "USB Point Monitor - Stable v2";
            Width = DesignSize.Width;
            Height = DesignSize.Height;
            MinimumSize = DesignMinimumSize;
            StartPosition = FormStartPosition.CenterScreen;

            Panel top = new Panel();
            top.Dock = DockStyle.Top;
            top.Height = 162;
            top.Padding = new Padding(10);
            Controls.Add(top);

            Label title = new Label();
            title.Text = "USB Point Monitor - direct USBPcapCMD capture, one UI, no log popups";
            title.Font = new Font(Font.FontFamily, 11f, FontStyle.Bold);
            title.AutoSize = true;
            title.Left = 10;
            title.Top = 8;
            top.Controls.Add(title);

            Label instructions = new Label();
            instructions.Text = "Flow: Refresh Status -> Start Capture -> unplug/replug dongle -> type tests -> let backlight sleep -> toggle Caps/Num/Scroll from another keyboard -> Stop + Analyze -> Copy Log.";
            instructions.AutoSize = true;
            instructions.Left = 10;
            instructions.Top = 34;
            instructions.Width = 1160;
            top.Controls.Add(instructions);

            Label slotLabel = new Label();
            slotLabel.Text = "USBPcap slot:";
            slotLabel.AutoSize = true;
            slotLabel.Left = 10;
            slotLabel.Top = 66;
            top.Controls.Add(slotLabel);

            slotCombo = new ComboBox();
            slotCombo.DropDownStyle = ComboBoxStyle.DropDownList;
            slotCombo.Left = 98;
            slotCombo.Top = 62;
            slotCombo.Width = 170;
            top.Controls.Add(slotCombo);
            for (int i = 1; i <= 16; i++) slotCombo.Items.Add("USBPcap" + i);
            slotCombo.SelectedIndex = 0;

            allSlots = new CheckBox();
            allSlots.Text = "Try/capture ALL slots 1..";
            allSlots.Checked = true;
            allSlots.AutoSize = true;
            allSlots.Left = 288;
            allSlots.Top = 65;
            top.Controls.Add(allSlots);

            maxSlotsBox = new NumericUpDown();
            maxSlotsBox.Minimum = 1;
            maxSlotsBox.Maximum = 32;
            maxSlotsBox.Value = 16;
            maxSlotsBox.Left = 435;
            maxSlotsBox.Top = 62;
            maxSlotsBox.Width = 55;
            top.Controls.Add(maxSlotsBox);

            Label slotHint = new Label();
            slotHint.Text = "Unknown slots just exit; alive slots are the real USB root hubs.";
            slotHint.AutoSize = true;
            slotHint.Left = 505;
            slotHint.Top = 66;
            top.Controls.Add(slotHint);

            refreshButton = MakeButton("Refresh Status", 10, 104, 115);
            refreshButton.Click += delegate { RefreshStatus(); };
            top.Controls.Add(refreshButton);

            diagButton = MakeButton("Diagnostics", 132, 104, 95);
            diagButton.Click += delegate { RunDiagnostics(); };
            top.Controls.Add(diagButton);

            startButton = MakeButton("Start Capture", 234, 104, 110);
            startButton.Click += delegate { StartCapture(); };
            top.Controls.Add(startButton);

            stopButton = MakeButton("Stop + Analyze", 352, 104, 125);
            stopButton.Enabled = false;
            stopButton.Click += delegate { StopAndAnalyze(); };
            top.Controls.Add(stopButton);

            killButton = MakeButton("Kill Stuck", 485, 104, 90);
            killButton.Click += delegate { KillStuckCaptureProcesses(); };
            top.Controls.Add(killButton);

            copyButton = MakeButton("Copy Log", 583, 104, 90);
            copyButton.Click += delegate { CopyLog(); };
            top.Controls.Add(copyButton);

            openButton = MakeButton("Open Folder", 681, 104, 100);
            openButton.Click += delegate { OpenSessionFolder(); };
            top.Controls.Add(openButton);

            clearButton = MakeButton("Clear", 789, 104, 70);
            clearButton.Click += delegate { logBox.Clear(); };
            top.Controls.Add(clearButton);

            statusLabel = new Label();
            statusLabel.Text = "Idle";
            statusLabel.AutoSize = false;
            statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            statusLabel.Left = 870;
            statusLabel.Top = 106;
            statusLabel.Width = 300;
            statusLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            top.Controls.Add(statusLabel);

            SplitContainer split = new SplitContainer();
            split.Dock = DockStyle.Fill;
            split.Orientation = Orientation.Horizontal;
            split.SplitterDistance = 145;
            Controls.Add(split);
            split.BringToFront();

            Panel testPanel = new Panel();
            testPanel.Dock = DockStyle.Fill;
            testPanel.Padding = new Padding(10, 6, 10, 10);
            split.Panel1.Controls.Add(testPanel);

            Label testLabel = new Label();
            testLabel.Text = "Test pad - click here and type known keys. KeyDown/KeyPress events get timestamped in the log. Do not type passwords.";
            testLabel.Dock = DockStyle.Top;
            testLabel.Height = 24;
            testPanel.Controls.Add(testLabel);

            testPad = new TextBox();
            testPad.Multiline = true;
            testPad.ScrollBars = ScrollBars.Vertical;
            testPad.Dock = DockStyle.Fill;
            testPad.Font = new Font("Consolas", 10f);
            testPanel.Controls.Add(testPad);
            testPad.BringToFront();
            testPad.KeyDown += TestPadKeyDown;
            testPad.KeyPress += TestPadKeyPress;

            Panel logPanel = new Panel();
            logPanel.Dock = DockStyle.Fill;
            logPanel.Padding = new Padding(10, 6, 10, 10);
            split.Panel2.Controls.Add(logPanel);

            Label logLabel = new Label();
            logLabel.Text = "Live log / analyzer output:";
            logLabel.Dock = DockStyle.Top;
            logLabel.Height = 24;
            logPanel.Controls.Add(logLabel);

            logBox = new TextBox();
            logBox.Multiline = true;
            logBox.ScrollBars = ScrollBars.Both;
            logBox.WordWrap = false;
            logBox.ReadOnly = true;
            logBox.Dock = DockStyle.Fill;
            logBox.Font = new Font("Consolas", 9f);
            logPanel.Controls.Add(logBox);
            logBox.BringToFront();
        }

        // Keeps the whole window above the taskbar: inside the work area of the monitor it opens on.
        private void FitToWorkingArea()
        {
            Rectangle workArea = Screen.FromControl(this).WorkingArea;
            MinimumSize = new Size(Math.Min(DesignMinimumSize.Width, workArea.Width), Math.Min(DesignMinimumSize.Height, workArea.Height));
            Bounds = FitBounds(workArea, DesignSize);
        }

        // The preferred size, shrunk where needed to fit inside the work area, centered in it.
        internal static Rectangle FitBounds(Rectangle workArea, Size preferred)
        {
            int width = Math.Min(preferred.Width, workArea.Width);
            int height = Math.Min(preferred.Height, workArea.Height);
            return new Rectangle(
                workArea.X + (workArea.Width - width) / 2,
                workArea.Y + (workArea.Height - height) / 2,
                width,
                height);
        }

        private Button MakeButton(string text, int left, int top, int width)
        {
            Button b = new Button();
            b.Text = text;
            b.Left = left;
            b.Top = top;
            b.Width = width;
            b.Height = 28;
            return b;
        }

        private void TestPadKeyDown(object sender, KeyEventArgs e)
        {
            AppendLog("TESTPAD KeyDown key=" + e.KeyCode + " keyData=" + e.KeyData + " modifiers=" + e.Modifiers);
        }

        private void TestPadKeyPress(object sender, KeyPressEventArgs e)
        {
            string desc;
            if (char.IsControl(e.KeyChar)) desc = "control 0x" + ((int)e.KeyChar).ToString("X2");
            else desc = "'" + e.KeyChar + "' 0x" + ((int)e.KeyChar).ToString("X2");
            AppendLog("TESTPAD KeyPress char=" + desc);
        }

        private void AppendLog(string message)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(AppendLog), message);
                return;
            }
            string line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + message + Environment.NewLine;
            logBox.AppendText(line);
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action<string>(SetStatus), text);
                return;
            }
            statusLabel.Text = text;
        }

        private static bool IsAdmin()
        {
            try
            {
                WindowsIdentity id = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(id);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        private void RefreshStatus()
        {
            SetStatus("Refreshing...");
            tsharkPath = FindTshark();
            usbpcapPath = FindUsbPcapCmd();

            AppendLog("TShark path: " + NullText(tsharkPath));
            AppendLog("USBPcapCMD path: " + NullText(usbpcapPath));
            AppendLog("Admin: " + IsAdmin());

            if (usbpcapPath == null)
            {
                AppendLog("ERROR: USBPcapCMD.exe not found. Run 1_INSTALL_DEPS_ONCE.cmd, then reboot if USBPcap was installed.");
            }
            if (tsharkPath == null)
            {
                AppendLog("WARNING: tshark.exe not found. Capture can start, but Stop + Analyze cannot decode PCAPs until Wireshark/TShark is installed.");
            }

            int dupeCount = CountExtcapCopies();
            AppendLog("Wireshark extcap USBPcapCMD copy count: " + dupeCount + " (direct capture does not need extcap; duplicate copies only hurt tshark -D/Wireshark UI). ");
            if (dupeCount > 1)
            {
                AppendLog("WARNING: duplicate USBPcapCMD extcap copies detected. Run 1_INSTALL_DEPS_ONCE.cmd or scripts\\repair_extcap_dupes.ps1 to dedupe.");
            }

            SetStatus(usbpcapPath == null ? "USBPcap missing" : "Ready");
            SavePasteLog();
        }

        private void RunDiagnostics()
        {
            AppendLog("===== DIAGNOSTICS START =====");
            AppendLog("Admin: " + IsAdmin());
            AppendLog("OS: " + Environment.OSVersion.VersionString);
            AppendLog("App base: " + AppDomain.CurrentDomain.BaseDirectory);
            AppendLog("Session folder: " + NullText(sessionFolder));
            tsharkPath = FindTshark();
            usbpcapPath = FindUsbPcapCmd();
            AppendLog("TShark path: " + NullText(tsharkPath));
            AppendLog("USBPcapCMD path: " + NullText(usbpcapPath));

            if (usbpcapPath != null)
            {
                AppendLimited("USBPcapCMD.exe -h", RunProcess(usbpcapPath, "-h", 15000), 120);
                AppendLog("Direct capture probe method: spawn USBPcapCMD.exe -d \\\\.\\USBPcapN -A -o file.pcap for N=1..max.");
                AppendLog("A real root hub slot stays alive while capture runs; a missing slot exits quickly.");
            }
            if (tsharkPath != null)
            {
                AppendLimited("tshark -v", RunProcess(tsharkPath, "-v", 30000), 40);
                AppendLimited("tshark -D (diagnostic only; NOT used for capture)", RunProcess(tsharkPath, "-D", 30000), 80);
            }
            AppendLog("Known Wireshark extcap dirs and USBPcapCMD copies:");
            foreach (string d in GetExtcapDirs())
            {
                AppendLog("  " + d + " exists=" + Directory.Exists(d));
                try
                {
                    if (Directory.Exists(d))
                    {
                        foreach (string f in Directory.GetFiles(d, "USBPcapCMD*", SearchOption.TopDirectoryOnly))
                        {
                            FileInfo fi = new FileInfo(f);
                            AppendLog("    " + fi.Name + " size=" + fi.Length + " modified=" + fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
                        }
                    }
                }
                catch (Exception ex) { AppendLog("    list failed: " + ex.Message); }
            }
            AppendLog("===== DIAGNOSTICS END =====");
            SavePasteLog();
            SetStatus("Diagnostics complete");
        }

        private void AppendLimited(string title, ProcResult r, int maxLines)
        {
            AppendLog("--- " + title + " exit=" + r.ExitCode + " timeout=" + r.TimedOut + " ---");
            string combined = (r.StdOut ?? "") + Environment.NewLine + (r.StdErr ?? "");
            string[] lines = SplitLines(combined);
            int shown = 0;
            foreach (string line in lines)
            {
                if (line.Trim().Length == 0) continue;
                if (shown >= maxLines)
                {
                    AppendLog("... output clipped after " + maxLines + " non-empty lines ...");
                    break;
                }
                AppendLog(line);
                shown++;
            }
        }

        private void StartCapture()
        {
            if (captureActive)
            {
                AppendLog("Capture already active.");
                return;
            }
            usbpcapPath = FindUsbPcapCmd();
            tsharkPath = FindTshark();
            if (!IsAdmin())
            {
                AppendLog("WARNING: not running as Administrator. 3_RUN_MONITOR.cmd should elevate; direct USBPcap capture may fail without admin.");
            }
            if (usbpcapPath == null)
            {
                AppendLog("Cannot start: USBPcapCMD.exe not found. Run 1_INSTALL_DEPS_ONCE.cmd, reboot if newly installed, then try again.");
                return;
            }

            List<int> targets = new List<int>();
            if (allSlots.Checked)
            {
                int max = (int)maxSlotsBox.Value;
                for (int i = 1; i <= max; i++) targets.Add(i);
            }
            else
            {
                targets.Add(slotCombo.SelectedIndex + 1);
            }

            sessionFolder = null;
            sessionFolder = CreateSessionFolder();
            AppendLog("===== CAPTURE START =====");
            AppendLog("Session folder: " + sessionFolder);
            AppendLog("USBPcapCMD: " + usbpcapPath);
            AppendLog("TShark for analysis: " + NullText(tsharkPath));
            AppendLog("Recommended sequence: unplug/replug dongle; type A/B/Shift/Ctrl/Caps; let backlight sleep; toggle Caps/Num/Scroll from another keyboard.");
            AppendLog("Starting direct USBPcap slots: " + JoinInts(targets));

            lock (captureLock)
            {
                captures.Clear();
                foreach (int slot in targets)
                {
                    StartOneCapture(slot);
                }
            }

            Thread.Sleep(1200);
            int alive = CountAliveCaptures();
            AppendLog("Initial capture alive count after probe delay: " + alive + " / " + captures.Count);
            if (alive == 0)
            {
                AppendLog("ERROR: No USBPcap slots stayed alive. Most likely USBPcap driver needs install/reboot, or the driver is blocked.");
                AppendLog("Next move: run 1_INSTALL_DEPS_ONCE.cmd, reboot Windows, then run 2_CHECK_DEPS_ONLY.cmd.");
            }

            captureActive = alive > 0;
            uiTimer.Start();
            startButton.Enabled = !captureActive;
            stopButton.Enabled = captureActive;
            SetStatus(captureActive ? "Capturing alive=" + alive : "No live slots");
            SavePasteLog();
        }

        private void StartOneCapture(int slot)
        {
            string device = "\\\\.\\USBPcap" + slot;
            try
            {
                string file = Path.Combine(sessionFolder, "USBPcap" + slot + ".pcap");
                string args = "-d " + device + " -A -o \"" + file + "\"";
                AppendLog("Starting USBPcap slot " + slot + ": " + usbpcapPath + " " + args);

                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = usbpcapPath;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;

                Process p = new Process();
                p.StartInfo = psi;
                p.EnableRaisingEvents = true;
                p.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendLog("USBPcap" + slot + " OUT: " + e.Data);
                };
                p.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) AppendLog("USBPcap" + slot + " ERR: " + e.Data);
                };
                p.Exited += delegate
                {
                    int code = -9999;
                    try { code = p.ExitCode; } catch { }
                    AppendLog("USBPcap" + slot + " exited code=" + code);
                };

                if (!p.Start())
                {
                    AppendLog("Failed to start USBPcap slot " + slot);
                    return;
                }
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();

                CaptureRun run = new CaptureRun();
                run.Slot = slot;
                run.DeviceName = device;
                run.Process = p;
                run.PcapPath = file;
                run.Started = DateTime.Now;
                run.StartedOk = true;
                captures.Add(run);
                AppendLog("USBPcap" + slot + " PID=" + p.Id + " file=" + file);
            }
            catch (Exception ex)
            {
                AppendLog("ERROR starting USBPcap" + slot + ": " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private int CountAliveCaptures()
        {
            int alive = 0;
            lock (captureLock)
            {
                foreach (CaptureRun run in captures)
                {
                    try { if (run.Process != null && !run.Process.HasExited) alive++; } catch { }
                }
            }
            return alive;
        }

        private void StopAndAnalyze()
        {
            if (!captureActive && captures.Count == 0)
            {
                AppendLog("No capture processes to stop.");
                return;
            }
            startButton.Enabled = false;
            stopButton.Enabled = false;
            SetStatus("Stopping/analyzing...");
            ThreadPool.QueueUserWorkItem(delegate
            {
                TryStopCaptures(true);
                AnalyzeCaptures();
                SavePasteLog();
                Ui(delegate
                {
                    startButton.Enabled = true;
                    stopButton.Enabled = false;
                });
                SetStatus("Stopped + analyzed");
            });
        }

        private void TryStopCaptures(bool log)
        {
            lock (captureLock)
            {
                if (captures.Count == 0) return;
                if (log) AppendLog("===== STOPPING CAPTURE =====");
                foreach (CaptureRun run in captures)
                {
                    try
                    {
                        Process p = run.Process;
                        if (p == null) continue;
                        if (!p.HasExited)
                        {
                            if (log) AppendLog("Stopping USBPcap" + run.Slot + " PID=" + p.Id + " using q on stdin...");
                            try { p.StandardInput.WriteLine("q"); p.StandardInput.Flush(); } catch { }
                            if (!p.WaitForExit(2500))
                            {
                                if (log) AppendLog("USBPcap" + run.Slot + " did not exit on q; killing PID=" + p.Id);
                                try { p.Kill(); } catch { }
                                try { p.WaitForExit(2500); } catch { }
                            }
                        }
                        if (log) AppendLog("Stopped USBPcap" + run.Slot + " PID=" + SafePid(p) + " exit=" + SafeExit(p));
                    }
                    catch (Exception ex)
                    {
                        if (log) AppendLog("Stop error: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
                captureActive = false;
                try { uiTimer.Stop(); } catch { }
            }
        }

        private void KillStuckCaptureProcesses()
        {
            AppendLog("===== KILL STUCK CAPTURE PROCESSES =====");
            TryStopCaptures(true);
            KillProcessesByName("USBPcapCMD");
            SetStatus("Kill attempt done");
        }

        private void KillProcessesByName(string name)
        {
            try
            {
                foreach (Process p in Process.GetProcessesByName(name))
                {
                    try
                    {
                        AppendLog("Killing " + name + " PID=" + p.Id);
                        p.Kill();
                    }
                    catch (Exception ex) { AppendLog("Kill failed PID=" + p.Id + ": " + ex.Message); }
                }
            }
            catch (Exception ex) { AppendLog("Process scan failed: " + ex.Message); }
        }

        private string SafePid(Process p)
        {
            try { return p.Id.ToString(); } catch { return "?"; }
        }

        private string SafeExit(Process p)
        {
            try { return p.ExitCode.ToString(); } catch { return "?"; }
        }

        private void AnalyzeCaptures()
        {
            AppendLog("===== ANALYSIS START =====");
            tsharkPath = FindTshark();
            if (tsharkPath == null)
            {
                AppendLog("Cannot decode PCAPs: tshark.exe not found. Copy this log anyway; file sizes still matter.");
                AppendFileList();
                AppendLog("===== ANALYSIS END =====");
                return;
            }

            List<CaptureRun> snapshot;
            lock (captureLock) snapshot = new List<CaptureRun>(captures);
            foreach (CaptureRun run in snapshot)
            {
                AnalyzeOne(run);
            }
            AppendFileList();
            AppendLog("===== ANALYSIS END =====");
        }

        private void AnalyzeOne(CaptureRun run)
        {
            AppendLog("--- PCAP USBPcap" + run.Slot + " ---");
            AppendLog("PCAP path: " + run.PcapPath);
            if (!File.Exists(run.PcapPath))
            {
                AppendLog("PCAP missing.");
                return;
            }
            long size = 0;
            try { size = new FileInfo(run.PcapPath).Length; } catch { }
            AppendLog("PCAP size bytes: " + size);
            if (size == 0)
            {
                AppendLog("PCAP is empty. This slot probably was not a valid USBPcap root hub or capture failed.");
                return;
            }

            AppendLimited("Protocol hierarchy", RunProcess(tsharkPath, "-r \"" + run.PcapPath + "\" -q -z io,phs", 60000), 80);

            string fieldArgs = "-r \"" + run.PcapPath + "\" -c 500 -T fields -E header=y -E separator=, -E quote=d " +
                "-e frame.number -e frame.time_relative -e frame.len -e _ws.col.Source -e _ws.col.Destination -e _ws.col.Protocol -e _ws.col.Info " +
                "-e usb.src -e usb.dst -e usb.device_address -e usb.endpoint_address -e usb.endpoint_number -e usb.endpoint_address.direction -e usb.transfer_type -e usb.urb_type " +
                "-e usb.setup.bmRequestType -e usb.setup.bRequest -e usb.setup.wValue -e usb.setup.wIndex -e usb.setup.wLength -e usb.capdata -e usbhid.data";
            AppendLimited("First 500 USB/HID-ish field rows", RunProcess(tsharkPath, fieldArgs, 60000), 160);

            string payloadArgs = "-r \"" + run.PcapPath + "\" -Y \"usb.capdata || usbhid.data\" -c 250 -T fields -E header=y -E separator=, -E quote=d " +
                "-e frame.number -e frame.time_relative -e frame.len -e _ws.col.Source -e _ws.col.Destination -e _ws.col.Info -e usb.src -e usb.dst -e usb.device_address -e usb.endpoint_address -e usb.capdata -e usbhid.data";
            AppendLimited("Payload candidate rows: usb.capdata/usbhid.data", RunProcess(tsharkPath, payloadArgs, 60000), 160);

            string hostOutArgs = "-r \"" + run.PcapPath + "\" -Y \"(usb.dst == host || usb.src == host || usb.capdata || usbhid.data || usb.setup.bRequest)\" -c 300 -T fields -E header=y -E separator=, -E quote=d " +
                "-e frame.number -e frame.time_relative -e frame.len -e _ws.col.Source -e _ws.col.Destination -e _ws.col.Protocol -e _ws.col.Info -e usb.src -e usb.dst -e usb.setup.bRequest -e usb.setup.wValue -e usb.capdata -e usbhid.data";
            AppendLimited("Host/device direction candidate rows", RunProcess(tsharkPath, hostOutArgs, 60000), 180);
        }

        private void AppendFileList()
        {
            AppendLog("--- Session files ---");
            try
            {
                if (sessionFolder != null && Directory.Exists(sessionFolder))
                {
                    foreach (string f in Directory.GetFiles(sessionFolder))
                    {
                        FileInfo fi = new FileInfo(f);
                        AppendLog("FILE " + fi.Name + " bytes=" + fi.Length + " path=" + fi.FullName);
                    }
                }
            }
            catch (Exception ex) { AppendLog("File list failed: " + ex.Message); }
        }

        private void UpdateCaptureStatus()
        {
            if (!captureActive) return;
            StringBuilder sb = new StringBuilder();
            int alive = 0;
            lock (captureLock)
            {
                foreach (CaptureRun run in captures)
                {
                    bool isAlive = false;
                    try { isAlive = run.Process != null && !run.Process.HasExited; } catch { }
                    if (isAlive) alive++;
                    long size = 0;
                    try { if (File.Exists(run.PcapPath)) size = new FileInfo(run.PcapPath).Length; } catch { }
                    if (isAlive || size > 0) sb.Append("USBPcap").Append(run.Slot).Append(" ").Append(size).Append("B ");
                }
            }
            SetStatus("Capturing alive=" + alive + " " + sb.ToString());
        }

        private void SavePasteLog()
        {
            try
            {
                if (sessionFolder == null) sessionFolder = CreateSessionFolder();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(logBox.Text);
                sb.AppendLine("---- TEST PAD CONTENT ----");
                sb.AppendLine(testPad.Text);
                File.WriteAllText(Path.Combine(sessionFolder, "paste_this_log.txt"), sb.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                AppendLog("Save log failed: " + ex.Message);
            }
        }

        private void CopyLog()
        {
            try
            {
                SavePasteLog();
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(logBox.Text);
                sb.AppendLine("---- TEST PAD CONTENT ----");
                sb.AppendLine(testPad.Text);
                Clipboard.SetText(sb.ToString());
                SetStatus("Log copied to clipboard");
                AppendLog("Copied log to clipboard.");
            }
            catch (Exception ex)
            {
                AppendLog("Clipboard copy failed: " + ex.GetType().Name + ": " + ex.Message);
                SetStatus("Clipboard failed");
            }
        }

        private void OpenSessionFolder()
        {
            try
            {
                if (sessionFolder == null) sessionFolder = CreateSessionFolder();
                Process.Start(sessionFolder);
            }
            catch (Exception ex)
            {
                AppendLog("Open folder failed: " + ex.Message);
            }
        }

        private string CreateSessionFolder()
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (String.IsNullOrEmpty(desktop)) desktop = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop");
            string root = Path.Combine(desktop, "usb_point_monitor_logs");
            Directory.CreateDirectory(root);
            if (String.IsNullOrEmpty(sessionFolder))
            {
                sessionFolder = Path.Combine(root, "session_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                Directory.CreateDirectory(sessionFolder);
            }
            return sessionFolder;
        }

        private string FindTshark()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, Environment.GetEnvironmentVariable("USBPM_TSHARK"));
            AddCandidate(candidates, @"C:\Program Files\Wireshark\tshark.exe");
            AddCandidate(candidates, @"C:\Program Files (x86)\Wireshark\tshark.exe");
            AddCandidate(candidates, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Wireshark\tshark.exe"));
            foreach (string c in candidates) if (File.Exists(c)) return c;
            ProcResult where = RunProcess("where.exe", "tshark.exe", 5000);
            foreach (string line in SplitLines(where.StdOut))
            {
                string p = line.Trim();
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private string FindUsbPcapCmd()
        {
            List<string> candidates = new List<string>();
            AddCandidate(candidates, Environment.GetEnvironmentVariable("USBPM_USBPCAPCMD"));
            AddCandidate(candidates, @"C:\Program Files\USBPcap\USBPcapCMD.exe");
            AddCandidate(candidates, @"C:\Program Files (x86)\USBPcap\USBPcapCMD.exe");
            foreach (string d in GetExtcapDirs()) AddCandidate(candidates, Path.Combine(d, "USBPcapCMD.exe"));
            foreach (string c in candidates) if (File.Exists(c)) return c;
            ProcResult where = RunProcess("where.exe", "USBPcapCMD.exe", 5000);
            foreach (string line in SplitLines(where.StdOut))
            {
                string p = line.Trim();
                if (File.Exists(p)) return p;
            }
            return null;
        }

        private string[] GetExtcapDirs()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return new string[]
            {
                @"C:\Program Files\Wireshark\extcap",
                @"C:\Program Files\Wireshark\extcap\wireshark",
                @"C:\Program Files (x86)\Wireshark\extcap",
                @"C:\Program Files (x86)\Wireshark\extcap\wireshark",
                Path.Combine(appData, @"Wireshark\extcap"),
                Path.Combine(appData, @"Wireshark\extcap\wireshark")
            };
        }

        private int CountExtcapCopies()
        {
            int count = 0;
            foreach (string d in GetExtcapDirs())
            {
                try
                {
                    string f = Path.Combine(d, "USBPcapCMD.exe");
                    if (File.Exists(f)) count++;
                }
                catch { }
            }
            return count;
        }

        private void AddCandidate(List<string> list, string path)
        {
            if (String.IsNullOrWhiteSpace(path)) return;
            if (!list.Contains(path)) list.Add(path);
        }

        private ProcResult RunProcess(string fileName, string args, int timeoutMs)
        {
            ProcResult result = new ProcResult();
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo();
                psi.FileName = fileName;
                psi.Arguments = args;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    StringBuilder stdout = new StringBuilder();
                    StringBuilder stderr = new StringBuilder();
                    Thread t1 = new Thread(delegate() { try { stdout.Append(p.StandardOutput.ReadToEnd()); } catch { } });
                    Thread t2 = new Thread(delegate() { try { stderr.Append(p.StandardError.ReadToEnd()); } catch { } });
                    t1.Start();
                    t2.Start();
                    if (!p.WaitForExit(timeoutMs))
                    {
                        result.TimedOut = true;
                        try { p.Kill(); } catch { }
                    }
                    try { t1.Join(1500); } catch { }
                    try { t2.Join(1500); } catch { }
                    try { result.ExitCode = p.ExitCode; } catch { result.ExitCode = -999; }
                    result.StdOut = stdout.ToString();
                    result.StdErr = stderr.ToString();
                }
            }
            catch (Exception ex)
            {
                result.ExitCode = -998;
                result.StdErr = ex.GetType().Name + ": " + ex.Message;
            }
            return result;
        }

        private string[] SplitLines(string s)
        {
            if (s == null) return new string[0];
            return s.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
        }

        private string NullText(string s)
        {
            return s == null ? "<not found>" : s;
        }

        private string JoinInts(List<int> values)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < values.Count; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append(values[i]);
            }
            return sb.ToString();
        }

        private void Ui(Action action)
        {
            if (InvokeRequired) BeginInvoke(action);
            else action();
        }
    }
}
