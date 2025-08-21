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
        public event Action<string> MessageReceived;

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
                            var message = Encoding.ASCII.GetString(buffer, 0, bytesRead);
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageReceived?.Invoke(message);
                            });
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

        private static int _internalReferenceCounter = 0;

        private byte[] BuildOrderRequest(Order order, string user)
        {
            Debug.WriteLine($"Order Instrument Symbol in BuildOrderRequest: {order.Instrument?.Symbol ?? "NULL"}");
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
            orderData.Append(EncodeGlField("0", order.Side == 'B' ? "1" : "2"));

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
