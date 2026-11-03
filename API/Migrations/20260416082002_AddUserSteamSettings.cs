using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddUserSteamSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SteamPasswordEncrypted",
                table: "Users",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SteamUsername",
                table: "Users",
                type: "nvarchar(255)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SteamPasswordEncrypted",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SteamUsername",
                table: "Users");
        }
    }
}
