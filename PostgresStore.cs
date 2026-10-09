using Npgsql;

namespace MintChatManager;

public sealed class PostgresStore(NpgsqlDataSource dataSource)
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        const string sql = """
            CREATE TABLE IF NOT EXISTS user_profiles (
                user_id BIGINT PRIMARY KEY,
                messages_count BIGINT NOT NULL DEFAULT 0,
                settings JSONB NOT NULL DEFAULT '{}'::jsonb,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );
            """;

        await using var command = dataSource.CreateCommand(sql);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordMessageAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO user_profiles (user_id, messages_count)
            VALUES (@user_id, 1)
            ON CONFLICT (user_id) DO UPDATE
            SET messages_count = user_profiles.messages_count + 1,
                last_seen_at = NOW();
            """;

        await using var command = dataSource.CreateCommand(sql);
        command.Parameters.AddWithValue("user_id", userId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PeerProfile?> GetProfileAsync(long userId, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT messages_count, settings::text, created_at
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
            reader.GetFieldValue<DateTimeOffset>(2));
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
}

public sealed record PeerProfile(long MessagesCount, string SettingsJson, DateTimeOffset CreatedAt);

public sealed record SqlQueryResult(
    string[] Columns,
    List<string[]> Rows,
    bool RowsTruncated,
    bool ColumnsTruncated);
