using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CAHFS_Recharges.Models
{
    [Table("C_AE_Feed_Items")]
    public class FeedItem
    {
        [Key]
        [Column("recordID")]
        public Guid RecordID { get; set; }

        [Column("orginalDocNumber")]
        public string OrignalDocNumber { get; set; } = null!;

        [Column("DebitChartString")]
        public string? DebitChartString { get; set; }

        [Column("CreditChartString")]
        public string? CreditChartString { get; set; }

        [Column("transactionDate")]
        public DateTime TransactionDate { get; set; }

        [Column("TestCode")]
        public string? TestCode { get; set; }

        [Column("TestName")]
        public string? TestName { get; set; }

        [Column("quantity")]
        public int? Quantity { get; set; }

        [Column("unitPrice")]
        public decimal? UnitPrice { get; set; }

        [Column("totalCharge")]
        public decimal TotalCharge { get; set; }

        [Column("batchID")]
        public Guid BatchID { get; set; }

        [Column("clientID")]
        public string? ClientID { get; set; }

        [Column("Client_Name")]
        public string? ClientName { get; set; }

        [Column("Organization_Name")]
        public string? OrganizationName { get; set; }

        [Column("systemID")]
        public int SystemID { get; set; }

        [Column("DebitStringValid")]
        public string? DebitStringValid { get; set; }

        [Column("CreditStringValid")]
        public string? CreditStringValid { get; set; }
        [Column("UnprocessedCOAString")]
        public string? UnprocessedCOAString { get; set; }

        [Column("DebitValidationError")]
        public string? DebitValidationError { get; set; }
        [Column("CreditValidationError")]
        public string? CreditValidationError { get; set; }


    }
}
