using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Cross_FIS_API_1._2.Models
{
    public enum OrderSide { Buy = 0, Sell = 1 }

    public class OrderParameters
    {
        public string Glid { get; set; } = string.Empty;
        public long Quantity { get; set; }
        public decimal Price { get; set; }
        public OrderSide Side { get; set; }
        // Add other parameters like OrderType, Validity, etc. as needed
    }

    public class FisConnectionService
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
                        byte[] subscriptionRequest = BuildRealTimeRepliesRequest();
                        await _stream.WriteAsync(subscriptionRequest, 0, subscriptionRequest.Length);
                        
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
                        // TODO: Process order confirmations/rejections (2019)
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

        public async Task SendNewOrderAsync(OrderParameters order)
        {
            if (!IsConnected || _stream == null) return;

            byte[] orderRequest = BuildNewOrderRequest(order);
            await _stream.WriteAsync(orderRequest, 0, orderRequest.Length);
        }

        #region Message Builders

        private byte[] BuildNewOrderRequest(OrderParameters order)
        {
            var dataBuilder = new List<byte>();
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("O")); // C: Request Category (Order)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes("0")); // D1: Command (New)
            dataBuilder.AddRange(EncodeField(order.Glid)); // G: Stock Code (GLID)
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 10))); // Filler

            // Simplified data part without bitmap
            dataBuilder.AddRange(EncodeField(((int)order.Side).ToString())); // 0: Side
            dataBuilder.AddRange(EncodeField(order.Quantity.ToString())); // 1: Quantity
            dataBuilder.AddRange(EncodeField("L")); // 2: Modality (Limit)
            dataBuilder.AddRange(EncodeField(order.Price.ToString("F4"))); // 3: Price (assuming 4 decimal places)
            dataBuilder.AddRange(EncodeField("D")); // 4: Validity (Day)

            var dataPayload = dataBuilder.ToArray();
            return BuildMessage(dataPayload, 2000);
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

        private byte[] BuildRealTimeRepliesRequest()
        {
            var dataBuilder = new List<byte>();
            dataBuilder.Add((byte)'1');
            dataBuilder.Add((byte)'1');
            dataBuilder.Add((byte)'1');
            dataBuilder.Add((byte)'0');
            dataBuilder.Add((byte)'0');
            dataBuilder.Add((byte)'0');
            dataBuilder.Add((byte)'0');
            dataBuilder.AddRange(Encoding.ASCII.GetBytes(new string(' ', 11)));
            var dataPayload = dataBuilder.ToArray();
            return BuildMessage(dataPayload, 2017);
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

        private bool VerifyLoginResponse(byte[] response, int length)
        {
            if (length < HeaderLength + 2) return false;
            string requestNumberStr = Encoding.ASCII.GetString(response, 26, 5);
            return requestNumberStr == "01100";
        }
        #endregion
    }
}
