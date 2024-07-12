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
echo "Starting MCU Proxy with arguments: '$@'..."
cd "$(dirname "$0")"
cd ..
sudo chmod +x SLS4All.Compact.McuApp
./SLS4All.Compact.McuApp $@ &

echo "Waiting for exit signal..."
wait