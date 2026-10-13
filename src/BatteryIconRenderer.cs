using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Battery24GTray
{
    /// <summary>
    /// 托盘电池图标渲染（高对比度、64x64）：
    /// 深色圆形底色 + 彩色电量弧 + 白色大字号百分比；充电时叠加闪电。
    /// </summary>
    internal static class BatteryIconRenderer
    {
        private const int Size = 64;

        /// <summary>渲染指定电量的圆形图标。</summary>
        public static Icon Render(int percent, bool charging)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            var bmp = new Bitmap(Size, Size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                // 深色圆底（在所有主题下都醒目）
                using (var bg = new SolidBrush(Color.FromArgb(40, 40, 50)))
                    g.FillEllipse(bg, 0, 0, Size, Size);

                // 亮色外圈边框
                using (var border = new Pen(Color.FromArgb(100, 100, 120), 2f))
                    g.DrawEllipse(border, 1f, 1f, Size - 2f, Size - 2f);

                // 电量进度弧颜色映射
                Color arcColor = charging ? Color.FromArgb(33, 150, 243)   // 蓝色=充电
                    : percent > 50 ? Color.FromArgb(76, 175, 80)           // 绿色=充足
                    : percent > 20 ? Color.FromArgb(255, 152, 0)          // 橙色=中等
                    : Color.FromArgb(244, 67, 54);                        // 红色=低电量

                using (var arc = new Pen(arcColor, 5f))
                {
                    if (percent >= 100)
                        g.DrawEllipse(arc, 5f, 5f, Size - 10f, Size - 10f);
                    else if (percent > 0)
                        g.DrawArc(arc, 5f, 5f, Size - 10f, Size - 10f, -90f, 3.6f * percent);
                }

                // 百分比文字（白色，居中）
                string txt = percent.ToString(CultureInfo.InvariantCulture);
                float fontSize = txt.Length == 1 ? 28f : txt.Length == 2 ? 22f : 18f;
                using (var f = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fg = new SolidBrush(Color.White))
                using (var sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString(txt, f, fg, new RectangleF(0f, 0f, Size, Size), sf);
                }

                if (charging) DrawBolt(g);
            }
            return CreateIconFromBitmap(bmp);
        }

        /// <summary>渲染"无数据"图标。</summary>
        public static Icon RenderUnknown()
        {
            using (var bmp = new Bitmap(Size, Size))
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                using (var bg = new SolidBrush(Color.FromArgb(40, 40, 50)))
                    g.FillEllipse(bg, 0, 0, Size, Size);

                using (var border = new Pen(Color.FromArgb(100, 100, 120), 2.5f))
                {
                    border.DashStyle = DashStyle.Dash;
                    g.DrawEllipse(border, 2f, 2f, Size - 4f, Size - 4f);
                }

                using (var f = new Font("Segoe UI", 20f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (var fg = new SolidBrush(Color.FromArgb(150, 150, 170)))
                using (var sf = new StringFormat())
                {
                    sf.Alignment = StringAlignment.Center;
                    sf.LineAlignment = StringAlignment.Center;
                    g.DrawString("--", f, fg, new RectangleF(0f, 0f, Size, Size), sf);
                }

                return CreateIconFromBitmap(bmp);
            }
        }

        private static void DrawBolt(Graphics g)
        {
            var bolt = new PointF[]
            {
                new PointF(44f, 22f),
                new PointF(32f, 42f),
                new PointF(38f, 42f),
                new PointF(34f, 56f),
                new PointF(48f, 34f),
                new PointF(42f, 34f),
                new PointF(46f, 22f)
            };
            using (var b = new SolidBrush(Color.FromArgb(255, 235, 59)))
                g.FillPolygon(b, bolt);
        }

        private static Icon CreateIconFromBitmap(Bitmap bmp)
        {
            IntPtr hIcon = bmp.GetHicon();
            var icon = Icon.FromHandle(hIcon);
            // NOTE: Do NOT call DestroyIcon here — Icon.FromHandle wraps the native handle,
            // destroying it would leave the managed Icon with a dangling handle (invisible tray icon).
            // GDI+ will release the hIcon when the Icon is disposed.
            return icon;
        }

        private static class NativeMethods
        {
            [DllImport("user32.dll", SetLastError = true)]
            internal static extern bool DestroyIcon(IntPtr hIcon);
        }
    }
}
