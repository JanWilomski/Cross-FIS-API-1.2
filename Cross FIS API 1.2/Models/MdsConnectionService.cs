using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Cross_FIS_API_1._2.Models
{
    public class MarketData
    {
        public string Glid { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty; // Dodano brakującą właściwość
        public decimal BidPrice { get; set; }
        public long BidSize { get; set; }
        public decimal AskPrice { get; set; }
        public long AskSize { get; set; }
        public decimal LastPrice { get; set; }
        public long LastSize { get; set; }
        public long Volume { get; set; }
    }

    public class MdsConnectionService
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private string _node = string.Empty; // Store node for session context
        private string _subnode = string.Empty; // Store subnode for session context

        private const byte Stx = 2;
        private const byte Etx = 3;
        private const int HeaderLength = 32;
        private const int FooterLength = 3;

        public bool IsConnected => _tcpClient?.Connected ?? false;
        public event Action<List<Instrument>>? InstrumentsReceived;
        public event Action<MarketData>? MarketDataUpdate;

        public async Task<bool> ConnectAndLoginAsync(string ipAddress, int port, string user, string password, string node, string subnode)
        {
            if (IsConnected) Disconnect();

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(ipAddress, port);
                if (!_tcpClient.Connected) return false;

                _stream = _tcpClient.GetStream();

                // Store node and subnode for subsequent messages
                _node = node;
                _subnode = subnode;

                var clientId = Encoding.ASCII.GetBytes("FISAPICLIENT    ");
                await _stream.WriteAsync(clientId, 0, clientId.Length);

                byte[] loginRequest = BuildLoginRequest(user, password);
                await _stream.WriteAsync(loginRequest, 0, loginRequest.Length);

                var buffer = new byte[1024];
                var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead > 0)
                {
                    bool loginSuccess = VerifyLoginResponse(buffer, bytesRead);
                    if (loginSuccess)
                    {
                        _ = Task.Run(ListenForMessages);
                        return true;
                    }
                }
                
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"MDS Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Disconnect();
                return false;
            }
        }

        private async Task ListenForMessages()
        {
            if (_stream == null) return;
            var buffer = new byte[32000];
            while (IsConnected)
            {
                try
                {
                    if (_stream.DataAvailable)
                    {
                        var bytesRead = await _stream.ReadAsync(buffer, 0, buffer.Length);
                        if (bytesRead > 0)
                        {
                            ProcessIncomingMessage(buffer, bytesRead);
                        }
                    }
                    await Task.Delay(50);
                }
                catch
                {
                    Disconnect();
                }
            }
        }

        public void Disconnect()
        {
            if (_tcpClient == null) return;
            _stream?.Close();
            _tcpClient?.Close();
            _tcpClient = null;
            _stream = null;
        }

        public async Task RequestAllInstrumentsAsync()
        {
            if (!IsConnected || _stream == null) return;

            int[] exchanges = { 40, 330, 331, 332 };
            int[] markets = { 1, 2, 3, 4, 5, 9, 16, 17, 20 };

            foreach (var exchange in exchanges)
            {
                foreach (var market in markets)
                {
                    string glid = $"{exchange:D4}00{market:D3}000";
                    byte[] dictionaryRequest = BuildDictionaryRequest(glid);
                    await _stream.WriteAsync(dictionaryRequest, 0, dictionaryRequest.Length);
                    await Task.Delay(100);
                }
            }
        }

        public async Task SubscribeToInstrumentAsync(string glid)
        {
            System.Diagnostics.Debug.WriteLine($"DEBUG: SubscribeToInstrumentAsync called with GLID: '{glid}'");
            if (!IsConnected || _stream == null) return;
            byte[] subscriptionRequest = BuildStockWatchRequest(glid, 1000);
            await _stream.WriteAsync(subscriptionRequest, 0, subscriptionRequest.Length);
        }

        public async Task UnsubscribeFromInstrumentAsync(string glid)
        {
            if (!IsConnected || _stream == null) return;
            byte[] unsubscriptionRequest = BuildStockWatchRequest(glid, 1002);
            await _stream.WriteAsync(unsubscriptionRequest, 0, unsubscriptionRequest.Length);
        }

        private void ProcessIncomingMessage(byte[] response, int length)
        {
            var stxPos = Array.IndexOf(response, Stx);
            if (stxPos == -1) return;

            string requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
            if (int.TryParse(requestNumberStr, out int requestNumber))
            {
                switch (requestNumber)
                {
                    case 5108: // Dictionary response
                        ProcessDictionaryResponse(response, length, stxPos);
                        break;
                    case 1000: // Stock watch update
                    case 1001:
                    case 1003:
                        ProcessMarketDataUpdate(response, length, stxPos);
                        break;
                }
            }
        }

        private void ProcessMarketDataUpdate(byte[] response, int length, int stxPos)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"DEBUG: Processing MarketDataUpdate. Raw response length: {length}");
                int currentPosition = stxPos + HeaderLength; // Start of data section

                // Loop until we reach the end of the data section (before the footer)
                while (currentPosition < length - FooterLength)
                {
                    // H0 Chaining
                    if (currentPosition >= length) break; // Safety check
                    byte chaining = response[currentPosition++];
                    System.Diagnostics.Debug.WriteLine($"DEBUG: Chaining: {chaining}");

                    // H1 GLID + Stockcode
                    string glidAndSymbol = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG: GLID+Symbol: '{glidAndSymbol}'");
                    if (string.IsNullOrEmpty(glidAndSymbol))
                    {
                        System.Diagnostics.Debug.WriteLine("ERROR: GLID+Symbol is empty, stopping parsing of MarketDataUpdate.");
                        break; // Stop if GLID+Symbol is empty, indicates end or error
                    }

                    var marketData = new MarketData { Glid = glidAndSymbol.Length >= 12 ? glidAndSymbol.Substring(0, 12) : glidAndSymbol };
                    marketData.Symbol = glidAndSymbol.Length > 12 ? glidAndSymbol.Substring(12) : string.Empty;

                    // H2 Filler (7 bajtów) - skip
                    currentPosition += 7;

                    // 0 Bid quantity
                    string bidSizeStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded BidSize: '{bidSizeStr}'");
                    if (long.TryParse(bidSizeStr, out long bidSize)) marketData.BidSize = bidSize;

                    // 1 Bid price
                    string bidPriceStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded BidPrice: '{bidPriceStr}'");
                    if (decimal.TryParse(bidPriceStr, out decimal bidPrice)) marketData.BidPrice = bidPrice;

                    // 2 Ask price
                    string askPriceStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded AskPrice: '{askPriceStr}'");
                    if (decimal.TryParse(askPriceStr, out decimal askPrice)) marketData.AskPrice = askPrice;

                    // 3 Ask quantity
                    string askSizeStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded AskSize: '{askSizeStr}'");
                    if (long.TryParse(askSizeStr, out long askSize)) marketData.AskSize = askSize;

                    // 4 Last traded price
                    string lastPriceStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded LastPrice: '{lastPriceStr}'");
                    if (decimal.TryParse(lastPriceStr, out decimal lastPrice)) marketData.LastPrice = lastPrice;

                    // 5 Last traded quantity
                    string lastSizeStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded LastSize: '{lastSizeStr}'");
                    if (long.TryParse(lastSizeStr, out long lastSize)) marketData.LastSize = lastSize;

                    // 6 Last trade time
                    string lastTradeTimeStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded LastTradeTime: '{lastTradeTimeStr}'");

                    // Field 7 (unnamed in doc, but present)
                    string field7Str = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded Field 7: '{field7Str}'");

                    // Field 8 Percentage variation
                    string percentageVariationStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded PercentageVariation: '{percentageVariationStr}'");

                    // 9 Total quantity exchanged
                    string volumeStr = DecodeField(response, ref currentPosition);
                    System.Diagnostics.Debug.WriteLine($"DEBUG:   Decoded Volume: '{volumeStr}'");
                    if (long.TryParse(volumeStr, out long volume)) marketData.Volume = volume;

                    System.Diagnostics.Debug.WriteLine($"DEBUG: Parsed MarketData: GLID={marketData.Glid}, Symbol={marketData.Symbol}, Bid={marketData.BidPrice}/{marketData.BidSize}, Ask={marketData.AskPrice}/{marketData.AskSize}, Last={marketData.LastPrice}/{marketData.LastSize}, Volume={marketData.Volume}");

                    MarketDataUpdate?.Invoke(marketData);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"ERROR: Failed to process MarketDataUpdate: {ex.Message}");
                // System.Diagnostics.Debug.WriteLine($"RAW RESPONSE: {BitConverter.ToString(response, stxPos).Replace("-", " ")}");
            }
        }

        private void ProcessDictionaryResponse(byte[] response, int length, int stxPos)
        {
            var instruments = new List<Instrument>();
            try
            {
                int position = stxPos + HeaderLength;
                byte chaining = response[position++];
                int numberOfGlid = int.Parse(Encoding.ASCII.GetString(response, position, 5));
                position += 5;

                for (int i = 0; i < numberOfGlid; i++)
                {
                    var instrument = new Instrument();
                    string glidAndSymbol = DecodeField(response, ref position);
                    if (!string.IsNullOrEmpty(glidAndSymbol))
                    {
                        if (glidAndSymbol.Length >= 12)
                        {
                            instrument.Glid = glidAndSymbol.Substring(0, 12);
                            instrument.Symbol = glidAndSymbol.Substring(12);
                        }
                        else
                        {
                            instrument.Glid = glidAndSymbol;
                            instrument.Symbol = glidAndSymbol;
                        }

                        // NOWA WALIDACJA GLID
                        if (!string.IsNullOrWhiteSpace(instrument.Glid) && instrument.Glid.All(char.IsDigit))
                        {
                            instrument.Name = DecodeField(response, ref position);
                            DecodeField(response, ref position); // Skip local code
                            instrument.ISIN = DecodeField(response, ref position);
                            DecodeField(response, ref position); // Skip group number

                            if (!string.IsNullOrEmpty(instrument.Symbol))
                            {
                                instruments.Add(instrument);
                            }
                        }
                        else
                        {
                            // Skip remaining fields for this invalid instrument
                            DecodeField(response, ref position); // Name
                            DecodeField(response, ref position); // Local code
                            DecodeField(response, ref position); // ISIN
                            DecodeField(response, ref position); // Group number
                        }
                    }
                }

                if (instruments.Any())
                {
                    InstrumentsReceived?.Invoke(instruments);
                }
            }
            catch { /* Silently fail on parsing error */ }
        }

        #region Message Builders
        private byte[] BuildStockWatchRequest(string glid, int requestNumber)
        {
            var dataPayload = EncodeField(glid);
            return BuildMessage(dataPayload, requestNumber);
        }

        private byte[] BuildDictionaryRequest(string glid)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("00001"));
            dataBuilder.AddRange(EncodeField(glid));
            var dataPayload = dataBuilder.ToArray();
            return BuildMessage(dataPayload, 5108); 
        }

        private byte[] BuildLoginRequest(string user, string password)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(user.PadLeft(3, '0')));
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(password.PadRight(16, ' ')));
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7)));
            dataBuilder.AddRange(EncodeField("15"));
            dataBuilder.AddRange(EncodeField("V5"));
            dataBuilder.AddRange(EncodeField("26"));
            dataBuilder.AddRange(EncodeField(user));
            var dataPayload = dataBuilder.ToArray();
            return BuildMessage(dataPayload, 1100);
        }

        private byte[] BuildMessage(byte[] dataPayload, int requestNumber)
        {
            int dataLength = dataPayload.Length;
            int totalLength = 2 + HeaderLength + dataLength + FooterLength;
            var message = new byte[totalLength];

            using (var ms = new MemoryStream(message))
            using (var writer = new BinaryWriter(ms))
            {
                writer.Write((byte)(totalLength % 256));
                writer.Write((byte)(totalLength / 256));
                writer.Write(Stx);
                writer.Write((byte)'0');
                writer.Write(Encoding.ASCII.GetBytes((HeaderLength + dataLength + FooterLength).ToString().PadLeft(5, '0')));
                writer.Write(Encoding.ASCII.GetBytes(_subnode.PadLeft(5, '0'))); // Use stored subnode
                writer.Write(Encoding.ASCII.GetBytes(new string(' ', 5)));
                writer.Write(Encoding.ASCII.GetBytes("00000"));
                writer.Write(Encoding.ASCII.GetBytes(new string(' ', 2)));
                writer.Write(Encoding.ASCII.GetBytes(requestNumber.ToString().PadLeft(5, '0')));
                writer.Write(Encoding.ASCII.GetBytes(new string(' ', 3)));
                writer.Write(dataPayload);
                writer.Write(Encoding.ASCII.GetBytes(new string(' ', 2)));
                writer.Write(Etx);
            }
            return message;
        }

        private byte[] EncodeField(string value)
        {
            var valueBytes = Encoding.ASCII.GetBytes(value);
            var encoded = new byte[valueBytes.Length + 1];
            encoded[0] = (byte)(valueBytes.Length + 32);
            Array.Copy(valueBytes, 0, encoded, 1, valueBytes.Length);
            return encoded;
        }

        private string DecodeField(byte[] data, ref int position)
        {
            try
            {
                if (position >= data.Length) return string.Empty;
                var fieldLength = data[position] - 32;
                if (fieldLength <= 0 || position + 1 + fieldLength > data.Length) return string.Empty;
                var value = Encoding.ASCII.GetString(data, position + 1, fieldLength);
                position += 1 + fieldLength;
                return value;
            }
            catch
            {
                return string.Empty;
            }
        }

        private bool VerifyLoginResponse(byte[] response, int length)
        {
            if (length < HeaderLength + 2) return false;
            string requestNumberStr = Encoding.ASCII.GetString(response, 26, 5);
            return requestNumberStr == "01100";
        }
        #endregion
    }
}