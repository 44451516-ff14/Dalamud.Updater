using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using XIVLauncher.Common.Dalamud;

namespace Dalamud.Updater
{
    public partial class FormMain : Form
    {
        private const string OTTERHOME = """
                                         如需帮助或者反馈,请前往:
                                         https://file.bluefissure.com/FFXIV/Dalamud
                                         https://github.com/ottercorp/Dalamud.Updater
                                         https://aonyx.ffxiv.wang/
                                         QQ频道:https://pd.qq.com/s/9ehyfcha3
                                         QQ频道:https://pd.ottercorp.net
                                         """;

        // private List<string> pidList = new List<string>();
        private bool firstHideHint = true;
        private bool isThreadRunning = true;
        private bool dotnetDownloadFinished = false;
        private bool desktopDownloadFinished = false;
        private Config config;
        private DalamudLoadingOverlay dalamudLoadingOverlay;

        private readonly DirectoryInfo addonDirectory;
        private readonly DirectoryInfo runtimeDirectory;
        private readonly DirectoryInfo xivlauncherDirectory;
        private readonly DirectoryInfo assetDirectory;
        private readonly DirectoryInfo configDirectory;

        private readonly DalamudUpdater dalamudUpdater;

        public string windowsTitle = "獭纪委 v" + Assembly.GetExecutingAssembly().GetName().Version;

        private int checkTimes = 0;
        private int injectTimes = 0;
        private bool isCheckingUpdate = false;

        private void CheckUpdate()
        {
            isCheckingUpdate = true;
            checkTimes++;
            if (checkTimes == 8)
            {
                MessageBox.Show("点这么多遍干啥？", windowsTitle);
            }
            else if (checkTimes == 9)
            {
                MessageBox.Show("还点？", windowsTitle);
            }
            else if (checkTimes > 10)
            {
                MessageBox.Show("有问题你发日志，别搁这瞎几把点了", windowsTitle);
            }
            dalamudUpdater.Run();
        }

        /**
         * 获取更新器的版本
         */
        private Version GetUpdaterVersion()
        {
            return Assembly.GetExecutingAssembly().GetName().Version;
        }

        private string getVersion()
        {
            var rgx = new Regex(@"^\d{2}-\d{2}-\d{2}-\d{2}$");
            var stgRgx = new Regex(@"^\d{2}-\d{2}-\d{2}-\d{2}$");
            var di = new DirectoryInfo(Path.Combine(addonDirectory.FullName, "Hooks"));
            var 空版本 = "00-00-00-00";
            var 空版本Int = int.Parse(空版本.Replace("-", ""));
            if (!di.Exists)
                return 空版本.ToString();

            var dirs = di.GetDirectories("*", SearchOption.TopDirectoryOnly).Where(dir => rgx.IsMatch(dir.Name)).ToArray();
            bool releaseVersionExists = false;

            foreach (var dir in dirs)
            {
                var dirName = dir.Name.Replace("-", "");
                var newVersion = int.Parse(dirName);
                if (newVersion > 空版本Int)
                {
                    releaseVersionExists = true;
                    空版本Int = newVersion;
                    空版本 = dir.Name;
                    dalamudUpdater.Runner = new FileInfo(Path.Combine(dir.FullName, "Dalamud.Injector.exe"));
                }
            }

            if (!releaseVersionExists)
            {
                var stgDirs = di.GetDirectories("*", SearchOption.TopDirectoryOnly).Where(dir => stgRgx.IsMatch(dir.Name)).ToArray();
                if (stgDirs.Length > 0)
                {
                    return stgDirs[0].Name;
                }
            }
            return 空版本;
        }


        public FormMain()
        {
            InitLogging();
            InitializeComponent();
            InitializePIDCheck();
            InitializeDeleteShit();
            addonDirectory = Directory.GetParent(Assembly.GetExecutingAssembly().Location);
            dalamudLoadingOverlay = new DalamudLoadingOverlay(this);
            dalamudLoadingOverlay.OnProgressBar += setProgressBar;
            dalamudLoadingOverlay.OnSetVisible += setVisible;
            dalamudLoadingOverlay.OnStatusLabel += setStatus;

            string locationFullName = Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName;

            if ("Roaming\\XIVLauncherCN".EndsWith(locationFullName))
            {
                addonDirectory = new DirectoryInfo(Path.Combine(DalamudConst.ROAMINGPATH, "addon"));
                runtimeDirectory = new DirectoryInfo(Path.Combine(DalamudConst.ROAMINGPATH, "runtime"));
                assetDirectory = new DirectoryInfo(Path.Combine(DalamudConst.ROAMINGPATH, "dalamudAssets"));
                configDirectory = new DirectoryInfo(Path.Combine(DalamudConst.ROAMINGPATH));
            }
            else
            {
                addonDirectory = new DirectoryInfo(Path.Combine(locationFullName, "XIVLauncherCN", "addon"));
                runtimeDirectory = new DirectoryInfo(Path.Combine(locationFullName, "XIVLauncherCN", "runtime"));
                xivlauncherDirectory = new DirectoryInfo(Path.Combine(locationFullName, "XIVLauncherCN"));
                assetDirectory = new DirectoryInfo(Path.Combine(locationFullName, "XIVLauncherCN", "dalamudAssets"));
                configDirectory = new DirectoryInfo(Path.Combine(locationFullName, "XIVLauncherCN"));
            }


            //labelVersion.Text = string.Format("卫月版本 : {0}", getVersion());
            string[] strArgs = Environment.GetCommandLineArgs();
            if (strArgs.Length >= 2 && strArgs[1].Equals("-startup"))
            {
                //this.WindowState = FormWindowState.Minimized;
                //this.ShowInTaskbar = false;
                if (firstHideHint)
                {
                    firstHideHint = false;
                    this.DalamudUpdaterIcon.ShowBalloonTip(2000, "自启动成功", "放心，我会在后台偷偷干活的。", ToolTipIcon.Info);
                }
            }
            dalamudUpdater = new DalamudUpdater(addonDirectory, runtimeDirectory, assetDirectory, configDirectory);
            dalamudUpdater.Overlay = dalamudLoadingOverlay;
            dalamudUpdater.OnUpdateEvent += DalamudUpdater_OnUpdateEvent;


            InitializeConfig();
            labelVer.Text = $"v{Assembly.GetExecutingAssembly().GetName().Version}";
            UpdateFormConfig();

            SetDalamudVersion();

            getVersionOL();

            // CheckUpdate();
        }

        private async void getVersionOL()
        {
            TimeSpan defaultTimeout = TimeSpan.FromMinutes(25);
            using var client = new HttpClient
            {
                Timeout = defaultTimeout,
            };
            var json = await client.GetStringAsync(DalamudConst.ASSET_STORE_URL);
            var remoteVer = JsonConvert.DeserializeObject<AssetManager.AssetInfo>(json);
            var currentDir = new DirectoryInfo(Path.Combine(assetDirectory.FullName, remoteVer.Version.ToString()));
            dalamudUpdater.AssetDirectory = currentDir;
        }

        private void DalamudUpdater_OnUpdateEvent(DalamudUpdater.DownloadState value)
        {
            this.isCheckingUpdate = false;
            switch (value)
            {
                case DalamudUpdater.DownloadState.Failed:
                    MessageBox.Show("更新Dalamud失败", windowsTitle, MessageBoxButtons.YesNo);
                    setStatus("更新Dalamud失败");
                    break;
                case DalamudUpdater.DownloadState.Unknown:
                    setStatus("未知错误");
                    break;
                case DalamudUpdater.DownloadState.NoIntegrity:
                    setStatus("卫月与游戏不兼容");
                    break;
                case DalamudUpdater.DownloadState.Done:
                    SetDalamudVersion();
                    setStatus("更新成功");
                    break;
                case DalamudUpdater.DownloadState.Checking:
                    setStatus("检查更新中...");
                    isCheckingUpdate = true;
                    break;
            }
        }

        public async void SetDalamudVersion()
        {
            string localVersion = getVersion();
            TimeSpan defaultTimeout = TimeSpan.FromMinutes(25);

            var REMOTE_VERSION = "https://raw.githubusercontent.com/44451516-ff14/Dalamud.Updater.Action/refs/heads/main/version_info.json";

            using var client = new HttpClient
            {
                Timeout = defaultTimeout,
            };

            var versionInfoJsonRelease = await client.GetStringAsync(REMOTE_VERSION).ConfigureAwait(false);


            DalamudVersionInfo versionInfoRelease = JsonConvert.DeserializeObject<DalamudVersionInfo>(versionInfoJsonRelease);

            var verStr = string.Format("本地版本:{0}\n远程版本:{1} ", localVersion, versionInfoRelease.AssemblyVersion);
            if (this.labelVersion.InvokeRequired)
            {
                Action<string> actionDelegate = (x) => { labelVersion.Text = x; };
                this.labelVersion.Invoke(actionDelegate, verStr);
            }
            else
            {
                labelVersion.Text = verStr;
            }
        }

        #region init

        private static void InitLogging()
        {
            var baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            var logPath = Path.Combine(baseDirectory, "Dalamud.Updater.log");

            var levelSwitch = new LoggingLevelSwitch();

#if DEBUG
            levelSwitch.MinimumLevel = LogEventLevel.Verbose;
#else
            levelSwitch.MinimumLevel = LogEventLevel.Information;
#endif


            Log.Logger = new LoggerConfiguration()
                //.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose)
                .WriteTo.Async(a => a.File(logPath)).MinimumLevel.ControlledBy(levelSwitch).CreateLogger();
        }

        private void InitializeConfig()
        {
            this.config = Config.Load(Path.Combine(Directory.GetParent(Assembly.GetExecutingAssembly().Location).FullName, "DalamudUpdaterConfig.json"));
        }

        private void InitializeDeleteShit()
        {
            var shitConfig = Path.Combine(Directory.GetCurrentDirectory(), "Dalamud.Updater.exe.config");
            if (File.Exists(shitConfig))
            {
                File.Delete(shitConfig);
            }

            var shitInjector = Path.Combine(Directory.GetCurrentDirectory(), "Dalamud.Injector.exe");
            if (File.Exists(shitInjector))
            {
                File.Delete(shitInjector);
            }

            var shitDalamud = Path.Combine(Directory.GetCurrentDirectory(), "6.3.0.9");
            if (Directory.Exists(shitDalamud))
            {
                Directory.Delete(shitDalamud, true);
            }

            var shitUIRes = Path.Combine(Directory.GetCurrentDirectory(), "XIVLauncherCN", "dalamudAssets", "UIRes");
            if (Directory.Exists(shitUIRes))
            {
                Directory.Delete(shitUIRes, true);
            }

            var shitAddon = Path.Combine(Directory.GetCurrentDirectory(), "addon");
            if (Directory.Exists(shitAddon))
            {
                Directory.Delete(shitAddon, true);
            }

            var shitRuntime = Path.Combine(Directory.GetCurrentDirectory(), "runtime");
            if (Directory.Exists(shitRuntime))
            {
                Directory.Delete(shitRuntime, true);
            }
        }

        private void InitializePIDCheck()
        {
            var thread = new Thread
            (
                () =>
                {
                    while (this.isThreadRunning)
                    {
                        try
                        {
                            if (this.isCheckingUpdate) throw new Exception("正在更新卫月...");

                            //var newPidList = Process.GetProcessesByName("ffxiv_dx11").Where(process =>
                            //{
                            //    return !process.MainWindowTitle.Contains("FINAL FANTASY XIV");
                            //}).ToList().ConvertAll(process => process.Id.ToString()).ToArray();
                            //为什么我开了FF检测不到啊.jpg
                            var newPidList = Process.GetProcesses().Where
                            (
                                process =>
                                {
                                    if (process.ProcessName == "ffxiv_dx11" || process.ProcessName == "ffxiv")
                                    {
                                        return !process.MainWindowTitle.Contains("FINAL FANTASY XIV");
                                    }
                                    return false;
                                }
                            ).ToList().ConvertAll(process => process.Id.ToString()).ToArray();
                            var newHash = String.Join(", ", newPidList).GetHashCode();
                            var oldPidList = this.comboBoxFFXIV.Items.Cast<Object>().Select(item => item.ToString()).ToArray();
                            var oldHash = String.Join(", ", oldPidList).GetHashCode();
                            if (oldHash != newHash && this.comboBoxFFXIV.IsHandleCreated)
                            {
                                this.comboBoxFFXIV.Invoke
                                (
                                    (MethodInvoker)delegate
                                    {
                                        // Running on the UI thread
                                        comboBoxFFXIV.Items.Clear();
                                        comboBoxFFXIV.Items.AddRange(newPidList);
                                        if (newPidList.Length > 0)
                                        {
                                            if (!comboBoxFFXIV.DroppedDown)
                                                this.comboBoxFFXIV.SelectedIndex = 0;
                                            if (this.checkBoxAutoInject.Checked)
                                            {
                                                foreach (var pidStr in newPidList)
                                                {
                                                    //Thread.Sleep((int)(this.injectDelaySeconds * 1000));
                                                    var pid = int.Parse(pidStr);
                                                    if (Process.GetProcessById(pid).ProcessName != "ffxiv_dx11")
                                                    {
                                                        this.DalamudUpdaterIcon.ShowBalloonTip(2000, "找不到游戏", $"进程{pid}不是dx11版FF。", ToolTipIcon.Warning);
                                                        Log.Information("{pid} is not dx11", pid);
                                                        continue;
                                                    }
                                                    if (this.Inject(pid, (int)(this.config.InjectDelaySeconds * 1000)))
                                                    {
                                                        this.DalamudUpdaterIcon.ShowBalloonTip(2000, "帮你注入了", $"帮你注入了进程{pid}，不用谢。", ToolTipIcon.Info);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                );
                            }
                        }
                        catch
                        {

                        }
                        Thread.Sleep(1000);
                    }
                }
            );
            thread.IsBackground = true;
            thread.Start();
        }

        #endregion

        private void FormMain_Load(object sender, EventArgs e)
        {
        }

        private void UpdateFormConfig()
        {
            this.checkBoxAutoInject.Checked = this.config.AutoInject.Value;
            this.checkBoxAutoStart.Checked = this.config.AutoStart.Value;
            this.delayBox.Value = (decimal)this.config.InjectDelaySeconds;
            this.checkBoxSafeMode.Checked = this.config.SafeMode.Value;
            this.CheckBox自动更新.Checked = this.config.自动更新.Value;
        }


        private void FormMain_Disposed(object sender, EventArgs e)
        {
            this.isThreadRunning = false;
        }

        private void FormMain_FormClosing(object sender, FormClosingEventArgs e)
        {
            e.Cancel = true;
            this.WindowState = FormWindowState.Minimized;
            this.Hide();
            //this.FormBorderStyle = FormBorderStyle.SizableToolWindow;
            //this.ShowInTaskbar = false;
            //this.Visible = false;
            if (firstHideHint)
            {
                firstHideHint = false;
                this.DalamudUpdaterIcon.ShowBalloonTip(2000, "小玩意挺会藏", "哎我藏起来了，单击托盘图标呼出程序界面。", ToolTipIcon.Info);
            }
        }

        private void DalamudUpdaterIcon_MouseClick(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (this.WindowState == FormWindowState.Minimized)
                {
                    this.Show();
                    this.WindowState = FormWindowState.Normal;
                }
                this.Activate();
            }
        }

        private void 显示ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            //WindowState = FormWindowState.Normal;
            if (!this.Visible) this.Visible = true;
            this.Activate();
        }

        private void 退出ToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Dispose();
            //this.Close();
            this.DalamudUpdaterIcon.Dispose();
            Application.Exit();
        }

        private void ButtonCheckForUpdate_Click(object sender, EventArgs e)
        {
            if (this.comboBoxFFXIV.SelectedItem != null)
            {
                var pid = int.Parse((string)this.comboBoxFFXIV.SelectedItem);
                var process = Process.GetProcessById(pid);
                if (isInjected(process))
                {
                    var choice = MessageBox.Show
                    (
                        "经检测存在 ffxiv_dx11.exe 进程，更新卫月需要关闭游戏，需要帮您代劳吗？", "关闭游戏",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Information
                    );
                    if (choice == DialogResult.Yes)
                    {
                        process.Kill();
                    }
                    else
                    {
                        return;
                    }
                }
            }
            CheckUpdate();
        }

        private void comboBoxFFXIV_Clicked(object sender, EventArgs e)
        {

        }

        private void linkLabel1_LinkClicked(object sender, LinkLabelLinkClickedEventArgs e)
        {
            Process.Start("https://qun.qq.com/qqweb/qunpro/share?_wv=3&_wwv=128&inviteCode=CZtWN&from=181074&biz=ka&shareSource=5");
        }



        private DalamudStartInfo GeneratingDalamudStartInfo(Process process, string dalamudPath, int injectDelay)
        {
            //E:\DevProject\FF14\Dalamud.Update.Soil\Dalamud.Updater\bin\Release\net48\XIVLauncherCN\addon\Hooks\25-03-12-01
            var ffxivDir = Path.GetDirectoryName(process.MainModule.FileName);
            var xivlauncherDir = xivlauncherDirectory.FullName;

            var gameVerStr = File.ReadAllText(Path.Combine(ffxivDir, "ffxivgame.ver"));
            Log.Information("奇怪的流程runtimeDirectory: {path}", runtimeDirectory.FullName);
            Log.Information("奇怪的流程runtimeDirectory: {path}", dalamudUpdater.AssetDirectory.FullName);

            var startInfo = new DalamudStartInfo
            {
                ConfigurationPath = Path.Combine(xivlauncherDir, "dalamudConfig.json"),
                PluginDirectory = Path.Combine(xivlauncherDir, "installedPlugins"),
                DefaultPluginDirectory = Path.Combine(xivlauncherDir, "devPlugins"),
                RuntimeDirectory = runtimeDirectory.FullName,
                AssetDirectory = this.dalamudUpdater.AssetDirectory.FullName,
                GameVersion = gameVerStr,
                Language = "4",
                OptOutMbCollection = false,
                WorkingDirectory = dalamudPath,
                DelayInitializeMs = injectDelay
            };
            Log.Information("更新流程下的路径: {path}", dalamudUpdater.AssetDirectory.FullName);
            return startInfo;
        }

        private bool isInjected(Process process)
        {
            try
            {
                for (var j = 0; j < process.Modules.Count; j++)
                {
                    if (process.Modules[j].ModuleName == "Dalamud.dll")
                    {
                        return true;
                    }
                }
            }
            catch
            {

            }
            return false;
        }

        private void DetectSomeShit(Process process)
        {
            try
            {
                for (var j = 0; j < process.Modules.Count; j++)
                {
                    if (process.Modules[j].ModuleName == "ws2detour_x64.dll")
                    {
                        MessageBox.Show("检测到使用网易UU加速器进程模式,有可能注入无反应。\n请使用路由模式。", windowsTitle, MessageBoxButtons.OK);
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsZombieProcess(int pid)
        {
            try
            {
                var process = Process.GetProcessById(pid);
                var mainModule = process.MainModule;
                var handle = SystemHelper.OpenProcess(0x001F0FFF, true, process.Id);
                if (handle == IntPtr.Zero)
                    throw new Exception("ERROR: OpenProcess()");

                SystemHelper.CloseHandle(handle);
            }
            catch (Exception ex)
            {
                MessageBox.Show
                (
                    """
                    无法访问/打开进程
                    1.请检查安全软件，将Dalamud程序以及相关目录加入白名单
                    2.打开任务管理器，检查是否存在未完全退出且无响应的FFXIV进程,并尝试结束
                    3.尝试重启电脑

                    """
                    + ex.Message, windowsTitle, MessageBoxButtons.YesNo
                );
                return true;
            }
            return false;
        }

        private bool Inject(int pid, int injectDelay = 0)
        {
            if (dalamudUpdater.Runner == null)
            {
                return false;
            }
            
            if (dalamudUpdater.AssetDirectory == null)
            {
                return false;
            }
            
            injectTimes = 0;
            var process = Process.GetProcessById(pid);
            if (process.ProcessName != "ffxiv_dx11")
            {
                Log.Error("{pid} is not dx11", pid);
                if (MessageBox.Show("此进程并非dx11版FFXIV,无法使用Dalamud。\n解决方法:\n点击确定使用浏览器查看 https://www.yuque.com/ffcafe/act/dx11", windowsTitle, MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    Process.Start("https://www.yuque.com/ffcafe/act/dx11");
                    return false;
                }
            }
            if (IsZombieProcess(pid))
            {
                return false;
            }
            if (isInjected(process))
            {
                return false;
            }
            DetectSomeShit(process);
            //var dalamudStartInfo = Convert.ToBase64String(Encoding.UTF8.GetBytes(GeneratingDalamudStartInfo(process)));
            //var startInfo = new ProcessStartInfo(injectorFile, $"{pid} {dalamudStartInfo}");
            //startInfo.WorkingDirectory = dalamudPath.FullName;
            //Process.Start(startInfo);
            Log.Information($"[Updater] dalamudUpdater.State:{dalamudUpdater.State}");
            if (dalamudUpdater.State == DalamudUpdater.DownloadState.NoIntegrity)
            {
                if (MessageBox.Show("当前Dalamud版本可能与游戏不兼容,确定注入吗？", windowsTitle, MessageBoxButtons.YesNo) != DialogResult.Yes)
                {
                    return false;
                }
            }
            var dalamudUpdaterRunner = dalamudUpdater.Runner;
            var fullName = Directory.GetParent(dalamudUpdaterRunner.FullName).FullName;
            //return false;
            var dalamudStartInfo = GeneratingDalamudStartInfo(process, fullName, injectDelay);
            var environment = new Dictionary<string, string>();
            // No use cuz we're injecting instead of launching, the Dalamud.Boot.dll is reading environment variables from ffxiv_dx11.exe
            /*
            var prevDalamudRuntime = Environment.GetEnvironmentVariable("DALAMUD_RUNTIME");
            if (string.IsNullOrWhiteSpace(prevDalamudRuntime))
                environment.Add("DALAMUD_RUNTIME", runtimeDirectory.FullName);
            */
            WindowsDalamudRunner.Inject(dalamudUpdaterRunner, process.Id, environment, DalamudLoadMethod.DllInject, dalamudStartInfo, this.safeMode);
            return true;
        }

        private void ButtonInject_Click(object sender, EventArgs e)
        {
            if (this.isCheckingUpdate)
            {
                injectTimes++;
                if (injectTimes == 3)
                {
                    MessageBox.Show("麻烦耐心等待更新完成 ^_^", "正在更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (checkTimes == 4)
                {
                    MessageBox.Show("都说了“麻烦”“耐心”“等待” ^_^##", "正在更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else if (checkTimes > 5)
                {
                    MessageBox.Show("憋点啦！ -_-##", "正在更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    MessageBox.Show("请等更新完成之后再注入", "正在更新", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (this.comboBoxFFXIV.SelectedItem != null
                && this.comboBoxFFXIV.SelectedItem.ToString().Length > 0)
            {
                var pidStr = this.comboBoxFFXIV.SelectedItem.ToString();
                if (int.TryParse(pidStr, out var pid))
                {
                    if (Inject(pid))
                    {
                        Log.Information("[DINJECT] Inject finished.");
                    }
                }
                else
                {
                    MessageBox.Show
                    (
                        "未能解析游戏进程ID", "找不到游戏",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            }
            else
            {
                MessageBox.Show
                (
                    "未选择游戏进程", "找不到游戏",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
            }

        }

        private void SetAutoRun(bool value)
        {
            string strFilePath = Application.ExecutablePath;
            try
            {
                SystemHelper.SetAutoRun($"\"{strFilePath}\"" + " -startup", "DalamudAutoInjector", value);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void checkBoxAutoStart_CheckedChanged(object sender, EventArgs e)
        {
            this.config.AutoStart = checkBoxAutoStart.Checked;
            SetAutoRun(this.config.AutoStart.Value);
            this.config.Save();
        }

        private void checkBoxAutoInject_CheckedChanged(object sender, EventArgs e)
        {
            this.config.AutoInject = checkBoxAutoInject.Checked;
            this.config.Save();
        }

        private void delayBox_ValueChanged(object sender, EventArgs e)
        {
            this.config.InjectDelaySeconds = (double)delayBox.Value;
            this.config.Save();
        }

        private void setProgressBar(int v)
        {
            if (this.toolStripProgressBar1.Owner.InvokeRequired)
            {
                Action<int> actionDelegate = (x) => { toolStripProgressBar1.Value = x; };
                this.toolStripProgressBar1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                this.toolStripProgressBar1.Value = v;
            }
        }

        private void setStatus(string v)
        {
            if (toolStripStatusLabel1.Owner.InvokeRequired)
            {
                Action<string> actionDelegate = (x) => { toolStripStatusLabel1.Text = x; };
                this.toolStripStatusLabel1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                this.toolStripStatusLabel1.Text = v;
            }
        }

        private void setVisible(bool v)
        {
            if (toolStripProgressBar1.Owner.InvokeRequired)
            {
                Action<bool> actionDelegate = (x) =>
                {
                    toolStripProgressBar1.Visible = x;
                    //toolStripStatusLabel1.Visible = v; 
                };
                this.toolStripStatusLabel1.Owner.Invoke(actionDelegate, v);
            }
            else
            {
                toolStripProgressBar1.Visible = v;
                //toolStripStatusLabel1.Visible = v;
            }
        }

        private bool safeMode = false;

        private void checkBox1_CheckedChanged(object sender, EventArgs e)
        {
            this.safeMode = this.checkBoxSafeMode.Checked;
        }

        private bool 自动更新 = false;

        private void 自动更新_CheckedChanged(object sender, EventArgs e)
        {
            this.自动更新 = this.CheckBox自动更新.Checked;
        }

        #region DeleteLink

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetFileAttributesEx(string lpFileName, int fInfoLevelId, out WIN32_FILE_ATTRIBUTE_DATA fileData);

        [StructLayout(LayoutKind.Sequential)]
        private struct WIN32_FILE_ATTRIBUTE_DATA
        {
            public FileAttributes dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
        }

        public static bool IsSymbolicLink(string path)
        {
            if (GetFileAttributesEx(path, 0, out WIN32_FILE_ATTRIBUTE_DATA fileAttributesData))
            {
                return (fileAttributesData.dwFileAttributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
            }

            return false;
        }

        public static void DeleteSymbolicLink(string path)
        {
            if (IsSymbolicLink(path))
            {
                // 检查路径是文件还是目录，然后删除
                if (Directory.Exists(path))
                {
                    // 如果是目录符号链接
                    Directory.Delete(path);
                    //Console.WriteLine($"Symbolic link directory '{path}' was deleted.");
                }
                else if (File.Exists(path))
                {
                    // 如果是文件符号链接
                    File.Delete(path);
                    //Console.WriteLine($"Symbolic link file '{path}' was deleted.");
                }
            }
        }

        #endregion
    }
}