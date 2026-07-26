using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EventFinder.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddGeocodingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LocationPrecision",
                table: "Events",
                type: "INTEGER",
                nullable: false,
                // LocationPrecision.None (3) -- any row from before this
                // column existed had its precision computed by the old,
                // address-blind cascade, so "we don't actually know" is the
                // honest default rather than falsely claiming Address.
                defaultValue: 3);

            migrationBuilder.CreateTable(
                name: "GeocodeCacheEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Query = table.Column<string>(type: "TEXT", nullable: false),
                    Found = table.Column<bool>(type: "INTEGER", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: true),
                    Longitude = table.Column<double>(type: "REAL", nullable: true),
                    Precision = table.Column<int>(type: "INTEGER", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GeocodeCacheEntries", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GeocodeCacheEntries_Query",
                table: "GeocodeCacheEntries",
                column: "Query",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GeocodeCacheEntries");

            migrationBuilder.DropColumn(
                name: "LocationPrecision",
                table: "Events");
        }
    }
}
