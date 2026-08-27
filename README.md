# AutoRotateService 🔄

A lightweight background Windows service written in C# (.NET) that enables dynamic auto-rotation on non-2-in-1 laptops (where Windows locks screen auto-rotation) using physical motherboard accelerometer sensors.

Instead of abrupt, unwanted display flips, **AutoRotateService** displays an **Android-style floating overlay button** at the bottom-right of your screen whenever physical tilt is detected. Clicking the icon confirms the rotation; ignoring it leaves your current screen orientation untouched.

---

## 🛠️ Hardware & OS Tested
* **Device:** Dell Latitude 7420 (Non 2-in-1 Clamshell Variant)
* **Processor:** Intel Core i7
* **RAM:** 32 GB
* **OS:** Windows 11 Pro
* **Sensor Infrastructure:** Intel(R) Integrated Sensor Solution / `HID Sensor Collection V2` reading raw Accelerometer G-force vectors.

---

## 🚀 Key Features
* **Bypasses Windows Clamshell Lock:** Overrides Windows disabling screen auto-rotation on fixed laptop form factors.
* **Android-Style Confirmation:** Shows a non-intrusive floating `🔄` prompt for 5 seconds upon physical tilt.
* **Zero Accidental Flips:** Display orientation updates strictly when you click the overlay button.
* **Modern Windows CCD APIs:** Utilizes User32 `SetDisplayConfig` APIs for native display path adjustments instead of legacy graphics mode hacks.
* **Clean Task Manager Integration:** Operates under `AutoRotateService V1.0.1` with active prompt windows labeled as `Prompting for Rotate`.

---

## 📦 Prerequisites & Installation

### Option 1: Quick Install (Pre-compiled Executable)
1. Download `AutoRotateService_V1.0.1.exe` from the **Releases** tab.
2. Ensure you have the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0) (or matching target runtime) installed.
3. Move `AutoRotateService_V1.0.1.exe` to a permanent location (e.g., `C:\Program Files\AutoRotateService`).
4. Press `Win + R`, type `shell:startup`, and press Enter.
5. Create a shortcut of `AutoRotateService_V1.0.1.exe` inside the Startup folder for auto-run at boot.

### Option 2: Building from Source
```bash
git clone [https://github.com/](https://github.com/)<MahendraVMahale>/AutoRotateService.git
cd AutoRotateService
dotnet build
dotnet run