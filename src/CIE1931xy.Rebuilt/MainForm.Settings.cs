using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using CIE1931xy.Core;
using CIE1931xy.Core.Rendering;

namespace CIE1931xy
{
    internal sealed partial class MainForm
    {
        private readonly string _settingsPath;
        private Timer _settingsTimer;
        private bool _settingsReady;
        private bool _settingsDirty;
        private string _savedSettings;

        private void InitializeSettings()
        {
            string warning;
            AppSettings settings = SettingsStore.Load(_settingsPath, out warning);
            bool fontFallback = false;
            settings.FontFamily = InstalledFont(settings.FontFamily, "Times New Roman", ref fontFallback);
            settings.ChineseFontFamily = InstalledFont(settings.ChineseFontFamily, "SimSun", ref fontFallback);
            _suppressUiEvents = true;
            try
            {
                settings.ApplyTo(_style);
                _style.UseBitmapBackground = false;
                _style.Bounds = FixedBounds();
                _cmbKind.SelectedIndex = settings.DiagramKind == DiagramKind.Uv1976 ? 1 : 0;
                _chkBlackbody.Checked = settings.ShowBlackbody;
                _chkGrid.Checked = settings.ShowGrid;
                _chkReferenceLine.Checked = settings.ShowReferenceLine;
                _chkWavelength.Checked = settings.ShowWavelengthLabels;
                _chkLegend.Checked = settings.ShowLegend;
                _chkLabels.Checked = settings.ShowPointLabels;
                _chkBlackBackground.Checked = settings.BlackBackground;
                _menuBlackBackground.Checked = settings.BlackBackground;
                _canvas.BackColor = _style.Background;
                _exportDpi = settings.ExportDpi;
                _numDpi.Value = settings.ExportDpi;
                _lastExportName = settings.LastExportName;
                UpdateReferenceLineState();
            }
            finally { _suppressUiEvents = false; }
            _savedSettings = SettingsStore.Serialize(CaptureSettings());
            _settingsTimer = new Timer { Interval = 400 };
            _settingsTimer.Tick += delegate { _settingsTimer.Stop(); SaveSettings(); };
            FormClosing += delegate { _settingsTimer.Stop(); SaveSettings(); };
            Disposed += delegate { _settingsTimer.Dispose(); _renderTimer.Dispose(); };
            _settingsReady = true;
            if (fontFallback) warning = "配置中的部分字体在本机不可用，已使用默认字体。";
            if (!string.IsNullOrEmpty(warning)) Shown += delegate { AddMessage(warning); };
        }

        private static string InstalledFont(string name, string fallback, ref bool usedFallback)
        {
            try { using (FontFamily font = new FontFamily(name)) { return name; } }
            catch (ArgumentException) { usedFallback = true; return fallback; }
        }

        private AppSettings CaptureSettings()
        {
            return AppSettings.Capture(_style, _exportDpi, _lastExportName);
        }

        private void ScheduleSettingsSave()
        {
            if (!_settingsReady || _suppressUiEvents || _settingsPath == null) return;
            _settingsDirty = true;
            _settingsTimer.Stop();
            _settingsTimer.Start();
        }

        private void SaveSettings()
        {
            if (!_settingsReady || !_settingsDirty || _settingsPath == null) return;
            string content = SettingsStore.Serialize(CaptureSettings());
            if (content == _savedSettings) { _settingsDirty = false; return; }
            try
            {
                SettingsStore.Save(_settingsPath, content);
                _savedSettings = content;
                _settingsDirty = false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
            {
                SetStatus("无法保存配置，请检查程序目录的写入权限：" + ex.Message);
            }
        }
    }
}
