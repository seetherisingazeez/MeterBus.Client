# MeterBus.Client

`MeterBus.Client` is a specialized, lightweight C# library designed to interact directly with the **M-Bus (Meter-Bus)** network via TCP/Ethernet gateways. 

Unlike heavy parsers that attempt to decode every proprietary vendor data record, this library strictly focuses on the **physical communication layer and framing protocol** (EN 13757). It seamlessly scans the network, assigns addresses, queries devices, and returns cleanly validated raw HEX frames ready for downstream processing.

## Features
- **TCP Gateway Support**: Natively connects to Ethernet-to-Serial M-Bus gateways by wrapping `System.Net.Sockets.TcpClient`.
- **Robust Connection Handling**: Predictable connection timeout loops and strict translation of broken streams into descriptive `MBusConnectionException` exceptions.
- **Strict Byte Framing**: Reads byte-by-byte and relies on protocol Start (`0x68`, `0x10`, `0xE5`), Length, and Stop tags (`0x16`) rather than arbitrary long timeouts to determine payload boundaries.
- **Secondary Address Scanning**: Contains a built-in recursive collision-detection scanner to map unknown/unaddressed meters on the network.
- **Primary Address Assignment**: Helper functionalities to automatically select a meter by its secondary address, set its primary address natively over M-Bus payloads, and cleanly unselect it across the bus.

## Example Use Cases

### 1. Requesting Data from a Known Primary Address
You can ping devices and request their main datagrams directly if you know their `Primary Address` (0-250 allowable).

```csharp
using MeterBus.Client;
using System;

// 1. Establish connection to M-Bus Gateway on Local Area Network
using var serial = new MbusTcpClient("192.168.1.50", 10001)
{
    ReadTimeoutMs = 1500,
    WriteTimeoutMs = 1500
};
serial.Connect();

var layer = new MbusPhysicalLayer(serial);
byte primaryAddress = 14;

// 2. Ping the device to ensure it's awake 
layer.SendPingFrame(primaryAddress);
byte[] pingAckRaw = layer.RecvFrame();
Console.WriteLine($"Ping Response: {layer.Load(pingAckRaw)}"); // Output: "E5"

// 3. Request User Data (REQ_UD2)
layer.SendRequestFrame(primaryAddress);
byte[] dataFrameBytes = layer.RecvFrame();

// 4. Load validates the CRC bounds and extracts the raw Payload String
string rawTelegramHex = layer.Load(dataFrameBytes);
Console.WriteLine($"Meter Telegram: {rawTelegramHex}");
```

### 2. Discovering Unmapped Devices (Network Scan)
If you have multiple meters with secondary addresses (printed on their casing) but you do not know their configured Primary codes, you can recursively wipe out collisions on the tree.

```csharp
using MeterBus.Client;
using System;

using var serial = new MbusTcpClient("192.168.1.50", 10001);
serial.Connect();

var layer = new MbusPhysicalLayer(serial);
var scanner = new MeterBusScanner(layer);

Console.WriteLine("Scanning M-BUS for unmapped devices...");

// The scanner takes an optional Action<string> callback that fires immediately upon discovery
var foundDevices = scanner.ScanSecondary((address) => 
{
    Console.WriteLine($"[Scanned] Found Device with Secondary Address: {address}");
});

Console.WriteLine($"Scan complete. Total mapped: {foundDevices.Count}");
```

### 3. Assigning a Primary Address
Once you've identified a device's secondary address (16-char hex), you can permanently assign it a Primary Address.

```csharp
using MeterBus.Client;
using System;

using var serial = new MbusTcpClient("192.168.1.50", 10001);
serial.Connect();

var layer = new MbusPhysicalLayer(serial);
var scanner = new MeterBusScanner(layer);

bool wasSuccessful = scanner.SetPrimaryAddress("1002233400000000", newPrimaryAddress: 55);

if (wasSuccessful) 
{
    Console.WriteLine("Successfully locked Primary address to 55.");
}
```

## Exceptions
The library raises structured exceptions:
* `MBusConnectionException`: Raised if the TCP connect times out, or the stream dies prematurely (handling remote LAN outages).
* `MBusFrameException`: Raised if M-Bus CRCs mismatch, a length block stretches out of bounds, or if the `RecvFrame` sliding window expires searching for an M-Bus `Start` byte. Handle this when devices are completely unresponsive.
