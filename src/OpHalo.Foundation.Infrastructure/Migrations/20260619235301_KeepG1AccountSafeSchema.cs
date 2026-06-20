using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpHalo.Foundation.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class KeepG1AccountSafeSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_keep_customers_account_phone",
                table: "keep_customers");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_business_activity_at",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AddColumn<string>(
                name: "canonical_phone",
                table: "keep_customers",
                type: "character varying(15)",
                maxLength: 15,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_requests_account_id",
                table: "keep_requests",
                columns: new[] { "account_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_keep_customers_account_id",
                table: "keep_customers",
                columns: new[] { "account_id", "id" });

            migrationBuilder.AddUniqueConstraint(
                name: "ak_account_users_account_id",
                table: "account_users",
                columns: new[] { "account_id", "id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id_attention_cleared_by_account_user_",
                table: "keep_requests",
                columns: new[] { "account_id", "attention_cleared_by_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id_feedback_reviewed_by_account_user_",
                table: "keep_requests",
                columns: new[] { "account_id", "feedback_reviewed_by_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id_first_responder_account_user_id",
                table: "keep_requests",
                columns: new[] { "account_id", "first_responder_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_requests_account_id_keep_customer_id",
                table: "keep_requests",
                columns: new[] { "account_id", "keep_customer_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_participants_account_id_request_id",
                table: "keep_request_participants",
                columns: new[] { "account_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_actor_account_user_id",
                table: "keep_request_events",
                columns: new[] { "account_id", "actor_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_participation_notification_i",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_notification_intended_recipient_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_participation_previous_respo",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_previous_responsible_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_participation_target_account",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_target_account_user_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_request_events_account_id_request_id",
                table: "keep_request_events",
                columns: new[] { "account_id", "request_id" });

            migrationBuilder.CreateIndex(
                name: "ix_keep_public_intake_links_account_active",
                table: "keep_public_intake_links",
                column: "account_id",
                unique: true,
                filter: "revoked_at_utc IS NULL AND deleted_at_utc IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_keep_customers_account_canonical_phone",
                table: "keep_customers",
                columns: new[] { "account_id", "canonical_phone" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_customers_accounts_account_id",
                table: "keep_customers",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_public_intake_links_accounts_account_id",
                table: "keep_public_intake_links",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_events_account_users_account_id_actor_account_",
                table: "keep_request_events",
                columns: new[] { "account_id", "actor_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_notification_intended_recipient_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_1",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_previous_responsible_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_2",
                table: "keep_request_events",
                columns: new[] { "account_id", "participation_target_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_events_keep_requests_account_id_request_id",
                table: "keep_request_events",
                columns: new[] { "account_id", "request_id" },
                principalTable: "keep_requests",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_participants_account_users_account_id_account_",
                table: "keep_request_participants",
                columns: new[] { "account_id", "account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_request_participants_keep_requests_account_id_request_",
                table: "keep_request_participants",
                columns: new[] { "account_id", "request_id" },
                principalTable: "keep_requests",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_account_users_account_id_attention_cleared_by",
                table: "keep_requests",
                columns: new[] { "account_id", "attention_cleared_by_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_account_users_account_id_feedback_reviewed_by",
                table: "keep_requests",
                columns: new[] { "account_id", "feedback_reviewed_by_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_account_users_account_id_first_responder_acco",
                table: "keep_requests",
                columns: new[] { "account_id", "first_responder_account_user_id" },
                principalTable: "account_users",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_accounts_account_id",
                table: "keep_requests",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_requests_keep_customers_account_id_keep_customer_id",
                table: "keep_requests",
                columns: new[] { "account_id", "keep_customer_id" },
                principalTable: "keep_customers",
                principalColumns: new[] { "account_id", "id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_keep_response_policies_accounts_account_id",
                table: "keep_response_policies",
                column: "account_id",
                principalTable: "accounts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_keep_customers_accounts_account_id",
                table: "keep_customers");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_public_intake_links_accounts_account_id",
                table: "keep_public_intake_links");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_events_account_users_account_id_actor_account_",
                table: "keep_request_events");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_",
                table: "keep_request_events");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_1",
                table: "keep_request_events");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_events_account_users_account_id_participation_2",
                table: "keep_request_events");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_events_keep_requests_account_id_request_id",
                table: "keep_request_events");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_participants_account_users_account_id_account_",
                table: "keep_request_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_request_participants_keep_requests_account_id_request_",
                table: "keep_request_participants");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_account_users_account_id_attention_cleared_by",
                table: "keep_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_account_users_account_id_feedback_reviewed_by",
                table: "keep_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_account_users_account_id_first_responder_acco",
                table: "keep_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_accounts_account_id",
                table: "keep_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_requests_keep_customers_account_id_keep_customer_id",
                table: "keep_requests");

            migrationBuilder.DropForeignKey(
                name: "fk_keep_response_policies_accounts_account_id",
                table: "keep_response_policies");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_requests_account_id",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_id_attention_cleared_by_account_user_",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_id_feedback_reviewed_by_account_user_",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_id_first_responder_account_user_id",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_requests_account_id_keep_customer_id",
                table: "keep_requests");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_participants_account_id_request_id",
                table: "keep_request_participants");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_actor_account_user_id",
                table: "keep_request_events");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_participation_notification_i",
                table: "keep_request_events");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_participation_previous_respo",
                table: "keep_request_events");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_participation_target_account",
                table: "keep_request_events");

            migrationBuilder.DropIndex(
                name: "ix_keep_request_events_account_id_request_id",
                table: "keep_request_events");

            migrationBuilder.DropIndex(
                name: "ix_keep_public_intake_links_account_active",
                table: "keep_public_intake_links");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_keep_customers_account_id",
                table: "keep_customers");

            migrationBuilder.DropIndex(
                name: "ix_keep_customers_account_canonical_phone",
                table: "keep_customers");

            migrationBuilder.DropUniqueConstraint(
                name: "ak_account_users_account_id",
                table: "account_users");

            migrationBuilder.DropColumn(
                name: "canonical_phone",
                table: "keep_customers");

            migrationBuilder.AlterColumn<DateTime>(
                name: "last_business_activity_at",
                table: "keep_requests",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_keep_customers_account_phone",
                table: "keep_customers",
                columns: new[] { "account_id", "primary_phone" },
                unique: true);
        }
    }
}
