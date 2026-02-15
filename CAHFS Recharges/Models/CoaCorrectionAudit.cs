using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CAHFS_Recharges.Models
{
    /// Tracks audit history for COA corrections made through the COA Validations page.
    [Table("C_AE_COA_Correction_Audit")]
    public class CoaCorrectionAudit
    {
        [Key]
        [Column("AuditID")]
        public Guid AuditId { get; set; }

        [Column("RecordID")]
        public Guid RecordId { get; set; }

        [Column("BatchID")]
        public Guid BatchId { get; set; }

        [Column("CorrectionType")]
        [MaxLength(10)]
        public string CorrectionType { get; set; } = null!;

        [Column("OldDebitCoa")]
        [MaxLength(500)]
        public string? OldDebitCoa { get; set; }

        [Column("NewDebitCoa")]
        [MaxLength(500)]
        public string? NewDebitCoa { get; set; }

        [Column("OldCreditCoa")]
        [MaxLength(500)]
        public string? OldCreditCoa { get; set; }

        [Column("NewCreditCoa")]
        [MaxLength(500)]
        public string? NewCreditCoa { get; set; }

        [Column("CorrectedBy")]
        [MaxLength(100)]
        public string CorrectedBy { get; set; } = null!;

        [Column("CorrectedAt")]
        public DateTime CorrectedAt { get; set; }

        [Column("CorrectionReason")]
        [MaxLength(500)]
        public string? CorrectionReason { get; set; }
        
        [Column("IntegrationType")]
        [MaxLength(10)]
        public string IntegrationType { get; set; } = null!;
    }
}
