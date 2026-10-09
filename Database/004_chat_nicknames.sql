CREATE TABLE IF NOT EXISTS chat_nicknames (
    peer_id BIGINT NOT NULL,
    user_id BIGINT NOT NULL,
    nickname VARCHAR(35) NOT NULL,
    set_by BIGINT NOT NULL,
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    PRIMARY KEY (peer_id, user_id)
);
