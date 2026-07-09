using System;
using System.IO;
using Hearthstone_Deck_Tracker;
using Hearthstone_Deck_Tracker.Utility.Logging;

namespace ShowPing
{
    public class ShowPingSettings
    {
        public bool ShowServerPing { get; set; } = true;
        public bool ShowPacketLoss { get; set; } = true;
        public bool ShowEndpointIp { get; set; }
        public bool CompactMode { get; set; }
        public bool NetworkOverlayManualPosition { get; set; }
        public int FontWeightMode { get; set; } = 1;
        public int TextScalePercent { get; set; } = 100;
        public int OverlayOpacityPercent { get; set; } = 75;
        public int CheckIntervalSeconds { get; set; } = 2;
        public double NetworkOverlayLeft { get; set; } = 100;
        public double NetworkOverlayTop { get; set; } = 100;
        public double NetworkOverlayWidth { get; set; } = 106;
        public double NetworkOverlayHeight { get; set; } = 36;

        public ShowPingSettings Clone()
        {
            return new ShowPingSettings
            {
                ShowServerPing = ShowServerPing,
                ShowPacketLoss = ShowPacketLoss,
                ShowEndpointIp = ShowEndpointIp,
                CompactMode = CompactMode,
                NetworkOverlayManualPosition = NetworkOverlayManualPosition,
                FontWeightMode = FontWeightMode,
                TextScalePercent = TextScalePercent,
                OverlayOpacityPercent = OverlayOpacityPercent,
                CheckIntervalSeconds = CheckIntervalSeconds,
                NetworkOverlayLeft = NetworkOverlayLeft,
                NetworkOverlayTop = NetworkOverlayTop,
                NetworkOverlayWidth = NetworkOverlayWidth,
                NetworkOverlayHeight = NetworkOverlayHeight
            };
        }

        public void Normalize()
        {
            if (CheckIntervalSeconds < 2)
                CheckIntervalSeconds = 2;
            if (CheckIntervalSeconds > 10)
                CheckIntervalSeconds = 10;
            if (TextScalePercent < 75)
                TextScalePercent = 75;
            if (TextScalePercent > 150)
                TextScalePercent = 150;
            if (OverlayOpacityPercent < 10)
                OverlayOpacityPercent = 10;
            if (OverlayOpacityPercent > 100)
                OverlayOpacityPercent = 100;
            if (FontWeightMode < 0)
                FontWeightMode = 0;
            if (FontWeightMode > 3)
                FontWeightMode = 3;
            if (NetworkOverlayWidth < 70)
                NetworkOverlayWidth = 70;
            if (NetworkOverlayHeight < 18)
                NetworkOverlayHeight = 18;
            if (double.IsNaN(NetworkOverlayLeft) || double.IsInfinity(NetworkOverlayLeft))
                NetworkOverlayLeft = 100;
            if (double.IsNaN(NetworkOverlayTop) || double.IsInfinity(NetworkOverlayTop))
                NetworkOverlayTop = 100;
            if (NetworkOverlayLeft < 0)
                NetworkOverlayLeft = 0;
            if (NetworkOverlayTop < 0)
                NetworkOverlayTop = 0;
        }
    }

    internal static class SettingsStore
    {
        private static readonly string FilePath = Path.Combine(Config.AppDataPath, "showping.xml");

        public static ShowPingSettings Load()
        {
            if (!File.Exists(FilePath))
                return new ShowPingSettings();

            try
            {
                var settings = XmlManager<ShowPingSettings>.Load(FilePath);
                settings.Normalize();
                return settings;
            }
            catch (Exception ex)
            {
                Log.Error("ShowPing settings load error:\n" + ex);
                return new ShowPingSettings();
            }
        }

        public static void Save(ShowPingSettings settings)
        {
            try
            {
                settings.Normalize();
                XmlManager<ShowPingSettings>.Save(FilePath, settings);
            }
            catch (Exception ex)
            {
                Log.Error("ShowPing settings save error:\n" + ex);
            }
        }
    }
}
