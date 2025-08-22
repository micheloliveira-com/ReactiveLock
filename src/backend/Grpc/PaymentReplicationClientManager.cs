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

    public async Task PublishPaymentsBatchAsync(
        IEnumerable<PaymentInsertRpcParameters> payments,
        PaymentReplicationService paymentReplicationService,
        int chunkSize = 30)
    {
        foreach (var pay in payments)
        {
            paymentReplicationService.HandleLocally(pay);
        }

        var chunks = payments
            .Select((payment, index) => new { payment, index })
            .GroupBy(x => x.index / chunkSize)
            .Select(g => g.Select(x => x.payment).ToList())
            .ToList();

        foreach (var remoteClient in RemoteClients)
        {
            var chunkTasks = chunks.Select(async chunk =>
            {
                using var remoteCall = remoteClient.PublishPayments();

                foreach (var payment in chunk)
                {
                    await remoteCall.RequestStream.WriteAsync(payment).ConfigureAwait(false);
                }

                await remoteCall.RequestStream.CompleteAsync().ConfigureAwait(false);
                await remoteCall.ResponseAsync.ConfigureAwait(false);
            });

            await Task.WhenAll(chunkTasks).ConfigureAwait(false);
        }
    }


}
