using System.Collections.Concurrent;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Replication.Grpc;

public class PaymentReplicationService : PaymentReplication.PaymentReplicationBase
{
    private ConcurrentBag<PaymentInsertRpcParameters> ReplicatedPayments { get; } = [];


    public override Task<Empty> PublishPaymentsBatch(PaymentBatch request, ServerCallContext context)
    {
        foreach (var payment in request.Payments)
        {
            HandleLocally(payment);
        }

        return Task.FromResult(new Empty());
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
