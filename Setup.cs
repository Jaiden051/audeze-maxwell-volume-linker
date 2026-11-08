using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

[assembly: AssemblyTitle("Audeze Maxwell Volume Linker Setup")]
[assembly: AssemblyDescription("Installer for Audeze Maxwell Volume Linker")]
[assembly: AssemblyProduct("Audeze Maxwell Volume Linker")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace VolumeLinkerSetup
{
    internal static class NativeMethods
    {
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr handle);
    }

    internal static class SetupEngine
    {
        public const string AppName = "Audeze Maxwell Volume Linker";
        public const string Publisher = "Audeze Maxwell Community Tools";
        public const string Version = "1.0.0";
        public const string ExeName = "VolumeLinker.exe";
        public const string StartupKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public const string StartupValueName = "AudezeMaxwellVolumeLinker";
        public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\AudezeMaxwellVolumeLinker";
        public const string AppSettingsKeyPath = @"Software\AudezeMaxwellVolumeLinker";

        public static string DefaultInstallDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AudezeMaxwellVolumeLinker");
            }
        }

        public static bool IsInstalled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, false))
                {
                    if (key != null)
                    {
                        string loc = key.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc) && File.Exists(Path.Combine(loc, ExeName)))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return File.Exists(Path.Combine(DefaultInstallDir, ExeName));
        }

        public static void StopRunningProcesses()
        {
            try
            {
                int currentPid = Process.GetCurrentProcess().Id;
                foreach (Process proc in Process.GetProcessesByName("VolumeLinker"))
                {
                    if (proc.Id != currentPid)
                    {
                        try
                        {
                            proc.Kill();
                            proc.WaitForExit(3000);
                        }
                        catch { }
                    }
                    proc.Dispose();
                }
            }
            catch { }
        }

        public static string Install(string installDir, bool startOnLogin, bool createStartMenuShortcut, bool createDesktopShortcut, bool launchNow)
        {
            StopRunningProcesses();

            if (!Directory.Exists(installDir))
            {
                Directory.CreateDirectory(installDir);
            }

            string targetExe = Path.Combine(installDir, ExeName);
            string uninstallerExe = Path.Combine(installDir, "Uninstall.exe");

            // Extract embedded VolumeLinker.exe resource
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream resStream = asm.GetManifestResourceStream("VolumeLinker.exe"))
            {
                if (resStream == null)
                {
                    throw new InvalidOperationException("Embedded payload 'VolumeLinker.exe' was not found.");
                }
                using (FileStream fs = new FileStream(targetExe, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    resStream.CopyTo(fs);
                }
            }

            // Copy setup executable as Uninstall.exe so user can uninstall anytime
            string currentSetupPath = Application.ExecutablePath;
            if (!string.Equals(Path.GetFullPath(currentSetupPath), Path.GetFullPath(uninstallerExe), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(currentSetupPath, uninstallerExe, true);
            }

            // Configure Run at Startup
            using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true))
            {
                if (runKey != null)
                {
                    if (startOnLogin)
                    {
                        runKey.SetValue(StartupValueName, "\"" + targetExe + "\"");
                    }
                    else
                    {
                        runKey.DeleteValue(StartupValueName, false);
                    }
                }
            }

            // Mark first-run startup initialized so VolumeLinker respects user's choice in installer
            using (RegistryKey settingsKey = Registry.CurrentUser.CreateSubKey(AppSettingsKeyPath))
            {
                if (settingsKey != null)
                {
                    settingsKey.SetValue("InitializedStartup", 1, RegistryValueKind.DWord);
                }
            }

            // Shortcuts
            if (createStartMenuShortcut)
            {
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    AppName);
                if (!Directory.Exists(startMenuDir))
                {
                    Directory.CreateDirectory(startMenuDir);
                }
                CreateShortcut(
                    Path.Combine(startMenuDir, AppName + ".lnk"),
                    targetExe,
                    installDir,
                    "Automatically links Audeze Maxwell Game and Chat audio streams");
                CreateShortcut(
                    Path.Combine(startMenuDir, "Uninstall " + AppName + ".lnk"),
                    uninstallerExe,
                    installDir,
                    "Uninstall " + AppName,
                    "--uninstall");
            }

            if (createDesktopShortcut)
            {
                string desktopDir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                CreateShortcut(
                    Path.Combine(desktopDir, AppName + ".lnk"),
                    targetExe,
                    installDir,
                    "Automatically links Audeze Maxwell Game and Chat audio streams");
            }

            // Register in Windows "Apps & features" / "Programs and Features"
            using (RegistryKey uninstallKey = Registry.CurrentUser.CreateSubKey(UninstallKeyPath))
            {
                if (uninstallKey != null)
                {
                    uninstallKey.SetValue("DisplayName", AppName);
                    uninstallKey.SetValue("DisplayVersion", Version);
                    uninstallKey.SetValue("Publisher", Publisher);
                    uninstallKey.SetValue("DisplayIcon", targetExe);
                    uninstallKey.SetValue("InstallLocation", installDir);
                    uninstallKey.SetValue("UninstallString", "\"" + uninstallerExe + "\" --uninstall");
                    uninstallKey.SetValue("QuietUninstallString", "\"" + uninstallerExe + "\" --uninstall --silent");
                    uninstallKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                    uninstallKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
                    long sizeKb = (new FileInfo(targetExe).Length + new FileInfo(uninstallerExe).Length) / 1024;
                    uninstallKey.SetValue("EstimatedSize", (int)Math.Max(sizeKb, 32), RegistryValueKind.DWord);
                }
            }

            if (launchNow)
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = targetExe,
                    WorkingDirectory = installDir,
                    UseShellExecute = true
                });
            }

            return targetExe;
        }

        public static void Uninstall(bool silent)
        {
            StopRunningProcesses();

            string installDir = DefaultInstallDir;
            try
            {
                using (RegistryKey uninstallKey = Registry.CurrentUser.OpenSubKey(UninstallKeyPath, false))
                {
                    if (uninstallKey != null)
                    {
                        string loc = uninstallKey.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc))
                        {
                            installDir = loc;
                        }
                    }
                }
            }
            catch { }

            // Remove Startup registry value
            try
            {
                using (RegistryKey runKey = Registry.CurrentUser.OpenSubKey(StartupKeyPath, true))
                {
                    if (runKey != null)
                    {
                        runKey.DeleteValue(StartupValueName, false);
                    }
                }
            }
            catch { }

            // Remove App Settings & Uninstall registry keys
            try { Registry.CurrentUser.DeleteSubKeyTree(AppSettingsKeyPath, false); } catch { }
            try { Registry.CurrentUser.DeleteSubKeyTree(UninstallKeyPath, false); } catch { }

            // Remove Start Menu & Desktop shortcuts
            try
            {
                string startMenuDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    "Programs",
                    AppName);
                if (Directory.Exists(startMenuDir))
                {
                    Directory.Delete(startMenuDir, true);
                }

                string desktopShortcut = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    AppName + ".lnk");
                if (File.Exists(desktopShortcut))
                {
                    File.Delete(desktopShortcut);
                }
            }
            catch { }

            // Remove target executable and schedule self-cleanup of install folder
            try
            {
                string targetExe = Path.Combine(installDir, ExeName);
                if (File.Exists(targetExe))
                {
                    File.Delete(targetExe);
                }
            }
            catch { }

            if (!silent)
            {
                MessageBox.Show(
                    AppName + " has been uninstalled from your computer.",
                    "Uninstall Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }

            // Self-delete Uninstall.exe and install directory after process exits
            try
            {
                if (Directory.Exists(installDir))
                {
                    string cmdArgs = string.Format("/C ping 127.0.0.1 -n 2 > nul & rmdir /s /q \"{0}\"", installDir);
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = cmdArgs,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true,
                        UseShellExecute = false
                    });
                }
            }
            catch { }
        }

        private static void CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description, string arguments = null)
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return;
                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                Type shortcutType = shortcut.GetType();
                shortcutType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                shortcutType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                shortcutType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });
                if (!string.IsNullOrEmpty(arguments))
                {
                    shortcutType.InvokeMember("Arguments", BindingFlags.SetProperty, null, shortcut, new object[] { arguments });
                }
                shortcutType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
            }
            catch { }
        }

        public static Icon CreateBrandIcon()
        {
            using (var bmp = new Bitmap(48, 48))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);

                using (var bgBrush = new SolidBrush(Color.FromArgb(16, 185, 129)))
                {
                    g.FillEllipse(bgBrush, 2, 2, 44, 44);
                }

                using (var pen = new Pen(Color.White, 3.8f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawArc(pen, 11, 10, 26, 24, 180, 180);

                    using (var cupBrush = new SolidBrush(Color.White))
                    {
                        g.FillRectangle(cupBrush, 9, 21, 6, 12);
                        g.FillRectangle(cupBrush, 33, 21, 6, 12);
                    }

                    g.DrawLine(pen, 18, 27, 30, 27);
                }

                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon temp = Icon.FromHandle(hIcon))
                    {
                        return (Icon)temp.Clone();
                    }
                }
                finally
                {
                    NativeMethods.DestroyIcon(hIcon);
                }
            }
        }
    }

    internal sealed class SetupForm : Form
    {
        private readonly TextBox _pathBox;
        private readonly CheckBox _chkStartup;
        private readonly CheckBox _chkStartMenu;
        private readonly CheckBox _chkDesktop;
        private readonly CheckBox _chkLaunchNow;
        private readonly Button _btnInstall;
        private readonly Button _btnUninstall;
        private readonly Button _btnCancel;
        private readonly Label _statusLabel;
        private readonly Icon _appIcon;

        public SetupForm()
        {
            _appIcon = SetupEngine.CreateBrandIcon();
            Icon = _appIcon;
            Text = SetupEngine.AppName + " Setup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(500, 385);
            BackColor = Color.FromArgb(249, 250, 251);
            Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            // Header Banner
            var headerPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 82,
                BackColor = Color.FromArgb(17, 24, 39)
            };

            var iconBox = new PictureBox
            {
                Image = _appIcon.ToBitmap(),
                SizeMode = PictureBoxSizeMode.StretchImage,
                Location = new Point(22, 17),
                Size = new Size(48, 48)
            };

            var titleLabel = new Label
            {
                Text = "Audeze Maxwell Volume Linker",
                ForeColor = Color.White,
                Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(82, 16)
            };

            var subtitleLabel = new Label
            {
                Text = "Synchronizes Game & Chat volume streams automatically on Windows",
                ForeColor = Color.FromArgb(156, 163, 175),
                Font = new Font("Segoe UI", 9f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(84, 45)
            };

            headerPanel.Controls.Add(iconBox);
            headerPanel.Controls.Add(titleLabel);
            headerPanel.Controls.Add(subtitleLabel);

            // Install Location Section
            var lblLocation = new Label
            {
                Text = "Installation Folder:",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoSize = true,
                Location = new Point(24, 102)
            };

            _pathBox = new TextBox
            {
                Text = SetupEngine.DefaultInstallDir,
                Location = new Point(24, 126),
                Size = new Size(360, 26),
                ReadOnly = false
            };

            var btnBrowse = new Button
            {
                Text = "Browse...",
                Location = new Point(392, 124),
                Size = new Size(84, 28),
                FlatStyle = FlatStyle.System
            };
            btnBrowse.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.SelectedPath = _pathBox.Text;
                    dlg.Description = "Select destination folder for Audeze Maxwell Volume Linker:";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _pathBox.Text = dlg.SelectedPath;
                    }
                }
            };

            // Options Group
            var lblOptions = new Label
            {
                Text = "Setup Options:",
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(31, 41, 55),
                AutoSize = true,
                Location = new Point(24, 170)
            };

            _chkStartup = new CheckBox
            {
                Text = "Start automatically when I log into Windows (Recommended)",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 198)
            };

            _chkStartMenu = new CheckBox
            {
                Text = "Create Start Menu shortcuts",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 226)
            };

            _chkDesktop = new CheckBox
            {
                Text = "Create a Desktop shortcut",
                Checked = false,
                AutoSize = true,
                Location = new Point(28, 254)
            };

            _chkLaunchNow = new CheckBox
            {
                Text = "Launch Audeze Maxwell Volume Linker immediately",
                Checked = true,
                AutoSize = true,
                Location = new Point(28, 282)
            };

            _statusLabel = new Label
            {
                Text = SetupEngine.IsInstalled()
                    ? "An existing installation was detected. Click Update / Reinstall or Uninstall."
                    : "Ready to install. No administrator privileges required.",
                ForeColor = Color.FromArgb(107, 114, 128),
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                AutoSize = true,
                Location = new Point(24, 316)
            };

            // Bottom Divider & Action Buttons
            var divider = new Panel
            {
                BackColor = Color.FromArgb(229, 231, 235),
                Location = new Point(0, 338),
                Size = new Size(500, 1)
            };

            _btnInstall = new Button
            {
                Text = SetupEngine.IsInstalled() ? "Update / Reinstall" : "Install",
                Location = new Point(256, 347),
                Size = new Size(124, 30),
                BackColor = Color.FromArgb(16, 185, 129),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Font = new Font("Segoe UI Semibold", 9.5f, FontStyle.Bold)
            };
            _btnInstall.FlatAppearance.BorderSize = 0;
            _btnInstall.Click += OnInstallClick;

            _btnCancel = new Button
            {
                Text = "Cancel",
                Location = new Point(388, 347),
                Size = new Size(88, 30),
                FlatStyle = FlatStyle.System
            };
            _btnCancel.Click += (s, e) => Close();

            _btnUninstall = new Button
            {
                Text = "Uninstall",
                Location = new Point(24, 347),
                Size = new Size(92, 30),
                FlatStyle = FlatStyle.System,
                Visible = SetupEngine.IsInstalled()
            };
            _btnUninstall.Click += OnUninstallClick;

            Controls.Add(headerPanel);
            Controls.Add(lblLocation);
            Controls.Add(_pathBox);
            Controls.Add(btnBrowse);
            Controls.Add(lblOptions);
            Controls.Add(_chkStartup);
            Controls.Add(_chkStartMenu);
            Controls.Add(_chkDesktop);
            Controls.Add(_chkLaunchNow);
            Controls.Add(_statusLabel);
            Controls.Add(divider);
            Controls.Add(_btnUninstall);
            Controls.Add(_btnInstall);
            Controls.Add(_btnCancel);

            AcceptButton = _btnInstall;
            CancelButton = _btnCancel;
        }

        private void OnInstallClick(object sender, EventArgs e)
        {
            try
            {
                _btnInstall.Enabled = false;
                _statusLabel.Text = "Installing Audeze Maxwell Volume Linker...";
                Application.DoEvents();

                string targetPath = SetupEngine.Install(
                    _pathBox.Text.Trim(),
                    _chkStartup.Checked,
                    _chkStartMenu.Checked,
                    _chkDesktop.Checked,
                    _chkLaunchNow.Checked);

                _statusLabel.ForeColor = Color.FromArgb(5, 150, 105);
                _statusLabel.Text = "Installation completed successfully!";

                MessageBox.Show(
                    this,
                    "Audeze Maxwell Volume Linker has been installed successfully!\n\n" +
                    "Installed to:\n" + targetPath + "\n\n" +
                    (_chkLaunchNow.Checked ? "The app is now running in your Windows system tray." : ""),
                    "Installation Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);

                Close();
            }
            catch (Exception ex)
            {
                _btnInstall.Enabled = true;
                _statusLabel.ForeColor = Color.FromArgb(220, 38, 38);
                _statusLabel.Text = "Installation failed: " + ex.Message;
                MessageBox.Show(
                    this,
                    "Failed to install Audeze Maxwell Volume Linker:\n\n" + ex.Message,
                    "Setup Error",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }

        private void OnUninstallClick(object sender, EventArgs e)
        {
            var confirm = MessageBox.Show(
                this,
                "Are you sure you want to uninstall Audeze Maxwell Volume Linker?",
                "Confirm Uninstall",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm == DialogResult.Yes)
            {
                SetupEngine.Uninstall(false);
                Close();
            }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try { NativeMethods.SetProcessDPIAware(); } catch { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool uninstall = false;
            bool silent = false;

            if (args != null)
            {
                foreach (string arg in args)
                {
                    if (string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "/uninstall", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(arg, "/u", StringComparison.OrdinalIgnoreCase))
                    {
                        uninstall = true;
                    }
                    else if (string.Equals(arg, "--silent", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(arg, "/silent", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(arg, "/s", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(arg, "/quiet", StringComparison.OrdinalIgnoreCase))
                    {
                        silent = true;
                    }
                }
            }

            // Also auto-detect if invoked directly as Uninstall.exe
            string exeName = Path.GetFileName(Application.ExecutablePath);
            if (string.Equals(exeName, "Uninstall.exe", StringComparison.OrdinalIgnoreCase) && !uninstall)
            {
                uninstall = true;
            }

            if (uninstall)
            {
                if (!silent)
                {
                    var confirm = MessageBox.Show(
                        "Are you sure you want to completely remove Audeze Maxwell Volume Linker?",
                        "Uninstall Audeze Maxwell Volume Linker",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (confirm != DialogResult.Yes)
                    {
                        return 0;
                    }
                }
                SetupEngine.Uninstall(silent);
                return 0;
            }

            if (silent)
            {
                SetupEngine.Install(SetupEngine.DefaultInstallDir, true, true, false, true);
                return 0;
            }

            Application.Run(new SetupForm());
            return 0;
        }
    }
}
