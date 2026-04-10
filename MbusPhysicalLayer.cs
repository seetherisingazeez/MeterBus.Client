using System;
using System.Collections.Generic;

namespace MeterBus.Client
{
    /// <summary>
    /// Represents the M-Bus Data Link / Physical layer. 
    /// Responsible for composing raw control bytes into valid M-Bus UART frames (Short, Long, Control blocks)
    /// and safely reading raw structured frames from a stream boundary.
    /// </summary>
    public class MbusPhysicalLayer
    {
        private readonly MbusTcpClient _serial;

        /// <summary>
        /// Injects the underlying TcpClient mechanism which will carry the M-Bus link payload.
        /// </summary>
        /// <param name="serial">The instantiated wrapper for the tcp/ip serial stream.</param>
        public MbusPhysicalLayer(MbusTcpClient serial)
        {
            _serial = serial;
        }

        /// <summary>
        /// Extricates the payload writing functionality directly, letting higher layers push custom long frames.
        /// </summary>
        /// <param name="frame">The fully constructed raw byte frame to send.</param>
        public void WriteRaw(byte[] frame)
        {
            _serial.Write(frame);
        }

        /// <summary>
        /// Sends an SND_NKE (Ping) Short Frame to the specified address.
        /// Transmits: 10 40 [address] [CS] 16
        /// </summary>
        /// <param name="address">The target M-Bus primary or special address.</param>
        public void SendPingFrame(byte address)
        {
            // Short Frame: 10 40 [address] [cs] 16
            byte cField = (byte)(Constants.CONTROL_MASK_SND_NKE | Constants.CONTROL_MASK_DIR_M2S);
            byte[] frame = new byte[5];
            frame[0] = Constants.FRAME_SHORT_START;
            frame[1] = cField;
            frame[2] = address;
            frame[3] = Constants.CalculateChecksum(frame, 1, 2);
            frame[4] = Constants.FRAME_STOP;

            _serial.Write(frame);
        }

        /// <summary>
        /// Sends a REQ_UD2 Request Frame asking the meter to transmit its User Data (Class 2).
        /// </summary>
        /// <param name="address">The Primary address, or 253 (Network Layer) if a meter was previously selected by its Secondary address.</param>
        public void SendRequestFrame(byte? address)
        {
            if (!address.HasValue) return;

            // Short Frame: 10 5B [address] [cs] 16
            byte cField = (byte)(Constants.CONTROL_MASK_REQ_UD2 | Constants.CONTROL_MASK_DIR_M2S);
            byte[] frame = new byte[5];
            frame[0] = Constants.FRAME_SHORT_START;
            frame[1] = cField;
            frame[2] = address.Value;
            frame[3] = Constants.CalculateChecksum(frame, 1, 2);
            frame[4] = Constants.FRAME_STOP;

            _serial.Write(frame);
        }

        /// <summary>
        /// Sends a REQ_UD2 Request Frame utilizing the Frame Count Bit (FCB) to toggle block retrieval
        /// for multi-telegram answers where total meter data size exceeds standard MTU payload.
        /// </summary>
        /// <param name="address">Target primary address, or 253 to target selected secondary.</param>
        public void SendRequestFrameMulti(byte? address)
        {
            if (!address.HasValue) return;

            byte cField = (byte)(Constants.CONTROL_MASK_REQ_UD2 | Constants.CONTROL_MASK_DIR_M2S | 
                                 Constants.CONTROL_MASK_FCV | Constants.CONTROL_MASK_FCB);
            byte[] frame = new byte[5];
            frame[0] = Constants.FRAME_SHORT_START;
            frame[1] = cField;
            frame[2] = address.Value;
            frame[3] = Constants.CalculateChecksum(frame, 1, 2);
            frame[4] = Constants.FRAME_STOP;

            _serial.Write(frame);
        }

        /// <summary>
        /// Sends an SND_UD Control Long Frame specifically crafted with CI=0x52 to select a meter 
        /// utilizing its 16-character secondary address. Once selected, a meter will answer to Address 253.
        /// </summary>
        /// <param name="secondaryAddress">The 16 character hex string of the meter (usually printed on the casing).</param>
        /// <exception cref="ArgumentException">Thrown if address string is not strictly 16 chars.</exception>
        public void SendSelectFrame(string secondaryAddress)
        {
            if (secondaryAddress == null || secondaryAddress.Length != 16)
                throw new ArgumentException("Secondary address must be 16 hex characters");

            // Long frame -> L = 11 (C + A + CI + 8 byte payload)
            // 68 0B 0B 68 53 FD 52 [8 bytes] CS 16
            byte[] frame = new byte[17];
            frame[0] = Constants.FRAME_LONG_START;
            frame[1] = 11;
            frame[2] = 11;
            frame[3] = Constants.FRAME_LONG_START;
            
            frame[4] = (byte)(Constants.CONTROL_MASK_SND_UD | Constants.CONTROL_MASK_DIR_M2S | Constants.CONTROL_MASK_FCB);
            frame[5] = Constants.ADDRESS_NETWORK_LAYER;
            frame[6] = Constants.CONTROL_INFO_SELECT_SLAVE;

            // Address string to bytes correctly formatted (reverse nested endians based on M-Bus spec)
            frame[7] = Convert.ToByte(secondaryAddress.Substring(14, 2), 16);
            frame[8] = Convert.ToByte(secondaryAddress.Substring(12, 2), 16);
            
            int mbMan = Convert.ToInt32(secondaryAddress.Substring(8, 4), 16);
            frame[9] = (byte)((mbMan >> 8) & 0xFF);
            frame[10] = (byte)(mbMan & 0xFF);

            frame[11] = Convert.ToByte(secondaryAddress.Substring(0, 2), 16);
            frame[12] = Convert.ToByte(secondaryAddress.Substring(2, 2), 16);
            frame[13] = Convert.ToByte(secondaryAddress.Substring(4, 2), 16);
            frame[14] = Convert.ToByte(secondaryAddress.Substring(6, 2), 16);

            frame[15] = Constants.CalculateChecksum(frame, 4, 11);
            frame[16] = Constants.FRAME_STOP;

            _serial.Write(frame);
        }

        /// <summary>
        /// Blocks and strictly reads a single M-Bus frame from the stream.
        /// Detects Start byte boundaries (0xE5, 0x10, 0x68) and reads exactly as many bytes as indicated by 
        /// the Long frame length (L) field, preventing blocking timeouts on clean reads.
        /// </summary>
        /// <returns>The complete raw byte array spanning the frame.</returns>
        /// <exception cref="MBusFrameException">Thrown if stream disconnects or timeouts expire before a boundary is verified.</exception>
        public byte[] RecvFrame()
        {
            List<byte> buffer = new List<byte>();

            while (true)
            {
                int b = _serial.ReadByte();
                if (b == -1) break; // Timeout/End of stream

                buffer.Add((byte)b);

                // ACK Frame
                if (buffer.Count == 1 && buffer[0] == Constants.FRAME_ACK_START)
                {
                    return buffer.ToArray();
                }

                // Wait for enough bytes to determine frame length
                if (buffer.Count >= 5)
                {
                    // Short Frame
                    if (buffer[0] == Constants.FRAME_SHORT_START)
                    {
                        if (buffer.Count == 5)
                        {
                            if (buffer[4] == Constants.FRAME_STOP)
                            {
                                return buffer.ToArray();
                            }
                            buffer.RemoveAt(0); // Invalid, slide window
                        }
                    }
                    // Long / Control Frame
                    else if (buffer[0] == Constants.FRAME_LONG_START)
                    {
                        if (buffer[1] == buffer[2] && buffer[3] == Constants.FRAME_LONG_START)
                        {
                            int expectedLen = buffer[1] + 6; // L+6 (Start,L,L,Start...CS,Stop)
                            if (buffer.Count == expectedLen)
                            {
                                if (buffer[expectedLen - 1] == Constants.FRAME_STOP)
                                {
                                    return buffer.ToArray();
                                }
                                buffer.RemoveAt(0); // Slide window
                            }
                        }
                        else 
                        {
                            buffer.RemoveAt(0); // Invalid structure, slide window
                        }
                    }
                    else
                    {
                        buffer.RemoveAt(0); // Not a valid start byte, slide window
                    }
                }
            }

            throw new MBusFrameException("Timeout or disconnected waiting for a valid M-Bus frame");
        }

        /// <summary>
        /// Reads / verifies bounds, returns the Hex representation of the raw telegram payload
        /// </summary>
        public string Load(byte[] data)
        {
            if (data == null || data.Length == 0)
                throw new MBusFrameException("Empty frame data");

            // Simple validation without deep record parsing
            if (data.Length == 1 && data[0] == Constants.FRAME_ACK_START)
                return Constants.BytesToHex(data);

            if (data[0] == Constants.FRAME_SHORT_START && data.Length == 5 && data[data.Length-1] == Constants.FRAME_STOP)
            {
                byte targetCs = Constants.CalculateChecksum(data, 1, 2);
                if (targetCs != data[3]) throw new MBusFrameException("Short frame Invalid Checksum");
                return Constants.BytesToHex(data);
            }

            if (data[0] == Constants.FRAME_LONG_START && data[3] == Constants.FRAME_LONG_START && data[data.Length-1] == Constants.FRAME_STOP)
            {
                int L = data[1];
                if (L + 6 != data.Length) throw new MBusFrameException("Long frame length mismatch");

                byte targetCs = Constants.CalculateChecksum(data, 4, L);
                if (targetCs != data[data.Length - 2]) throw new MBusFrameException("Long frame Invalid Checksum");
                
                return Constants.BytesToHex(data);
            }

            throw new MBusFrameException("Unknown or severely malformed M-Bus frame");
        }
    }
}
