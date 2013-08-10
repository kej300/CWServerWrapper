using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Runtime.InteropServices;
using System.Reflection;

namespace CWServerWrapper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private Process ServerConsole;
        private bool serverRunning;
        private bool restartOnCrash;
        private bool autoStart;
        private bool timeStamps;
        private int procID;
        private string procName;
        private DispatcherTimer _heartbeat;

        public MainWindow()
        {
            InitializeComponent();
            serverRunning = false;

            // Setup heartbeat for crash check
            _heartbeat = new DispatcherTimer();
            _heartbeat.Interval = new TimeSpan(0, 0, 0, 1);
            _heartbeat.Tick += new EventHandler(HeartbeatCheck);

            // Setup console
            ServerConsole = new Process();

            ServerConsole.StartInfo.FileName = "Server.exe";
            ServerConsole.StartInfo.WorkingDirectory = System.AppDomain.CurrentDomain.BaseDirectory;
            ServerConsole.StartInfo.RedirectStandardOutput = true;
            ServerConsole.StartInfo.RedirectStandardInput = true;
            ServerConsole.StartInfo.UseShellExecute = false;
            ServerConsole.StartInfo.ErrorDialog = false;
            ServerConsole.StartInfo.CreateNoWindow = true;

            ServerConsole.EnableRaisingEvents = true;
            ServerConsole.OutputDataReceived += new DataReceivedEventHandler(ServerConsole_OutputDataReceived);
            ServerConsole.Exited += new EventHandler(ServerConsole_Exited);

            if (Properties.Settings.Default.AutoStart)
            {
                ConsoleText.Text += "==== Server automatically starting. ====\n";
                StartServer();
            }
        }
        private void HeartbeatCheck(Object sender, EventArgs e)
        {
            try
            {
                // Server crashed?
                Process[] plist = Process.GetProcessesByName(procName);
                if (plist.Length > 0)
                {
                    // Server still alive and kicking
                    //ConsoleText.Text += "Beat.\n";
                }
                else
                {
                    // Crashed, restart
                    ConsoleText.Text += "Crash detected [" + procID + " : " + procName + "]\n";
                    ConsoleText.ScrollToEnd();
                    if (!restartOnCrash)
                        StopAndResetAfterCrash();
                    else RestartServer();
                }
            }
            catch { }
        }
        private void RestartServer()
        {
            ConsoleText.Text += "Restarting...\n";
            ConsoleText.ScrollToEnd();
            using (new ChangeErrorMode(ChangeErrorMode.ErrorModes.FailCriticalErrors |
                ChangeErrorMode.ErrorModes.NoAlignmentFaultExcept |
                ChangeErrorMode.ErrorModes.NoOpenFileErrorBox |
                ChangeErrorMode.ErrorModes.NoGpFaultErrorBox))
            {
                ServerConsole.Start();
                ServerConsole.BeginOutputReadLine();
                procID = ServerConsole.Id;
                procName = ServerConsole.ProcessName;
            }
            serverRunning = true;
            StopServerBtn.IsEnabled = true;
            StartServerBtn.IsEnabled = false;

            // Inject it
            if (procID >= 0)
            {
                IntPtr hProcess = (IntPtr)OpenProcess(0x1F0FFF, 1, procID);
                if (hProcess == null)
                {
                    MessageBox.Show("OpenProcess() Failed!");
                    return;
                }
                else
                    InjectDLL(hProcess, "ServerFix.dll");
            }
        }
        private void StopAndResetAfterCrash()
        {
            ConsoleText.Text += "Resetting...\n";
            ConsoleText.ScrollToEnd();
            _heartbeat.Stop();
            serverRunning = false;
            StopServerBtn.IsEnabled = false;
            StartServerBtn.IsEnabled = true;
        }

        private void ServerConsole_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            Dispatcher.Invoke(new Action(() =>
            {
                if (Properties.Settings.Default.TimeStamps)
                {
                    ConsoleText.Text += DateTime.Now.ToString("[HH:mm:ss] ");
                }
                ConsoleText.Text += e.Data + "\r\n";
                ConsoleText.ScrollToEnd();
            }));
        }
        private void ServerConsole_Exited(object sender, EventArgs e)
        {
            ServerConsole.CancelOutputRead();
            ServerConsole.Close();
        }
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (!File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location), "settings.ini")))
                CreateCWFixerSettingsIni();
            ReadSettings();
        }
        private void CreateCWFixerSettingsIni()
        {
            // Don't have a settings.ini, write a basic one for CWFixer.dll to read from
            IniFile f = new IniFile("settings.ini");
            f.Write("Players", "20", "Settings");
            f.Write("Seed", "73", "Settings");
        }
        private void ReadSettings()
        {
            if (!Properties.Settings.Default.AutoStart) StopServerBtn.IsEnabled = false;
            CommandInput.Focus();
            AutoRestartChk.IsChecked = restartOnCrash = Properties.Settings.Default.AutoRestart;
            AutoStartChk.IsChecked = autoStart = Properties.Settings.Default.AutoStart;
            TimeStampChk.IsChecked = timeStamps = Properties.Settings.Default.TimeStamps;
        }
        private void Window_Closing(object sender, CancelEventArgs e)
        {
            try
            {
                Properties.Settings.Default.Save();
                ServerConsole.StandardInput.WriteLine("q");
                ServerConsole.WaitForExit(1000);
                if (!ServerConsole.HasExited)
                {
                    ConsoleText.Text += "ERROR: The Server doesn't want to Stop!\r\n";
                    e.Cancel = true;
                }
                ServerConsole.CancelOutputRead();
                ServerConsole.Close();
                ServerConsole.Kill();
            }
            catch { }
        }
        private void CommandInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            if (CommandInput.Text.ToString() == "q" && serverRunning)
            {
                _heartbeat.Stop();
                serverRunning = false;
                StopServerBtn.IsEnabled = false;
                StartServerBtn.IsEnabled = true;
                ServerConsole.StandardInput.WriteLine(CommandInput.Text);
                ConsoleText.Text += CommandInput.Text + "\r\n";
            }
            CommandInput.Clear();
        }

        private void StartServerBtn_Click(object sender, RoutedEventArgs e)
        {
            StartServer();
        }
        private void StartServer()
        {
            try { var x = ServerConsole.StartTime; return; }
            catch { }
            ReadSettings();

            using (new ChangeErrorMode(ChangeErrorMode.ErrorModes.FailCriticalErrors | ChangeErrorMode.ErrorModes.NoAlignmentFaultExcept |
                ChangeErrorMode.ErrorModes.NoOpenFileErrorBox |
                ChangeErrorMode.ErrorModes.NoGpFaultErrorBox))
            {
                ServerConsole.Start();
                ServerConsole.BeginOutputReadLine();
                procID = ServerConsole.Id;
                procName = ServerConsole.ProcessName;
            }
            _heartbeat.Start();
            serverRunning = true;
            StopServerBtn.IsEnabled = true;
            StartServerBtn.IsEnabled = false;

            // Inject it
            if (procID >= 0)
            {
                IntPtr hProcess = (IntPtr)OpenProcess(0x1F0FFF, 1, procID);
                if (hProcess == null)
                {
                    MessageBox.Show("OpenProcess() Failed!");
                    return;
                }
                else
                    InjectDLL(hProcess, "ServerFix.dll");
            }
        }
        private void StopServerBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (serverRunning)
                {
                    ServerConsole.StandardInput.WriteLine("q");
                    _heartbeat.Stop();
                    serverRunning = false;
                    StopServerBtn.IsEnabled = false;
                    StartServerBtn.IsEnabled = true;
                }
            }
            catch { }
        }
        private void KillServerBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (serverRunning)
                {
                    ConsoleText.Text += "\n==== Server process forcibly killed. ====\n";
                    ServerConsole.Kill();
                    _heartbeat.Stop();
                    serverRunning = false;
                    StopServerBtn.IsEnabled = false;
                    StartServerBtn.IsEnabled = true;
                }
            }
            catch { }
        }

        private void SendInput(object sender, RoutedEventArgs e)
        {
            try
            {
                if (CommandInput.Text.ToString() == "q" && serverRunning)
                {
                    _heartbeat.Stop();
                    serverRunning = false;
                    StopServerBtn.IsEnabled = false;
                    StartServerBtn.IsEnabled = true;
                }
                ServerConsole.StandardInput.WriteLine(CommandInput.Text);
                ConsoleText.Text += CommandInput.Text + "\r\n";
                CommandInput.Clear();
            }
            catch { }
        }

        private void AutoRestartChk_Checked(object sender, RoutedEventArgs e)
        {
            restartOnCrash = Properties.Settings.Default.AutoRestart = true;
        }
        private void AutoRestartChk_Unchecked(object sender, RoutedEventArgs e)
        {
            restartOnCrash = Properties.Settings.Default.AutoRestart = false;
        }

        private void AutoStartChk_Checked(object sender, RoutedEventArgs e)
        {
            autoStart = Properties.Settings.Default.AutoStart = true;
        }
        private void AutoStartChk_Unchecked(object sender, RoutedEventArgs e)
        {
            autoStart = Properties.Settings.Default.AutoStart = false;
        }

        private void TimeStampChk_Checked(object sender, RoutedEventArgs e)
        {
            timeStamps = Properties.Settings.Default.TimeStamps = true;
        }
        private void TimeStampChk_Unchecked(object sender, RoutedEventArgs e)
        {
            timeStamps = Properties.Settings.Default.TimeStamps = false;
        }

        private void MenuItem_ClearConsole_Click(object sender, RoutedEventArgs e)
        {
            ConsoleText.Clear();
        }

        #region DLLInject
        // Injection
        [DllImport("kernel32")]
        public static extern IntPtr CreateRemoteThread(IntPtr hProcess, IntPtr lpThreadAttributes, uint dwStackSize, UIntPtr lpStartAddress,
          IntPtr lpParameter, uint dwCreationFlags, out IntPtr lpThreadId);

        [DllImport("kernel32.dll")]
        public static extern IntPtr OpenProcess(UInt32 dwDesiredAccess, Int32 bInheritHandle, Int32 dwProcessId);

        [DllImport("kernel32.dll")]
        public static extern Int32 CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern bool VirtualFreeEx(IntPtr hProcess, IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern UIntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
        static extern IntPtr VirtualAllocEx(IntPtr hProcess, IntPtr lpAddress, uint dwSize, uint flAllocationType, uint flProtect);

        [DllImport("kernel32.dll")]
        static extern bool WriteProcessMemory(IntPtr hProcess, IntPtr lpBaseAddress, string lpBuffer, UIntPtr nSize, out IntPtr lpNumberOfBytesWritten);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32", SetLastError = true, ExactSpelling = true)]
        internal static extern Int32 WaitForSingleObject(IntPtr handle, Int32 milliseconds);

        public void InjectDLL(IntPtr hProcess, String strDLLName)
        {
            IntPtr bytesout;

            // Length of string containing the DLL file name +1 byte padding
            Int32 LenWrite = strDLLName.Length + 1;
            // Allocate memory within the virtual address space of the target process
            IntPtr AllocMem = (IntPtr)VirtualAllocEx(hProcess, (IntPtr)null, (uint)LenWrite, 0x1000, 0x40); //allocation pour WriteProcessMemory

            // Write DLL file name to allocated memory in target process
            WriteProcessMemory(hProcess, AllocMem, strDLLName, (UIntPtr)LenWrite, out bytesout);
            // Function pointer "Injector"
            UIntPtr Injector = (UIntPtr)GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryA");

            if (Injector == null)
            {
                MessageBox.Show(" Injector Error! \n ");
                // return failed
                return;
            }

            // Create thread in target process, and store handle in hThread
            IntPtr hThread = (IntPtr)CreateRemoteThread(hProcess, (IntPtr)null, 0, Injector, AllocMem, 0, out bytesout);
            // Make sure thread handle is valid
            if (hThread == null)
            {
                //incorrect thread handle ... return failed
                MessageBox.Show(" hThread [ 1 ] Error! \n ");
                return;
            }
            // Time-out is 10 seconds...
            int Result = WaitForSingleObject(hThread, 10 * 1000);
            // Check whether thread timed out...
            if (Result == 0x00000080L || Result == 0x00000102L || Result == 0xFFFFFFFF)
            {
                /* Thread timed out... */
                MessageBox.Show(" hThread [ 2 ] Error! \n ");
                // Make sure thread handle is valid before closing... prevents crashes.
                if (hThread != null)
                {
                    //Close thread in target process
                    CloseHandle(hThread);
                }
                return;
            }
            // Sleep thread for 1 second
            Thread.Sleep(1000);
            // Clear up allocated space ( Allocmem )
            VirtualFreeEx(hProcess, AllocMem, (UIntPtr)0, 0x8000);
            // Make sure thread handle is valid before closing... prevents crashes.
            if (hThread != null)
            {
                //Close thread in target process
                CloseHandle(hThread);
            }
            // return succeeded
            return;
        }
        #endregion
    }
    public struct ChangeErrorMode : IDisposable
    {
        [Flags]
        public enum ErrorModes
        {
            Default = 0x0,
            FailCriticalErrors = 0x1,
            NoGpFaultErrorBox = 0x2,
            NoAlignmentFaultExcept = 0x4,
            NoOpenFileErrorBox = 0x8000
        }

        private int _oldMode;

        public ChangeErrorMode(ErrorModes mode)
        {
            _oldMode = SetErrorMode((int)mode);
        }

        void IDisposable.Dispose()
        {
            SetErrorMode(_oldMode);
        }

        [DllImport("kernel32.dll")]
        private static extern int SetErrorMode(int newMode);
    }
    public class IniFile
    {
        public string Path;
        static string EXE = Assembly.GetExecutingAssembly().GetName().Name;

        [DllImport("kernel32")]
        static extern long WritePrivateProfileString(string Section, string Key, string Value, string FilePath);

        [DllImport("kernel32")]
        static extern int GetPrivateProfileString(string Section, string Key, string Default, StringBuilder RetVal, int Size, string FilePath);

        public IniFile(string IniPath = null)
        {
            Path = new FileInfo(IniPath != null ? IniPath : EXE + ".ini").FullName.ToString();
        }

        public string Read(string Key, string Section = null)
        {
            StringBuilder RetVal = new StringBuilder(255);
            GetPrivateProfileString(Section != null ? Section : EXE, Key, "", RetVal, 255, Path);
            return RetVal.ToString();
        }

        public void Write(string Key, string Value, string Section = null)
        {
            WritePrivateProfileString(Section != null ? Section : EXE, Key, Value, Path);
        }

        public void DeleteKey(string Key, string Section = null)
        {
            Write(Key, null, Section != null ? Section : EXE);
        }

        public void DeleteSection(string Section = null)
        {
            Write(null, null, Section != null ? Section : EXE);
        }

        public bool KeyExists(string Key, string Section = null)
        {
            return Read(Key, Section).Length > 0 ? true : false;
        }
    }
    public class ConfigSettings : ObservableCollection<IniSetting>
    {
        public ConfigSettings()
            : base()
        {
            Add(new IniSetting("Settings", "Players", 20));
            Add(new IniSetting("Settings", "Seed", 73));
            Add(new IniSetting("Settings", "AllowTimeReset", 1));
        }
    }
    [Serializable]
    public class IniSetting : INotifyPropertyChanged
    {
        private string section;
        private string name;
        private int val;

        public event PropertyChangedEventHandler PropertyChanged;

        public IniSetting(string s, string n, int v)
        {
            this.section = s;
            this.name = n;
            this.val = v;
        }
        public string Section
        {
            get { return section; }
            set
            {
                section = value;
                NotifyPropertyChanged("Section");
            }
        }
        public string Name
        {
            get { return name; }
            set
            {
                name = value;
                NotifyPropertyChanged("Name");
            }
        }
        public int Value
        {
            get
            {
                IniFile f = new IniFile("settings.ini");
                val = Int32.Parse(f.Read(this.Name, "Settings"));
                return val;
            }
            set
            {
                if (this.Name == "Players")
                {
                    if (value > 255 || value < 0)
                        return;
                }
                if (this.Name == "AllowTimeReset")
                {
                    if (value != 0 && value != 1)
                        return;
                }
                val = value;
                IniFile f = new IniFile("settings.ini");
                f.Write(this.name, val.ToString(), "Settings");
                NotifyPropertyChanged("Value");
            }
        }
        private void NotifyPropertyChanged(string propertyName)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(propertyName));
            }

        }
    }
}
