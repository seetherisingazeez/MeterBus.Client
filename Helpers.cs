#pragma warning disable CS1591
using System;

namespace MeterBus.Client
{
    public static class Constants
    {
        public const byte ADDRESS_BROADCAST_REPLY = 0xFE;
        public const byte ADDRESS_BROADCAST_NOREPLY = 0xFF;
        public const byte ADDRESS_NETWORK_LAYER = 0xFD;

        public const byte CONTROL_MASK_SND_NKE = 0x40;
        public const byte CONTROL_MASK_SND_UD = 0x53; // Or 0x73 if tracking FCB bit
        public const byte CONTROL_MASK_REQ_UD2 = 0x5B; // Or 0x7B if tracking FCB bit
        
        public const byte CONTROL_MASK_DIR_M2S = 0x40;
        public const byte CONTROL_MASK_DIR_S2M = 0x00;

        public const byte CONTROL_MASK_FCB = 0x20;
        public const byte CONTROL_MASK_FCV = 0x10;

        public const byte CONTROL_INFO_DATA_SEND = 0x51;
        public const byte CONTROL_INFO_SELECT_SLAVE = 0x52;

        public const byte FRAME_ACK_START = 0xE5;
        public const byte FRAME_SHORT_START = 0x10;
        public const byte FRAME_LONG_START = 0x68;
        public const byte FRAME_STOP = 0x16;

        public static byte CalculateChecksum(byte[] buffer, int offset, int length)
        {
            int sum = 0;
            for (int i = offset; i < offset + length; i++)
            {
                sum += buffer[i];
            }
            return (byte)(sum % 256);
        }

        public static string BytesToHex(byte[] buffer)
        {
            return BitConverter.ToString(buffer).Replace("-", "").ToUpperInvariant();
        }
    }

    public class MBusFrameException(string message) : Exception(message)
    {
    }

    public class MBusConnectionException(string message, Exception? innerException = null) : Exception(message, innerException)
    {
    }
}
