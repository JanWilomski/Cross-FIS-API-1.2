using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Cross_FIS_API_1._2.Models;

namespace Cross_FIS_API_1._2.Models
{
    public class FisConnectionService
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
        public event Action<OrderUpdate>? OrderStatusUpdated;
        public event Action<string> RawMessageReceived; // For debugging/unhandled messages

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
                        // Request real-time messages (2017) after successful login
                        byte[] subscribeRequest = BuildSubscribeRequest();
                        await _stream.WriteAsync(subscribeRequest, 0, subscribeRequest.Length);
                        Debug.WriteLine("Sent 2017 (Subscribe) request.");

                        _ = Task.Run(ListenForMessages);
                        return true;
                    }
                }
                Disconnect();
                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"SLE Connection failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Disconnect();
                return false;
            }
        }

        public async Task PlaceOrder(Order order, string user)
        {
            if (!IsConnected || _stream == null) return;
            Debug.WriteLine($"Placing order: {order.Instrument.Symbol}, {order.Quantity}@{order.Price}, Side: {order.Side}, Validity: {order.Validity}, ClientType: {order.ClientCodeType}");
            byte[] orderRequest = BuildOrderRequest(order, user);
            Debug.WriteLine($"Order request: {Encoding.ASCII.GetString(orderRequest)}");
            await _stream.WriteAsync(orderRequest, 0, orderRequest.Length);
        }

        private async Task ListenForMessages()
        {
            if (_stream == null) return;
            var buffer = new byte[4096];
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
                    Debug.WriteLine($"Error in SLE ListenForMessages: {ex.Message}");
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

        private void ProcessIncomingMessage(byte[] response, int length)
        {
            int currentPos = 0;
            while (currentPos < length)
            {
                var stxPos = Array.IndexOf(response, Stx, currentPos);
                if (stxPos == -1) break;

                // Ensure there's enough data for header and length bytes
                if (stxPos + HeaderLength + FooterLength > length) break;

                // Extract total message length from bytes before STX
                int totalMessageLength = response[stxPos - 2] + 256 * response[stxPos - 1];

                // Check if the full message is within the buffer
                if (stxPos + totalMessageLength > length)
                {
                    // Not enough data for the full message, break and wait for more
                    break;
                }

                string requestNumberStr = Encoding.ASCII.GetString(response, stxPos + 26, 5); // Request number is at offset 26 in header
                if (int.TryParse(requestNumberStr, out int requestNumber))
                {
                    switch (requestNumber)
                    {
                        case 2019: // Real-time order message
                            ProcessOrderMessage(response, stxPos);
                            break;
                        case 2008: // Reply consultation (e.g., for order book)
                            // ProcessReplyConsultationMessage(response, stxPos); // Implement if needed
                            Debug.WriteLine($"Received 2008 message: {Encoding.ASCII.GetString(response, stxPos, totalMessageLength)}");
                            break;
                        case 1100: // Login response (already handled, but might receive unsolicited)
                            Debug.WriteLine($"Received 1100 message: {Encoding.ASCII.GetString(response, stxPos, totalMessageLength)}");
                            break;
                        default:
                            Debug.WriteLine($"Received unhandled message type {requestNumber}: {Encoding.ASCII.GetString(response, stxPos, totalMessageLength)}");
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                RawMessageReceived?.Invoke(Encoding.ASCII.GetString(response, stxPos, totalMessageLength));
                            });
                            break;
                    }
                }
                currentPos = stxPos + totalMessageLength; // Move to the next message
            }
        }

        private void ProcessOrderMessage(byte[] response, int stxPos)
        {
            try
            {
                int pos = stxPos + HeaderLength;
                // A Chaining (1 byte)
                pos++;
                // B User number (5 bytes)
                string userNumber = Encoding.ASCII.GetString(response, pos, 5);
                pos += 5;
                // C Request category (1 byte)
                pos++;
                // D Reply type (1 byte)
                pos++;
                // E Index (6 bytes)
                pos += 6;
                // F Number of replies (5 bytes)
                pos += 5;
                // G Stockcode (GL format)
                string stockCode = DecodeGlField(response, ref pos);
                // Filler (10 bytes)
                pos += 10;

                // Data for order (Bitmap fields)
                var orderUpdate = new OrderUpdate
                {
                    StockCode = stockCode,
                    UserNumber = userNumber
                };

                while (pos < response.Length && response[pos] != Etx)
                {
                    string tag = DecodeGlFieldTag(response, ref pos);
                    string value = DecodeGlFieldValue(response, ref pos);

                    switch (tag)
                    {
                        case "12": orderUpdate.InternalReference = value; break; // Internal reference
                        case "51": orderUpdate.ExchangeOrderNumber = value; break; // Exchange order number
                        case "52": orderUpdate.OrderStatus = value; break; // Order status
                        case "53": orderUpdate.CumulatedQuantity = long.TryParse(value, out var cq) ? cq : 0; break; // Cumulated quantity
                        case "54": orderUpdate.RemainingQuantity = long.TryParse(value, out var rq) ? rq : 0; break; // Remaining quantity
                        case "55": orderUpdate.AveragePrice = decimal.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ap) ? ap : 0; break; // Average price
                        case "1155": orderUpdate.RejectReason = value; break; // Reject reason
                        case "1072": orderUpdate.CoreTradeTimestamp = value; break; // Core Trade Timestamp
                        case "1073": orderUpdate.CoreAcknowledgeTimestamp = value; break; // Core Acknowledge Timestamp
                        case "1080": orderUpdate.RejectTimestamp = value; break; // Reject Timestamp
                        case "1085": orderUpdate.OrderServerCreationDate = value; break; // Order server creation date
                        case "1200": orderUpdate.CumulReverseTradeQuantity = long.TryParse(value, out var crtq) ? crtq : 0; break; // Cumul Reverse Trade Quantity
                        case "1267": orderUpdate.APClientReferenceID = value; break; // AP Client Reference ID
                        case "1358": orderUpdate.UserID = value; break; // User ID
                        case "1470": orderUpdate.ClientIdentificationCode = value; break; // Client Identification Code
                        case "1482": orderUpdate.ExecutionDecisionMakerID = value; break; // Execution Decision Maker ID
                        case "1483": orderUpdate.ExchangeInvestmentDecisionMakerID = value; break; // Exchange Investment Decision Maker ID
                        case "1488": orderUpdate.ExecutionDecisionMakerType = value; break; // Execution Decision Maker Type
                        case "1489": orderUpdate.InvestmentDecisionMakerType = value; break; // Investment Decision Maker Type
                        case "1532": orderUpdate.ConfirmationForValue = value; break; // Confirmation for value
                        case "1533": orderUpdate.ConfirmationForVolume = value; break; // Confirmation for volume
                        case "1534": orderUpdate.ConfirmationForCollar = value; break; // Confirmation for Collar
                    }
                }

                Application.Current.Dispatcher.Invoke(() =>
                {
                    OrderStatusUpdated?.Invoke(orderUpdate);
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error processing 2019 message: {ex.Message}");
            }
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

        private string DecodeGlField(byte[] data, ref int position)
        {
            try
            {
                if (position >= data.Length) return string.Empty;
                var tagLength = data[position] - 32;
                if (tagLength <= 0 || position + 1 + tagLength > data.Length) return string.Empty;
                position += 1 + tagLength; // Skip tag

                if (position >= data.Length) return string.Empty;
                var valueLength = data[position] - 32;
                if (valueLength <= 0 || position + 1 + valueLength > data.Length) return string.Empty;
                var value = Encoding.ASCII.GetString(data, position + 1, valueLength);
                position += 1 + valueLength;
                return value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error decoding GL field at position {position}: {ex.Message}");
                return string.Empty;
            }
        }

        private string DecodeGlFieldTag(byte[] data, ref int position)
        {
            try
            {
                if (position >= data.Length) return string.Empty;
                var tagLength = data[position] - 32;
                if (tagLength <= 0 || position + 1 + tagLength > data.Length) return string.Empty;
                var tag = Encoding.ASCII.GetString(data, position + 1, tagLength);
                position += 1 + tagLength;
                return tag;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error decoding GL field tag at position {position}: {ex.Message}");
                return string.Empty;
            }
        }

        private string DecodeGlFieldValue(byte[] data, ref int position)
        {
            try
            {
                if (position >= data.Length) return string.Empty;
                var valueLength = data[position] - 32;
                if (valueLength <= 0 || position + 1 + valueLength > data.Length) return string.Empty;
                var value = Encoding.ASCII.GetString(data, position + 1, valueLength);
                position += 1 + valueLength;
                return value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error decoding GL field value at position {position}: {ex.Message}");
                return string.Empty;
            }
        }
        
        #region Message Builders
        
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

        private byte[] BuildSubscribeRequest()
        {
            var dataBuilder = new List<byte>();
            // E1-E7: All set to '1' to subscribe to all reply types
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("1111111"));
            // Filler (11 bytes)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 11)));
            return BuildMessage(dataBuilder.ToArray(), 2017); // Request 2017 for real-time subscription
        }

        private static int _internalReferenceCounter = 0;

        private byte[] BuildOrderRequest(Order order, string user)
        {
            Debug.WriteLine($"Order Instrument Symbol in BuildOrderRequest: {order.Instrument?.Symbol ?? "NULL"}");
            // Add null check for order.Instrument
            if (order.Instrument == null)
            {
                throw new ArgumentNullException(nameof(order.Instrument), "Instrument cannot be null when building an order request.");
            }

            // *** NEW DEBUGGING FOR 'user' ***
            if (user == null)
            {
                Debug.WriteLine("CRITICAL ERROR: 'user' parameter is NULL in BuildOrderRequest!");
                throw new ArgumentNullException(nameof(user), "User parameter cannot be null.");
            }
            Debug.WriteLine($"User parameter in BuildOrderRequest: '{user}' (Długość: {user.Length})");
            // *** END NEW DEBUGGING ***

            var dataBuilder = new List<byte>();
            // B - User Number
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(user.PadLeft(5, '0')));
            // C - Request Category
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("O")); // O for Order
            // D1 - Command
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("A")); // A for Add
            // G - Stock code
            dataBuilder.AddRange(EncodeField(order.Instrument.Symbol));
            // Filler
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 10)));

            // Data for order (Bitmap fields)
            var orderData = new StringBuilder();

            // #0 Side (1 for Buy, 2 for Sell)
            // Debugging: Check order.Side value
            Debug.WriteLine($"Order.Side value: {order.Side}");
            string sideValue = (order.Side == 'B' ? "1" : "2");
            Debug.WriteLine($"Side value for EncodeGlField: {sideValue}");

            // Debugging: Check if orderData is null (should not be)
            if (orderData == null)
            {
                Debug.WriteLine("ERROR: orderData is null!");
                // This should not happen, but adding for extreme debugging
                throw new InvalidOperationException("orderData StringBuilder is null.");
            }

            // Debugging: Check if EncodeGlField returns null (should not)
            string encodedSideField = EncodeGlField("0", sideValue);
            Debug.WriteLine($"Encoded Side Field: {encodedSideField}");
            if (encodedSideField == null)
            {
                Debug.WriteLine("ERROR: EncodeGlField returned null!");
                throw new InvalidOperationException("EncodeGlField returned null.");
            }

            orderData.Append(encodedSideField);

            // #1 Quantity
            orderData.Append(EncodeGlField("1", order.Quantity.ToString()));

            // #2 Modality (L for Limit)
            orderData.Append(EncodeGlField("2", "L"));

            // #3 Price
            if (order.Type == 'L')
            {
                orderData.Append(EncodeGlField("3", order.Price.ToString("F2").Replace(",", ".")));
            }

            // #4 Validity
            string validityCode = "0"; // Default to Day
            switch (order.Validity)
            {
                case "GTC": validityCode = "1"; break;
                case "IOC": validityCode = "4"; break;
                case "FOK": validityCode = "5"; break;
            }
            orderData.Append(EncodeGlField("4", validityCode));

            // #12 Internal reference
            var internalRef = System.Threading.Interlocked.Increment(ref _internalReferenceCounter);
            order.InternalReference = internalRef.ToString();
            orderData.Append(EncodeGlField("12", order.InternalReference));

            // #17 Client Code Type
            string clientCodeTypeCode = order.ClientCodeType == "Principal" ? "P" : "C";
            orderData.Append(EncodeGlField("17", clientCodeTypeCode));

            // #106 GLID
            orderData.Append(EncodeGlField("106", order.Instrument.Glid));

            dataBuilder.AddRange(Encoding.ASCII.GetBytes(orderData.ToString()));

            return BuildMessage(dataBuilder.ToArray(), 2000);
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
                writer.Write((byte)' '); // API Version for SLE V4 is space
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

        private string EncodeGlField(string tag, string value)
        {
            return $"{(char)(tag.Length + 32)}{tag}{(char)(value.Length + 32)}{value}";
        }

        private byte[] EncodeField(string value)
        {
            var valueBytes = Encoding.ASCII.GetBytes(value);
            var encoded = new byte[valueBytes.Length + 1];
            encoded[0] = (byte)(valueBytes.Length + 32);
            Array.Copy(valueBytes, 0, encoded, 1, valueBytes.Length);
            return encoded;
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
