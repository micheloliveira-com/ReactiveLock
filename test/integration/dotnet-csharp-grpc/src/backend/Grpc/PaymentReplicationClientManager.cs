using Google.Protobuf.WellKnownTypes;
using Grpc.Net.Client;
using Replication.Grpc;

public class PaymentReplicationClientManager
{
    private List<PaymentReplication.PaymentReplicationClient> RemoteClients { get; } = [];

    public PaymentReplicationClientManager(string local, params string[] remoteGrpcUrls)
    {
        foreach (var url in remoteGrpcUrls)
        {
            if (local == url)
            {
                break;
            }
            var channel = GrpcChannel.ForAddress(url);
            RemoteClients.Add(new PaymentReplication.PaymentReplicationClient(channel));
        }
    }

    public async Task PublishPaymentsBatchAsync(IEnumerable<PaymentInsertRpcParameters> payments, PaymentReplicationService paymentReplicationService)
    {
        // Handle locally first
        foreach (var pay in payments)
        {
            paymentReplicationService.HandleLocally(pay);
        }

        // Build the batch
        var batch = new PaymentBatch();
        batch.Payments.AddRange(payments);

        // Send to each remote client sequentially
        foreach (var remoteClient in RemoteClients)
        {
            await remoteClient.PublishPaymentsBatchAsync(batch).ResponseAsync.ConfigureAwait(false);
        }
    }

    public async Task ClearPaymentsAsync(PaymentReplicationService paymentReplicationService)
    {
        paymentReplicationService.ClearLocalPayments();

        foreach (var remoteClient in RemoteClients)
        {
            await remoteClient.ClearPaymentsAsync(new Empty()).ConfigureAwait(false);
        }
    }

}
