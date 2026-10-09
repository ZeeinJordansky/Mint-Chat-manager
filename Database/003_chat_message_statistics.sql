ALTER TABLE user_daily_messages
    ADD COLUMN IF NOT EXISTS peer_id BIGINT NOT NULL DEFAULT 0;

ALTER TABLE user_daily_messages
    DROP CONSTRAINT IF EXISTS user_daily_messages_pkey;

DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM pg_constraint
        WHERE conname = 'pk_user_daily_messages_by_peer'
          AND conrelid = 'user_daily_messages'::regclass
    ) THEN
        ALTER TABLE user_daily_messages
            ADD CONSTRAINT pk_user_daily_messages_by_peer
            PRIMARY KEY (peer_id, user_id, message_date);
    END IF;
END $$;

CREATE TABLE IF NOT EXISTS chat_message_totals (
    peer_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    message_count BIGINT NOT NULL DEFAULT 0 CHECK (message_count >= 0),
    last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (peer_id, user_id)
);

ALTER TABLE chat_message_totals
    ADD COLUMN IF NOT EXISTS last_message_at TIMESTAMPTZ NOT NULL DEFAULT NOW();
