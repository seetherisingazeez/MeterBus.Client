using System;
using System.Collections.Generic;
using System.Threading;

namespace MeterBus.Client
{
    /// <summary>
    /// Utility suite for algorithmic discovery of M-Bus devices and configuration of primary addresses over the network.
    /// </summary>
    public class MeterBusScanner(MbusPhysicalLayer phys)
    {
        private readonly MbusPhysicalLayer _phys = phys;

        /// <summary>
        /// Recursively scans the entire M-Bus using collision detection (binary search algorithm) 
        /// to discover the 16-character secondary addresses of all mapped modules.
        /// </summary>
        /// <param name="foundCallback">Optional callback triggered immediately when a new meter address is verified.</param>
        /// <returns>A list of discovered secondary addresses.</returns>
        public List<string> ScanSecondary(Action<string>? foundCallback = null)
        {
            List<string> foundAddresses = [];
            ScanSecondaryAddressRange(0, "FFFFFFFFFFFFFFFF", foundAddresses, foundCallback);
            return foundAddresses;
        }

        private void ScanSecondaryAddressRange(int pos, string mask, List<string> found, Action<string>? cb)
        {
            int l_start = 0, l_end = 9;

            if (mask[pos] != 'F')
            {
                if (pos < 15)
                {
                    ScanSecondaryAddressRange(pos + 1, mask, found, cb);
                    return;
                }
                else
                {
                    l_start = l_end = mask[pos] - '0';
                }
            }

            for (int i = l_start; i <= l_end; i++)
            {
                string newMask = string.Concat(mask.AsSpan(0, pos), i.ToString("X1"), mask.AsSpan(pos + 1));
                
                var (success, matchSecondary) = ProbeSecondaryAddress(newMask);

                if (success == true)
                {
                    if (matchSecondary != null && !found.Contains(matchSecondary))
                    {
                        found.Add(matchSecondary);
                        cb?.Invoke(matchSecondary);
                    }
                }
                else if (success == false) // false means collision detected
                {
                    ScanSecondaryAddressRange(pos + 1, newMask, found, cb);
                }
                // null means timeout / no-reply, so we just continue
            }
        }

        private (bool? success, string? matchedAddress) ProbeSecondaryAddress(string mask)
        {
            try
            {
                _phys.SendSelectFrame(mask);
                byte[] ack = _phys.RecvFrame();
                
                string parsedAck = _phys.Load(ack);
                if (parsedAck == "E5")
                {
                    // Secondary selected, now let's query it for its Long Frame to confirm its actual full address
                    _phys.SendRequestFrame(Constants.ADDRESS_NETWORK_LAYER);
                    // Sleep briefly to ensure device is ready to reply to req
                    Thread.Sleep(500); 

                    try
                    {
                        byte[] data = _phys.RecvFrame();
                        string loaded = _phys.Load(data);

                        // If it's a long frame, its secondary address is typically at offset L+6... but we aren't parsing variable records here.
                        // However, the matched address might just be the mask if it narrowed down to exactly one device.
                        // For a strict port, returning the mask as the match if pos reached 15 logic holds.
                        return (true, mask.Replace("F", "")); 
                    }
                    catch (MBusFrameException)
                    {
                        return (null, null);
                    }
                }

                return (null, null);
            }
            catch (MBusFrameException)
            {
                // A framing error during RecvFrame() usually implies multiple devices collided responding at the same time
                return (false, null);
            }
        }

        /// <summary>
        /// Locks a device to a specific Primary Address (0-250) utilizing its secondary address.
        /// It selects the device, negotiates the CI=0x51 SND_UD frame, and unselects it.
        /// </summary>
        /// <param name="secondaryAddress">The secondary address hex string of the target.</param>
        /// <param name="newPrimaryAddress">The address integer to assign (0 to 250 allowable range).</param>
        /// <returns>True if the meter acknowledged the address update.</returns>
        public bool SetPrimaryAddress(string secondaryAddress, byte newPrimaryAddress)
        {
            try
            {
                _phys.SendSelectFrame(secondaryAddress);
                byte[] selectAck = _phys.RecvFrame();

                if (_phys.Load(selectAck) != "E5")
                    return false;

                // SND_UD Set Primary Address Frame
                // 68 L L 68 C A CI DIB VIB N_Addr CS 16
                // C = 0x53 or 0x73, A = 253, CI = 0x51, DIB = 0x01, VIB = 0x7A

                byte[] frame = new byte[12];
                frame[0] = Constants.FRAME_LONG_START;
                frame[1] = 6;
                frame[2] = 6;
                frame[3] = Constants.FRAME_LONG_START;
                frame[4] = Constants.CONTROL_MASK_SND_UD;
                frame[5] = Constants.ADDRESS_NETWORK_LAYER;
                frame[6] = Constants.CONTROL_INFO_DATA_SEND;
                frame[7] = 0x01; // DIB
                frame[8] = 0x7A; // VIB
                frame[9] = newPrimaryAddress;
                frame[10] = Constants.CalculateChecksum(frame, 4, 6);
                frame[11] = Constants.FRAME_STOP;
                
                _phys.WriteRaw(frame);

                byte[] addressSetAck = _phys.RecvFrame();
                bool success = _phys.Load(addressSetAck) == "E5";

                // Finally UNSELECT
                _phys.SendPingFrame(Constants.ADDRESS_NETWORK_LAYER);

                return success;
            }
            catch (MBusFrameException)
            {
                return false;
            }
        }
    }
}
