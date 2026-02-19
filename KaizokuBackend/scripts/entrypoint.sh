#!/bin/sh
set -e

PUID=${PUID:-99}
PGID=${PGID:-100}
USERNAME=kaizoku

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
echo "Setting permissions on/config"
chown -R "$user_name:$group_name" /config
chmod -R 777 /config

# Detect architecture and add IKVM library path
ARCH=$(uname -m)
if [ "$ARCH" = "x86_64" ]; then
    IKVM_LIB_PATH="/app/ikvm/linux-x64/bin"
elif [ "$ARCH" = "aarch64" ]; then
    IKVM_LIB_PATH="/app/ikvm/linux-arm64/bin"
else
    echo "Warning: Unknown architecture $ARCH, IKVM libs may not be found"
    IKVM_LIB_PATH="/app/ikvm/linux-x64/bin"
fi

# Add IKVM library directories to LD_LIBRARY_PATH
export LD_LIBRARY_PATH="${IKVM_LIB_PATH}:${LD_LIBRARY_PATH}"

# Run the app as the correct user
exec gosu "$user_name" xvfb-run --auto-servernum /app/KaizokuBackend
