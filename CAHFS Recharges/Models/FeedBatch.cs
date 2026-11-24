using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CAHFS_Recharges.Models
{
    [Table("C_AE_Feed_Batch")]
    public class FeedBatch
    {
        [Key]
        [Column("batchID")]
        public Guid BatchID { get; set; }

        public DateTime? DateSent { get; set; }
        public string? PickupSentFlag { get; set; }
        public string? AEConsumerReferenceID { get; set; }
        public string? AEConsumnerNotes { get; set; }
        public string? AERequestStatus { get; set; }
        public string? ErrorDetail { get; set; }
        public Guid? AEConsumerRequestID { get; set; }
        public DateTime? AETransactionDate { get; set; }
        public string? AEJournalName { get; set; }
        public string? AEJournalDescription { get; set; }
        public string? AEJournalReference { get; set; }
        public decimal? BatchTotal { get; set; }

        /// <summary>Month-slice start (1–7, 8–14, …) used by daily feed load find-or-create.</summary>
        [Column("WeekStartDate")]
        public DateTime? WeekStartDate { get; set; }

        /// <summary>Month-slice end (inclusive).</summary>
        [Column("WeekEndDate")]
        public DateTime? WeekEndDate { get; set; }
    }
}
