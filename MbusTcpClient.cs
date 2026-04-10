using System;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace MeterBus.Client
{
    /// <summary>
    /// Wrapper for a TCP connection exposing basic read/write capabilities with timeouts, 
    /// specifically tailored for bridging M-Bus protocols over Ethernet sockets.
    /// </summary>
    public class MbusTcpClient : IDisposable
    {
        private readonly string _host;
        private readonly int _port;
        private TcpClient? _client;
        private NetworkStream? _stream;
        public int ReadTimeoutMs { get; set; } = 1500;
        public int WriteTimeoutMs { get; set; } = 1000;

        /// <summary>
        /// Instantiates a new client configuring the target host and port.
        /// </summary>
        /// <param name="host">The IPv4/IPv6 address or hostname of the M-Bus gateway.</param>
        /// <param name="port">The TCP Port of the gateway.</param>
        public MbusTcpClient(string host, int port)
        {
            _host = host;
            _port = port;
        }

        /// <summary>
        /// Synchronously attempts to connect to the configured endpoint. 
        /// Ensures a short timeout window to prevent hanging socket configurations.
        /// </summary>
        /// <exception cref="MBusConnectionException">Thrown when the connection times out or the endpoint rejects it.</exception>
        public void Connect()
        {
            try
            {
                _client = new TcpClient();
                // Avoid using synchronous blocked connect indefinitely
                if (!_client.ConnectAsync(_host, _port).Wait(3000))
                {
                    throw new MBusConnectionException($"Timeout connecting to tcp://{_host}:{_port}");
                }

                _stream = _client.GetStream();
                _stream.ReadTimeout = ReadTimeoutMs;
                _stream.WriteTimeout = WriteTimeoutMs;
            }
            catch (Exception ex)
            {
                throw new MBusConnectionException($"TCP connection failed to {_host}:{_port}: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Pushes a raw array of bytes across the active TCP stream.
        /// </summary>
        /// <param name="data">The byte payload to write.</param>
        /// <exception cref="MBusConnectionException">Thrown if the stream is dead or write fails.</exception>
        public void Write(byte[] data)
        {
            try
            {
                if (_stream == null || !(_client?.Connected ?? false))
                    throw new MBusConnectionException("Client is not connected.");

                _stream.Write(data, 0, data.Length);
            }
            catch (IOException ex)
            {
                throw new MBusConnectionException("Failed to write to TCP stream.", ex);
            }
        }

        /// <summary>
        /// Reads a single byte from the Network stream. 
        /// </summary>
        /// <returns>The byte read, or -1 if EOF or an expected ReadTimeout occurs.</returns>
        /// <exception cref="MBusConnectionException">Thrown if connection faults unexpectedly.</exception>
        public int ReadByte()
        {
            try
            {
                if (_stream == null || !(_client?.Connected ?? false))
                    throw new MBusConnectionException("Client is not connected.");

                return _stream.ReadByte(); // returns -1 on EOF, throws on timeout
            }
            catch (IOException)
            {
                return -1; // Treat read timeouts as end-of-frame gracefully
            }
            catch (Exception ex)
            {
                 throw new MBusConnectionException("Failed to read from TCP stream.", ex);
            }
        }

        public void Dispose()
        {
            _stream?.Dispose();
            _client?.Dispose();
        }
    }
}
