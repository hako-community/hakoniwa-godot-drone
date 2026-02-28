using System;
namespace hakoniwa.environment.interfaces
{
    public interface IDataPacket
    {
        public string GetRobotName();
        public int GetChannelId();
        public byte[] GetPduData();
        public byte[] Encode();
    }
    public static class PduMagicNumbers
    {
        public const uint DeclarePduForRead = 0x52455044;  // "REPD" (Read Pdu)
        public const uint DeclarePduForWrite = 0x57505044; // "WPPD" (Write Pdu)
    }    
}
