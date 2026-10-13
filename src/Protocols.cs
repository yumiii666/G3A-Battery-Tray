using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;

namespace Battery24GTray
{
    /// <summary>鍘傚晢 2.4G 鏃犵嚎鍗忚鎶借薄锛氬尮閰嶆帴鏀跺櫒骞舵煡璇㈢數閲忋€?/summary>
    internal interface IWirelessProtocol
    {
        string Name { get; }
        bool Matches(HidDeviceInfo device);
        BatteryReading Query(HidDevice device);
    }

    /// <summary>
    /// 缃楁妧 HID++ 2.0 鍗忚锛圲nifying / Lightspeed / Bolt 鎺ユ敹鍣級銆?
    /// 娴佺▼: GetFeatureIndex(0x1000) 鈫?GetBatteryLevelStatus锛?
    /// 澶囬€?0x1004 UnifiedBattery銆?
    /// 杩欐槸鍏朵粬鍘傚晢绉佹湁鍗忚閫傞厤鐨勬爣鍑嗗弬鑰冨疄鐜帮紙涓?EasyBluetooth 鐨勫仛娉曚竴鑷达細
    /// 閫氳繃鎺ユ敹鍣ㄧ殑 vendor HID 閫氶亾鏀跺彂绉佹湁鎶ユ枃锛屼粠鍝嶅簲瀛楄妭涓В鏋愮數閲忥級銆?
    /// </summary>
    internal class LogitechHidPlusPlus : IWirelessProtocol
    {
        private const byte ReportIdShort = 0x10;
        private const byte SwId = 0x0A;

        public string Name { get { return "Logitech HID++ 2.0"; } }

        public bool Matches(HidDeviceInfo d)
        {
            // 缃楁妧 VID 046D锛汬ID++ 鎶ユ枃鍙瓨鍦ㄤ簬 vendor-defined(0xFFxx) 椤跺眰闆嗗悎
            return d.Vid == 0x046D && d.UsagePage >= 0xFF00;
        }

        public BatteryReading Query(HidDevice dev)
        {
            // 涓€涓帴鏀跺櫒鏈€澶氭寕 5 鍙版棤绾胯澶囷紝閫愪釜灏濊瘯
            for (byte devIdx = 1; devIdx <= 5; devIdx++)
            {
                byte featIdx = GetFeatureIndex(dev, devIdx, 0x1000);
                if (featIdx != 0)
                {
                    BatteryReading r = QueryBatteryStatus(dev, devIdx, featIdx);
                    if (r != null) return r;
                }
                else
                {
                    byte unified = GetFeatureIndex(dev, devIdx, 0x1004);
                    if (unified != 0)
                    {
                        BatteryReading r2 = QueryUnifiedBattery(dev, devIdx, unified);
                        if (r2 != null) return r2;
                    }
                }
            }
            return null;
        }

        // feature 0x1000 fn0 GetBatteryLevelStatus:
        // 鍝嶅簲: [id, dev, feat, fn, level(0-100), nextLevel, status]
        private BatteryReading QueryBatteryStatus(HidDevice dev, byte devIdx, byte featIdx)
        {
            byte[] resp = FeatureCall(dev, devIdx, featIdx, 0, 0, 0, 0);
            if (resp == null || resp.Length < 8) return null;
            if (resp[0] != ReportIdShort || resp[1] != devIdx || resp[2] != featIdx) return null;

            int level = resp[4] & 0xFF;
            byte status = resp[6];
            if (level == 0 || level > 100) return null;

            bool charging = status == 0x01 || status == 0x02 || status == 0x03;
            return new BatteryReading
            {
                Percent = level,
                Charging = charging,
                Online = true,
                SourceName = "Logitech 2.4G",
                Detail = string.Format(CultureInfo.InvariantCulture, "dev{0} status=0x{1:X2}", devIdx, status)
            };
        }

        // feature 0x1004 fn1 GetStatus:
        // 鍝嶅簲: [id, dev, feat, fn, levelLo, levelHi, state]
        private BatteryReading QueryUnifiedBattery(HidDevice dev, byte devIdx, byte featIdx)
        {
            byte[] resp = FeatureCall(dev, devIdx, featIdx, 1, 0, 0, 0);
            if (resp == null || resp.Length < 8) return null;
            if (resp[0] != ReportIdShort || resp[1] != devIdx || resp[2] != featIdx) return null;

            int level = (resp[4] & 0xFF) | ((resp[5] & 0xFF) << 8);
            byte state = resp[6];
            if (level == 0 || level > 100) return null;

            bool charging = state == 0x01 || state == 0x02;
            return new BatteryReading
            {
                Percent = level,
                Charging = charging,
                Online = true,
                SourceName = "Logitech 2.4G",
                Detail = "feature 0x1004"
            };
        }

        // Root.feature(0x0000) fn0 GetFeature: 鏌ヨ feature code 瀵瑰簲鐨勮繍琛屾湡绱㈠紩
        private byte GetFeatureIndex(HidDevice dev, byte devIdx, ushort featureCode)
        {
            byte[] resp = FeatureCall(dev, devIdx, 0x00, 0,
                (byte)(featureCode >> 8), (byte)(featureCode & 0xFF), 0x00);
            if (resp == null || resp.Length < 6) return 0;
            if (resp[0] != ReportIdShort || resp[1] != devIdx) return 0;
            if (resp[2] == 0x8F) return 0; // HID++ 閿欒鍝嶅簲: 璇ヨ澶囦笉鏀寔姝?feature
            if (resp[2] != 0x00) return 0;
            return resp[4];
        }

        private byte[] FeatureCall(HidDevice dev, byte devIdx, byte featIdx, byte fn, byte p1, byte p2, byte p3)
        {
            byte[] req = new byte[8];
            req[0] = ReportIdShort;
            req[1] = devIdx;
            req[2] = featIdx;
            req[3] = (byte)((fn << 4) | SwId);
            req[4] = p1;
            req[5] = p2;
            req[6] = p3;

            if (!dev.FeatureSet(req)) return null;
            Thread.Sleep(12); // 绛夊緟鎺ユ敹鍣ㄥ鐞嗗苟灏嗗搷搴斿啓鍏?feature 缂撳啿
            return dev.FeatureGet(ReportIdShort, 8);
        }
    }

    /// <summary>
    /// 杩堜粠(MCHOSE) 2.4G 鍗忚锛岃鐩栦袱涓崗璁棌锛堝潎閫嗗悜鑷畼鏂?M HUB 椹卞姩锛夛細
    ///  1. G3 A 鏂板崗璁棌锛圴ID A8A5锛夛細vendor collection 0xFF01 涓婄殑 65 瀛楄妭
    ///     output/input 鎶ュ憡锛圵ebHID 鍚屾甯э級锛屽懡浠?0x55 0x30 鏌ヨ鐢甸噺锛?
    ///  2. L7/M7/A7/K7 鏃у崗璁棌锛圴ID 5253/3837锛夛細feature report 0x11 鍛戒护 0x06锛?
    ///     杞借嵎鏁翠綋鍙栧弽(^0xFF)锛屽搷搴斿甫鍥炴樉鏍￠獙銆?
    /// </summary>
    internal class MchoseProtocol : IWirelessProtocol
    {
        public string Name { get { return "MCHOSE 2.4G"; } }

        public bool Matches(HidDeviceInfo d)
        {
            // G3 A锛氭帴鏀跺櫒 vendor 閫氶亾 65 瀛楄妭 in/out锛堟棤 feature report锛?
            if (d.Vid == 0xA8A5 && d.UsagePage == 0xFF01 &&
                d.OutputLen == 65 && d.InputLen == 65)
                return true;
            // L7/M7/A7/K7锛歠eature report 閫氶亾锛?x11 鐭抚 20 瀛楄妭 + report id = 21锛?
            if ((d.Vid == 0x5253 || d.Vid == 0x3837) &&
                d.UsagePage >= 0xFF00 && d.FeatureLen >= 21)
                return true;
            return false;
        }

        public BatteryReading Query(HidDevice dev)
        {
            if (dev.Info.OutputLen == 65 && dev.Info.InputLen == 65)
                return QueryInterrupt(dev);   // G3 A 鏃?
            return QueryFeature(dev);         // L7 鏃?
        }

        // G3 A锛歰utput 鎶ュ憡璇锋眰 鈫?input 鎶ュ憡鍝嶅簲锛堥€嗗悜鑷?M HUB 缃戦〉椹卞姩 getBatteryInfo锛?
        // 璇锋眰: [00] 55 30 A5 0B 2E 01 01 01 00 00 ...
        // 鍝嶅簲杞借嵎: [0]=AA [1]=30(鍥炴樉) ... [8]=鐢甸噺 [9]=鍏呯數鏍囧織
        private BatteryReading QueryInterrupt(HidDevice dev)
        {
            byte[] req = new byte[65];
            req[0] = 0x00;          // report id
            req[1] = 0x55;
            req[2] = 0x30;          // 鐢甸噺鏌ヨ鍛戒护
            req[3] = 0xA5;
            req[4] = 0x0B;
            req[5] = 0x2E;
            req[6] = 1; req[7] = 1; req[8] = 1;

            // 榧犳爣浼戠湢鏃舵帴鏀跺櫒鍙兘鏃犲搷搴旓紝澶氶噸璇曞嚑娆?
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] resp = dev.IoTransfer(req, 250);
                BatteryReading r = ParseInterruptResponse(resp);
                if (r != null) return r;
                Thread.Sleep(40);
            }
            return null;
        }

        private static BatteryReading ParseInterruptResponse(byte[] resp)
        {
            if (resp == null || resp.Length < 10) return null;
            // 0xAA 0x30 鍙兘浠庝笅鏍?0锛堟棤 report id 鍓嶇紑锛夋垨 1锛圵indows 鍓嶇紑 0x00锛夊紑濮?
            for (int i = 0; i <= 1; i++)
            {
                if (resp[i] == 0xAA && resp[i + 1] == 0x30 && i + 9 < resp.Length)
                {
                    int pct = resp[i + 8] & 0xFF;
                    if (pct > 100) return null;
                    return new BatteryReading
                    {
                        Percent = pct,
                        Charging = (resp[i + 9] & 0xFF) != 0,
                        Online = true,
                        SourceName = "MCHOSE 2.4G",
                        Detail = "g3 0x55,0x30"
                    };
                }
            }
            return null;
        }

        // L7 鏃忥細feature report 0x11 鍛戒护 0x06锛岃浇鑽峰彇鍙嶏紱鍝嶅簲 [0]=11 [1]=cmd^FF [2..]=杞借嵎^FF
        // 鐢甸噺 = resp[11]^FF锛坧ayload: vid:u16,pid:u16,fw:u32,flags:u8,battery:u8,charge:u8锛?
        private BatteryReading QueryFeature(HidDevice dev)
        {
            const byte cmd = 0x06;
            int len = dev.Info.FeatureLen >= 21 ? dev.Info.FeatureLen : 21;
            byte[] req = new byte[len];
            req[0] = 0x11;                       // report id 涓嶅彇鍙?
            for (int i = 1; i < len; i++)
                req[i] = (byte)((i == 1 ? cmd : 0) ^ 0xFF);

            for (int attempt = 0; attempt < 6; attempt++)  // 瀹樻柟椹卞姩鍚屾锛氬洖鏄句笉鍖归厤閲嶈瘯 6 娆?
            {
                if (!dev.FeatureSet(req)) return null;
                Thread.Sleep(20);
                byte[] resp = dev.FeatureGet(0x11, len);
                if (resp != null && resp.Length >= 13 &&
                    resp[0] == 0x11 && resp[1] == (cmd ^ 0xFF))
                {
                    int pct = (resp[11] ^ 0xFF) & 0xFF;
                    if (pct > 100) return null;
                    return new BatteryReading
                    {
                        Percent = pct,
                        Charging = ((resp[12] ^ 0xFF) & 0xFF) != 0,
                        Online = true,
                        SourceName = "MCHOSE 2.4G",
                        Detail = "feature 0x11/0x06"
                    };
                }
                Thread.Sleep(25);
            }
            return null;
        }
    }

    /// <summary>
    /// 閰嶇疆椹卞姩鐨勫巶鍟嗗崗璁紙protocols.ini锛夛紝瀵瑰簲 EasyBluetooth 鐨勭ぞ鍖洪€傞厤娴佺▼:
    /// 鎶撳寘锛圲SBPcap锛夆啋 瀹氫綅鐢甸噺瀛楁 鈫?鍐欓厤缃嵆鐢熸晥锛屾棤闇€鏀逛唬鐮併€?
    /// 杩堜粠(MCHOSE)/VGN/ATK 绛夋湭鍏紑鍗忚鐨勬帴鏀跺櫒鍧囧彲鐢ㄦ鏂瑰紡鎺ュ叆銆?
    /// </summary>
    internal class ConfigurableProtocol : IWirelessProtocol
    {
        private readonly string _name;
        private readonly ushort _vid;
        private readonly List<ushort> _pids;
        private readonly string _mode;
        private readonly byte[] _request;
        private readonly int _responseReportId;
        private readonly int _responseLen;
        private readonly int _batteryByte;
        private readonly int _chargingByte;
        private readonly int _chargingMask;
        private readonly string _scale;
        private readonly int _minMv;
        private readonly int _maxMv;
        private readonly int _ioTimeoutMs;

        private ConfigurableProtocol(string name, ushort vid, List<ushort> pids, string mode,
            byte[] request, int responseReportId, int responseLen, int batteryByte,
            int chargingByte, int chargingMask, string scale, int minMv, int maxMv, int ioTimeoutMs)
        {
            _name = name;
            _vid = vid;
            _pids = pids;
            _mode = mode;
            _request = request;
            _responseReportId = responseReportId;
            _responseLen = responseLen;
            _batteryByte = batteryByte;
            _chargingByte = chargingByte;
            _chargingMask = chargingMask;
            _scale = scale;
            _minMv = minMv;
            _maxMv = maxMv;
            _ioTimeoutMs = ioTimeoutMs;
        }

        public string Name { get { return _name; } }

        public bool Matches(HidDeviceInfo d)
        {
            if (d.Vid != _vid) return false;
            if (_pids.Count > 0 && !_pids.Contains(d.Pid)) return false;
            return true;
        }

        public BatteryReading Query(HidDevice dev)
        {
            byte[] resp;
            if (_mode == "io")
            {
                resp = dev.IoTransfer(_request, _ioTimeoutMs);
            }
            else
            {
                if (!dev.FeatureSet(_request)) return null;
                Thread.Sleep(15);
                int len = _responseLen > 0 ? _responseLen
                    : (dev.Info.FeatureLen > 0 ? dev.Info.FeatureLen : _request.Length);
                resp = dev.FeatureGet(_responseReportId, len);
            }
            if (resp == null) return null;
            if (_batteryByte < 0 || _batteryByte >= resp.Length) return null;

            int percent;
            if (_scale == "linear")
            {
                int mv = ((resp[_batteryByte] & 0xFF) << 8);
                if (_batteryByte + 1 < resp.Length) mv |= (resp[_batteryByte + 1] & 0xFF);
                if (_maxMv <= _minMv) return null;
                percent = (int)Math.Round((mv - _minMv) * 100.0 / (_maxMv - _minMv));
            }
            else
            {
                percent = resp[_batteryByte] & 0xFF;
            }
            if (percent < 0 || percent > 100) return null; // 瓒婄晫瑙嗕负鏃犳晥鎶ユ枃

            bool charging = false;
            if (_chargingByte >= 0 && _chargingByte < resp.Length)
                charging = (resp[_chargingByte] & _chargingMask) != 0;

            return new BatteryReading
            {
                Percent = percent,
                Charging = charging,
                Online = true,
                SourceName = _name,
                Detail = "config"
            };
        }

        /// <summary>浠?exe 鍚岀洰褰?protocols.ini 鍔犺浇鍏ㄩ儴鍗忚鑺傘€?/summary>
        internal static List<ConfigurableProtocol> LoadAll()
        {
            List<ConfigurableProtocol> list = new List<ConfigurableProtocol>();
            string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protocols.ini");
            if (!File.Exists(file)) return list;
            try
            {
                Dictionary<string, Dictionary<string, string>> sections = ParseIni(file);
                foreach (KeyValuePair<string, Dictionary<string, string>> kv in sections)
                {
                    ConfigurableProtocol p = Build(kv.Key, kv.Value);
                    if (p != null) list.Add(p);
                }
            }
            catch { }
            return list;
        }

        private static ConfigurableProtocol Build(string section, Dictionary<string, string> c)
        {
            try
            {
                if (!c.ContainsKey("vid") || !c.ContainsKey("request")) return null;

                ushort vid = ParseHex(c["vid"]);
                List<ushort> pids = new List<ushort>();
                if (c.ContainsKey("pids") && c["pids"].Trim().Length > 0)
                {
                    foreach (string t in c["pids"].Split(new char[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                        pids.Add(ParseHex(t));
                }

                byte[] request = ParseHexBytes(c["request"]);
                if (request.Length < 2) return null;

                string mode = c.ContainsKey("mode") ? c["mode"].Trim().ToLowerInvariant() : "feature";
                int responseReportId = c.ContainsKey("responsereportid") ? (int)ParseHex(c["responsereportid"]) : request[0];
                int responseLen = GetInt(c, "responselen", 0);
                int batteryByte = GetInt(c, "batterybyte", 4);

                int chargingByte = -1, chargingMask = 0x01;
                if (c.ContainsKey("charging") && c["charging"].Trim().Length > 0)
                {
                    string[] parts = c["charging"].Split(':');
                    chargingByte = int.Parse(parts[0].Trim(), CultureInfo.InvariantCulture);
                    if (parts.Length > 1) chargingMask = (int)ParseHex(parts[1].Trim());
                }

                string scale = c.ContainsKey("scale") ? c["scale"].Trim().ToLowerInvariant() : "direct";
                int minMv = GetInt(c, "minmv", 3200);
                int maxMv = GetInt(c, "maxmv", 4200);
                int ioTimeout = GetInt(c, "iotimeoutms", 300);
                string name = c.ContainsKey("name") ? c["name"] : section;

                return new ConfigurableProtocol(name, vid, pids, mode, request,
                    responseReportId, responseLen, batteryByte, chargingByte, chargingMask,
                    scale, minMv, maxMv, ioTimeout);
            }
            catch { return null; }
        }

        private static int GetInt(Dictionary<string, string> c, string key, int def)
        {
            string v;
            if (!c.TryGetValue(key, out v)) return def;
            int r;
            if (int.TryParse(v.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out r)) return r;
            return def;
        }

        private static Dictionary<string, Dictionary<string, string>> ParseIni(string file)
        {
            Dictionary<string, Dictionary<string, string>> sections =
                new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
            string current = null;
            foreach (string raw in File.ReadAllLines(file))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    current = line.Substring(1, line.Length - 2).Trim();
                    if (current.Length > 0 && !sections.ContainsKey(current))
                        sections[current] = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    continue;
                }
                int eq = line.IndexOf('=');
                if (eq <= 0 || current == null) continue;
                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();
                sections[current][key] = val;
            }
            return sections;
        }

        private static ushort ParseHex(string s)
        {
            s = s.Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);
            return Convert.ToUInt16(s, 16);
        }

        private static byte[] ParseHexBytes(string s)
        {
            s = s.Trim().Replace("0x", "").Replace(",", " ").Replace("-", " ");
            List<byte> b = new List<byte>();
            foreach (string t in s.Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
                b.Add(Convert.ToByte(t, 16));
            return b.ToArray();
        }
    }
}
