using i25Core.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace i25Core.Core.Configuration
{
    public class UserMap : IEntityTypeConfiguration<User>
    {
        public void Configure(EntityTypeBuilder<User> builder)
        {
            builder.ToTable("Users");

            builder.Property(x => x.Email).IsRequired();
            builder.Property(x => x.NormalizedEmail).IsRequired();
            builder.Property(x => x.PasswordHash).IsRequired();

            // One account per email address (ignoring case).
            builder.HasIndex(x => x.NormalizedEmail).IsUnique();

            builder.HasMany(x => x.UserRoles)
                .WithOne(x => x.User)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class RoleMap : IEntityTypeConfiguration<Role>
    {
        public void Configure(EntityTypeBuilder<Role> builder)
        {
            builder.ToTable("Roles");

            builder.Property(x => x.Name).IsRequired();
            builder.HasIndex(x => x.Name).IsUnique();

            // A role that is still assigned cannot be deleted by accident.
            builder.HasMany(x => x.UserRoles)
                .WithOne(x => x.Role)
                .HasForeignKey(x => x.RoleId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }

    public class UserRoleMap : IEntityTypeConfiguration<UserRole>
    {
        public void Configure(EntityTypeBuilder<UserRole> builder)
        {
            builder.ToTable("UserRoles");
            builder.HasKey(x => new { x.UserId, x.RoleId });
        }
    }

    public class RefreshTokenMap : IEntityTypeConfiguration<RefreshToken>
    {
        public void Configure(EntityTypeBuilder<RefreshToken> builder)
        {
            builder.ToTable("RefreshTokens");

            builder.Property(x => x.TokenHash).IsRequired();
            builder.HasIndex(x => x.TokenHash).IsUnique();
            builder.HasIndex(x => new { x.UserId, x.FamilyId });

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class PasswordResetTokenMap : IEntityTypeConfiguration<PasswordResetToken>
    {
        public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
        {
            builder.ToTable("PasswordResetTokens");

            builder.Property(x => x.TokenHash).IsRequired();
            builder.HasIndex(x => x.TokenHash).IsUnique();
            builder.HasIndex(x => x.UserId);

            builder.HasOne<User>()
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }
}
