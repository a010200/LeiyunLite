using System;

namespace RazerBatteryTray
{
    public class MouseBatteryInfo
    {
        public int ProductId { get; set; }
        public string DeviceKey { get; set; }
        public string InterfacePath { get; set; }
        public string RawProductString { get; set; }
        public string ConnectionKind { get; set; }
        public DeviceKind DeviceKind { get; set; }
        public DeviceProtocolStatus ProtocolStatus { get; set; }
        public bool BatteryKnown { get; set; }
        public bool IsWriteSupported { get; set; }
        public bool IsWriteHardwareVerified { get; set; }
        public bool IsConnected { get; set; }
        public bool IsSleeping { get; set; }
        public bool IsDonglePresent { get; set; }
        public string DeviceName { get; set; }
        public int BatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public DateTime LastUpdated { get; set; }

        public int Dpi { get; set; }
        public int DpiStage { get; set; }
        public int DpiStageCount { get; set; }
        public int[] DpiStages { get; set; }
        public int PollingRate { get; set; }
        public bool RotationKnown { get; set; }
        public int RotationAngle { get; set; }
        public bool IsRotationWriteSupported { get; set; }
        public bool IsRotationHardwareVerified { get; set; }
    }
}
