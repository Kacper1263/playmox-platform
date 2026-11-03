using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PlaymoxAPI.Migrations
{
    /// <inheritdoc />
    public partial class AddPortsAndGameProvisioning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "VirtualGameInstances",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "InstanceKind",
                table: "VirtualGameInstances",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)");

            migrationBuilder.AddColumn<int>(
                name: "GamePublicPortCount",
                table: "VirtualGameInstances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GamePublicPortProtocol",
                table: "VirtualGameInstances",
                type: "nvarchar(255)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GamePublicPortStart",
                table: "VirtualGameInstances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SshPublicPort",
                table: "VirtualGameInstances",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AllowSshPublicPort",
                table: "UsersQuotas",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProvisioningJobs",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)");

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "ProvisioningJobs",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(255)");

            migrationBuilder.AddColumn<int>(
                name: "RequiredPublicPorts",
                table: "GameDefinitions",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GamePublicPortCount",
                table: "VirtualGameInstances");

            migrationBuilder.DropColumn(
                name: "GamePublicPortProtocol",
                table: "VirtualGameInstances");

            migrationBuilder.DropColumn(
                name: "GamePublicPortStart",
                table: "VirtualGameInstances");

            migrationBuilder.DropColumn(
                name: "SshPublicPort",
                table: "VirtualGameInstances");

            migrationBuilder.DropColumn(
                name: "AllowSshPublicPort",
                table: "UsersQuotas");

            migrationBuilder.DropColumn(
                name: "RequiredPublicPorts",
                table: "GameDefinitions");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "VirtualGameInstances",
                type: "nvarchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "InstanceKind",
                table: "VirtualGameInstances",
                type: "nvarchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "ProvisioningJobs",
                type: "nvarchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AlterColumn<string>(
                name: "Operation",
                table: "ProvisioningJobs",
                type: "nvarchar(255)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");
        }
    }
}
