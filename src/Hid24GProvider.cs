using System;
using System.Collections.Generic;

namespace Battery24GTray
{
    /// <summary>
    /// 2.4G USB 接收器电量数据源：
    /// 枚举 USB HID 设备 → 匹配已注册厂商协议（内置罗技 + protocols.ini 配置）→ 查询电量。
    /// 找到可用接收器后缓存设备路径，轮询时走快路径，降低常驻开销；
    /// 拔出或读取失败时自动回退全量扫描。
    /// </summary>
    public class Hid24GProvider : IBatteryProvider
    {
        private readonly List<IWirelessProtocol> _protocols = new List<IWirelessProtocol>();
        private HidDeviceInfo _cachedInfo;
        private IWirelessProtocol _cachedProtocol;

        public string Name { get { return "2.4G Receiver"; } }

        public Hid24GProvider()
        {
            _protocols.Add(new LogitechHidPlusPlus());
            _protocols.Add(new MchoseProtocol());
            _protocols.AddRange(ConfigurableProtocol.LoadAll());
        }

        public BatteryReading Read()
        {
            // 快路径：上次成功的接收器（避免每 30 秒全量枚举）
            if (_cachedInfo != null && _cachedProtocol != null)
            {
                try
                {
                    using (HidDevice fast = new HidDevice())
                    {
                        if (fast.Open(_cachedInfo))
                        {
                            BatteryReading r = _cachedProtocol.Query(fast);
                            if (r != null && r.Percent.HasValue) return r;
                        }
                    }
                }
                catch { }
                _cachedInfo = null; // 快路径失败 → 重新全量扫描
                _cachedProtocol = null;
            }
            return FullScan();
        }

        private BatteryReading FullScan()
        {
            List<HidDeviceInfo> devices;
            try { devices = HidNative.Enumerate(); }
            catch { return null; }

            foreach (HidDeviceInfo info in devices)
            {
                foreach (IWirelessProtocol proto in _protocols)
                {
                    if (!proto.Matches(info)) continue;
                    HidDevice dev = new HidDevice();
                    try
                    {
                        if (!dev.Open(info)) continue;
                        BatteryReading r = proto.Query(dev);
                        if (r != null && r.Percent.HasValue)
                        {
                            _cachedInfo = info;
                            _cachedProtocol = proto;
                            return r;
                        }
                    }
                    catch { }
                    finally { dev.Dispose(); }
                }
            }
            return null;
        }
    }
}
