using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Battery24GTray
{
    /// <summary>枚举到的单个 HID 顶层集合信息。</summary>
    internal class HidDeviceInfo
    {
        public string Path;
        public ushort Vid;
        public ushort Pid;
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputLen;
        public ushort OutputLen;
        public ushort FeatureLen;
        public string Product;
    }

    /// <summary>
    /// Windows HID 原生层（SetupAPI + hid.dll）：
    /// 枚举当前所有 USB HID 设备，并提供 feature / io 两种传输通道。
    /// 这是对 2.4G 无线接收器（USB Dongle）收发厂商私有报文的底层基础。
    /// </summary>
    internal static class HidNative
    {
        internal const uint GENERIC_READ = 0x80000000;
        internal const uint GENERIC_WRITE = 0x40000000;
        internal const int DIGCF_PRESENT = 0x2;
        internal const int DIGCF_DEVICEINTERFACE = 0x10;
        private static readonly IntPtr InvalidHandle = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")]
        internal static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetAttributes(SafeFileHandle hidDevice, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetPreparsedData(SafeFileHandle hidDevice, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        internal static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        internal static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_SetFeature(SafeFileHandle hidDevice, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetFeature(SafeFileHandle hidDevice, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        internal static extern bool HidD_GetProductString(SafeFileHandle hidDevice, byte[] buffer, int bufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, int flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiEnumDeviceInterfaces(
            IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid,
            int memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern bool SetupDiGetDeviceInterfaceDetail(
            IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData,
            IntPtr deviceInterfaceDetailData, int deviceInterfaceDetailDataSize,
            out int requiredSize, IntPtr deviceInfoData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        internal static extern SafeFileHandle CreateFile(
            string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition,
            uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        // ---- 重叠(Overlapped) I/O ----
        // 关键教训：HID 设备上"同步 ReadFile 挂起中再 WriteFile/SetOutputReport"会被
        // 串行化阻塞；且 MCHOSE 等设备只认中断 OUT 管道的写入（控制传输 SET_REPORT
        // 会被静默忽略）。Chromium WebHID 同样使用重叠 I/O，这是能正常通信的前提。

        internal const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        internal const int ERROR_IO_PENDING = 997;

        [StructLayout(LayoutKind.Sequential)]
        internal struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "ReadFile")]
        internal static extern bool ReadFileOverlapped(
            SafeFileHandle hFile, byte[] buffer, uint nNumberOfBytesToRead,
            IntPtr lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true, EntryPoint = "WriteFile")]
        internal static extern bool WriteFileOverlapped(
            SafeFileHandle hFile, byte[] buffer, uint nNumberOfBytesToWrite,
            IntPtr lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool GetOverlappedResult(
            SafeFileHandle hFile, ref OVERLAPPED lpOverlapped,
            out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CancelIoEx(SafeFileHandle hFile, IntPtr lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        internal static extern bool CancelIoEx(SafeFileHandle hFile, ref OVERLAPPED lpOverlapped);

        /// <summary>枚举当前系统所有可打开的 HID 顶层集合。</summary>
        public static List<HidDeviceInfo> Enumerate()
        {
            List<HidDeviceInfo> list = new List<HidDeviceInfo>();
            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr set = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero,
                DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (set == InvalidHandle) return list;
            try
            {
                for (int i = 0; ; i++)
                {
                    SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                    ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref hidGuid, i, ref ifData)) break;

                    int required;
                    // 注意：首次调用传 null detail 时按设计返回 false(ERROR_INSUFFICIENT_BUFFER)，
                    // 但会填充 requiredSize —— 不能把 false 当作失败跳过，否则枚举结果永远为空
                    SetupDiGetDeviceInterfaceDetail(set, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required <= 0) continue;

                    IntPtr buf = Marshal.AllocHGlobal(required);
                    try
                    {
                        // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize: x86 = 6, x64 = 8
                        Marshal.WriteInt32(buf, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(set, ref ifData, buf, required, out required, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringUni(new IntPtr(buf.ToInt64() + 4));
                        if (string.IsNullOrEmpty(path)) continue;

                        HidDeviceInfo info = TryProbe(path);
                        if (info != null) list.Add(info);
                    }
                    finally { Marshal.FreeHGlobal(buf); }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return list;
        }

        private static HidDeviceInfo TryProbe(string path)
        {
            SafeFileHandle h = CreateFile(path, GENERIC_READ | GENERIC_WRITE, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (h == null || h.IsInvalid) { if (h != null) h.Dispose(); return null; }
            try
            {
                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (!HidD_GetAttributes(h, ref attr)) return null;

                IntPtr pre;
                if (!HidD_GetPreparsedData(h, out pre)) return null;
                ushort usage = 0, usagePage = 0, inLen = 0, outLen = 0, featLen = 0;
                try
                {
                    HIDP_CAPS caps = new HIDP_CAPS();
                    caps.Reserved = new ushort[17];
                    if (HidP_GetCaps(pre, ref caps) != 0x00110000) return null; // HIDP_STATUS_SUCCESS
                    usage = caps.Usage;
                    usagePage = caps.UsagePage;
                    inLen = caps.InputReportByteLength;
                    outLen = caps.OutputReportByteLength;
                    featLen = caps.FeatureReportByteLength;
                }
                finally { HidD_FreePreparsedData(pre); }

                string product = null;
                byte[] nameBuf = new byte[512];
                if (HidD_GetProductString(h, nameBuf, nameBuf.Length))
                {
                    string s = Encoding.Unicode.GetString(nameBuf);
                    int nul = s.IndexOf('\0');
                    if (nul > 0) product = s.Substring(0, nul);
                }

                return new HidDeviceInfo
                {
                    Path = path,
                    Vid = attr.VendorID,
                    Pid = attr.ProductID,
                    Usage = usage,
                    UsagePage = usagePage,
                    InputLen = inLen,
                    OutputLen = outLen,
                    FeatureLen = featLen,
                    Product = product
                };
            }
            finally { h.Dispose(); }
        }
    }

    /// <summary>
    /// 已打开的 HID 设备句柄，封装两种传输通道：
    ///  - feature 通道: HidD_SetFeature / HidD_GetFeature（控制传输，请求-响应式，最常用）
    ///  - io 通道: WriteFile / ReadFile（中断管道，部分厂商使用）
    /// io 通道由常驻后台线程读取，避免 ReadFile 阻塞与超时泄漏。
    /// </summary>
    internal sealed class HidDevice : IDisposable
    {
        private SafeFileHandle _handle;
        private Thread _readThread;
        private readonly BlockingCollection<byte[]> _ioReports =
            new BlockingCollection<byte[]>(new ConcurrentQueue<byte[]>());
        private bool _disposed;

        public HidDeviceInfo Info { get; private set; }

        public bool Open(HidDeviceInfo info)
        {
            Info = info;
            _handle = HidNative.CreateFile(info.Path,
                HidNative.GENERIC_READ | HidNative.GENERIC_WRITE,
                3, IntPtr.Zero, 3, HidNative.FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (_handle == null || _handle.IsInvalid)
            {
                if (_handle != null) _handle.Dispose();
                _handle = null;
                return false;
            }
            _readThread = new Thread(ReadLoop);
            _readThread.IsBackground = true;
            _readThread.Start();
            return true;
        }

        private void ReadLoop()
        {
            int len = Math.Max((int)Info.InputLen, 8);
            try
            {
                while (!_disposed)
                {
                    // 局部捕获句柄：Dispose 可能把 _handle 置 null，防止空引用进 P/Invoke
                    SafeFileHandle handle = _handle;
                    if (handle == null || handle.IsClosed) break;

                    byte[] buf = new byte[len];
                    ManualResetEvent evt = new ManualResetEvent(false);
                    try
                    {
                        HidNative.OVERLAPPED ov = new HidNative.OVERLAPPED();
                        ov.hEvent = evt.SafeWaitHandle.DangerousGetHandle();
                        bool ok;
                        int err;
                        try
                        {
                            ok = HidNative.ReadFileOverlapped(handle, buf, (uint)len, IntPtr.Zero, ref ov);
                            err = ok ? 0 : Marshal.GetLastWin32Error();
                        }
                        catch (Exception)
                        {
                            break; // 句柄已被 Dispose 关闭/置空 → 读循环结束
                        }
                        if (ok)
                        {
                            EnqueueReport(buf, len);
                            continue;
                        }
                        if (err != HidNative.ERROR_IO_PENDING) break; // 句柄已关闭
                        evt.WaitOne(); // Dispose 时 CancelIoEx/CloseHandle 会唤醒
                        if (_disposed) break;
                        uint xfer;
                        if (!HidNative.GetOverlappedResult(handle, ref ov, out xfer, false) || xfer == 0)
                        {
                            if (_disposed) break;
                            continue;
                        }
                        EnqueueReport(buf, (int)xfer);
                    }
                    finally { evt.Dispose(); }
                }
            }
            catch (Exception)
            {
                // 兜底：后台线程未处理异常会终止整个进程，这里必须吞掉
            }
        }

        private void EnqueueReport(byte[] buf, int len)
        {
            byte[] copy = new byte[len];
            Array.Copy(buf, copy, len);
            try { _ioReports.Add(copy); }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        public bool FeatureSet(byte[] report)
        {
            if (_handle == null) return false;
            return HidNative.HidD_SetFeature(_handle, report, report.Length);
        }

        public byte[] FeatureGet(int reportId, int length)
        {
            if (_handle == null) return null;
            byte[] buf = new byte[length];
            buf[0] = (byte)reportId;
            if (!HidNative.HidD_GetFeature(_handle, buf, length)) return null;
            return buf;
        }

        public byte[] IoTransfer(byte[] outputReport, int timeoutMs)
        {
            if (_handle == null) return null;
            byte[] stale;
            while (_ioReports.TryTake(out stale)) { } // 丢弃残留报文

            // 重叠写入中断 OUT 管道（控制传输 SetOutputReport 会被部分设备忽略）
            ManualResetEvent evt = new ManualResetEvent(false);
            try
            {
                HidNative.OVERLAPPED ov = new HidNative.OVERLAPPED();
                ov.hEvent = evt.SafeWaitHandle.DangerousGetHandle();
                uint written;
                bool ok = HidNative.WriteFileOverlapped(
                    _handle, outputReport, (uint)outputReport.Length, IntPtr.Zero, ref ov);
                int err = ok ? 0 : Marshal.GetLastWin32Error();
                if (!ok)
                {
                    if (err != HidNative.ERROR_IO_PENDING) return null;
                    if (!evt.WaitOne(timeoutMs))
                    {
                        HidNative.CancelIoEx(_handle, ref ov); // 写超时：取消，防止堆积
                        return null;
                    }
                    if (!HidNative.GetOverlappedResult(_handle, ref ov, out written, false))
                        return null;
                }

                byte[] resp;
                if (_ioReports.TryTake(out resp, timeoutMs)) return resp;
                return null;
            }
            finally { evt.Dispose(); }
        }

        /// <summary>从后台读队列等待一条输入报告（供活动监视等旁路消费者使用）。</summary>
        public byte[] WaitForReport(int timeoutMs)
        {
            byte[] r;
            if (_ioReports.TryTake(out r, timeoutMs)) return r;
            return null;
        }

        public void Dispose()
        {
            _disposed = true;
            if (_handle != null)
            {
                try { HidNative.CancelIoEx(_handle, IntPtr.Zero); } catch { } // 唤醒挂起的重叠读
                _handle.Dispose(); // 关闭句柄使后台 ReadLoop 退出
                _handle = null;
            }
            try { _ioReports.Dispose(); } catch { }
        }
    }

    /// <summary>
    /// 鼠标活动监视：以共享方式打开标准鼠标接口（usagePage 0x01 / usage 0x02），
    /// 任何输入报告 = 用户在动鼠标 = 鼠标在线。用于电量跳变过滤的放行判据：
    /// 鼠标活动时接收器回报的电量才可信；鼠标没动时的大跳变视为休眠垃圾值。
    /// 打开失败则静默停用（跳变过滤退回"30 分钟持续放行"的兜底路径）。
    /// </summary>
    internal static class MouseActivityMonitor
    {
        private static long _lastTicksUtc = DateTime.MinValue.Ticks;

        /// <summary>最后一次看到鼠标动作的 UTC 时间；从未看到则为 DateTime.MinValue。</summary>
        public static DateTime LastActivityUtc
        {
            get { return new DateTime(Interlocked.Read(ref _lastTicksUtc), DateTimeKind.Utc); }
        }

        public static void Start(ushort preferredVid)
        {
            try
            {
                List<HidDeviceInfo> list = HidNative.Enumerate();
                HidDeviceInfo target = null;
                foreach (HidDeviceInfo d in list)
                    if (d.UsagePage == 0x01 && d.Usage == 0x02 && d.Vid == preferredVid)
                    { target = d; break; }
                if (target == null)
                    foreach (HidDeviceInfo d in list)
                        if (d.UsagePage == 0x01 && d.Usage == 0x02)
                        { target = d; break; }
                if (target == null) return;

                HidDevice dev = new HidDevice();
                if (!dev.Open(target)) return;

                Thread t = new Thread(delegate()
                {
                    while (true)
                    {
                        byte[] r = dev.WaitForReport(2000);
                        if (r != null && r.Length > 0)
                            Interlocked.Exchange(ref _lastTicksUtc, DateTime.UtcNow.Ticks);
                    }
                });
                t.IsBackground = true;
                t.Start();
            }
            catch { }
        }
    }
}
