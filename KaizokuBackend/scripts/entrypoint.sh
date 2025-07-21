#!/bin/sh
set -e
if command -v Xvfb >/dev/null; then
  command="xvfb-run --auto-servernum java"
  if [ -d /opt/kcef/jcef ]; then
    # if we have KCEF downloaded in the container, attempt to link it into the data directory where Suwayomi expects it
    if [ ! -d /config/Suwayomi/bin ]; then
      mkdir -p /config/Suwayomi/bin
    fi
    if [ ! -d /config/Suwayomi/bin/kcef ] && [ ! -L /config/Suwayomi/bin/kcef ]; then
      ln -s /opt/kcef/jcef /config/Suwayomi/bin/kcef
    fi
  fi
  if [ -d /config/Suwayomi/bin/kcef ] || [ -L /config/Suwayomi/bin/kcef ]; then
    # make sure all files are always executable. KCEF (and our downloader) ensure this on creation, but if the flag is lost
    # at some point, CEF will die
    chmod -R a+x /config/Suwayomi/bin/kcef 2>/dev/null || true
  fi
  export LD_PRELOAD=/config/Suwayomi/bin/kcef/libcef.so
else
  command="java"
  echo "Suwayomi built without KCEF support, not starting Xvfb"
fi
if [ -f /opt/catch_abort.so ]; then
  export LD_PRELOAD="/opt/catch_abort.so $LD_PRELOAD"
fi
if [ -f /opt/catch_abort.so ]; then
  export LD_PRELOAD="/opt/catch_abort.so $LD_PRELOAD"
fi

PUID=${PUID:-99}
PGID=${PGID:-100}
USERNAME=kaizoku

cron

# Resolve group name from PGID if it already exists
existing_group=$(getent group "$PGID" | cut -d: -f1)
if [ -z "$existing_group" ]; then
    echo "Creating group '$USERNAME' with GID $PGID"
    groupadd -g "$PGID" "$USERNAME"
    group_name="$USERNAME"
else
    echo "Group with GID $PGID already exists: $existing_group"
    group_name="$existing_group"
fi

# Resolve user name from PUID if it already exists
existing_user=$(getent passwd "$PUID" | cut -d: -f1)
if [ -z "$existing_user" ]; then
    echo "Creating user '$USERNAME' with UID $PUID"
    useradd -u "$PUID" -g "$PGID" -d /config --no-log-init -G audio,video "$USERNAME"
    user_name="$USERNAME"
else
    echo "User with UID $PUID already exists: $existing_user"
    user_name="$existing_user"
fi

# Fix permissions
echo "Setting permissions on /app/KaizokuBackend and /config"
chmod +x /app/KaizokuBackend
chown -R "$user_name:$group_name" /config
chmod -R 777 /config
rm -rf /tmp/*

# Run the app as the correct user
exec gosu "$user_name" /app/KaizokuBackend "$command"
