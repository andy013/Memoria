using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Application = System.Windows.Application;
using ComboBox = System.Windows.Controls.ComboBox;

namespace Memoria.Launcher
{
    public sealed class SettingsGrid_VanillaDisplay : UiGrid, INotifyPropertyChanged
    {
        private readonly ObservableCollection<string> _resChoices = new ObservableCollection<string>();
        private readonly ComboBox _resComboBox;

        public SettingsGrid_VanillaDisplay()
        {
            DataContext = this;

            CreateHeading("Settings.Display");

            String[] comboboxchoices = GetAvailableMonitors();
            CreateCombobox("ActiveMonitor", comboboxchoices, 50, "Settings.ActiveMonitor", "Settings.ActiveMonitor_Tooltip", "", true);

            comboboxchoices = new String[]
            {
                "Settings.Window",
                "Settings.ExclusiveFullscreen",
                "Settings.BorderlessFullscreen"
            };
            ComboBox modeComboBox = CreateCombobox("WindowMode", comboboxchoices, 50, "Settings.WindowMode", "Settings.WindowMode_Tooltip");

            _resComboBox = CreateCombobox("ScreenResolution", _resChoices, 50, "Settings.Resolution", "Settings.Resolution_Tooltip", "", true);
            _resComboBox.ItemsSource = _resChoices;

            modeComboBox.SelectionChanged += (s, e) =>
            {
                _resComboBox.IsEnabled = modeComboBox.SelectedIndex != 2;
            };

            try
            {
                LoadSettings();
            }
            catch (Exception ex)
            {
                UiHelper.ShowError(Application.Current.MainWindow, ex);
            }
        }

        public String ActiveMonitor
        {
            get { return _activeMonitor; }
            set
            {
                if (_activeMonitor != value)
                {
                    _activeMonitor = value;
                    OnPropertyChanged();
                }
            }
        }

        public String ScreenResolution
        {
            get { return _resolution == "0x0" ? (String)Lang.Res["Launcher.Auto"] : AddRatio(_resolution); }
            set
            {
                if (value != null && _resolution != value)
                {
                    if (value == (String)Lang.Res["Launcher.Auto"])
                        _resolution = "0x0";
                    else
                        _resolution = RemoveRatio(value);
                    OnPropertyChanged();
                }
            }
        }

        public Int16 WindowMode
        {
            get { return _windowMode; }
            set
            {
                if (_windowMode != value)
                {
                    _windowMode = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        private async void OnPropertyChanged([CallerMemberName] String propertyName = null)
        {
            try
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

                IniFile iniFile = IniFile.SettingsIni;
                switch (propertyName)
                {
                    case nameof(ActiveMonitor):
                        if (Int32.TryParse(ActiveMonitor.Substring(0, 1), out int index) && index < Screen.AllScreens.Length)
                            iniFile.SetSetting("Settings", propertyName, Screen.AllScreens[index].DeviceName);
                        else
                            iniFile.SetSetting("Settings", propertyName, String.Empty);

                        _resChoices.Clear();
                        _resChoices.Add((string)Lang.Res["Launcher.Auto"]);
                        var newItems = EnumerateDisplaySettings().OrderByDescending(x => Convert.ToInt32(x.Split('x')[0])).ToArray();

                        foreach (var item in newItems)
                        {
                            _resChoices.Add(AddRatio(item));
                        }

                        var resolutionIndex = _resChoices.IndexOf(ScreenResolution);

                        _resComboBox.SelectedIndex = resolutionIndex >= 0 ? resolutionIndex : 0;
                        break;
                    case nameof(ScreenResolution):
                        // Use _resolution here so we don't save aspect ratio into the ini.
                        iniFile.SetSetting("Settings", propertyName, _resolution ?? "0x0");
                        break;
                    case nameof(WindowMode):
                        iniFile.SetSetting("Settings", propertyName, WindowMode.ToString());
                        break;
                }
                iniFile.Save();
            }
            catch (Exception ex)
            {
                UiHelper.ShowError(Application.Current.MainWindow, ex);
            }
        }

        private String _activeMonitor = "";
        private String _resolution = "";
        private Int16 _windowMode;

        public void LoadSettings()
        {
            try
            {
                IniFile.PreventWrite = true;
                IniFile iniFile = IniFile.SettingsIni;

                // Make sure we load ActiveMonitor before ScreenResolution, otherwise we might check the wrong display.
                String value = iniFile.GetSetting("Settings", nameof(ActiveMonitor));
                if (!String.IsNullOrEmpty(value))
                {
                    var index = Array.FindIndex(Screen.AllScreens, s => s.DeviceName == value);
                    var name = FormatMonitorString(index);

                    _activeMonitor = name;
                }

                value = iniFile.GetSetting("Settings", nameof(ScreenResolution));

                //if res in settings.ini exists AND corresponds to something in the res list
                if ((!String.IsNullOrEmpty(value)) && EnumerateDisplaySettings().ToArray().Any(value.Contains))
                    _resolution = value;
                //else we choose the largest available one
                else if (value == "0x0")
                    _resolution = value;
                else
                    _resolution = EnumerateDisplaySettings().OrderByDescending(x => Convert.ToInt32(x.Split('x')[0])).ToArray()[0];


                value = iniFile.GetSetting("Settings", nameof(WindowMode));
                if (!String.IsNullOrEmpty(value))
                {
                    String newvalue = "";
                    if (value == (String)Lang.Res["Settings.Window"]) newvalue = "0";
                    if (value == (String)Lang.Res["Settings.ExclusiveFullscreen"]) newvalue = "1";
                    if (value == (String)Lang.Res["Settings.BorderlessFullscreen"]) newvalue = "2";
                    if (newvalue.Length > 0)
                    {
                        value = newvalue;
                        IniFile.PreventWrite = false;
                        iniFile.SetSetting("Settings", nameof(WindowMode), value);
                        iniFile.Save();
                        IniFile.PreventWrite = true;
                    }
                }
                if (!Int16.TryParse(value, out _windowMode))
                    _windowMode = 0;

                OnPropertyChanged(nameof(ActiveMonitor));
                OnPropertyChanged(nameof(ScreenResolution));
                OnPropertyChanged(nameof(WindowMode));

            }
            catch (Exception ex)
            {
                UiHelper.ShowError(Application.Current.MainWindow, ex);
            }
            finally
            {
                IniFile.PreventWrite = false;
            }
        }

        [DllImport("user32.dll")]
        private static extern Boolean EnumDisplaySettings(String deviceName, Int32 modeNum, ref DevMode devMode);

        public IEnumerable<String> EnumerateDisplaySettings()
        {
            HashSet<String> set = new HashSet<String>();
            DevMode devMode = new DevMode();
            Int32 modeNum = 0;

            var allScreens = Screen.AllScreens;
            Int32.TryParse(ActiveMonitor.Substring(0, 1), out int index);
            string? name = null;

            if (index >= 0 && index < allScreens.Length)
                name = allScreens[index].DeviceName;

            while (EnumDisplaySettings(name, modeNum++, ref devMode))
            {
                if (devMode.dmPelsWidth >= 640 && devMode.dmPelsHeight >= 480)
                {
                    String resolution = $"{devMode.dmPelsWidth.ToString(CultureInfo.InvariantCulture)}x{devMode.dmPelsHeight.ToString(CultureInfo.InvariantCulture)}";

                    if (set.Add(resolution))
                        yield return resolution;
                }
            }
        }

        private static String AddRatio(String resolution)
        {
            if (!resolution.Contains("|") && resolution.Contains("x"))
            {
                String ratio = "";
                Int32 x = Int32.Parse(resolution.Split('x')[0]);
                Int32 y = Int32.Parse(resolution.Split('x')[1]);

                if ((x / 16) == (y / 9)) ratio = " | 16:9";
                else if ((x / 8) == (y / 5)) ratio = " | 16:10";
                else if ((x / 4) == (y / 3)) ratio = " | 4:3";
                else if ((x / 14) == (y / 9)) ratio = " | 14:9";
                else if ((x / 32) == (y / 9)) ratio = " | 32:9";
                else if ((x / 64) == (y / 27)) ratio = " | 64:27";
                else if ((x / 3) == (y / 2)) ratio = " | 3:2";
                else if ((x / 5) == (y / 4)) ratio = " | 5:4";
                else if ((x / 256) == (y / 135)) ratio = " | 256:135";
                else if ((x / 25) == (y / 16)) ratio = " | 25:16";
                else if ((x) == (y)) ratio = " | 1:1";
                resolution += ratio;
            }
            return resolution;
        }

        private static String RemoveRatio(String resolution)
        {
            return resolution.Split('|')[0].Trim(' ');
        }

        public String[] GetAvailableMonitors()
        {
            Screen[] allScreens = Screen.AllScreens;
            String[] result = new String[allScreens.Length];
            for (Int32 index = 0; index < allScreens.Length; index++)
            {
                result[index] = FormatMonitorString(index);

                if (allScreens[index].Primary)
                    _activeMonitor = result[index];
            }
            return result;
        }

        public static String FormatMonitorString(Int32 index)
        {
            Screen[] allScreens = Screen.AllScreens;
            Dictionary<Int32, String> friendlyNames = ScreenInterrogatory.GetAllMonitorFriendlyNamesSafe();

            StringBuilder sb = new StringBuilder();

            sb.Append(index);
            sb.Append(" - ");

            if (index >= 0 && index < allScreens.Length)
            {
                Screen screen = allScreens[index];

                if (!friendlyNames.TryGetValue(index, out String name) || string.IsNullOrEmpty(name))
                    name = screen.DeviceName;

                sb.Append(name);

                if (screen.Primary)
                    sb.Append((String)Lang.Res["Settings.PrimaryMonitor"]);
            }
            else
            {
                sb.Append("Unknown Monitor");
            }

            return (sb.ToString());
        }

        private struct DevMode
        {
            private const Int32 CCHDEVICENAME = 32;
            private const Int32 CCHFORMNAME = 32;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public String dmDeviceName;
            public Int16 dmSpecVersion;
            public Int16 dmDriverVersion;
            public Int16 dmSize;
            public Int16 dmDriverExtra;
            public Int32 dmFields;
            public Int32 dmPositionX;
            public Int32 dmPositionY;
            public ScreenOrientation dmDisplayOrientation;
            public Int32 dmDisplayFixedOutput;
            public Int16 dmColor;
            public Int16 dmDuplex;
            public Int16 dmYResolution;
            public Int16 dmTTOption;
            public Int16 dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public String dmFormName;
            public Int16 dmLogPixels;
            public Int32 dmBitsPerPel;
            public Int32 dmPelsWidth;
            public Int32 dmPelsHeight;
            public Int32 dmDisplayFlags;
            public Int32 dmDisplayFrequency;
            public Int32 dmICMMethod;
            public Int32 dmICMIntent;
            public Int32 dmMediaType;
            public Int32 dmDitherType;
            public Int32 dmReserved1;
            public Int32 dmReserved2;
            public Int32 dmPanningWidth;
            public Int32 dmPanningHeight;
        }
    }
}
