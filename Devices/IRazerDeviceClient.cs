namespace RazerBatteryTray
{
    // The UI uses this boundary; tests can supply a device without touching USB.
    public interface IRazerDeviceClient
    {
        int CachedBatteryPercent { get; }
        MouseBatteryInfo QueryRazerDeviceInfo();
        bool FastQueryDpi(out int dpi, out int stage, out int count);
        bool SetRazerDpiStage(int stage);
        bool SetRazerDpi(int dpi);
        bool SetRazerPollingRate(int hz);
    }
}
