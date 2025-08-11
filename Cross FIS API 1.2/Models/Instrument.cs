namespace Cross_FIS_API_1._2.Models
{
    public class Instrument
    {
        public string Glid { get; set; } = string.Empty;
        public string Symbol { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string ISIN { get; set; } = string.Empty;

        public string GlidAndSymbol => $"{Glid}{Symbol}";
    }
}