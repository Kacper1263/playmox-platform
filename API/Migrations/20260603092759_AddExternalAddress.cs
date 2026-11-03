using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddExternalAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExternalAddress",
                table: "ProxmoxSettings",
                type: "nvarchar(255)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExternalAddress",
                table: "ProxmoxSettings");
        }
    }
}
