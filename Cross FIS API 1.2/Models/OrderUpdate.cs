namespace Cross_FIS_API_1._2.Models
{
    public class OrderUpdate
    {
        public string StockCode { get; set; } = string.Empty;
        public string UserNumber { get; set; } = string.Empty;
        public string InternalReference { get; set; } = string.Empty;
        public string ExchangeOrderNumber { get; set; } = string.Empty;
        public string OrderStatus { get; set; } = string.Empty;
        public long CumulatedQuantity { get; set; }
        public long RemainingQuantity { get; set; }
        public decimal AveragePrice { get; set; }
        public string RejectReason { get; set; } = string.Empty;
        public string CoreTradeTimestamp { get; set; } = string.Empty;
        public string CoreAcknowledgeTimestamp { get; set; } = string.Empty;
        public string RejectTimestamp { get; set; } = string.Empty;
        public string OrderServerCreationDate { get; set; } = string.Empty;
        public long CumulReverseTradeQuantity { get; set; }
        public string APClientReferenceID { get; set; } = string.Empty;
        public string UserID { get; set; } = string.Empty;
        public string ClientIdentificationCode { get; set; } = string.Empty;
        public string ExecutionDecisionMakerID { get; set; } = string.Empty;
        public string ExchangeInvestmentDecisionMakerID { get; set; } = string.Empty;
        public string ExecutionDecisionMakerType { get; set; } = string.Empty;
        public string InvestmentDecisionMakerType { get; set; } = string.Empty;
        public string ConfirmationForValue { get; set; } = string.Empty;
        public string ConfirmationForVolume { get; set; } = string.Empty;
        public string ConfirmationForCollar { get; set; } = string.Empty;
    }
}
