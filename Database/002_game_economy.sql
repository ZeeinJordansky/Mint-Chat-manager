ALTER TABLE user_profiles
    ADD COLUMN IF NOT EXISTS wallet_balance BIGINT NOT NULL DEFAULT 0 CHECK (wallet_balance >= 0),
    ADD COLUMN IF NOT EXISTS bank_balance BIGINT NOT NULL DEFAULT 0 CHECK (bank_balance >= 0),
    ADD COLUMN IF NOT EXISTS reputation BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS beer BIGINT NOT NULL DEFAULT 0,
    ADD COLUMN IF NOT EXISTS btc_balance BIGINT NOT NULL DEFAULT 0 CHECK (btc_balance >= 0),
    ADD COLUMN IF NOT EXISTS daily_bonus_day SMALLINT NOT NULL DEFAULT 1,
    ADD COLUMN IF NOT EXISTS last_daily_claimed_at TIMESTAMPTZ;

CREATE INDEX IF NOT EXISTS ix_user_profiles_wallet_balance
    ON user_profiles (wallet_balance DESC);
