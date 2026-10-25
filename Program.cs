using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using CTSoundCore;

namespace HeadphoneHotkey
{
    internal static class Bootstrap
    {
        [STAThread]
        private static void Main()
        {
            string interopPath = FindCreativeInteropAssembly();
            if (interopPath == null)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Creative Sound Blaster Command could not be found. Install Sound Blaster Command and its device driver, then start this program again.",
                    "Output Switch for Creative Sound Blaster Command",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Error);
                return;
            }

            AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs args)
            {
                if (!string.Equals(new System.Reflection.AssemblyName(args.Name).Name,
                        "Interop.CtSndCr", StringComparison.OrdinalIgnoreCase))
                    return null;
                return System.Reflection.Assembly.LoadFrom(interopPath);
            };

            Program.Run();
        }

        private static string FindCreativeInteropAssembly()
        {
            string fileName = "Interop.CtSndCr.dll";
            string[] roots = new string[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
            };

            foreach (string root in roots)
            {
                if (string.IsNullOrEmpty(root))
                    continue;
                string path = root;
                if (!string.Equals(Path.GetFileName(root), "Program Files (x86)", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(Path.GetFileName(root), "Program Files", StringComparison.OrdinalIgnoreCase))
                    path = Path.Combine(root, fileName);
                else
                    path = Path.Combine(root, "Creative", "Sound Blaster Command", "Platform", fileName);

                if (File.Exists(path))
                    return path;
            }

            return null;
        }
    }

    internal static class Program
    {
        static Program()
        {
        }

        private const int HotkeyId = 1;
        private const int ExitHotkeyId = 2;
        private const uint ModAlt = 0x0001;
        private const uint ModControl = 0x0002;
        private const uint ModShift = 0x0004;
        private const uint ModNoRepeat = 0x4000;
        private const uint VkF12 = 0x7B;
        private const uint VkF10 = 0x79;
        private const int WmHotkey = 0x0312;
        private const uint DeviceStateActive = 0x00000001;
        private const uint StgmRead = 0;
        private const int FeatureHeadphoneOutput = 0x01000001;
        private const int ParamSelectOutput = 15;
        private const uint SupportedModifiers = ModAlt | ModControl | ModShift;

        private static readonly PropertyKey SoundCoreClsidKey =
            new PropertyKey(new Guid("C949C6AA-132B-4511-BB1B-35261A2A6333"), 0);
        private static readonly PropertyKey SoundCoreClsidFallbackKey =
            new PropertyKey(new Guid("d8570091-af3f-4615-9faa-a24845d10936"), 0);
        private static readonly PropertyKey FriendlyNameKey =
            new PropertyKey(new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), 14);
        private static readonly SoundCoreEventCallback EventCallback = new SoundCoreEventCallback();
        private static uint _hotkeyModifiers = ModControl | ModAlt;
        private static uint _hotkeyVirtualKey = VkF12;
        private static NotifyIcon _trayIcon;
        private static Icon _ownedTrayIcon;
        private static int _lastTrayOutput = -2;
        private static Timer _statusTimer;
        private static ToolStripMenuItem _statusMenuItem;
        private static ToolStripMenuItem _hotkeyMenuItem;
        private static bool _exitHotkeyRegistered;

        [STAThread]
        internal static void Run()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            LoadHotkeySettings();
            InitializeTrayIcon();
            bool hotkeyRegistered = RegisterCurrentHotkey();
            if (!hotkeyRegistered)
                ShowTrayMessage("Hotkey unavailable", FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey) +
                    " is already in use. Change it from the tray menu.", ToolTipIcon.Warning);

            _exitHotkeyRegistered = RegisterHotKey(IntPtr.Zero, ExitHotkeyId,
                ModControl | ModAlt | ModNoRepeat, VkF10);
            Log("HeadphoneHotkey started. Toggle: " +
                FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey) + "; Exit: Ctrl+Alt+F10.");
            try
            {
                NativeMessage message;
                int result;
                while ((result = GetMessage(out message, IntPtr.Zero, 0, 0)) > 0)
                {
                    if (message.message == WmHotkey)
                    {
                        int hotkeyId = message.wParam.ToInt32();
                        if (hotkeyId == HotkeyId)
                            ToggleHeadphoneOutput(true);
                        else if (hotkeyId == ExitHotkeyId)
                            break;
                    }

                    TranslateMessage(ref message);
                    DispatchMessage(ref message);
                }
            }
            finally
            {
                UnregisterHotKey(IntPtr.Zero, HotkeyId);
                if (_exitHotkeyRegistered)
                    UnregisterHotKey(IntPtr.Zero, ExitHotkeyId);
                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                    _trayIcon.Dispose();
                }
                if (_ownedTrayIcon != null)
                    _ownedTrayIcon.Dispose();
                if (_statusTimer != null)
                    _statusTimer.Dispose();
            }
        }

        private static void InitializeTrayIcon()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            _statusMenuItem = new ToolStripMenuItem("Reading status …") { Enabled = false };
            menu.Items.Add(_statusMenuItem);
            _hotkeyMenuItem = new ToolStripMenuItem("Hotkey: " + FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey))
                { Enabled = false };
            menu.Items.Add(_hotkeyMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Switch to Headphones", null, delegate { SetHeadphoneOutput(true, true, null); });
            menu.Items.Add("Switch to Speakers", null, delegate { SetHeadphoneOutput(false, true, null); });
            menu.Items.Add("Toggle Output", null, delegate { ToggleHeadphoneOutput(true); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Change Hotkey …", null, ChangeHotkey);
            menu.Items.Add("Exit", null, delegate { PostQuitMessage(0); });
            menu.Opening += delegate { RefreshTrayStatus(); };

            _ownedTrayIcon = CreateTrayIcon(-1);
            _trayIcon = new NotifyIcon
            {
                Icon = _ownedTrayIcon,
                Text = "HeadphoneHotkey",
                ContextMenuStrip = menu,
                Visible = true
            };
            _trayIcon.DoubleClick += delegate { ToggleHeadphoneOutput(true); };
            RefreshTrayStatus();
            _statusTimer = new Timer { Interval = 2000 };
            _statusTimer.Tick += delegate { RefreshTrayStatus(); };
            _statusTimer.Start();
        }

        private static void RefreshTrayStatus()
        {
            if (_trayIcon == null)
                return;

            _hotkeyMenuItem.Text = "Hotkey: " + FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey);
            try
            {
                uint selectedOutput = ReadSelectedOutput();
                bool headphones = selectedOutput == 0;
                _statusMenuItem.Text = headphones ? "Current output: Headphones" : "Current output: Speakers";
                _trayIcon.Text = (headphones ? "Headphones" : "Speakers") + " – " +
                    FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey);
                UpdateTrayOutputIcon((int)selectedOutput);
            }
            catch (Exception ex)
            {
                _statusMenuItem.Text = "Output status unavailable";
                _trayIcon.Text = "Output status unavailable";
                UpdateTrayOutputIcon(-1);
                Log("Could not read tray status: " + ex);
            }
        }

        private static void UpdateTrayOutputIcon(int output)
        {
            if (_trayIcon == null || output == _lastTrayOutput)
                return;

            Icon previousIcon = _ownedTrayIcon;
            _ownedTrayIcon = CreateTrayIcon(output);
            _trayIcon.Icon = _ownedTrayIcon;
            _lastTrayOutput = output;
            if (previousIcon != null)
                previousIcon.Dispose();
        }

        private static bool RegisterCurrentHotkey()
        {
            return RegisterHotKey(IntPtr.Zero, HotkeyId,
                _hotkeyModifiers | ModNoRepeat, _hotkeyVirtualKey);
        }

        private static void ChangeHotkey(object sender, EventArgs e)
        {
            using (HotkeySettingsDialog dialog = new HotkeySettingsDialog(
                _hotkeyModifiers, _hotkeyVirtualKey))
            {
                if (dialog.ShowDialog() != DialogResult.OK)
                    return;

                uint oldModifiers = _hotkeyModifiers;
                uint oldVirtualKey = _hotkeyVirtualKey;
                UnregisterHotKey(IntPtr.Zero, HotkeyId);
                if (!RegisterHotKey(IntPtr.Zero, HotkeyId,
                        dialog.HotkeyModifiers | ModNoRepeat, dialog.HotkeyVirtualKey))
                {
                    bool restored = RegisterHotKey(IntPtr.Zero, HotkeyId,
                        oldModifiers | ModNoRepeat, oldVirtualKey);
                    ShowTrayMessage("Hotkey not changed",
                        "This key combination is already in use." +
                        (restored ? " The previous hotkey is still active." : " The previous hotkey could not be restored."),
                        ToolTipIcon.Warning);
                    return;
                }

                _hotkeyModifiers = dialog.HotkeyModifiers;
                _hotkeyVirtualKey = dialog.HotkeyVirtualKey;
                SaveHotkeySettings();
                RefreshTrayStatus();
                ShowTrayMessage("Hotkey changed", FormatHotkey(_hotkeyModifiers, _hotkeyVirtualKey), ToolTipIcon.Info);
            }
        }

        private static void LoadHotkeySettings()
        {
            string settingsPath = GetSettingsPath();
            if (!File.Exists(settingsPath))
                return;

            try
            {
                foreach (string line in File.ReadAllLines(settingsPath))
                {
                    string[] parts = line.Split('=');
                    uint value;
                    if (parts.Length != 2 || !uint.TryParse(parts[1], out value))
                        continue;
                    if (string.Equals(parts[0], "Modifiers", StringComparison.OrdinalIgnoreCase) &&
                        value != 0 && (value & ~SupportedModifiers) == 0)
                        _hotkeyModifiers = value;
                    else if (string.Equals(parts[0], "VirtualKey", StringComparison.OrdinalIgnoreCase) &&
                        value > 0 && value <= 0xFF)
                        _hotkeyVirtualKey = value;
                }
            }
            catch (Exception ex)
            {
                Log("Could not load hotkey settings: " + ex);
            }
        }

        private static void SaveHotkeySettings()
        {
            try
            {
                string path = GetSettingsPath();
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path,
                    "Modifiers=" + _hotkeyModifiers + Environment.NewLine +
                    "VirtualKey=" + _hotkeyVirtualKey + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Log("Could not save hotkey settings: " + ex);
                ShowTrayMessage("Settings not saved",
                    "The hotkey will work until the app exits, but could not be saved permanently.",
                    ToolTipIcon.Warning);
            }
        }

        private static string GetSettingsPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "HeadphoneHotkey", "settings.ini");
        }

        internal static string FormatHotkey(uint modifiers, uint virtualKey)
        {
            List<string> parts = new List<string>();
            if ((modifiers & ModControl) != 0) parts.Add("Ctrl");
            if ((modifiers & ModAlt) != 0) parts.Add("Alt");
            if ((modifiers & ModShift) != 0) parts.Add("Shift");
            parts.Add(((Keys)virtualKey).ToString());
            return string.Join("+", parts.ToArray());
        }

        private static void ShowTrayMessage(string title, string message, ToolTipIcon icon)
        {
            if (_trayIcon != null)
                _trayIcon.ShowBalloonTip(4000, title, message, icon);
            else
                ShowMessage(title + "\n\n" + message);
        }

        private static Icon CreateTrayIcon(int output)
        {
            using (Bitmap bitmap = new Bitmap(32, 32))
            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.Clear(Color.Transparent);
                using (SolidBrush background = new SolidBrush(Color.FromArgb(30, 39, 61)))
                    graphics.FillEllipse(background, 1, 1, 30, 30);
                using (Pen stroke = new Pen(Color.White, 2.5f))
                {
                    stroke.StartCap = LineCap.Round;
                    stroke.EndCap = LineCap.Round;
                    if (output == 0)
                    {
                        graphics.DrawArc(stroke, 8, 5, 16, 19, 180, 180);
                        using (SolidBrush earcups = new SolidBrush(Color.FromArgb(44, 204, 209)))
                        {
                            graphics.FillRectangle(earcups, 5, 15, 5, 10);
                            graphics.FillRectangle(earcups, 22, 15, 5, 10);
                        }
                    }
                    else if (output == 1)
                    {
                        Point[] speaker = new Point[]
                        {
                            new Point(6, 13), new Point(12, 13), new Point(19, 7),
                            new Point(19, 25), new Point(12, 19), new Point(6, 19)
                        };
                        using (SolidBrush fill = new SolidBrush(Color.FromArgb(44, 204, 209)))
                            graphics.FillPolygon(fill, speaker);
                        graphics.DrawPolygon(stroke, speaker);
                        graphics.DrawArc(stroke, 13, 9, 13, 14, -65, 130);
                        graphics.DrawArc(stroke, 10, 5, 19, 22, -65, 130);
                    }
                    else
                    {
                        using (Font font = new Font("Arial", 21, FontStyle.Bold, GraphicsUnit.Pixel))
                        using (StringFormat format = new StringFormat
                        {
                            Alignment = StringAlignment.Center,
                            LineAlignment = StringAlignment.Center
                        })
                            graphics.DrawString("?", font, Brushes.White, new RectangleF(2, 1, 28, 30), format);
                    }
                }

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    using (Icon temporary = Icon.FromHandle(handle))
                        return (Icon)temporary.Clone();
                }
                finally
                {
                    DestroyIcon(handle);
                }
            }
        }

        private static uint ReadSelectedOutput()
        {
            EndpointInfo endpoint = FindSoundCoreRenderEndpoint();
            object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(endpoint.SoundCoreClsid, true));
            if (instance == null)
                throw new InvalidOperationException("Could not initialize Creative SoundCore.");

            try
            {
                ISoundCore soundCore = (ISoundCore)instance;
                _stHardwareInfo hardwareInfo = CreateHardwareInfo(endpoint.Id);
                soundCore.BindHardware(ref hardwareInfo);
                _etContext context;
                soundCore.GetContext(out context);
                ((ICreativeEventNotify)instance).RegisterEventCallback(255, EventCallback);

                _stParam parameter = CreateOutputParameter(context);
                _stParamValue value;
                soundCore.GetParamValue(parameter, out value);
                if (value.paramType != _etParamType.eParamType_Dword || value.paramVal.dwordVal > 1)
                    throw new InvalidOperationException("The driver returned an unknown SelectOutput state.");
                return value.paramVal.dwordVal;
            }
            finally
            {
                if (Marshal.IsComObject(instance))
                    Marshal.FinalReleaseComObject(instance);
            }
        }

        private static bool ToggleHeadphoneOutput(bool showErrors)
        {
            uint currentOutput;
            try
            {
                currentOutput = ReadSelectedOutput();
            }
            catch (Exception ex)
            {
                Log("Could not read Creative output state: " + ex);
                if (showErrors)
                    ShowTrayMessage("Output status unavailable", ex.Message, ToolTipIcon.Error);
                return false;
            }

            bool oldHeadphoneState = currentOutput == 0;
            return SetHeadphoneOutput(!oldHeadphoneState, showErrors, oldHeadphoneState);
        }

        private static _stParam CreateOutputParameter(_etContext context)
        {
            _stParam parameter = new _stParam();
            parameter.paramId = new _utParamId();
            parameter.paramId.paramIdMalcolmDeviceControl =
                (_etParamMalcolmDeviceControl)ParamSelectOutput;
            parameter.featureId = (_etFeature)FeatureHeadphoneOutput;
            parameter.contextId = context;
            return parameter;
        }

        private static bool SetHeadphoneOutput(bool headphones, bool showErrors, bool? oldHeadphones)
        {
            try
            {
                EndpointInfo endpoint = FindSoundCoreRenderEndpoint();
                Type soundCoreType = Type.GetTypeFromCLSID(endpoint.SoundCoreClsid, true);
                object instance = Activator.CreateInstance(soundCoreType);
                if (instance == null)
                    throw new InvalidOperationException("Could not initialize Creative SoundCore.");

                try
                {
                    ISoundCore soundCore = (ISoundCore)instance;
                    _stHardwareInfo hardwareInfo = CreateHardwareInfo(endpoint.Id);
                    soundCore.BindHardware(ref hardwareInfo);

                    _etContext context;
                    soundCore.GetContext(out context);
                    ((ICreativeEventNotify)instance).RegisterEventCallback(255, EventCallback);

                    _stParam parameter = CreateOutputParameter(context);

                    _stParamValue newValue = new _stParamValue();
                    newValue.paramType = _etParamType.eParamType_Dword;
                    newValue.paramVal.dwordVal = headphones ? 0u : 1u;

                    soundCore.SetParamValue(parameter, newValue);
                    Log(string.Format(
                        "Creative SelectOutput succeeded: Endpoint=\"{0}\" ({1}), {2} -> {3}.",
                        endpoint.FriendlyName, endpoint.Id,
                        oldHeadphones.HasValue ? (oldHeadphones.Value ? "Headphones" : "Speakers") : "target mode",
                        headphones ? "Headphones" : "Speakers"));
                    RefreshTrayStatus();
                    return true;
                }
                finally
                {
                    if (Marshal.IsComObject(instance))
                        Marshal.FinalReleaseComObject(instance);
                }
            }
            catch (Exception ex)
            {
                Log("Output switch failed: " + ex);
                if (showErrors)
                    ShowTrayMessage("Output switch failed",
                        "See %LOCALAPPDATA%\\HeadphoneHotkey\\HeadphoneHotkey.log for details. " + ex.Message,
                        ToolTipIcon.Error);
                return false;
            }
        }

        private static _stHardwareInfo CreateHardwareInfo(string endpointId)
        {
            ushort[] endpointChars = endpointId.Select(delegate(char ch) { return (ushort)ch; }).ToArray();
            if (endpointChars.Length >= 260)
                throw new InvalidOperationException("The Windows audio endpoint ID is unexpectedly long.");

            ushort[] fixedEndpointId = new ushort[260];
            Array.Copy(endpointChars, fixedEndpointId, endpointChars.Length);

            _utInfo info = new _utInfo();
            info.endpointId = fixedEndpointId;

            _stHardwareInfo hardwareInfo = new _stHardwareInfo();
            hardwareInfo.infoType = _etHardwareInfoType.eHardwareInfoType_EndpointId;
            hardwareInfo.info = info;
            return hardwareInfo;
        }

        private static EndpointInfo FindSoundCoreRenderEndpoint()
        {
            IMMDeviceEnumerator enumerator =
                (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
            IMMDeviceCollection devices = null;
            try
            {
                CheckHResult(
                    enumerator.EnumAudioEndpoints(DataFlow.Render, DeviceStateActive, out devices),
                    "Could not enumerate Windows audio endpoints.");
                uint count;
                CheckHResult(devices.GetCount(out count),
                    "Could not read the number of audio endpoints.");

                List<EndpointInfo> candidates = new List<EndpointInfo>();
                for (uint i = 0; i < count; i++)
                {
                    IMMDevice device = null;
                    IPropertyStore store = null;
                    try
                    {
                        CheckHResult(devices.Item(i, out device),
                            "Could not open audio endpoint " + i + ".");
                        string id;
                        CheckHResult(device.GetId(out id),
                            "Could not read the ID of audio endpoint " + i + ".");
                        CheckHResult(device.OpenPropertyStore(StgmRead, out store),
                            "Could not read the properties of audio endpoint " + i + ".");

                        Guid? clsid = ReadGuidProperty(store, SoundCoreClsidKey);
                        if (!clsid.HasValue)
                            clsid = ReadGuidProperty(store, SoundCoreClsidFallbackKey);
                        if (!clsid.HasValue)
                            continue;

                        string friendlyName = ReadStringProperty(store, FriendlyNameKey);
                        if (string.IsNullOrEmpty(friendlyName))
                            friendlyName = id;
                        candidates.Add(new EndpointInfo(id, friendlyName, clsid.Value));
                    }
                    finally
                    {
                        ReleaseComObject(store);
                        ReleaseComObject(device);
                    }
                }

                if (candidates.Count == 0)
                {
                    throw new InvalidOperationException(
                        "No active playback endpoint with a Creative SoundCore property was found. " +
                        "Is the Sound Blaster driver installed?");
                }

                EndpointInfo namedSoundBlaster = candidates.FirstOrDefault(
                    delegate(EndpointInfo item)
                    {
                        return item.FriendlyName.IndexOf(
                            "Sound Blaster Z", StringComparison.OrdinalIgnoreCase) >= 0;
                    });
                if (namedSoundBlaster != null)
                    return namedSoundBlaster;

                if (candidates.Count == 1)
                    return candidates[0];

                string defaultId = GetDefaultRenderEndpointId(enumerator);
                EndpointInfo defaultCandidate = candidates.FirstOrDefault(
                    delegate(EndpointInfo item)
                    {
                        return string.Equals(item.Id, defaultId, StringComparison.OrdinalIgnoreCase);
                    });
                if (defaultCandidate != null)
                    return defaultCandidate;

                throw new InvalidOperationException(
                    "Multiple Creative SoundCore playback endpoints were found, but none is named " +
                    "\"Sound Blaster Z\" or is the Windows default output. Nothing was switched.");
            }
            finally
            {
                ReleaseComObject(devices);
                ReleaseComObject(enumerator);
            }
        }

        private static string GetDefaultRenderEndpointId(IMMDeviceEnumerator enumerator)
        {
            IMMDevice device = null;
            try
            {
                CheckHResult(
                    enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia, out device),
                    "Could not determine the Windows default playback endpoint.");
                string id;
                CheckHResult(device.GetId(out id),
                    "Could not read the ID of the Windows default output.");
                return id;
            }
            finally
            {
                ReleaseComObject(device);
            }
        }

        private static Guid? ReadGuidProperty(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            CheckHResult(store.GetValue(ref key, out value),
                "Could not read property " + key.FormatId + "/" + key.PropertyId + ".");
            try
            {
                if (value.VariantType == 72 && value.Pointer != IntPtr.Zero)
                    return (Guid)Marshal.PtrToStructure(value.Pointer, typeof(Guid));

                string text = ReadVariantString(value);
                Guid clsid;
                if (Guid.TryParse(text, out clsid))
                    return clsid;
                return null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        private static string ReadStringProperty(IPropertyStore store, PropertyKey key)
        {
            PropVariant value;
            CheckHResult(store.GetValue(ref key, out value),
                "Could not read property " + key.FormatId + "/" + key.PropertyId + ".");
            try
            {
                return ReadVariantString(value);
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }

        private static string ReadVariantString(PropVariant value)
        {
            if (value.Pointer == IntPtr.Zero)
                return null;
            if (value.VariantType == 31)
                return Marshal.PtrToStringUni(value.Pointer);
            if (value.VariantType == 8)
                return Marshal.PtrToStringBSTR(value.Pointer);
            return null;
        }

        private static void CheckHResult(int hr, string message)
        {
            if (hr < 0)
                Marshal.ThrowExceptionForHR(hr);
            if (hr != 0)
                throw new InvalidOperationException(message);
        }

        private static void ReleaseComObject(object value)
        {
            if (value != null && Marshal.IsComObject(value))
                Marshal.FinalReleaseComObject(value);
        }

        private static string LogDirectory
        {
            get
            {
                string path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "HeadphoneHotkey");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        private static void Log(string message)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(LogDirectory, "HeadphoneHotkey.log"),
                    DateTime.Now.ToString("o") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
            }
        }

        private static void ShowMessage(string message)
        {
            NativeMessageBox(IntPtr.Zero, message, "HeadphoneHotkey", 0x00000040);
        }

        private sealed class EndpointInfo
        {
            public readonly string Id;
            public readonly string FriendlyName;
            public readonly Guid SoundCoreClsid;

            public EndpointInfo(string id, string friendlyName, Guid soundCoreClsid)
            {
                Id = id;
                FriendlyName = friendlyName;
                SoundCoreClsid = soundCoreClsid;
            }
        }


        [ComVisible(true)]
        [Guid("b353c442-c49d-4532-9e3a-1b20a182fd00")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreativeEventCallback
        {
            void EventCallback(_stEventInfo eventInfo);
        }

        [ComImport]
        [Guid("f6cb394a-a680-45c0-acd2-f0595626a3fd")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface ICreativeEventNotify
        {
            void RegisterEventCallback(uint callbackId, ICreativeEventCallback callback);
            void UnregisterEventCallback();
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        private sealed class SoundCoreEventCallback : ICreativeEventCallback
        {
            public void EventCallback(_stEventInfo eventInfo)
            {
            }
        }

        private enum DataFlow
        {
            Render = 0,
            Capture = 1,
            All = 2
        }

        private enum Role
        {
            Console = 0,
            Multimedia = 1,
            Communications = 2
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PropertyKey
        {
            public Guid FormatId;
            public uint PropertyId;

            public PropertyKey(Guid formatId, uint propertyId)
            {
                FormatId = formatId;
                PropertyId = propertyId;
            }
        }

        [StructLayout(LayoutKind.Explicit, Size = 16)]
        private struct PropVariant
        {
            [FieldOffset(0)] public ushort VariantType;
            [FieldOffset(8)] public IntPtr Pointer;
        }

        [ComImport]
        [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
        private class MMDeviceEnumeratorComObject
        {
        }

        [ComImport]
        [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceEnumerator
        {
            [PreserveSig]
            int EnumAudioEndpoints(DataFlow dataFlow, uint stateMask, out IMMDeviceCollection devices);

            [PreserveSig]
            int GetDefaultAudioEndpoint(DataFlow dataFlow, Role role, out IMMDevice endpoint);

            [PreserveSig]
            int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

            [PreserveSig]
            int RegisterEndpointNotificationCallback(IntPtr client);

            [PreserveSig]
            int UnregisterEndpointNotificationCallback(IntPtr client);
        }

        [ComImport]
        [Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDeviceCollection
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int Item(uint index, out IMMDevice device);
        }

        [ComImport]
        [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IMMDevice
        {
            [PreserveSig]
            int Activate(ref Guid interfaceId, uint classContext, IntPtr activationParams,
                [MarshalAs(UnmanagedType.IUnknown)] out object instance);

            [PreserveSig]
            int OpenPropertyStore(uint storageAccess, out IPropertyStore properties);

            [PreserveSig]
            int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);

            [PreserveSig] int GetState(out uint state);
        }

        [ComImport]
        [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPropertyStore
        {
            [PreserveSig] int GetCount(out uint count);
            [PreserveSig] int GetAt(uint index, out PropertyKey key);
            [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
            [PreserveSig] int SetValue(ref PropertyKey key, ref PropVariant value);
            [PreserveSig] int Commit();
        }

        [DllImport("ole32.dll")]
        private static extern int PropVariantClear(ref PropVariant value);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetMessage(out NativeMessage message, IntPtr hWnd, uint min, uint max);

        [DllImport("user32.dll")]
        private static extern void PostQuitMessage(int exitCode);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref NativeMessage message);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref NativeMessage message);

        [DllImport("user32.dll", EntryPoint = "MessageBoxW", CharSet = CharSet.Unicode)]
        private static extern int NativeMessageBox(IntPtr hWnd, string text, string caption, uint type);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMessage
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int x;
            public int y;
            public uint Private;
        }
}

    internal sealed class HotkeySettingsDialog : Form
{
    private readonly TextBox _hotkeyBox;
    private readonly Label _hint;
    private bool _validHotkey = true;

    internal uint HotkeyModifiers { get; private set; }
    internal uint HotkeyVirtualKey { get; private set; }

    internal HotkeySettingsDialog(uint modifiers, uint virtualKey)
    {
        HotkeyModifiers = modifiers;
        HotkeyVirtualKey = virtualKey;

        Text = "Change Toggle Hotkey";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        ClientSize = new Size(385, 148);

        Label prompt = new Label
        {
            AutoSize = true,
            Location = new Point(12, 12),
            Text = "Click the field and press the desired key combination:"
        };
        Controls.Add(prompt);

        _hotkeyBox = new TextBox
        {
            Location = new Point(12, 38),
            Size = new Size(360, 24),
            ReadOnly = true,
            TabStop = true,
            Text = Program.FormatHotkey(modifiers, virtualKey)
        };
        _hotkeyBox.KeyDown += CaptureHotkey;
        Controls.Add(_hotkeyBox);

        _hint = new Label
        {
            AutoSize = true,
            Location = new Point(12, 70),
            Text = "At least one modifier key (Ctrl, Alt, or Shift) is required."
        };
        Controls.Add(_hint);

        Button okButton = new Button
        {
            Location = new Point(216, 108),
            Size = new Size(75, 27),
            Text = "OK"
        };
        okButton.Click += delegate
        {
            if (!_validHotkey)
            {
                MessageBox.Show(this, "Please choose a valid key combination with Ctrl, Alt, or Shift.",
                    "Toggle Hotkey", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        Controls.Add(okButton);
        AcceptButton = okButton;

        Button cancelButton = new Button
        {
            Location = new Point(297, 108),
            Size = new Size(75, 27),
            Text = "Cancel",
            DialogResult = DialogResult.Cancel
        };
        Controls.Add(cancelButton);
        CancelButton = cancelButton;

        Shown += delegate { _hotkeyBox.Focus(); };
    }

    private void CaptureHotkey(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        e.SuppressKeyPress = true;

        Keys key = e.KeyCode;
        if (key == Keys.ControlKey || key == Keys.Menu || key == Keys.ShiftKey ||
            key == Keys.LWin || key == Keys.RWin)
            return;

        uint modifiers = 0;
        if ((e.Modifiers & Keys.Control) != 0) modifiers |= 0x0002;
        if ((e.Modifiers & Keys.Alt) != 0) modifiers |= 0x0001;
        if ((e.Modifiers & Keys.Shift) != 0) modifiers |= 0x0004;

        if (modifiers == 0)
        {
            _validHotkey = false;
            _hint.Text = "Please also hold Ctrl, Alt, or Shift.";
            return;
        }

        HotkeyModifiers = modifiers;
        HotkeyVirtualKey = (uint)key;
        _validHotkey = true;
        _hotkeyBox.Text = Program.FormatHotkey(HotkeyModifiers, HotkeyVirtualKey);
        _hint.Text = "New combination: " + _hotkeyBox.Text;
    }
    }
}





