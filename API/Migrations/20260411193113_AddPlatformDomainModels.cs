using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformDomainModels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GameDefinitions",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Key = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    LinuxGsmServerId = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    DefaultPort = table.Column<int>(type: "int", nullable: true),
                    JsonSchema = table.Column<string>(type: "varchar(MAX)", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UsersQuotas",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    CpuCoresLimit = table.Column<int>(type: "int", nullable: false),
                    MemoryMbLimit = table.Column<int>(type: "int", nullable: false),
                    MaxInstancesLimit = table.Column<int>(type: "int", nullable: false),
                    DiskGbLimit = table.Column<int>(type: "int", nullable: true),
                    MaxPublicPortsLimit = table.Column<int>(type: "int", nullable: true),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UsersQuotas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UsersQuotas_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VirtualGameInstances",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OwnerId = table.Column<long>(type: "bigint", nullable: false),
                    GameDefinitionId = table.Column<long>(type: "bigint", nullable: false),
                    VmId = table.Column<int>(type: "int", nullable: false),
                    InstanceKind = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CpuCores = table.Column<int>(type: "int", nullable: false),
                    MemoryMb = table.Column<int>(type: "int", nullable: false),
                    DiskGb = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    InternalIp = table.Column<string>(type: "nvarchar(255)", nullable: true),
                    PublicHost = table.Column<string>(type: "nvarchar(255)", nullable: true),
                    PublicPort = table.Column<int>(type: "int", nullable: true),
                    ConfigurationJson = table.Column<string>(type: "varchar(MAX)", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VirtualGameInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VirtualGameInstances_GameDefinitions_GameDefinitionId",
                        column: x => x.GameDefinitionId,
                        principalTable: "GameDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VirtualGameInstances_Users_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ProvisioningJobs",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RequestedByUserId = table.Column<long>(type: "bigint", nullable: false),
                    VirtualGameInstanceId = table.Column<long>(type: "bigint", nullable: true),
                    Operation = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    StartedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedDateUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PayloadJson = table.Column<string>(type: "varchar(MAX)", nullable: true),
                    ResultJson = table.Column<string>(type: "varchar(MAX)", nullable: true),
                    ErrorMessage = table.Column<string>(type: "varchar(MAX)", nullable: true),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProvisioningJobs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProvisioningJobs_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProvisioningJobs_VirtualGameInstances_VirtualGameInstanceId",
                        column: x => x.VirtualGameInstanceId,
                        principalTable: "VirtualGameInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GameDefinitions_Key",
                table: "GameDefinitions",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningJobs_RequestedByUserId",
                table: "ProvisioningJobs",
                column: "RequestedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningJobs_Status_CreatedDate",
                table: "ProvisioningJobs",
                columns: new[] { "Status", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningJobs_VirtualGameInstanceId_Status",
                table: "ProvisioningJobs",
                columns: new[] { "VirtualGameInstanceId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UsersQuotas_UserId",
                table: "UsersQuotas",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VirtualGameInstances_GameDefinitionId",
                table: "VirtualGameInstances",
                column: "GameDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_VirtualGameInstances_OwnerId_Status",
                table: "VirtualGameInstances",
                columns: new[] { "OwnerId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_VirtualGameInstances_VmId",
                table: "VirtualGameInstances",
                column: "VmId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProvisioningJobs");

            migrationBuilder.DropTable(
                name: "UsersQuotas");

            migrationBuilder.DropTable(
                name: "VirtualGameInstances");

            migrationBuilder.DropTable(
                name: "GameDefinitions");
        }
    }
}
