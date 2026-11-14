using Clippy.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Clippy.Data.Configurations;
public sealed class ShortUrlConfiguration : IEntityTypeConfiguration<ShortUrl>
{
    public void Configure(EntityTypeBuilder<ShortUrl> builder)
    {
        builder.ToTable("ShortUrls");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.shortCode).IsRequired().HasMaxLength(16).IsUnicode(false);
        builder.Property(x => x.CustomAlias).HasMaxLength(64).IsUnicode(false);
        builder.Property(x => x.DestinationUrl).IsRequired().HasMaxLength(2048);
        builder.Property(x => x.Title).HasMaxLength(200);
        builder.Property(x => x.SafetyStatus).HasConversion<int>();
        builder.Property(x => x.RowVersion).IsRowVersion();
        builder.HasIndex(x => x.shortCode).IsUnique().HasDatabaseName("IX_ShortUrls_ShortCode_Unique");
        builder.HasIndex(x => x.CustomAlias).IsUnique().HasFilter("\"CustomAlias\" IS NOT NULL").HasDatabaseName("IX_ShortUrls_CustomAlias_Unique");
        builder.HasIndex(x => new { x.UserId, x.CreatedAtUtc }).IsDescending(false, true).HasDatabaseName("IX_ShortUrls_UserId_CreatedAtUtc");
        builder.HasOne(x => x.User).WithMany(x => x.ShortUrls).HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
    }
}