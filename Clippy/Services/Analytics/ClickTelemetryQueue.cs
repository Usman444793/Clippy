using System.Threading.Channels;
namespace Clippy.Services.Analytics;
public class ClickTelemetryQueue : IClickTelemetryQueue
{
    private readonly Channel<clickTelemetryEvent> _channel;
    public ClickTelemetryQueue()
    {
        var options = new BoundedChannelOptions(50_000)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<clickTelemetryEvent>(options);
    }
    public void EnqueueClick(clickTelemetryEvent telemetryEvent)
    {
        _channel.Writer.TryWrite(telemetryEvent);
    }
    public IAsyncEnumerable<clickTelemetryEvent> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}
