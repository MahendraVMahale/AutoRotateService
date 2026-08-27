using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Devices.Sensors;

namespace AutoRotateService
{
    public class App : Application
    {
        private static System.Threading.Mutex? appMutex;

        [STAThread]
        public static void Main()
        {
            // Catch any unhandled crashes to prevent silent background exit
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            System.IO.File.WriteAllText("crashlog.txt", e.ExceptionObject.ToString());
        };
            // Prevent multiple instances from running simultaneously
            bool createdNew;
            appMutex = new System.Threading.Mutex(true, "AutoRotateService_UniqueMutexGuid", out createdNew);

            if (!createdNew)
            {
                // Already running in background
                return;
            }

            App app = new App();
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Keep WPF process alive permanently without active windows
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            // Initialize sensor monitoring engine
            RotationManager.Initialize();
        }
    }

    public static class RotationManager
    {
        [DllImport("user32.dll")]
        private static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPathArrayElements, out uint numModeInfoArrayElements);

        [DllImport("user32.dll")]
        private static extern int QueryDisplayConfig(uint flags, ref uint numPathArrayElements, [Out] DISPLAYCONFIG_PATH_INFO[] pathArray, ref uint numModeInfoArrayElements, [Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, IntPtr currentTopologyId);

        [DllImport("user32.dll")]
        private static extern int SetDisplayConfig(uint numPathArrayElements, [In] DISPLAYCONFIG_PATH_INFO[] pathArray, uint numModeInfoArrayElements, [In] DISPLAYCONFIG_MODE_INFO[] modeInfoArray, uint flags);

        private const uint QDC_ONLY_ACTIVE_PATHS = 0x0002;
        private const uint SDC_APPLY = 0x00000080;
        private const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x00000020;

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_INFO
        {
            public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
            public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
            public uint flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_SOURCE_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_PATH_TARGET_INFO
        {
            public LUID adapterId;
            public uint id;
            public uint modeInfoIdx;
            public uint outputTechnology;
            public uint rotation;
            public uint scaling;
            public DISPLAYCONFIG_RATIONAL refreshRate;
            public uint scanLineOrdering;
            public bool targetAvailable;
            public uint statusFlags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_RATIONAL
        {
            public uint Numerator;
            public uint Denominator;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct LUID
        {
            public uint LowPart;
            public int HighPart;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DISPLAYCONFIG_MODE_INFO
        {
            public uint infoType;
            public uint id;
            public LUID adapterId;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]
            public byte[] modeInfo;
        }

        public const uint DISPLAYCONFIG_ROTATION_IDENTITY = 1; // Landscape (0°)
        public const uint DISPLAYCONFIG_ROTATION_ROTATE90 = 2; // Portrait (90°)
        public const uint DISPLAYCONFIG_ROTATION_ROTATE180 = 3; // Inverted Landscape (180°)
        public const uint DISPLAYCONFIG_ROTATION_ROTATE270 = 4; // Inverted Portrait (270°)

        private static uint currentAppliedRotation = DISPLAYCONFIG_ROTATION_IDENTITY;
        private static uint detectedPendingRotation = 0;
        private static PromptWindow? activePrompt = null;

        public static void Initialize()
        {
            Console.WriteLine("==================================================");
            Console.WriteLine(" Auto-Rotate Service Started");
            Console.WriteLine(" Listening for physical orientation changes...");
            Console.WriteLine(" Press CTRL+C in this terminal to terminate.");
            Console.WriteLine("==================================================");

            Accelerometer? accelerometer = Accelerometer.GetDefault();
            if (accelerometer == null)
            {
                Console.WriteLine("[!] Error: Accelerometer hardware not found.");
                return;
            }

            uint minInterval = accelerometer.MinimumReportInterval;
            accelerometer.ReportInterval = Math.Max(minInterval, 300u);
            accelerometer.ReadingChanged += OnReadingChanged;
        }

        private static void OnReadingChanged(Accelerometer sender, AccelerometerReadingChangedEventArgs args)
        {
            double x = args.Reading.AccelerationX;
            double y = args.Reading.AccelerationY;

            double angle = Math.Atan2(x, -y) * (180.0 / Math.PI);
            uint newDetected = currentAppliedRotation;

            // Inverted rotation angles to match physical hardware axis orientation
            if (angle >= -45 && angle <= 45)             newDetected = DISPLAYCONFIG_ROTATION_IDENTITY;  // Landscape (0°)
            else if (angle > 45 && angle < 135)          newDetected = DISPLAYCONFIG_ROTATION_ROTATE90;   // Portrait (Swapped from 270)
            else if (angle < -45 && angle > -135)        newDetected = DISPLAYCONFIG_ROTATION_ROTATE270;  // Portrait Inverted (Swapped from 90)
            else if (angle >= 135 || angle <= -135)      newDetected = DISPLAYCONFIG_ROTATION_ROTATE180; // Inverted Landscape (180°)

            if (newDetected != currentAppliedRotation && newDetected != detectedPendingRotation)
            {
                detectedPendingRotation = newDetected;
                string label = GetRotationLabel(newDetected);
                Console.WriteLine($"\n[+] Physical Tilt Detected -> Prompting for: {label}");

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowRotationPrompt(newDetected);
                });
            }
        }

        private static void ShowRotationPrompt(uint targetRotation)
        {
            if (activePrompt != null)
            {
                activePrompt.Close();
                activePrompt = null;
            }

            activePrompt = new PromptWindow(targetRotation);
            activePrompt.Show();
        }

        public static void ConfirmRotation(uint targetRotation)
        {
            GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, out uint pathCount, out uint modeCount);

            DISPLAYCONFIG_PATH_INFO[] paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
            DISPLAYCONFIG_MODE_INFO[] modes = new DISPLAYCONFIG_MODE_INFO[modeCount];

            if (QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) == 0)
            {
                paths[0].targetInfo.rotation = targetRotation;

                int status = SetDisplayConfig(pathCount, paths, modeCount, modes, SDC_APPLY | SDC_USE_SUPPLIED_DISPLAY_CONFIG);

                if (status == 0)
                {
                    currentAppliedRotation = targetRotation;
                    detectedPendingRotation = 0;
                    Console.WriteLine($"[✓] Screen orientation successfully applied: {GetRotationLabel(targetRotation)}");
                }
                else
                {
                    Console.WriteLine($"[!] Failed to set rotation. Error Code: {status}");
                }
            }
        }

        private static string GetRotationLabel(uint rotation) => rotation switch
        {
            DISPLAYCONFIG_ROTATION_IDENTITY => "Landscape (0°)",
            DISPLAYCONFIG_ROTATION_ROTATE90 => "Portrait (90°)",
            DISPLAYCONFIG_ROTATION_ROTATE180 => "Inverted Landscape (180°)",
            DISPLAYCONFIG_ROTATION_ROTATE270 => "Inverted Portrait (270°)",
            _ => "Unknown"
        };
    }

    public class PromptWindow : Window
    {
        private readonly DispatcherTimer autoHideTimer;
        private readonly uint targetMode;

        public PromptWindow(uint targetRotation)
        {
            targetMode = targetRotation;
            
            // Sets the exact name displayed in Task Manager under Apps
            Title = "Confirming Rotation (5 sec)";

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Width = 60;
            Height = 60;

            Left = SystemParameters.WorkArea.Width - 80;
            Top = SystemParameters.WorkArea.Height - 80;

            Border container = new Border
            {
                CornerRadius = new CornerRadius(30),
                Background = new SolidColorBrush(Color.FromArgb(230, 30, 30, 30)),
                BorderBrush = Brushes.DimGray,
                BorderThickness = new Thickness(1)
            };

            Button btn = new Button
            {
                Content = "🔄",
                FontSize = 24,
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };

            btn.Click += (s, e) =>
            {
                Console.WriteLine("    -> Button clicked by user. Executing rotation switch...");
                RotationManager.ConfirmRotation(targetMode);
                Close();
            };

            container.Child = btn;
            Content = container;

            autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            autoHideTimer.Tick += (s, e) =>
            {
                Console.WriteLine("    -> Prompt timed out (5s). Rotation cancelled.");
                autoHideTimer.Stop();
                Close();
            };
            autoHideTimer.Start();
        }
    }
}
