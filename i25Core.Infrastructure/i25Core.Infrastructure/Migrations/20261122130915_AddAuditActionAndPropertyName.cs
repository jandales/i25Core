using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace i25Core.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditActionAndPropertyName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PropertyName",
                table: "AuditLogChange",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Action",
                table: "AuditLog",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PropertyName",
                table: "AuditLogChange");

            migrationBuilder.DropColumn(
                name: "Action",
                table: "AuditLog");
        }
    }
}
