using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentChangeSource : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_account_user_id",
                table: "account_capability_package_enrollments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Nullable first so existing rows can be backfilled with a real, meaningful value
            // (every pre-existing row was created by an internal actor) before the NOT NULL
            // constraint is applied — same pattern as AddActualWorkRecorderAccountUserId.
            migrationBuilder.AddColumn<string>(
                name: "change_source",
                table: "account_capability_package_enrollments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE account_capability_package_enrollments SET change_source = 'InternalUser' " +
                "WHERE change_source IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "change_source",
                table: "account_capability_package_enrollments",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_account_capability_package_enrollments_actor_by_source",
                table: "account_capability_package_enrollments",
                sql: "(change_source = 'InternalUser' AND changed_by_account_user_id IS NOT NULL) OR (change_source = 'SystemProvisioning' AND changed_by_account_user_id IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not safely reversible once any SystemProvisioning row exists (ADR-496): that row's
            // changed_by_account_user_id is null by design, and there is no real actor to invent
            // for it. Fail loudly instead of silently fabricating one (e.g. an all-zero guid).
            migrationBuilder.Sql("""
                DO $$
                DECLARE null_actor_count integer;
                BEGIN
                    SELECT count(*) INTO null_actor_count
                    FROM account_capability_package_enrollments
                    WHERE changed_by_account_user_id IS NULL;

                    IF null_actor_count > 0 THEN
                        RAISE EXCEPTION
                            'Cannot roll back AddEnrollmentChangeSource: % row(s) are SystemProvisioning enrollments with no actor. Rolling back would require inventing an actor for them.',
                            null_actor_count;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_account_capability_package_enrollments_actor_by_source",
                table: "account_capability_package_enrollments");

            migrationBuilder.DropColumn(
                name: "change_source",
                table: "account_capability_package_enrollments");

            migrationBuilder.AlterColumn<Guid>(
                name: "changed_by_account_user_id",
                table: "account_capability_package_enrollments",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
