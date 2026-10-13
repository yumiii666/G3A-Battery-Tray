using System;
using System.Collections.Generic;

namespace Battery24GTray
{
    /// <summary>一次电量读取结果。</summary>
    public class BatteryReading
    {
        public int? Percent;       // 0-100；null = 未知
        public bool Charging;      // 充电中
        public bool Online;        // 设备当前可达
        public string SourceName;  // 数据来源描述
        public string Detail;      // 附加信息（协议细节等）
    }

    /// <summary>电量数据源抽象：2.4G 接收器、模拟设备等。</summary>
    public interface IBatteryProvider
    {
        string Name { get; }
        BatteryReading Read();
    }

    /// <summary>按优先级聚合多个数据源，返回第一个有效的读数。</summary>
    public class ProviderChain : IBatteryProvider
    {
        private readonly List<IBatteryProvider> _list = new List<IBatteryProvider>();

        public string Name { get { return "Auto"; } }

        public void Add(IBatteryProvider provider) { _list.Add(provider); }

        public BatteryReading Read()
        {
            foreach (IBatteryProvider p in _list)
            {
                try
                {
                    BatteryReading r = p.Read();
                    if (r != null && r.Online && r.Percent.HasValue) return r;
                }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// 模拟数据源（--demo 演示模式）：电量缓慢循环放电/充电，
    /// 用于在没有任何 2.4G 接收器时验证托盘 UI 与刷新链路。
    /// </summary>
    public class SimulatedProvider : IBatteryProvider
    {
        private readonly Random _rnd = new Random();
        private int _level = 76;
        private bool _charging;
        private DateTime _last = DateTime.UtcNow;

        public string Name { get { return "Simulated 2.4G Mouse"; } }

        public BatteryReading Read()
        {
            DateTime now = DateTime.UtcNow;
            if ((now - _last).TotalSeconds >= 8)
            {
                _last = now;
                if (!_charging)
                {
                    _level -= 1 + _rnd.Next(2);
                    if (_level <= 8) { _level = 8; _charging = true; }
                }
                else
                {
                    _level += 2;
                    if (_level >= 96) { _level = 96; _charging = false; }
                }
            }
            return new BatteryReading
            {
                Percent = _level,
                Charging = _charging,
                Online = true,
                SourceName = Name,
                Detail = "演示模式"
            };
        }
    }
}
