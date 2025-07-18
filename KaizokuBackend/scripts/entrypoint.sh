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
echo "LD_PRELOAD=$LD_PRELOAD"
PUID=${PUID:-99}
PGID=${PGID:-100}
USERNAME=kaizoku
#CUSTOM_TMP="/config/tmp"

cron

if getent passwd $PUID >/dev/null; then
    userdel -r "$(getent passwd "$PUID" | cut -d: -f1)"
fi
if getent group $PGID >/dev/null; then
    groupdel "$(getent group "$PGID" | cut -d: -f1)"
fi

# Create group if it doesn't exist
if ! getent group "$PGID" >/dev/null; then
    echo "Creating group $USERNAME with GID $PGID"
    groupadd -g "$PGID" "$USERNAME"
else
    echo "Group with GID $PGID already exists"
fi

# Create user if it doesn't exist
if ! getent passwd "$PUID" >/dev/null; then
    echo "Creating user $USERNAME with UID $PUID"
    useradd -u "$PUID" -g "$PGID" -d /config --no-log-init -G audio,video "$USERNAME"
else
    echo "User with UID $PUID already exists"
fi

# Fix permissions and make binary executable
echo "Setting permissions on /app/KaizokuBackend and /config"
chmod +x /app/KaizokuBackend

#mkdir -p "$CUSTOM_TMP"
#rm -rf "${CUSTOM_TMP:?}/"*

# Symlink /tmp if not already a symlink
#if [ ! -L /tmp ]; then
#    rm -rf /tmp
#    ln -s "$CUSTOM_TMP" /tmp
#fi
#export TMPDIR=/tmp

chown -R "$USERNAME:$USERNAME" /config
chmod -R 777 /config
rm -rf /tmp/*

# Execute as the user with the wrapped command as an argument to KaizokuBackend
exec gosu "$USERNAME" /app/KaizokuBackend "$command"
