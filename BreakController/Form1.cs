using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Serialization;
using Microsoft.Win32;

namespace BreakController
{
    public class AppSettings
    {
        public int WorkMinutes { get; set; } = 60;
        public int RestMinutes { get; set; } = 3;
        public bool AutoStart { get; set; } = false;
        private static string FilePath = Path.Combine(Application.StartupPath, "settings.xml");

        public void Save()
        {
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                using (StreamWriter writer = new StreamWriter(FilePath)) serializer.Serialize(writer, this);
                SetStartup(AutoStart);
            }
            catch { }
        }

        public static AppSettings Load()
        {
            if (!File.Exists(FilePath)) return new AppSettings();
            try
            {
                XmlSerializer serializer = new XmlSerializer(typeof(AppSettings));
                using (StreamReader reader = new StreamReader(FilePath)) return (AppSettings)serializer.Deserialize(reader);
            }
            catch { return new AppSettings(); }
        }

        private void SetStartup(bool start)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (start) rk.SetValue("BreakController", Application.ExecutablePath);
                    else rk.DeleteValue("BreakController", false);
                }
            }
            catch { }
        }
    }

    public partial class Form1 : Form
    {
        private Timer breakTimer;
        private NotifyIcon trayIcon;
        private AppSettings settings;
        private Icon appIcon = SystemIcons.Shield;

        [DllImport("user32.dll")]
        private static extern bool BlockInput(bool fBlockIt);

        public Form1()
        {
            this.WindowState = FormWindowState.Minimized;
            this.ShowInTaskbar = false;
            this.Icon = appIcon;

            settings = AppSettings.Load();

            breakTimer = new Timer();
            breakTimer.Tick += BreakTimer_Tick;
            ApplySettings();

            trayIcon = new NotifyIcon { Icon = appIcon, Text = "BreakController", Visible = true };
            var menu = new ContextMenuStrip();
            menu.Items.Add("Настройки", null, (s, e) => ShowSettingsForm());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Выход", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = menu;
        }

        private void ApplySettings()
        {
            breakTimer.Stop();
            breakTimer.Interval = settings.WorkMinutes * 60 * 1000;
            breakTimer.Start();
        }

        private void ShowSettingsForm()
        {
            using (Form f = new Form { Text = "Настройки", Size = new Size(300, 260), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, Icon = appIcon })
            {
                Label l1 = new Label { Text = "Работа (мин):", Location = new Point(20, 25), AutoSize = true };
                NumericUpDown nWork = new NumericUpDown { Location = new Point(150, 23), Value = settings.WorkMinutes, Minimum = 1, Maximum = 999 };
                Label l2 = new Label { Text = "Отдых (мин):", Location = new Point(20, 65), AutoSize = true };
                NumericUpDown nRest = new NumericUpDown { Location = new Point(150, 63), Value = settings.RestMinutes, Minimum = 1, Maximum = 999 };
                CheckBox cb = new CheckBox { Text = "Запускать с Windows", Location = new Point(25, 110), Checked = settings.AutoStart, AutoSize = true };

                Button btn = new Button { Text = "Сохранить", Location = new Point(90, 165), Size = new Size(110, 35), FlatStyle = FlatStyle.System };
                btn.Click += (s, e) => {
                    settings.WorkMinutes = (int)nWork.Value;
                    settings.RestMinutes = (int)nRest.Value;
                    settings.AutoStart = cb.Checked;
                    settings.Save();
                    ApplySettings();
                    f.Close();
                };

                f.Controls.AddRange(new Control[] { l1, nWork, l2, nRest, cb, btn });
                f.ShowDialog();
            }
        }

        private async void BreakTimer_Tick(object sender, EventArgs e)
        {
            breakTimer.Stop();

            using (Form choice = CreateChoiceForm())
            {
                if (choice.ShowDialog() != DialogResult.OK)
                {
                    breakTimer.Start();
                    return;
                }

                if ((string)choice.Tag == "postpone")
                {
                    breakTimer.Interval = 2 * 60 * 1000;
                    breakTimer.Start();
                }
                else
                {
                    await StartRestAsync();
                    ApplySettings();
                }
            }
        }

        private Form CreateChoiceForm()
        {
            Form f = new Form { Text = "Перерыв", Size = new Size(400, 220), StartPosition = FormStartPosition.CenterScreen, TopMost = true, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false, Icon = appIcon };
            Label lbl = new Label { Text = "Пора немного отдохнуть!", Dock = DockStyle.Top, Height = 70, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 12, FontStyle.Bold) };
            Button b1 = new Button { Text = "Отложить 2 мин", Size = new Size(140, 45), Location = new Point(40, 100), FlatStyle = FlatStyle.System };
            b1.Click += (s, e) => { f.Tag = "postpone"; f.DialogResult = DialogResult.OK; f.Close(); };
            Button b2 = new Button { Text = "Начать отдых", Size = new Size(140, 45), Location = new Point(210, 100), FlatStyle = FlatStyle.System };
            b2.Click += (s, e) => { f.Tag = "start"; f.DialogResult = DialogResult.OK; f.Close(); };
            f.Controls.AddRange(new Control[] { lbl, b1, b2 });
            return f;
        }

        private async Task StartRestAsync()
        {
            List<RestForm> forms = new List<RestForm>();
            foreach (var scr in Screen.AllScreens)
            {
                RestForm f = new RestForm(settings.RestMinutes * 60)
                {
                    StartPosition = FormStartPosition.Manual,
                    Bounds = scr.Bounds
                };
                forms.Add(f);
                f.Show();
            }

            // Блокировка клавиатуры и мыши
            BlockInput(true);

            while (forms.Exists(f => !f.IsDisposed && f.Visible)) await Task.Delay(200);

            // Разблокировка ввода
            BlockInput(false);

            if (ShowContinueForm()) await StartRestAsync();
        }

        private bool ShowContinueForm()
        {
            using (Form f = new Form { Text = "Готово", Size = new Size(350, 200), StartPosition = FormStartPosition.CenterScreen, TopMost = true, FormBorderStyle = FormBorderStyle.FixedDialog, ControlBox = false, Icon = appIcon })
            {
                Label lbl = new Label { Text = "Отдых завершен.\nПродолжить работу?", Dock = DockStyle.Top, Height = 80, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 10, FontStyle.Bold) };
                Button b1 = new Button { Text = "Да", DialogResult = DialogResult.OK, Location = new Point(50, 100), Size = new Size(110, 40), FlatStyle = FlatStyle.System };
                Button b2 = new Button { Text = "Еще отдых", DialogResult = DialogResult.Yes, Location = new Point(180, 100), Size = new Size(110, 40), FlatStyle = FlatStyle.System };
                f.Controls.AddRange(new Control[] { lbl, b1, b2 });
                return f.ShowDialog() == DialogResult.Yes;
            }
        }
    }

    public class Star { public float X, Y, Size, Speed; public Color Color; }

    public class RestForm : Form
    {
        private int remainingSeconds;
        private List<Star> stars = new List<Star>();
        private Timer updateTimer = new Timer { Interval = 30 };
        private Timer clockTimer = new Timer { Interval = 1000 };
        private Random rnd = new Random();

        public RestForm(int duration)
        {
            this.DoubleBuffered = true;
            this.BackColor = Color.Black;
            this.TopMost = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.WindowState = FormWindowState.Maximized;
            this.remainingSeconds = duration;

            this.Load += (s, e) =>
            {
                int w = Math.Max(1, this.ClientSize.Width);
                int h = Math.Max(1, this.ClientSize.Height);
                int count = w * h / 15000;

                stars.Clear();
                for (int i = 0; i < count; i++)
                    stars.Add(new Star
                    {
                        X = rnd.Next(0, w),
                        Y = rnd.Next(0, h),
                        Size = rnd.Next(2, 6),
                        Speed = (float)(rnd.NextDouble() * 3 + 1),
                        Color = Color.FromArgb(rnd.Next(150, 255), Color.White)
                    });

                updateTimer.Start();
                clockTimer.Start();
            };

            updateTimer.Tick += (s, e) =>
            {
                int w = Math.Max(1, this.ClientSize.Width);
                int h = Math.Max(1, this.ClientSize.Height);

                foreach (var st in stars)
                {
                    st.Y += st.Speed;
                    if (st.Y > h)
                    {
                        st.Y = -10;
                        st.X = rnd.Next(0, w);
                    }
                }
                this.Invalidate();
            };

            clockTimer.Tick += (s, e) =>
            {
                remainingSeconds--;
                if (remainingSeconds <= 0) this.Close();
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            int w = Math.Max(1, this.ClientSize.Width);
            int h = Math.Max(1, this.ClientSize.Height);

            foreach (var st in stars)
            {
                float x = Math.Min(Math.Max(0, st.X), w - 1);
                float y = Math.Min(Math.Max(0, st.Y), h - 1);
                float size = Math.Max(1, st.Size);
                using (SolidBrush b = new SolidBrush(st.Color))
                    e.Graphics.FillEllipse(b, x, y, size, size);
            }

            // Надпись "ОТДЫХ"
            using (Font titleFont = new Font("Segoe UI", 48, FontStyle.Bold))
            {
                string title = "ОТДЫХ";
                SizeF sz = e.Graphics.MeasureString(title, titleFont);
                e.Graphics.DrawString(title, titleFont, Brushes.White, (w - sz.Width) / 2, h * 0.25f - sz.Height / 2);
            }

            // Таймер
            string time = TimeSpan.FromSeconds(remainingSeconds).ToString(@"mm\:ss");
            using (Font fnt = new Font("Segoe UI", 72, FontStyle.Bold))
            {
                SizeF sz = e.Graphics.MeasureString(time, fnt);
                e.Graphics.DrawString(time, fnt, Brushes.White, (w - sz.Width) / 2, h * 0.45f - sz.Height / 2);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            updateTimer.Stop();
            clockTimer.Stop();
            base.OnFormClosing(e);
        }
    }
}