#!/bin/bash

list_descendants()
{
  local children=$(ps -o pid= --ppid "$1")
  for pid in $children
  do
    list_descendants "$pid"
  done
  echo "$children"
}

kill_descendants()
{
    kill $(list_descendants $$) >/dev/null 2>&1
}

trap 'kill_descendants' KILL INT TERM EXIT # just a precaution to not to keep anything running

# kill previous instance of this script, if running
script_name=${BASH_SOURCE[0]}
for pid in $(pidof -x $script_name); do
    if [ $pid != $$ ]; then
        echo "Killing previous instance of this script ($script_name)"
        kill $pid
        sleep 5
    fi 
done

# run
ARCH=$(uname -m)
case $ARCH in
    x64)
        BIN="linux_x64.elf"
        ;;
    x86_64)
        BIN="linux_x64.elf"
        ;;
    arm)
        BIN="linux_arm.elf"
        ;;
    aarch64)
        BIN="linux_arm64.elf"
        ;;
    arm64)
        BIN="linux_arm64.elf"
        ;;
    *)
        echo "Unsupported architecture: $ARCH"
        BIN="__unsupported__"
esac

echo "Starting Klipper MCU/Host '$BIN' for architecture $ARCH..."
cd "$(dirname "$0")"
sudo chmod +x $BIN
sudo rm -f /tmp/klipper_host_mcu
sudo ./$BIN -r &
PID=$!
(
    while :; do
        if ps -p $PID > /dev/null; then
            sudo chmod 0777 /tmp/klipper_host_mcu
            if [ $? -eq 0 ]; then
                echo "Successfully set permissions."
                exit
            else
                sleep 0.1
            fi
        else
            exit
        fi
    done
)&

echo "Waiting for exit signal..."
wait