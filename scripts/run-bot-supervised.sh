#!/bin/sh

bot_dll=${1:-/app/MintChatManager.dll}
child_pid=

stop_bot() {
    if [ -n "$child_pid" ]; then
        kill -TERM "$child_pid" 2>/dev/null || true
        wait "$child_pid" 2>/dev/null || true
    fi
    exit 0
}

trap stop_bot INT TERM

while :; do
    timeout --signal=INT --kill-after=30s 7200s dotnet "$bot_dll" &
    child_pid=$!
    wait "$child_pid"
    status=$?
    child_pid=

    if [ "$status" -eq 0 ]; then
        exit 0
    fi

    echo "Bot process exited with status $status; restarting in 2 seconds." >&2
    sleep 2
done
