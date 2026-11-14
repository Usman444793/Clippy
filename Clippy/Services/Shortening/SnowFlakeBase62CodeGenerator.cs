using Clippy.Extensions;
using Microsoft.Extensions.Options;
namespace Clippy.Services.Shortening;
public sealed class SnowFlakeBase62CodeGenerator : IShortCodeGenerator
{
    private const string Alphabet = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
    private static readonly long CustomEpochMs = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
    private const int MachineIdBits = 10;
    private const int SequenceBits = 12;
    private const long MaxMachineId = (1L << MachineIdBits) - 1;
    private const long MaxSequence = (1L << SequenceBits) - 1;
    private readonly long _machineId;
    private readonly Lock _syncLock = new();
    private long _lasttimeStamp = -1L;
    private long _sequence = 0L;
    public SnowFlakeBase62CodeGenerator(IOptions<ShortenerConfigOptions> options)
    {
        _machineId = Math.Clamp(options.Value.SnowflakeMachineId, 1, MaxMachineId);
    }
    public GeneratedShortId NextId()
    {
        long snowflakeId;
        lock (_syncLock)
        {
            var currentTimeStamp = GetCurrentTimeStampMs();
            if (currentTimeStamp < _lasttimeStamp)
            {
                currentTimeStamp = WaitNextMillis(_lasttimeStamp);
            }
            if (currentTimeStamp == _lasttimeStamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    currentTimeStamp = WaitNextMillis(_lasttimeStamp);
                }
            }
            else
            {
                _sequence = 0L;
            }
            _lasttimeStamp = currentTimeStamp;
            snowflakeId = (currentTimeStamp - CustomEpochMs) << (MachineIdBits + SequenceBits) | (_machineId << SequenceBits) | _sequence;
            var shortCode = EncodeBase62(snowflakeId);
            return new GeneratedShortId(snowflakeId, shortCode);
        }
    }
    public string EncodeBase62(long value)
    {
        if (value == 0) return Alphabet[0].ToString();
        Span<char> buffer = stackalloc char[16];
        var index = buffer.Length;
        while (value > 0)
        {
            buffer[--index] = Alphabet[(int)(value % 62)];
            value /= 62;
        }
        return new string(buffer[index..]);
    }
    private static long GetCurrentTimeStampMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static long WaitNextMillis(long lastTimeStamp)
    {
        var timeStamp = GetCurrentTimeStampMs();
        while (timeStamp <= lastTimeStamp)
        {
            Thread.SpinWait(10);
            timeStamp = GetCurrentTimeStampMs();
        }
        return timeStamp;
    }
}
