using LockboxCashReceiptPoster.Models;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace LockboxCashReceiptPoster.Abstractions
{
   
    /// ERP destination boundary. Swap GP eConnect for TraceFirst without changing the run loop.
    public interface ICashReceiptPoster
    {
        Task<PostResult> PostAsync(CashReceiptRequest request, CancellationToken cancellationToken = default);
    }

    public interface IPendingReceiptSource
    {
        Task<IReadOnlyList<CashReceiptRequest>> GetPendingAsync(int? maxRows = null, CancellationToken cancellationToken = default);
    }

    public interface IPostStatusStore
    {
        Task MarkPostedAsync(int stagingId, string? documentNumber, CancellationToken cancellationToken = default);
        Task MarkFailedAsync(int stagingId, string errorMessage, CancellationToken cancellationToken = default);
    }

    public interface IFailureNotifier
    {
        Task NotifyAsync(string company, int postedCount, int failedCount, IReadOnlyList<string> errors, CancellationToken cancellationToken = default);
    }
}
