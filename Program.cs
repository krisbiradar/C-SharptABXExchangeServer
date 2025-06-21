using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ABXExchangeClient
{
    public enum BuySellIndicator
    {
        B,
        S
    }

    public class Packet : IValidatableObject
    {
        [Required]
        public string Symbol { get; set; }

        [EnumDataType(typeof(BuySellIndicator))]
        [Required]
        public string BuySellIndicator { get; set; }

        [Range(1, int.MaxValue)]
        [Required]
        public int Quantity { get; set; }

        [Range(0, int.MaxValue)]
        [Required]
        public int Price { get; set; }

        [Range(1, int.MaxValue)]
        [Required]
        public int PacketSequence { get; set; }

        public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
        {
            if (!Enum.TryParse(this.BuySellIndicator, true, out BuySellIndicator value))
            {
                yield return new ValidationResult("buy sell indicator must be any of (B,S)", new[] { nameof(BuySellIndicator) });
            }
        }
    }

    public class ABXClient
    {
        private readonly IPAddress ServerHost = IPAddress.Loopback;
        private const int ServerPort = 3000;
        private const int PacketSize = 17;
        private const int ReadTimeoutMs = 3000;

        public async Task<List<Packet>> GetAllPacketsAsync()
        {
            var allPackets = new Dictionary<int, Packet>();
            var initialPackets = await RetryHelper.RetryAsync(RequestAllPacketsAsync, maxAttempts: 3, delayMs: 2000);
            if (initialPackets == null)
            {
                Console.WriteLine("Unable to connect to server after multiple attempts.");
                return null;
            }
            foreach (var packet in initialPackets)
            {
                allPackets[packet.PacketSequence] = packet;
            }

            if (allPackets.Count == 0)
                return new List<Packet>();

            var sequences = allPackets.Keys.OrderBy(x => x).ToList();
            var minSeq = sequences.First();
            var maxSeq = sequences.Last();

            var missingSequences = new List<int>();
            for (int i = minSeq; i <= maxSeq; i++)
            {
                if (!allPackets.ContainsKey(i))
                    missingSequences.Add(i);
            }

            foreach (var seq in missingSequences)
            {
                try
                {
                    var packet = await RequestSpecificPacketAsync(seq);
                    if (packet != null)
                        allPackets[packet.PacketSequence] = packet;
                }
                catch { }
            }

            return allPackets.Values.OrderBy(p => p.PacketSequence).ToList();
        }

        private async Task<List<Packet>> RequestAllPacketsAsync()
        {
            var packets = new List<Packet>();

            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ReadTimeoutMs);
            await RetryHelper.RetryAsync<Task>(async () =>
            {
                await client.ConnectAsync(ServerHost, ServerPort);
                return Task.CompletedTask;

            }, maxAttempts: 1, delayMs: 1000, shutDown: true);

            using var stream = client.GetStream();
            stream.WriteTimeout = ReadTimeoutMs;
            stream.ReadTimeout = ReadTimeoutMs;

            var request = new byte[] { 1, 0 };
            await stream.WriteAsync(request, 0, request.Length, cts.Token);

            var dataReceived = new MemoryStream();
            var buffer = new byte[1024];

            while (true)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                }
                catch
                {
                    break;
                }

                if (bytesRead == 0) break;

                dataReceived.Write(buffer, 0, bytesRead);
            }

            packets = ParsePacketsFromBytes(dataReceived.ToArray());
            return packets;
        }

        private async Task<Packet?> RequestSpecificPacketAsync(int sequenceNumber)
        {
            using var client = new TcpClient();
            using var cts = new CancellationTokenSource(ReadTimeoutMs);
            await client.ConnectAsync(ServerHost, ServerPort);
            using var stream = client.GetStream();
            stream.ReadTimeout = ReadTimeoutMs;
            stream.WriteTimeout = ReadTimeoutMs;

            var request = new byte[] { 2, (byte)sequenceNumber };
            await stream.WriteAsync(request, 0, request.Length, cts.Token);

            var buffer = new byte[PacketSize];
            int totalRead = 0;

            while (totalRead < PacketSize)
            {
                int bytesRead;
                try
                {
                    bytesRead = await stream.ReadAsync(buffer, totalRead, PacketSize - totalRead, cts.Token);
                }
                catch
                {
                    break;
                }

                if (bytesRead == 0) break;
                totalRead += bytesRead;
            }

            if (totalRead == PacketSize)
                return ParseSinglePacket(buffer);

            return null;
        }

        private List<Packet> ParsePacketsFromBytes(byte[] data)
        {
            var packets = new List<Packet>();
            for (int i = 0; i <= data.Length - PacketSize; i += PacketSize)
            {
                var packetBytes = new byte[PacketSize];
                Array.Copy(data, i, packetBytes, 0, PacketSize);
                var packet = ParseSinglePacket(packetBytes);
                if (packet != null)
                    packets.Add(packet);
            }

            return packets;
        }

        private Packet? ParseSinglePacket(byte[] packetBytes)
        {
            if (packetBytes.Length != PacketSize)
                return null;

            try
            {
                int offset = 0;

                var symbolBytes = new byte[4];
                Array.Copy(packetBytes, offset, symbolBytes, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(symbolBytes);
                string symbol = Encoding.ASCII.GetString(symbolBytes).TrimEnd('\0');
                offset += 4;

                char buySellIndicator = (char)packetBytes[offset];
                offset += 1;

                var quantityBytes = new byte[4];
                Array.Copy(packetBytes, offset, quantityBytes, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(quantityBytes);
                int quantity = BitConverter.ToInt32(quantityBytes, 0);
                offset += 4;

                var priceBytes = new byte[4];
                Array.Copy(packetBytes, offset, priceBytes, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(priceBytes);
                int price = BitConverter.ToInt32(priceBytes, 0);
                offset += 4;

                var sequenceBytes = new byte[4];
                Array.Copy(packetBytes, offset, sequenceBytes, 0, 4);
                if (BitConverter.IsLittleEndian) Array.Reverse(sequenceBytes);
                int packetSequence = BitConverter.ToInt32(sequenceBytes, 0);

                var packet = new Packet
                {
                    Symbol = symbol,
                    BuySellIndicator = buySellIndicator.ToString(),
                    Quantity = quantity,
                    Price = price,
                    PacketSequence = packetSequence
                };

                var validationResults = new List<ValidationResult>();
                if (Validator.TryValidateObject(packet, new ValidationContext(packet), validationResults, true))
                    return packet;

                return null;
            }
            catch
            {
                return null;
            }
        }
    }

    class Program
    {

        static async Task Main(string[] args)
        {
            var client = new ABXClient();
            var packets = await client.GetAllPacketsAsync();

            if (packets.Count > 0)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonOutput = JsonSerializer.Serialize(packets, options);
                string fileName = $"abx_data_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                await File.WriteAllTextAsync(fileName, jsonOutput);

                Console.WriteLine($"Retrieved {packets.Count} packets");
                Console.WriteLine($"Saved to: {fileName}");

                foreach (var packet in packets.Take(5))
                    Console.WriteLine($"Seq: {packet.PacketSequence}, Symbol: {packet.Symbol}, {packet.BuySellIndicator}, Qty: {packet.Quantity}, Price: {packet.Price}");
            }
            else
            {
                Console.WriteLine("No packets received.");
            }

            Console.ReadKey();
        }
    }


    public static class RetryHelper
    {
        public static async Task<T?> RetryAsync<T>(Func<Task<T?>> action, int maxAttempts, int delayMs, bool shutDown = false)
        {
            for (int attempt = 1; attempt <= maxAttempts + 1; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (SocketException ex) when (attempt <= maxAttempts)
                {
                    Console.WriteLine($"Error connecting server {ex.Message}");
                    await Task.Delay(delayMs);
                }
                catch (IOException ex) when (attempt <= maxAttempts)
                {
                    Console.WriteLine($"Error connecting server {ex.Message}");
                    await Task.Delay(delayMs);
                }
                catch (TimeoutException) when (attempt <= maxAttempts)
                {
                    Console.WriteLine($"Connection timed out");
                    await Task.Delay(delayMs);
                }
                catch (Exception e) when (attempt > maxAttempts)
                {
                    Console.WriteLine($"All retry attempts exhausted Returning...");
                    if (shutDown)
                    {
                        Console.OutputEncoding = System.Text.Encoding.UTF8;

                        Console.WriteLine("Shutting Down...");
                        Console.WriteLine("Adios Amigos 😉");
                        Environment.Exit(0);
                    }
                    return default;
                }
            }

            return default;
        }
    }

}
