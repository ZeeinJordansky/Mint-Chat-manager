using Npgsql;

namespace MintChatManager;

public sealed class PostgresStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var databaseDirectory = Path.Combine(AppContext.BaseDirectory, "Database");
        var schemaFiles = Directory.GetFiles(databaseDirectory, "*.sql")
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (schemaFiles.Length == 0)
        {
            throw new InvalidOperationException($"No database schema files were found in {databaseDirectory}.");
        }

        foreach (var schemaFile in schemaFiles)
        {
            var sql = await File.ReadAllTextAsync(schemaFile, cancellationToken);
            await using var command = dataSource.CreateCommand(sql);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    public async Task<GameAccount> GetGameAccountAsync(long userId, CancellationToken cancellationToken)
    {
        await EnsureProfileAsync(userId, cancellationToken);
        const string sql = """
            SELECT wallet_balance, bank_balance, reputation, beer, btc_balance,
                   daily_bonus_day, last_daily_claimed_at
            FROM user_profiles
            WHERE user_id = @user_id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Failed to load game account for user {userId}.");
        }

        return new GameAccount(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt16(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));
    }

    public async Task<GameAccount?> MoveMoneyToOrFromBankAsync(
        long userId,
        long amount,
        bool deposit,
        CancellationToken cancellationToken)
    {
        await EnsureProfileAsync(userId, cancellationToken);
        var sql = deposit
            ? """
              UPDATE user_profiles
              SET wallet_balance = wallet_balance - @amount,
                  bank_balance = bank_balance + @amount
              WHERE user_id = @user_id AND wallet_balance >= @amount
              RETURNING wallet_balance, bank_balance, reputation, beer, btc_balance,
                        daily_bonus_day, last_daily_claimed_at;
              """
            : """
              UPDATE user_profiles
              SET wallet_balance = wallet_balance + @amount,
                  bank_balance = bank_balance - @amount
              WHERE user_id = @user_id AND bank_balance >= @amount
              RETURNING wallet_balance, bank_balance, reputation, beer, btc_balance,
                        daily_bonus_day, last_daily_claimed_at;
              """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("amount", amount);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? ReadGameAccount(reader)
            : null;
    }

    public async Task<bool> BuyBitcoinAsync(
        long userId,
        long count,
        long price,
        CancellationToken cancellationToken)
    {
        var totalCost = checked(count * price);
        await EnsureProfileAsync(userId, cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE user_profiles
            SET wallet_balance = wallet_balance - @total_cost,
                btc_balance = btc_balance + @count
            WHERE user_id = @user_id AND wallet_balance >= @total_cost;
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.AddWithValue("total_cost", totalCost);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task<long?> SellBitcoinAsync(
        long userId,
        long count,
        long price,
        CancellationToken cancellationToken)
    {
        var totalRefund = checked(count * price);
        await EnsureProfileAsync(userId, cancellationToken);
        await using var command = dataSource.CreateCommand(
            """
            UPDATE user_profiles
            SET wallet_balance = wallet_balance + @total_refund,
                btc_balance = btc_balance - @count
            WHERE user_id = @user_id AND btc_balance >= @count
            RETURNING wallet_balance;
            """);
        command.Parameters.AddWithValue("user_id", userId);
        command.Parameters.AddWithValue("count", count);
        command.Parameters.AddWithValue("total_refund", totalRefund);
        var result = await command.ExecuteScalarAsync(cancellationToken);
        return result is long walletBalance ? walletBalance : null;
    }

    public async Task<bool> TransferMoneyAsync(
        long senderId,
        long recipientId,
        long amount,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        foreach (var userId in new[] { senderId, recipientId }.Order())
        {
            await using var ensure = new NpgsqlCommand(
                "INSERT INTO user_profiles (user_id) VALUES (@user_id) ON CONFLICT (user_id) DO NOTHING;",
                connection,
                transaction);
            ensure.Parameters.AddWithValue("user_id", userId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        var balances = new Dictionary<long, long>(2);
        await using (var read = new NpgsqlCommand(
                         """
                         SELECT user_id, wallet_balance
                         FROM user_profiles
                         WHERE user_id = ANY(@user_ids)
                         ORDER BY user_id
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            read.Parameters.AddWithValue("user_ids", new[] { senderId, recipientId });
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                balances.Add(reader.GetInt64(0), reader.GetInt64(1));
            }
        }

        if (!balances.TryGetValue(senderId, out var senderBalance) || senderBalance < amount ||
            !balances.ContainsKey(recipientId))
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE user_profiles
                         SET wallet_balance = CASE user_id
                             WHEN @sender_id THEN wallet_balance - @amount
                             WHEN @recipient_id THEN wallet_balance + @amount
                             END
                         WHERE user_id IN (@sender_id, @recipient_id);
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("sender_id", senderId);
            update.Parameters.AddWithValue("recipient_id", recipientId);
            update.Parameters.AddWithValue("amount", amount);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<DailyBonusClaimResult> ClaimDailyBonusAsync(
        long userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var ensure = new NpgsqlCommand(
                         "INSERT INTO user_profiles (user_id) VALUES (@user_id) ON CONFLICT (user_id) DO NOTHING;",
                         connection,
                         transaction))
        {
            ensure.Parameters.AddWithValue("user_id", userId);
            await ensure.ExecuteNonQueryAsync(cancellationToken);
        }

        short day;
        DateTimeOffset? lastClaimedAt;
        bool isReady;
        await using (var read = new NpgsqlCommand(
                         """
                         SELECT daily_bonus_day, last_daily_claimed_at,
                                last_daily_claimed_at IS NULL OR
                                last_daily_claimed_at <= NOW() - INTERVAL '24 hours'
                         FROM user_profiles
                         WHERE user_id = @user_id
                         FOR UPDATE;
                         """,
                         connection,
                         transaction))
        {
            read.Parameters.AddWithValue("user_id", userId);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new InvalidOperationException($"Failed to lock game account for user {userId}.");
            }

            day = reader.GetInt16(0);
            lastClaimedAt = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1);
            isReady = reader.GetBoolean(2);
        }

        if (!isReady)
        {
            await transaction.RollbackAsync(cancellationToken);
            return new DailyBonusClaimResult(false, day, 0, 0, 0, lastClaimedAt?.AddHours(24));
        }

        var reward = day switch
        {
            1 => new DailyBonusReward(25_000, 0, 0),
            2 => new DailyBonusReward(0, 2, 0),
            3 => new DailyBonusReward(0, 0, 3),
            4 => new DailyBonusReward(75_000, 0, 0),
            5 => new DailyBonusReward(0, 4, 0),
            6 => new DailyBonusReward(0, 0, 7),
            7 => new DailyBonusReward(300_000, 0, 0),
            _ => throw new InvalidOperationException($"Invalid daily bonus day {day} for user {userId}.")
        };

        await using (var update = new NpgsqlCommand(
                         """
                         UPDATE user_profiles
                         SET wallet_balance = wallet_balance + @money,
                             reputation = reputation + @reputation,
                             beer = beer + @beer,
                             daily_bonus_day = CASE WHEN daily_bonus_day >= 7 THEN 1 ELSE daily_bonus_day + 1 END,
                             last_daily_claimed_at = NOW()
                         WHERE user_id = @user_id;
                         """,
                         connection,
                         transaction))
        {
            update.Parameters.AddWithValue("user_id", userId);
            update.Parameters.AddWithValue("money", reward.Money);
            update.Parameters.AddWithValue("reputation", reward.Reputation);
            update.Parameters.AddWithValue("beer", reward.Beer);
            await update.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return new DailyBonusClaimResult(
            true,
            day,
            reward.Money,
            reward.Reputation,
            reward.Beer,
            null);
    }

    private async Task EnsureProfileAsync(long userId, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            "INSERT INTO user_profiles (user_id) VALUES (@user_id) ON CONFLICT (user_id) DO NOTHING;");
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static GameAccount ReadGameAccount(NpgsqlDataReader reader) =>
        new(
            reader.GetInt64(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            reader.GetInt64(4),
            reader.GetInt16(5),
            reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6));

    public async Task RecordMessageAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = """
            WITH updated_profile AS (
                INSERT INTO user_profiles (user_id, messages_count)
                VALUES (@user_id, 1)
                ON CONFLICT (user_id) DO UPDATE
                SET messages_count = user_profiles.messages_count + 1,
                    last_seen_at = NOW()
                RETURNING user_id
            )
            INSERT INTO user_daily_messages (user_id, message_date, messages_count)
            SELECT user_id, (NOW() AT TIME ZONE 'Europe/Moscow')::date, 1 FROM updated_profile
            ON CONFLICT (user_id, message_date) DO UPDATE
            SET messages_count = user_daily_messages.messages_count + 1;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PeerProfile?> GetProfileAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT messages_count, settings::text, created_at, last_seen_at, global_role
            FROM user_profiles
            WHERE user_id = @user_id;
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PeerProfile(
            reader.GetInt64(0),
            reader.GetString(1),
            reader.GetFieldValue<DateTimeOffset>(2),
            reader.GetFieldValue<DateTimeOffset>(3),
            reader.GetDecimal(4));
    }

    public async Task<UserStatistics?> GetUserStatisticsAsync(
        long peerId,
        long userId,
        IReadOnlySet<long> administratorIds,
        CancellationToken cancellationToken)
    {
        var profile = await GetProfileAsync(userId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        const string sql = """
            SELECT
                COALESCE((SELECT messages_count FROM user_daily_messages
                          WHERE user_id = @user_id
                            AND message_date = (NOW() AT TIME ZONE 'Europe/Moscow')::date), 0),
                COALESCE((SELECT SUM(messages_count) FROM user_daily_messages
                          WHERE user_id = @user_id
                            AND message_date >= (NOW() AT TIME ZONE 'Europe/Moscow')::date - 6), 0),
                (SELECT COUNT(*)::int FROM moderation_actions
                 WHERE peer_id = @peer_id AND target_id = @user_id
                   AND action_type = 'warn' AND active),
                EXISTS (SELECT 1 FROM moderation_actions
                        WHERE peer_id = @peer_id AND target_id = @user_id
                          AND action_type = 'mute' AND active
                          AND (expires_at IS NULL OR expires_at > NOW())),
                COALESCE((SELECT role FROM chat_roles WHERE peer_id = @peer_id AND user_id = @user_id), 0);
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        var role = administratorIds.Contains(userId)
            ? 12m
            : Math.Max(profile.GlobalRole, reader.GetDecimal(4));
        return new UserStatistics(
            userId,
            reader.GetInt64(0),
            reader.GetInt64(1),
            profile.MessagesCount,
            reader.GetInt32(2),
            reader.GetBoolean(3),
            role,
            profile.CreatedAt,
            profile.LastSeenAt);
    }

    public async Task<decimal> GetEffectiveRoleAsync(
        long peerId,
        long userId,
        IReadOnlySet<long> administratorIds,
        CancellationToken cancellationToken)
    {
        if (administratorIds.Contains(userId))
        {
            return 12;
        }

        const string sql = """
            SELECT GREATEST(
                COALESCE((SELECT global_role FROM user_profiles WHERE user_id = @user_id), 0),
                COALESCE((SELECT role FROM chat_roles WHERE peer_id = @peer_id AND user_id = @user_id), 0)
            );
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("user_id", userId);
        return (decimal)(await command.ExecuteScalarAsync(cancellationToken) ?? 0m);
    }

    public async Task<ChatSettings> GetChatSettingsAsync(long peerId, CancellationToken cancellationToken)
    {
        const string insertSql = "INSERT INTO chat_settings (peer_id) VALUES (@peer_id) ON CONFLICT DO NOTHING;";
        await using (var insert = dataSource.CreateCommand(insertSql))
        {
            insert.Parameters.AddWithValue("peer_id", peerId);
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        const string selectSql = """
            SELECT is_active, rules, info, no_prefix FROM chat_settings WHERE peer_id = @peer_id;
            """;
        await using var select = dataSource.CreateCommand(selectSql);
        select.Parameters.AddWithValue("peer_id", peerId);
        await using var reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException($"Failed to load settings for peer {peerId}.");
        }

        return new ChatSettings(peerId, reader.GetBoolean(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3));
    }

    public async Task UpdateChatSettingsAsync(
        long peerId,
        string setting,
        string value,
        CancellationToken cancellationToken)
    {
        string column;
        object parameterValue;
        switch (setting)
        {
            case "active":
                column = "is_active";
                parameterValue = bool.Parse(value);
                break;
            case "rules":
                column = "rules";
                parameterValue = value;
                break;
            case "info":
                column = "info";
                parameterValue = value;
                break;
            case "no_prefix":
                column = "no_prefix";
                parameterValue = bool.Parse(value);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting), "Unknown chat setting.");
        }
        var sql = $"UPDATE chat_settings SET {column} = @value, updated_at = NOW() WHERE peer_id = @peer_id;";
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("value", parameterValue);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetRoleAsync(
        long peerId,
        long userId,
        decimal role,
        bool global,
        long setBy,
        CancellationToken cancellationToken)
    {
        if (global)
        {
            const string sql = """
                INSERT INTO user_profiles (user_id, global_role)
                VALUES (@user_id, @role)
                ON CONFLICT (user_id) DO UPDATE SET global_role = EXCLUDED.global_role;
                """;
            await using var command = dataSource.CreateCommand(sql);
            command.Parameters.AddWithValue("user_id", userId);
            command.Parameters.AddWithValue("role", role);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        if (role <= 0)
        {
            const string deleteSql = "DELETE FROM chat_roles WHERE peer_id = @peer_id AND user_id = @user_id;";
            await using var command = dataSource.CreateCommand(deleteSql);
            command.Parameters.AddWithValue("peer_id", peerId);
            command.Parameters.AddWithValue("user_id", userId);
            await command.ExecuteNonQueryAsync(cancellationToken);
            return;
        }

        const string upsertSql = """
            INSERT INTO chat_roles (peer_id, user_id, role, set_by)
            VALUES (@peer_id, @user_id, @role, @set_by)
            ON CONFLICT (peer_id, user_id) DO UPDATE
            SET role = EXCLUDED.role, set_by = EXCLUDED.set_by, updated_at = NOW();
            """;
        await using var upsert = dataSource.CreateCommand(upsertSql);
        upsert.Parameters.AddWithValue("peer_id", peerId);
        upsert.Parameters.AddWithValue("user_id", userId);
        upsert.Parameters.AddWithValue("role", role);
        upsert.Parameters.AddWithValue("set_by", setBy);
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task ClearAllRolesAsync(
        long peerId,
        long userId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var global = new NpgsqlCommand(
            "UPDATE user_profiles SET global_role = 0 WHERE user_id = @user_id;",
            connection,
            transaction))
        {
            global.Parameters.AddWithValue("user_id", userId);
            await global.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var local = new NpgsqlCommand(
            "DELETE FROM chat_roles WHERE peer_id = @peer_id AND user_id = @user_id;",
            connection,
            transaction))
        {
            local.Parameters.AddWithValue("peer_id", peerId);
            local.Parameters.AddWithValue("user_id", userId);
            await local.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task TransferConversationOwnerAsync(
        long peerId,
        long previousOwnerId,
        long nextOwnerId,
        long changedBy,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var removePrevious = new NpgsqlCommand(
            "DELETE FROM chat_roles WHERE peer_id = @peer_id AND user_id = @user_id;",
            connection,
            transaction))
        {
            removePrevious.Parameters.AddWithValue("peer_id", peerId);
            removePrevious.Parameters.AddWithValue("user_id", previousOwnerId);
            await removePrevious.ExecuteNonQueryAsync(cancellationToken);
        }

        const string upsertNextSql = """
            INSERT INTO chat_roles (peer_id, user_id, role, set_by)
            VALUES (@peer_id, @user_id, 6, @set_by)
            ON CONFLICT (peer_id, user_id) DO UPDATE
            SET role = 6, set_by = EXCLUDED.set_by, updated_at = NOW();
            """;
        await using (var setNext = new NpgsqlCommand(upsertNextSql, connection, transaction))
        {
            setNext.Parameters.AddWithValue("peer_id", peerId);
            setNext.Parameters.AddWithValue("user_id", nextOwnerId);
            setNext.Parameters.AddWithValue("set_by", changedBy);
            await setNext.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<int> AddModerationActionAsync(
        long peerId,
        long actorId,
        long targetId,
        string actionType,
        string reason,
        TimeSpan? duration,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        if (actionType != "warn")
        {
            const string deactivateSql = """
                UPDATE moderation_actions SET active = FALSE
                WHERE peer_id = @peer_id AND target_id = @target_id
                  AND action_type = @action_type AND active;
                """;
            await using var deactivate = new NpgsqlCommand(deactivateSql, connection, transaction);
            deactivate.Parameters.AddWithValue("peer_id", peerId);
            deactivate.Parameters.AddWithValue("target_id", targetId);
            deactivate.Parameters.AddWithValue("action_type", actionType);
            await deactivate.ExecuteNonQueryAsync(cancellationToken);
        }

        const string insertSql = """
            INSERT INTO moderation_actions (peer_id, user_id, target_id, action_type, reason, expires_at)
            VALUES (@peer_id, @user_id, @target_id, @action_type, @reason,
                    CASE WHEN @duration_seconds IS NULL THEN NULL
                         ELSE NOW() + make_interval(secs => @duration_seconds) END);
            """;
        await using (var insert = new NpgsqlCommand(insertSql, connection, transaction))
        {
            insert.Parameters.AddWithValue("peer_id", peerId);
            insert.Parameters.AddWithValue("user_id", actorId);
            insert.Parameters.AddWithValue("target_id", targetId);
            insert.Parameters.AddWithValue("action_type", actionType);
            insert.Parameters.AddWithValue("reason", reason);
            insert.Parameters.AddWithValue(
                "duration_seconds",
                duration is null ? DBNull.Value : (object)(int)Math.Ceiling(duration.Value.TotalSeconds));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        const string countSql = """
            SELECT COUNT(*)::int FROM moderation_actions
            WHERE peer_id = @peer_id AND target_id = @target_id
              AND action_type = 'warn' AND active
              AND (expires_at IS NULL OR expires_at > NOW());
            """;
        await using var count = new NpgsqlCommand(countSql, connection, transaction);
        count.Parameters.AddWithValue("peer_id", peerId);
        count.Parameters.AddWithValue("target_id", targetId);
        var result = await count.ExecuteScalarAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result is null or DBNull ? 0 : Convert.ToInt32(result);
    }

    public async Task<bool> RemoveLatestWarningAsync(
        long peerId,
        long targetId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            DELETE FROM moderation_actions
            WHERE id = (
                SELECT id FROM moderation_actions
                WHERE peer_id = @peer_id AND target_id = @target_id
                  AND action_type = 'warn' AND active
                ORDER BY created_at DESC, id DESC
                LIMIT 1
            )
            RETURNING id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("target_id", targetId);
        return await command.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task<ModerationStatus> GetModerationStatusAsync(
        long peerId,
        long userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                (SELECT COUNT(*)::int FROM moderation_actions
                 WHERE peer_id = @peer_id AND target_id = @user_id
                   AND action_type = 'warn' AND active
                   AND (expires_at IS NULL OR expires_at > NOW())),
                EXISTS (SELECT 1 FROM moderation_actions
                        WHERE peer_id = @peer_id AND target_id = @user_id
                          AND action_type = 'mute' AND active
                          AND (expires_at IS NULL OR expires_at > NOW())),
                EXISTS (SELECT 1 FROM moderation_actions
                        WHERE peer_id = @peer_id AND target_id = @user_id
                          AND action_type = 'ban' AND active
                          AND (expires_at IS NULL OR expires_at > NOW()));
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return new ModerationStatus(0, false, false);
        }

        return new ModerationStatus(reader.GetInt32(0), reader.GetBoolean(1), reader.GetBoolean(2));
    }

    public async Task<IReadOnlyList<ChatStaffMember>> GetChatStaffAsync(
        long peerId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT user_id, role FROM chat_roles WHERE peer_id = @peer_id ORDER BY role DESC, user_id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var staff = new List<ChatStaffMember>();
        while (await reader.ReadAsync(cancellationToken))
        {
            staff.Add(new ChatStaffMember(reader.GetInt64(0), reader.GetDecimal(1)));
        }

        return staff;
    }

    public async Task<RoleAssignment?> GetRoleAssignmentAsync(
        long peerId,
        long userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT
                COALESCE(profiles.global_role, 0),
                COALESCE(roles.role, 0),
                roles.set_by,
                roles.updated_at
            FROM (SELECT @user_id::bigint AS user_id) target
            LEFT JOIN user_profiles profiles ON profiles.user_id = target.user_id
            LEFT JOIN chat_roles roles
                ON roles.peer_id = @peer_id AND roles.user_id = target.user_id;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("user_id", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new RoleAssignment(
            userId,
            reader.GetDecimal(0),
            reader.GetDecimal(1),
            reader.IsDBNull(2) ? null : reader.GetInt64(2),
            reader.IsDBNull(3) ? null : reader.GetFieldValue<DateTimeOffset>(3));
    }

    public async Task<IReadOnlyList<ModerationRecord>> GetModerationListAsync(
        long peerId,
        string actionType,
        CancellationToken cancellationToken,
        int limit = 50,
        int offset = 0)
    {
        const string sql = """
            SELECT target_id, user_id, reason, created_at, expires_at
            FROM moderation_actions
            WHERE peer_id = @peer_id AND action_type = @action_type AND active
              AND (expires_at IS NULL OR expires_at > NOW())
            ORDER BY created_at DESC
            LIMIT @limit OFFSET @offset;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("action_type", actionType);
        command.Parameters.AddWithValue("limit", limit);
        command.Parameters.AddWithValue("offset", offset);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var records = new List<ModerationRecord>();
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ModerationRecord(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.GetString(2),
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? null : reader.GetFieldValue<DateTimeOffset>(4)));
        }

        return records;
    }

    public async Task<bool> HasActiveBanAsync(
        long peerId,
        long userId,
        CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (SELECT 1 FROM moderation_actions
                           WHERE peer_id = @peer_id AND target_id = @user_id
                             AND action_type = 'ban' AND active
                             AND (expires_at IS NULL OR expires_at > NOW()));
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("user_id", userId);
        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    public async Task ClearModerationActionAsync(
        long peerId,
        long targetId,
        string actionType,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE moderation_actions SET active = FALSE
            WHERE peer_id = @peer_id AND target_id = @target_id
              AND action_type = @action_type AND active;
            """;
        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("peer_id", peerId);
        command.Parameters.AddWithValue("target_id", targetId);
        command.Parameters.AddWithValue("action_type", actionType);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<SqlQueryResult> ExecuteReadOnlyQueryAsync(
        string sql,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        await using (var safety = new NpgsqlCommand("SET TRANSACTION READ ONLY", connection, transaction))
        {
            await safety.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var statementTimeout = new NpgsqlCommand(
            "SET LOCAL statement_timeout = '3s'",
            connection,
            transaction))
        {
            await statementTimeout.ExecuteNonQueryAsync(cancellationToken);
        }

        await using (var lockTimeout = new NpgsqlCommand(
            "SET LOCAL lock_timeout = '1s'",
            connection,
            transaction))
        {
            await lockTimeout.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var command = new NpgsqlCommand(sql, connection, transaction)
        {
            CommandTimeout = 4
        };
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var columns = Enumerable.Range(0, Math.Min(reader.FieldCount, 8))
            .Select(reader.GetName)
            .ToArray();
        var rows = new List<string[]>(16);
        var truncated = false;
        while (await reader.ReadAsync(cancellationToken))
        {
            if (rows.Count == 15)
            {
                truncated = true;
                break;
            }

            var values = new string[columns.Length];
            for (var column = 0; column < columns.Length; column++)
            {
                var value = reader.IsDBNull(column) ? "NULL" : Convert.ToString(reader.GetValue(column)) ?? "";
                values[column] = value.Length <= 120 ? value : value[..117] + "...";
            }

            rows.Add(values);
        }

        return new SqlQueryResult(columns, rows, truncated, reader.FieldCount > columns.Length);
    }

    public async Task VacuumAsync(bool full, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand(full ? "VACUUM (FULL)" : "VACUUM", connection)
        {
            CommandTimeout = 0
        };
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record DailyBonusReward(long Money, long Reputation, long Beer);
}

public sealed record PeerProfile(
    long MessagesCount,
    string SettingsJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    decimal GlobalRole);

public sealed record GameAccount(
    long WalletBalance,
    long BankBalance,
    long Reputation,
    long Beer,
    long Bitcoin,
    short DailyBonusDay,
    DateTimeOffset? LastDailyClaimedAt);

public sealed record DailyBonusClaimResult(
    bool IsClaimed,
    short Day,
    long Money,
    long Reputation,
    long Beer,
    DateTimeOffset? AvailableAt);

public sealed record ChatSettings(long PeerId, bool IsActive, string Rules, string Info, bool NoPrefix);

public sealed record ModerationStatus(int WarningCount, bool IsMuted, bool IsBanned);

public sealed record ChatStaffMember(long UserId, decimal Role);

public sealed record RoleAssignment(
    long UserId,
    decimal GlobalRole,
    decimal ChatRole,
    long? AssignedBy,
    DateTimeOffset? AssignedAt);

public sealed record ModerationRecord(
    long TargetId,
    long ActorId,
    string Reason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt);

public sealed record UserStatistics(
    long UserId,
    long MessagesToday,
    long MessagesThisWeek,
    long MessagesAllTime,
    int WarningCount,
    bool IsMuted,
    decimal Role,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt);

public sealed record SqlQueryResult(
    string[] Columns,
    List<string[]> Rows,
    bool RowsTruncated,
    bool ColumnsTruncated);
