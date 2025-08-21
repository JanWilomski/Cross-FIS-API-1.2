
namespace Cross_FIS_API_1._2.Models
{
    public class Order
    {
        public Instrument Instrument { get; set; }
        public char Side { get; set; } // 'B' for Buy, 'S' for Sell
        public int Quantity { get; set; }
        public decimal Price { get; set; }
        public char Type { get; set; } // 'L' for Limit, 'M' for Market
        public string Validity { get; set; }
        public string ClientCodeType { get; set; }
        public string InternalReference { get; set; }
    }
}
