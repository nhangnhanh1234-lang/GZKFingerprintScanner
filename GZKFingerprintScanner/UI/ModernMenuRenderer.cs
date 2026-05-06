using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace GZKFingerprintScanner.UI
{
    /// <summary>
    /// Bảng màu theo phong cách Fluent / Windows 11 (dark theme).
    /// </summary>
    internal sealed class ModernColorTable : ProfessionalColorTable
    {
        public static readonly Color Background = Color.FromArgb(32, 32, 32);
        public static readonly Color BackgroundHover = Color.FromArgb(50, 50, 50);
        public static readonly Color BackgroundPressed = Color.FromArgb(64, 64, 64);
        public static readonly Color Border = Color.FromArgb(60, 60, 60);
        public static readonly Color Separator = Color.FromArgb(60, 60, 60);
        public static readonly Color Text = Color.FromArgb(232, 232, 232);
        public static readonly Color TextDisabled = Color.FromArgb(150, 150, 150);
        public static readonly Color Accent = Color.FromArgb(0, 120, 212);

        public override Color MenuBorder { get { return Border; } }
        public override Color MenuItemBorder { get { return BackgroundHover; } }
        public override Color MenuItemSelected { get { return BackgroundHover; } }
        public override Color MenuItemSelectedGradientBegin { get { return BackgroundHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return BackgroundHover; } }
        public override Color MenuItemPressedGradientBegin { get { return BackgroundPressed; } }
        public override Color MenuItemPressedGradientEnd { get { return BackgroundPressed; } }
        public override Color ToolStripDropDownBackground { get { return Background; } }
        public override Color ImageMarginGradientBegin { get { return Background; } }
        public override Color ImageMarginGradientMiddle { get { return Background; } }
        public override Color ImageMarginGradientEnd { get { return Background; } }
        public override Color SeparatorDark { get { return Separator; } }
        public override Color SeparatorLight { get { return Separator; } }
        public override Color CheckBackground { get { return Accent; } }
        public override Color CheckSelectedBackground { get { return Accent; } }
        public override Color CheckPressedBackground { get { return Accent; } }
    }

    /// <summary>
    /// Renderer dark theme có padding rộng, font Segoe UI, không vẽ image margin column dạng cũ.
    /// </summary>
    internal sealed class ModernMenuRenderer : ToolStripProfessionalRenderer
    {
        public ModernMenuRenderer() : base(new ModernColorTable())
        {
            RoundedEdges = false;
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var brush = new SolidBrush(ModernColorTable.Background))
            {
                e.Graphics.FillRectangle(brush, e.AffectedBounds);
            }
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            // Vẽ viền 1px nhẹ nhàng quanh menu
            using (var pen = new Pen(ModernColorTable.Border))
            {
                var r = e.AffectedBounds;
                e.Graphics.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? ModernColorTable.Text : ModernColorTable.TextDisabled;
            e.TextFont = e.Item.Font;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var rect = e.Item.ContentRectangle;
            int y = rect.Height / 2;
            using (var pen = new Pen(ModernColorTable.Separator))
            {
                e.Graphics.DrawLine(pen, rect.Left + 8, y, rect.Right - 8, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            // Vẽ dấu check tròn nhỏ kiểu hiện đại
            var bounds = e.ImageRectangle;
            int size = 14;
            int x = bounds.Left + (bounds.Width - size) / 2;
            int y = bounds.Top + (bounds.Height - size) / 2;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(ModernColorTable.Accent))
            {
                e.Graphics.FillEllipse(brush, x, y, size, size);
            }
            using (var pen = new Pen(Color.White, 2f))
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new Point(x + 3, y + 7),
                    new Point(x + 6, y + 10),
                    new Point(x + 11, y + 4)
                });
            }
        }
    }
}
