using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EFCore.Observability.API.Migrations
{
    /// <inheritdoc />
    public partial class initcreation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bills",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AccountNumber = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DueDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bills", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Payments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BillId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentDate = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETDATE()"),
                    TransactionId = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Payments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Payments_Bills_BillId",
                        column: x => x.BillId,
                        principalTable: "Bills",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Bills",
                columns: new[] { "Id", "AccountNumber", "Amount", "CreatedAt", "DueDate", "Status" },
                values: new object[,]
                {
                    { 1, "ACC001", 100.50m, new DateTime(2026, 2, 28, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7692), new DateTime(2026, 3, 30, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7574), "Pending" },
                    { 2, "ACC002", 250.00m, new DateTime(2026, 2, 28, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7716), new DateTime(2026, 3, 15, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7707), "Pending" },
                    { 3, "ACC003", 75.25m, new DateTime(2026, 2, 28, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7735), new DateTime(2026, 4, 14, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(7728), "Paid" }
                });

            migrationBuilder.InsertData(
                table: "Payments",
                columns: new[] { "Id", "Amount", "BillId", "PaymentDate", "TransactionId" },
                values: new object[,]
                {
                    { 1, 100.50m, 1, new DateTime(2026, 2, 28, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(8253), "TXN001" },
                    { 2, 75.25m, 3, new DateTime(2026, 2, 28, 22, 14, 19, 23, DateTimeKind.Local).AddTicks(8272), "TXN002" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bills_Status",
                table: "Bills",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Payments_BillId",
                table: "Payments",
                column: "BillId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Payments");

            migrationBuilder.DropTable(
                name: "Bills");
        }
    }
}
