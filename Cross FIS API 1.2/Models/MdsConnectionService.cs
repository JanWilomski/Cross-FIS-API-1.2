using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Cross_FIS_API_1._2.Models
{
    public class MdsConnectionService
    {
        private TcpClient? _tcpClient;
        private NetworkStream? _stream;
        private string _node = string.Empty;
        private string _subnode = string.Empty;

        private const byte Stx = 2;
        private const byte Etx = 3;
        private const int HeaderLength = 32;
        private const int FooterLength = 3;

        public bool IsConnected => _tcpClient?.Connected ?? false;
        public event Action<List<Instrument>>? InstrumentsReceived;
        public event Action<InstrumentDetails>? InstrumentDetailsReceived;

        public async Task<bool> ConnectAndLoginAsync(string ipAddress, int port, string user, string password, string node, string subnode)
        {
            if (IsConnected) Disconnect();

            try
            {
                _tcpClient = new TcpClient();
                await _tcpClient.ConnectAsync(ipAddress, port);
                if (!_tcpClient.Connected) return false;

                _stream = _tcpClient.GetStream();

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
                Debug.WriteLine($"MDS Connection failed: {ex.Message}");
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
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error in MDS ListenForMessages: {ex.Message}");
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
                    await Task.Delay(50);
                }
            }
        }

        public async Task RequestInstrumentDetails(string glidAndSymbol)
        {
            if (!IsConnected || _stream == null) return;
            byte[] request = BuildStockWatchRequest(glidAndSymbol);
            await _stream.WriteAsync(request, 0, request.Length);
        }

        private void ProcessIncomingMessage(byte[] response, int length)
        {
            int currentPos = 0;
            while(currentPos < length)
            {
                var stxPos = Array.IndexOf(response, Stx, currentPos);
                if (stxPos == -1) break;

                string requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 24, 5);
                if (int.TryParse(requestNumberStr, out int requestNumber))
                {
                    switch(requestNumber)
                    {
                        case 5108: // Dictionary response
                            ProcessDictionaryResponse(response, length, stxPos);
                            break;
                        case 1000: 
                            ProcessInstrumentDetailsResponse(response, length, stxPos);
                            break;
                        case 1001:
                        case 1003:
                            ProcessInstrumentDetailsResponse(response, length, stxPos);
                            break;
                    }
                }
                int messageLength = response[stxPos - 2] + 256 * response[stxPos - 1];
                currentPos = stxPos + messageLength;
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
                    string glidAndSymbol = DecodeField(response, ref position);
                    string name = DecodeField(response, ref position);
                    DecodeField(response, ref position); // Skip local code
                    string isin = DecodeField(response, ref position);
                    DecodeField(response, ref position); // Skip group number

                    if (!string.IsNullOrEmpty(glidAndSymbol) && glidAndSymbol.Length >= 12)
                    {
                        var instrument = new Instrument
                        {
                            Glid = glidAndSymbol.Substring(0, 12),
                            Symbol = glidAndSymbol.Length > 12 ? glidAndSymbol.Substring(12) : "",
                            Name = name,
                            ISIN = isin
                        };
                        if (!string.IsNullOrEmpty(instrument.Symbol)) instruments.Add(instrument);
                    }
                }
                if (instruments.Any()) InstrumentsReceived?.Invoke(instruments);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ProcessDictionaryResponse: {ex.Message}");
            }
        }

        private bool IsFieldSet(int fieldNumber, byte[] bitmap)
        {
            int byteIndex = fieldNumber / 8;
            int bitIndex = fieldNumber % 8;
            if (byteIndex >= bitmap.Length) return false;
            return (bitmap[byteIndex] & (1 << bitIndex)) != 0;
        }

        private void ProcessInstrumentDetailsResponse(byte[] response, int length, int stxPos)
        {
            try
            {
                int pos = stxPos + HeaderLength;
                var details = new InstrumentDetails();

                byte chaining = response[pos++]; // H0
                details.GlidAndSymbol = DecodeField(response, ref pos); // H1
                if (string.IsNullOrEmpty(details.GlidAndSymbol)) return;

                pos += 7; // H2: Skip filler - This filler IS likely present in MR/1000 reply

                // Read the bitmap to determine which fields are present.
                // Assuming a 32-byte bitmap for up to 256 fields, which is a common standard.
                int bitmapLength = 32;
                byte[] bitmap = new byte[bitmapLength];
                if (pos + bitmapLength > length) return; // Not enough data for a bitmap
                Array.Copy(response, pos, bitmap, 0, bitmapLength);
                pos += bitmapLength;

                // Iterate through all possible fields and decode them if their bit is set.
                // This time, use fixed-length ASCII for ALL data fields.
                for (int fieldNumber = 0; fieldNumber <= 224; fieldNumber++)
                {
                    if (IsFieldSet(fieldNumber, bitmap))
                    {
                        int fixedLength = 0;
                        switch (fieldNumber)
                        {
                            case 0: // Bid quantity
                            case 1: // Bid price
                            case 2: // Ask price
                            case 3: // Ask quantity
                            case 4: // Last price
                            case 9: // Volume
                            case 10: // Opening price
                            case 11: // High price
                            case 12: // Low price
                            case 16: // Closing price
                                fixedLength = 16; 
                                break;
                            case 5: // Last size
                            case 6: // Last trade time
                            case 8: // Percentage variation
                                fixedLength = 5; 
                                break;
                            case 13: // Suspension indicator
                            case 14: // Variation sign
                                fixedLength = 1; 
                                break;
                            case 88: // ISIN Code
                                fixedLength = 12; 
                                break;
                            case 140: // Trading phase
                                fixedLength = 4; 
                                break;
                            default:
                                // For unknown fields, we still need to advance 'pos'.
                                // This is a guess, but if the field is present, it must have some length.
                                // This is where a full spec is needed.
                                fixedLength = 10; // Arbitrary default fixed length for unknown fields.
                                Debug.WriteLine($"DEBUG: Field {fieldNumber}: Unknown fixed length. Assuming {fixedLength}.");
                                break;
                        }

                        if (pos + fixedLength > response.Length)
                        {
                            Debug.WriteLine($"DEBUG: Attempting to read field {fieldNumber} but fixed length {fixedLength} goes out of bounds.");
                            break; // Exit loop if out of bounds
                        }

                        string fieldValue = Encoding.ASCII.GetString(response, pos, fixedLength).Trim();
                        pos += fixedLength;
                        Debug.WriteLine($"DEBUG: Field {fieldNumber}: Value='{fieldValue}' (Fixed ASCII {fixedLength})");

                        switch (fieldNumber)
                        {
                            case 0: details.BidSize = ParseLong(fieldValue); break;
                            case 1: details.BidPrice = ParseDecimal(fieldValue); break;
                            case 2: details.AskPrice = ParseDecimal(fieldValue); break;
                            case 3: details.AskSize = ParseLong(fieldValue); break;
                            case 4: details.LastPrice = ParseDecimal(fieldValue); break;
                            case 5: details.LastSize = ParseLong(fieldValue); break;
                            case 6: details.LastTradeTime = fieldValue; break;
                            case 8: details.PercentageVariation = ParseDecimal(fieldValue); break;
                            case 9: details.Volume = ParseLong(fieldValue); break;
                            case 10: details.OpeningPrice = ParseDecimal(fieldValue); break;
                            case 11: details.HighPrice = ParseDecimal(fieldValue); break;
                            case 12: details.LowPrice = ParseDecimal(fieldValue); break;
                            case 13: details.SuspensionIndicator = fieldValue; break;
                            case 14: details.VariationSign = fieldValue; break;
                            case 16: details.ClosingPrice = ParseDecimal(fieldValue); break;
                            case 88: details.ISIN = fieldValue; break;
                            case 140: details.TradingPhase = fieldValue; break;
                            default: break; // Value already read and discarded.
                        }
                    }
                }
                InstrumentDetailsReceived?.Invoke(details);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to parse instrument details: {ex.Message}");
            }
        }

        #region Message Builders
        private byte[] BuildStockWatchRequest(string glidAndStockcode)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 7))); // H0 Filler
            dataBuilder.AddRange(EncodeField(glidAndStockcode)); // H1 GLID + Stockcode
            return BuildMessage(dataBuilder.ToArray(), 1000); // 1000 for snapshot
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
                writer.Write(Encoding.ASCII.GetBytes(_subnode.PadLeft(5, '0')));
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
            catch (Exception ex)
            {
                Debug.WriteLine($"Error decoding field at position {position}: {ex.Message}");
                return string.Empty;
            }
        }

        private bool VerifyLoginResponse(byte[] response, int length)
        {
            if (length < HeaderLength + 2) return false;
            string requestNumberStr = Encoding.ASCII.GetString(response, 26, 5);
            return requestNumberStr == "01100";
        }

        private long ParseLong(string value) => long.TryParse(value, out var result) ? result : 0;
        private decimal ParseDecimal(string value) => decimal.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out var result) ? result : 0;

        #endregion
    }
}