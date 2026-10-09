CREATE TABLE IF NOT EXISTS user_profiles (
    user_id BIGINT PRIMARY KEY,
    messages_count BIGINT NOT NULL DEFAULT 0,
    settings JSONB NOT NULL DEFAULT '{}'::jsonb,
    global_role NUMERIC(4,1) NOT NULL DEFAULT 0,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    last_seen_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS global_role NUMERIC(4,1) NOT NULL DEFAULT 0;

CREATE TABLE IF NOT EXISTS chat_roles (
    peer_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    role NUMERIC(4,1) NOT NULL,
    set_by BIGINT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (peer_id, user_id)
);

CREATE TABLE IF NOT EXISTS chat_settings (
    peer_id BIGINT PRIMARY KEY,
    is_active BOOLEAN NOT NULL DEFAULT FALSE,
    rules TEXT NOT NULL DEFAULT '',
    info TEXT NOT NULL DEFAULT '',
    no_prefix BOOLEAN NOT NULL DEFAULT FALSE,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
);

CREATE TABLE IF NOT EXISTS user_daily_messages (
    user_id BIGINT NOT NULL,
    message_date DATE NOT NULL,
    messages_count BIGINT NOT NULL DEFAULT 0,
    PRIMARY KEY (user_id, message_date)
);

CREATE TABLE IF NOT EXISTS moderation_actions (
    id BIGSERIAL PRIMARY KEY,
    peer_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    target_id BIGINT NOT NULL,
    action_type TEXT NOT NULL,
    reason TEXT NOT NULL DEFAULT '',
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    expires_at TIMESTAMPTZ,
    active BOOLEAN NOT NULL DEFAULT TRUE
);

CREATE INDEX IF NOT EXISTS ix_moderation_actions_active
    ON moderation_actions (peer_id, target_id, action_type, active);
