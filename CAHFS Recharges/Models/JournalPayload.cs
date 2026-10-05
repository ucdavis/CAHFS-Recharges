using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CAHFS_Recharges.Models
{
    /// Exact glJournalRequest JSON posted when a batch was sent.
    [Table("C_AE_Journal_Payload")]
    public class JournalPayload
    {
        [Key]
        [Column("PayloadID")]
        public Guid PayloadID { get; set; }

        [Column("batchID")]
        public Guid BatchID { get; set; }

        [Column("SentUtc")]
        public DateTime SentUtc { get; set; }

        [Column("PayloadJson")]
        public string PayloadJson { get; set; } = "";
    }
}
