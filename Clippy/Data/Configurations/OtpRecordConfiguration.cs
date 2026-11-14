using Clippy.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
namespace Clippy.Data.Configurations;
public class OtpRecordConfiguration : IEntityTypeConfiguration<OtpRecord>
{
    public void Configure(EntityTypeBuilder<OtpRecord> builder)
    {
        builder.ToTable("OtpRecord");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TargetIdentifier).IsRequired().HasMaxLength(256);
        builder.Property(x => x.Purpose).IsRequired().HasMaxLength(50).IsUnicode(false);
        builder.Property(x => x.CodeHmac).IsRequired().HasMaxLength(128).IsUnicode(false);
        builder.HasIndex(x => new { x.TargetIdentifier, x.Purpose, x.ExpiresAtUtc }).HasDatabaseName("IX_OtpRecords_Target_Purpose_Expiry");
    }
}
