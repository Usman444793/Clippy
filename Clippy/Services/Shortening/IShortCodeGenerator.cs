namespace Clippy.Services.Shortening;
public readonly record struct GeneratedShortId(long SnowflakeId, string ShortCode);

public interface IShortCodeGenerator
{
    GeneratedShortId NextId();
    string EncodeBase62(long value);
}