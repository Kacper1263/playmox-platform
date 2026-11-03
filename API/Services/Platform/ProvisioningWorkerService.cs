namespace PlaymoxAPI.Services.Platform;

public class ProvisioningWorkerService : BackgroundService
{
    private const int VmLaneWorkers = 3;
    private readonly ProvisioningJobProcessor jobProcessor;
    private readonly ILogger<ProvisioningWorkerService> logger;

    public ProvisioningWorkerService(ProvisioningJobProcessor jobProcessor, ILogger<ProvisioningWorkerService> logger)
    {
        this.jobProcessor = jobProcessor;
        this.logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var workers = new List<Task>
        {
            RunLaneLoopAsync(JobProcessingLane.Proxmox, stoppingToken)
        };

        for (var worker = 0; worker < VmLaneWorkers; worker++)
        {
            workers.Add(RunLaneLoopAsync(JobProcessingLane.VmScoped, stoppingToken));
        }

        await Task.WhenAll(workers);
    }

    private async Task RunLaneLoopAsync(JobProcessingLane lane, CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await jobProcessor.ProcessNextAsync(lane, stoppingToken);
                if (!processed)
                {
                    await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Provisioning worker loop failed for lane {Lane}.", lane);
                await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
            }
        }
    }
}
