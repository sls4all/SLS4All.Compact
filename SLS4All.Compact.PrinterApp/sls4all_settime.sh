#!/bin/bash
#set -x # echo on

# disable NTP
sudo systemctl stop systemd-timesyncd

# set date
sudo date -s "@$1"