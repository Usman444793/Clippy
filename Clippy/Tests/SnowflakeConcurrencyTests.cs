using System.Collections.Concurrent;
using Clippy.Extensions;
using Clippy.Services.Shortening;
using Microsoft.Extensions.Options;
using Xunit;
namespace Clippy.Tests;
public class SnowflakeConcurrencyTests
{
    private static SnowFlakeBase62CodeGenerator CreateGenerator(int machineId = 1)
    {
        var options = Options.Create(new ShortenerConfigOptions
        {
            SnowflakeMachineId = machineId
        });
        return new SnowFlakeBase62CodeGenerator(options);
    }
    [Fact]
    public void NextId_UnderHighParallelism_Generates100kUniqueIdsAndCodesWithZeroCollisions()
    {
        var generator = CreateGenerator(machineId: 7);
        const int totalIterations = 100_000;
        var snowflakeIds = new ConcurrentBag<long>();
        var shortCodes = new ConcurrentBag<string>();
        Parallel.For(0, totalIterations, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 }, _ =>
        {
            var generated = generator.NextId();
            snowflakeIds.Add(generated.SnowflakeId);
            shortCodes.Add(generated.ShortCode);
        });
        Assert.Equal(totalIterations, snowflakeIds.Count);
        Assert.Equal(totalIterations, snowflakeIds.Distinct().Count());
        Assert.Equal(totalIterations, shortCodes.Distinct(StringComparer.Ordinal).Count());
    }
    [Theory]
    [InlineData(1L)]
    [InlineData(61L)]
    [InlineData(62L)]
    [InlineData(987654321012345L)]
    public void EncodeBase62_ProducesAlphanumericNonEmptyStrings(long inputId)
    {
        var generator = CreateGenerator();
        var encoded = generator.EncodeBase62(inputId);
        Assert.False(string.IsNullOrWhiteSpace(encoded));
        Assert.Matches("^[a-zA-Z0-9]+$", encoded);
    }
}