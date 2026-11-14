using Clippy.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Clippy.Data.Configurations;
public class UrlClickLogConfiguration : IEntityTypeConfiguration<UrlClickLog>
{
    public void Configure(EntityTypeBuilder<UrlClickLog> builder)
    {
        builder.ToTable("UrlClickLogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.ShortCode).IsRequired().HasMaxLength(16).IsUnicode(false);
        builder.Property(x => x.AnonymizedVisitorHash).IsRequired().HasMaxLength(64).IsUnicode(false);
        builder.Property(x => x.CountryCode).HasMaxLength(2).IsUnicode(false);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.BrowserName).HasMaxLength(50);
        builder.Property(x => x.OsName).HasMaxLength(50);
        builder.Property(x => x.ReferrerHost).HasMaxLength(250);
        builder.HasIndex(x => new { x.ShortUrlId, x.ClickedAtUtc }).IsDescending(false, true).HasDatabaseName("IX_UrlClickLogs_ShortUrlId_ClickedAtUtc");
        builder.HasOne(x => x.ShortUrl).WithMany(u => u.ClickLogs).HasForeignKey(x => x.ShortUrlId).OnDelete(DeleteBehavior.Cascade);
    }
}
