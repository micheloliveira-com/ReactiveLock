using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Replication.Grpc;

public class PaymentReplicationService : PaymentReplication.PaymentReplicationBase
{
    private ConcurrentBag<PaymentInsertRpcParameters> ReplicatedPayments { get; } = [];

    public override async Task<Google.Protobuf.WellKnownTypes.Empty> PublishPayments(
        IAsyncStreamReader<PaymentInsertRpcParameters> requestStream,
        ServerCallContext context)
    {
        await foreach (var payment in requestStream.ReadAllAsync(context.CancellationToken).ConfigureAwait(false))
        {
            HandleLocally(payment);
        }

        return new Google.Protobuf.WellKnownTypes.Empty();
    }

    public override Task<Empty> ClearPayments(
        Empty request,
        ServerCallContext context)
    {
        ClearLocalPayments();
        return Task.FromResult(new Empty());
    }

    public void HandleLocally(PaymentInsertRpcParameters payment)
    {
        ReplicatedPayments.Add(payment);
    }

    public PaymentInsertRpcParameters[] GetReplicatedPaymentsSnapshot()
    {
        return ReplicatedPayments.ToArray();
    }


    public void ClearLocalPayments()
    {
        while (!ReplicatedPayments.IsEmpty)
        {
            ReplicatedPayments.TryTake(out _);
        }
    }
}
