using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CAHFS_Recharges.Models
{
    [Table("C_AE_Feed_Items")]   // optional, but matches your table name
    public class FeedItem
    {
        [Key]
        [Column("recordID")]
        public Guid RecordID { get; set; }

        [Column("orginalDocNumber")]
        public string OrignalDocNumber { get; set; } = null!;

        [Column("receivablesChartString")]
        public string ReceivablesChartString { get; set; } = null!;

        [Column("incomeChartString")]
        public string IncomeChartString { get; set; } = null!;

        [Column("transactionDate")]
        public DateTime? TransactionDate { get; set; }

        [Column("quantity")]
        public int? Quantity { get; set; }

        [Column("unitPrice")]
        public decimal? UnitPrice { get; set; }

        [Column("totalCharge")]
        public decimal TotalCharge { get; set; }

        [Column("batchID")]
        public Guid BatchID { get; set; }

        [Column("clientID")]
        public string ClientID { get; set; } = null!;

        [Column("systemID")]
        public int? SystemID { get; set; }

        [Column("incomeStringValid")]
        public string? IncomeStringValid { get; set; }

        [Column("receivableStringValid")]
        public string? ReceivableStringValid { get; set; }

        [Column("description")]
        public string? Description { get; set; }
    }
}
