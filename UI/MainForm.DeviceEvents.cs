using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        // Hardware Change Notifications (USB Plug/Unplug, Wireless/Wired switch)
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        [StructLayout(LayoutKind.Sequential)]
        struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public short dbcc_name;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
        const uint RIDEV_INPUTSINK = 0x00000100;
        const int WM_INPUT = 0x00FF;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterDeviceNotification(IntPtr Handle);

        const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        const int DBT_DEVTYP_DEVICEINTERFACE = 5;

        private IntPtr hDevNotify = IntPtr.Zero;
        private System.Windows.Forms.Timer deviceChangeTimer1;
        private System.Windows.Forms.Timer deviceChangeTimer2;
        private System.Windows.Forms.Timer updateTimer;

        // Mouse Sleeping & Instant Wakeup
        private volatile bool isMouseSleeping = false;
        private System.Windows.Forms.Timer wakeBurstTimer;
        private int wakeRetryCount = 0;
        private System.Windows.Forms.Timer bootPollTimer;
        private int bootPollCount = 0;
        private void RegisterMouseRawInput()
        {
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
                rid[0].dwFlags = RIDEV_INPUTSINK;
                rid[0].hwndTarget = this.Handle;
                RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_DEVICECHANGE)
            {
                int wp = m.WParam.ToInt32();
                if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVNODES_CHANGED)
                {
                    OnDeviceHardwareChange();
                }
            }
            else if (m.Msg == WM_INPUT)
            {
                OnMouseRawInputActivity();
            }
            base.WndProc(ref m);
        }

        private void OnMouseRawInputActivity()
        {
            if (isMouseSleeping || lastInfo == null || !lastInfo.IsConnected || lastInfo.BatteryPercent <= 0)
            {
                TriggerWakeBurst();
            }
        }

        private void TriggerWakeBurst()
        {
            if (wakeBurstTimer == null)
            {
                wakeBurstTimer = new System.Windows.Forms.Timer();
                wakeBurstTimer.Tick += WakeBurstTimer_Tick;
            }

            if (!wakeBurstTimer.Enabled)
            {
                wakeRetryCount = 0;
                wakeBurstTimer.Interval = 80;
                wakeBurstTimer.Start();
            }
        }

        private void WakeBurstTimer_Tick(object sender, EventArgs e)
        {
            wakeRetryCount++;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                var info = device.QueryRazerDeviceInfo();
                try
                {
                    this.BeginInvoke(new Action(() =>
                    {
                        if (info != null)
                        {
                            lastInfo = info;
                            UpdateUI(info, false);

                            if (info.IsConnected && !info.IsSleeping && info.BatteryPercent > 0)
                            {
                                if (wakeBurstTimer != null) wakeBurstTimer.Stop();
                                wakeRetryCount = 0;
                                return;
                            }
                        }

                        if (wakeRetryCount >= 6)
                        {
                            if (wakeBurstTimer != null) wakeBurstTimer.Stop();
                            wakeRetryCount = 0;
                        }
                        else if (wakeBurstTimer != null && wakeBurstTimer.Enabled)
                        {
                            wakeBurstTimer.Interval = 100 + wakeRetryCount * 150;
                        }
                    }));
                }
                catch { }
            });
        }

        private void StartBootPolling()
        {
            bootPollCount = 0;
            if (bootPollTimer == null)
            {
                bootPollTimer = new System.Windows.Forms.Timer();
                bootPollTimer.Interval = 1200;
                bootPollTimer.Tick += (s, e) =>
                {
                    bootPollCount++;
                    if (lastInfo == null || lastInfo.IsSleeping || !lastInfo.IsConnected || lastInfo.BatteryPercent <= 0)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            var info = device.QueryRazerDeviceInfo();
                            try
                            {
                                this.BeginInvoke(new Action(() =>
                                {
                                    lastInfo = info;
                                    UpdateUI(info, false);
                                }));
                            }
                            catch { }
                        });
                    }
                    if (bootPollCount >= 8 || (lastInfo != null && lastInfo.IsConnected && !lastInfo.IsSleeping && lastInfo.BatteryPercent > 0))
                    {
                        bootPollTimer.Stop();
                    }
                };
            }
            bootPollTimer.Start();
        }

        private void RegisterUsbNotification()
        {
            try
            {
                Guid hidGuid;
                HidNative.HidD_GetHidGuid(out hidGuid);

                DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE();
                dbi.dbcc_size = Marshal.SizeOf(dbi);
                dbi.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
                dbi.dbcc_reserved = 0;
                dbi.dbcc_classguid = hidGuid;

                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
                try
                {
                    Marshal.StructureToPtr(dbi, buffer, true);
                    hDevNotify = RegisterDeviceNotification(this.Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { }
        }

private void OnDeviceHardwareChange()
        {
            if (deviceChangeTimer1 == null)
            {
                deviceChangeTimer1 = new System.Windows.Forms.Timer();
                deviceChangeTimer1.Tick += (s, e) =>
                {
                    deviceChangeTimer1.Stop();
                    RefreshBatteryStatus(false);
                };
            }
            deviceChangeTimer1.Stop();
            deviceChangeTimer1.Interval = 250;
            deviceChangeTimer1.Start();

            if (deviceChangeTimer2 == null)
            {
                deviceChangeTimer2 = new System.Windows.Forms.Timer();
                deviceChangeTimer2.Tick += (s, e) =>
                {
                    deviceChangeTimer2.Stop();
                    RefreshBatteryStatus(false);
                };
            }
            deviceChangeTimer2.Stop();
            deviceChangeTimer2.Interval = 1000;
            deviceChangeTimer2.Start();
        }
    }
}
