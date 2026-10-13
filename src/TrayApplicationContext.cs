using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using ThreadTimer = System.Threading.Timer;

namespace Battery24GTray
{
    /// <summary>
    /// 托盘应用主上下文：
    ///  - NotifyIcon 圆形电池图标固定驻留系统托盘（非悬浮窗）
    ///  - 定时轮询 + 事件触发刷新电量
    ///  - 监听电源适配器插拔（WM_POWERBROADCAST → StatusChange），自动恢复显示并刷新
    ///  - 监听 TaskbarCreated 消息，explorer 重启后自动恢复托盘图标
    /// </summary>
    internal class TrayApplicationContext : ApplicationContext
    {
        private const int PollIntervalMs = 30000;

        private NotifyIcon _notifyIcon;
        private readonly IBatteryProvider _provider;
        private ThreadTimer _timer;
        private ThreadTimer _selfTestTimer;
        private ThreadTimer _heartbeat;
        private TaskbarWatcher _watcher;
        private ToolStripMenuItem _sourceItem;
        private ToolStripMenuItem _autoStartItem;
        private readonly bool _demo;
        private readonly bool _selfTest;
        private readonly int _selfTestSeconds;
        private IntPtr _lastIconHandle = IntPtr.Zero;
        private GCHandle _lastIconHandleGC;
        private int _pollBusy;
        private SynchronizationContext _syncCtx;
        private bool _initialized;

        /// <summary>Exposes the UI thread sync context for external observers.</summary>
        public SynchronizationContext SyncContext
        {
            get { return _syncCtx; }
        }

        private void SetIconHandle(Icon icon)
        {
            if (_lastIconHandle != IntPtr.Zero && _lastIconHandleGC.IsAllocated)
                _lastIconHandleGC.Free();
            var gc = GCHandle.Alloc(icon);
            _lastIconHandle = icon.Handle;
            _lastIconHandleGC = gc;
            _notifyIcon.Icon = icon;
        }

        public TrayApplicationContext(bool demo, int selfTestSeconds)
        {
            AppendLog("[CTX] constructor start");
            _demo = demo;
            _selfTest = selfTestSeconds > 0;
            _selfTestSeconds = selfTestSeconds;
            if (_selfTest)
            {
                try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
            }

            try
            {
                // Build provider before Application.Run — no WinForms dependencies
                _provider = BuildProvider();
                AppendLog("[CTX] provider built");

                // NOTE: All WinForms controls (ContextMenuStrip, NotifyIcon, TaskbarWatcher)
                // must be created AFTER Application.Run() starts, when SynchronizationContext
                // is available and the UI thread message pump is running.
                // Call Initialize() from Main() after Application.Run() begins.
            }
            catch (Exception ex)
            {
                AppendLog("[CTX] FATAL exception in constructor: " + ex);
                try { Console.Error.WriteLine("[CTX] FATAL: " + ex); } catch { }
                throw;
            }
        }

        /// <summary>
        /// Must be called AFTER Application.Run() starts, on the UI thread.
        /// Creates all WinForms controls and sets up the tray icon.
        /// </summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            AppendLog("[CTX] Initialize() called");

            try
            {
                _syncCtx = SynchronizationContext.Current;
                AppendLog("[CTX] sync context: " + (_syncCtx != null));

                ContextMenuStrip menu = new ContextMenuStrip();
                AppendLog("[CTX] context menu created");

                _sourceItem = new ToolStripMenuItem("数据源: 检测中...");
                _sourceItem.Enabled = false;
                menu.Items.Add(_sourceItem);
                menu.Items.Add(new ToolStripSeparator());

                ToolStripMenuItem refreshItem = new ToolStripMenuItem("立即刷新(&R)");
                refreshItem.Click += delegate { QueueRefresh(); };
                menu.Items.Add(refreshItem);

                ToolStripMenuItem demoItem = new ToolStripMenuItem("演示模式（模拟电量）");
                demoItem.Checked = _demo;
                demoItem.Click += delegate
                {
                    System.Diagnostics.Process.Start(Application.ExecutablePath, _demo ? "" : "--demo");
                    ExitThread();
                };
                menu.Items.Add(demoItem);

                // 需求 3：开机自动启动（HKCU Run 注册表，无需管理员权限）
                _autoStartItem = new ToolStripMenuItem("开机自动启动(&S)");
                _autoStartItem.Checked = AutoStartManager.IsEnabled();
                _autoStartItem.Click += delegate
                {
                    AutoStartManager.SetEnabled(!_autoStartItem.Checked);
                    _autoStartItem.Checked = AutoStartManager.IsEnabled();
                };
                menu.Items.Add(_autoStartItem);

                menu.Items.Add(new ToolStripSeparator());
                ToolStripMenuItem exitItem = new ToolStripMenuItem("退出(&X)");
                exitItem.Click += delegate { ExitThread(); };
                menu.Items.Add(exitItem);

                _notifyIcon = new NotifyIcon();
                AppendLog("[CTX] notifyicon created");
                _notifyIcon.ContextMenuStrip = menu;
                _notifyIcon.Text = "Battery24GTray";
                var unknownIcon = BatteryIconRenderer.RenderUnknown();
                AppendLog("[CTX] RenderUnknown done, setting icon");
                SetIconHandle(unknownIcon);
                AppendLog("[CTX] icon set, making visible");
                _notifyIcon.Visible = true; // 固定驻留托盘
                AppendLog("[CTX] notifyicon visible=true");

                // explorer.exe 重启后托盘图标会被清除，监听 TaskbarCreated 恢复
                _watcher = new TaskbarWatcher();
                AppendLog("[CTX] taskbar watcher created");
                _watcher.TaskbarCreated += delegate
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Visible = true;
                };

                // 需求 4：电源适配器插入/移除（交流电状态切换）时自动激活/恢复显示
                AppendLog("[CTX] subscribing to PowerModeChanged");
                SystemEvents.PowerModeChanged += OnPowerModeChanged;
                AppendLog("[CTX] power mode handler registered");

                _timer = new ThreadTimer(delegate { QueueRefresh(); }, null, 0, PollIntervalMs);
                AppendLog("[CTX] timer started");

                // 心跳：每秒写一条日志，确认消息循环仍在运行；退出时立刻停止
                _heartbeat = new ThreadTimer(
                    delegate { AppendLog("[hb] alive"); }, null, 1000, 1000);
                AppendLog("[CTX] heartbeat started");

                if (_selfTestSeconds > 0)
                {
                    _selfTestTimer = new ThreadTimer(delegate { _heartbeat.Dispose(); ExitThread(); },
                        null, _selfTestSeconds * 1000, Timeout.Infinite);
                    AppendLog("[CTX] selftest timer set for " + _selfTestSeconds + "s");
                }
                AppendLog("[CTX] constructor done");
            }
            catch (Exception ex)
            {
                AppendLog("[CTX] FATAL in Initialize(): " + ex);
                try { Console.Error.WriteLine("[CTX] FATAL Init: " + ex); } catch { }
                throw;
            }
        }

        private IBatteryProvider BuildProvider()
        {
            if (_demo) return new SimulatedProvider();
            ProviderChain chain = new ProviderChain();
            chain.Add(new Hid24GProvider());
            return chain;
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            // StatusChange 对应 WM_POWERBROADCAST 的 PBT_APMPOWERSTATUSCHANGE：
            // 交流适配器插入/拔出时触发 → 确保托盘图标可见并立即刷新电量
            if (e.Mode == PowerModes.StatusChange)
            {
                _notifyIcon.Visible = true;
                QueueRefresh();
            }
        }

        /// <summary>排队一次电量刷新（防重入；读数在后台线程，UI 更新回主线程）。</summary>
        private static void AppendLog(string line)
        {
            try
            {
                lock (Program._logLock)
                using (var fs = new FileStream(Program.LogPath, FileMode.Append, FileAccess.Write, FileShare.Read))
                using (var sw = new StreamWriter(fs))
                    sw.WriteLine(line);
            }
            catch { }
        }

        private void QueueRefresh()
        {
            AppendLog("[QueueRefresh] called");
            if (Interlocked.CompareExchange(ref _pollBusy, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                BatteryReading reading = null;
                try { reading = _provider.Read(); }
                catch (Exception ex) { AppendLog("[Poll] exception: " + ex); }
                try
                {
                    var ctx = _syncCtx;
                    if (ctx != null)
                        ctx.Post(delegate { ApplyReading(reading); }, null);
                    else
                        ApplyReading(reading); // fallback: run directly
                }
                catch (ObjectDisposedException) { AppendLog("[QueueRefresh] disposed"); }
                finally { Interlocked.Exchange(ref _pollBusy, 0); }
            });
        }

        private void ApplyReading(BatteryReading r)
        {
            AppendLog("[ApplyReading] r=" + (r == null ? "null" : "pct=" + r.Percent) + " src=" + (r != null ? r.SourceName : "null"));
            string text;
            string source;
            if (r != null && r.Percent.HasValue)
            {
                SetIconHandle(BatteryIconRenderer.Render(r.Percent.Value, r.Charging));
                string state = r.Charging ? " 充电中" : "";
                text = string.Format("{0}  {1}%{2}", r.SourceName, r.Percent.Value, state);
                source = "数据源: " + r.SourceName;
            }
            else
            {
                SetIconHandle(BatteryIconRenderer.RenderUnknown());
                text = _demo ? "演示模式" : "未检测到支持的 2.4G 接收器";
                source = "数据源: 无";
            }
            if (text.Length > 63) text = text.Substring(0, 63); // NotifyIcon.Text 上限
            _notifyIcon.Text = text;
            _sourceItem.Text = source;

            if (_selfTest) Console.WriteLine("[selftest] " + text);
        }

        protected override void ExitThreadCore()
        {
            try
            {
                AppendLog("[CTX] ExitThreadCore called");
                if (_heartbeat != null) { try { _heartbeat.Dispose(); } catch { } }
                if (_timer != null) { try { _timer.Dispose(); } catch { } }
                if (_selfTestTimer != null) { try { _selfTestTimer.Dispose(); } catch { } }
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                if (_lastIconHandleGC.IsAllocated)
                {
                    var icon = _lastIconHandleGC.Target as System.Drawing.Icon;
                    _lastIconHandleGC.Free();
                    if (icon != null) icon.Dispose();
                    _lastIconHandle = IntPtr.Zero;
                }
                if (_notifyIcon != null)
                {
                    try { _notifyIcon.Visible = false; } catch { }
                    try { _notifyIcon.Dispose(); } catch { }
                }
            }
            catch { }
            base.ExitThreadCore();
        }
    }

    /// <summary>
    /// 隐藏消息窗口：监听系统广播的 TaskbarCreated 消息，
    /// 在 explorer.exe 崩溃/重启后恢复托盘图标（托盘常驻程序的必备恢复机制）。
    /// </summary>
    internal class TaskbarWatcher : NativeWindow
    {
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int RegisterWindowMessage(string message);

        private static readonly int WmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");

        public event Action TaskbarCreated;

        public TaskbarWatcher()
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WmTaskbarCreated)
            {
                Action handler = TaskbarCreated;
                if (handler != null) handler();
            }
            base.WndProc(ref m);
        }
    }
}
