using System.Threading.Channels;
public sealed record MailJob(string Username,string Email,string Purpose);
public sealed class MailQueue : BackgroundService
{
    readonly Channel<MailJob> jobs = Channel.CreateBounded<MailJob>(new BoundedChannelOptions(100) { SingleReader=true,FullMode=BoundedChannelFullMode.Wait });
    readonly AccountService accounts;
    public MailQueue(AccountService accounts) => this.accounts=accounts;
    public bool Enqueue(MailJob job) => jobs.Writer.TryWrite(job);
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var job in jobs.Reader.ReadAllAsync(stoppingToken))
        {
            try { await accounts.Send(job.Username,job.Email,job.Purpose); }
            catch { /* Intentionally excludes account data and transport exceptions from logs. */ }
        }
    }
}
