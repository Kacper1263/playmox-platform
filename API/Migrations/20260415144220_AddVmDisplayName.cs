using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddVmDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VmDisplayName",
                table: "VirtualGameInstances",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE VirtualGameInstances
                SET VmDisplayName = LTRIM(RTRIM(JSON_VALUE(ConfigurationJson, '$.user.vmDisplayName')))
                WHERE VmDisplayName IS NULL
                  AND JSON_VALUE(ConfigurationJson, '$.user.vmDisplayName') IS NOT NULL
                  AND LTRIM(RTRIM(JSON_VALUE(ConfigurationJson, '$.user.vmDisplayName'))) <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VmDisplayName",
                table: "VirtualGameInstances");
        }
    }
}
