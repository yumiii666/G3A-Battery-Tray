using System;
using Microsoft.Win32;

namespace Battery24GTray
{
    /// <summary>
    /// 开机自启动管理：HKCU\...\Run 注册表键。
    /// 当前用户级别，无需管理员权限，卸载/关闭时删除即可。
    /// </summary>
    public static class AutoStartManager
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppName = "Battery24GTray";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null) return false;
                    object v = k.GetValue(AppName);
                    return v != null && v.ToString().Length > 0;
                }
            }
            catch { return false; }
        }

        public static void SetEnabled(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    if (k == null) return;
                    if (enable)
                    {
                        string exe = typeof(AutoStartManager).Assembly.Location;
                        k.SetValue(AppName, "\"" + exe + "\"");
                    }
                    else if (k.GetValue(AppName) != null)
                    {
                        k.DeleteValue(AppName, false);
                    }
                }
            }
            catch { }
        }
    }
}
