using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddProxmoxSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProxmoxSettings",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProxmoxUrl = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ProxmoxNode = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ApiUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ApiTokenId = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ApiTokenSecret = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    TemplateVmid = table.Column<int>(type: "int", nullable: false),
                    Storage = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    Bridge = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProxmoxSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProxmoxSettings");
        }
    }
}
