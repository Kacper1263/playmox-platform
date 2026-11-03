using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class SecurityQueueAndPortLeases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ProvisioningJobs_VirtualGameInstanceId_Status",
                table: "ProvisioningJobs");

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptUtc",
                table: "ProvisioningJobs",
                type: "datetime2",
                nullable: true);

            migrationBuilder.Sql(
                """
                ;WITH duplicates AS (
                    SELECT
                        Id,
                        ROW_NUMBER() OVER (
                            PARTITION BY VirtualGameInstanceId, Status
                            ORDER BY Id DESC
                        ) AS rn
                    FROM ProvisioningJobs
                    WHERE VirtualGameInstanceId IS NOT NULL
                      AND Status IN ('Queued', 'Running')
                )
                UPDATE pj
                SET
                    Status = 'Failed',
                    CompletedDateUtc = COALESCE(CompletedDateUtc, SYSUTCDATETIME()),
                    ErrorMessage = COALESCE(NULLIF(ErrorMessage, ''), 'Normalized during security migration: duplicate active job.'),
                    NextAttemptUtc = NULL
                FROM ProvisioningJobs pj
                INNER JOIN duplicates d ON d.Id = pj.Id
                WHERE d.rn > 1;
                """);

            migrationBuilder.CreateTable(
                name: "PortLeases",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    VirtualGameInstanceId = table.Column<long>(type: "bigint", nullable: false),
                    Port = table.Column<int>(type: "int", nullable: false),
                    CreatedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ModifiedUser = table.Column<string>(type: "nvarchar(255)", nullable: false),
                    ModifiedDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Extra = table.Column<string>(type: "varchar(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PortLeases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PortLeases_VirtualGameInstances_VirtualGameInstanceId",
                        column: x => x.VirtualGameInstanceId,
                        principalTable: "VirtualGameInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "UX_ProvisioningJobs_ActivePerInstanceAndStatus",
                table: "ProvisioningJobs",
                columns: new[] { "VirtualGameInstanceId", "Status" },
                unique: true,
                filter: "[VirtualGameInstanceId] IS NOT NULL AND [Status] IN ('Queued','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_PortLeases_Port",
                table: "PortLeases",
                column: "Port",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PortLeases_VirtualGameInstanceId",
                table: "PortLeases",
                column: "VirtualGameInstanceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PortLeases");

            migrationBuilder.DropIndex(
                name: "UX_ProvisioningJobs_ActivePerInstanceAndStatus",
                table: "ProvisioningJobs");

            migrationBuilder.DropColumn(
                name: "NextAttemptUtc",
                table: "ProvisioningJobs");

            migrationBuilder.CreateIndex(
                name: "IX_ProvisioningJobs_VirtualGameInstanceId_Status",
                table: "ProvisioningJobs",
                columns: new[] { "VirtualGameInstanceId", "Status" });
        }
    }
}
