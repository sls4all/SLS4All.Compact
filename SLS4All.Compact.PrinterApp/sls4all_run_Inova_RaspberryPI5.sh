#!/bin/bash
#set -x # echo on

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

sudo_grep_tee()
{
  FILENAME=$1
  TEXT=$2
  sudo cat "$FILENAME" | grep "$TEXT"
  if [ $? -ne 0 ]; then
    echo "$TEXT" | sudo tee -a "$FILENAME"
  fi
}

trap 'kill_descendants' KILL INT TERM EXIT # just a precaution to not to keep anything running

if [ -z "$BASEURL" ]; then
    BASEURL="http://localhost"
fi
PINGURL="ping"
INDEXURL="?local=1"
if [ -z "$APPENV_NAME" ]; then
    APPENV_NAME="Inova-RaspberryPi5"
fi
if [ -z "$APPROOT_DIR" ]; then
    APPROOT_DIR="${HOME}/SLS4All"
fi
if [ -z "$SPLASH_DIR" ]; then
    export SPLASH_DIR="${HOME}"
fi
ARCHIVE_DIR="${APPROOT_DIR}/Archive" 
PREVIOUS_DIR="${APPROOT_DIR}/Previous"
PREVIOUS_CURRENT_DIR="${APPROOT_DIR}/Previous/Current"
CURRENT_DIR="${APPROOT_DIR}/Current"
STAGING_DIR="${APPROOT_DIR}/Staging"
ROLLBACK_DIR="${APPROOT_DIR}/Rollback"
UNVERIFIED_FILE="${APPROOT_DIR}/current_unverified"
LIFETIME_COMMAND_FILE="${APPROOT_DIR}/lifetime_command"
ENTRY_POINT=SLS4All.Compact.PrinterApp
SHELL_EXEC="false"
SHOW_SPLASH=false
NO_BROWSER=false
RUN_PROXY=false
ADDITIONAL_ARGS=""
PROXY_PORT=5001
LIFETIME_COMMAND=""

while getopts "d,l,s,ne:p:" flag
do
    case "${flag}" in
        d) ADDITIONAL_ARGS="$ADDITIONAL_ARGS --debug";;
        l) RUN_PROXY=true;;
        s) SHOW_SPLASH=true;;
        n) NO_BROWSER=true;;
        e) SHELL_EXEC="$OPTARG";;
        p) PROXY_PORT="$OPTARG";;
        [?]) print >&2 "Usage: $0 [-s]"
             exit 1;;
    esac
done

show_splash()
{
    if $SHOW_SPLASH; then
        (xinput disable 6; /usr/bin/bash -c "exec -a 'startup_splash' pqiv -f -t -t -i $1"; xinput enable 6; sleep infinity) &
        sleep 0.1
    fi
}

# disable screensaver, screen power saving and blanking
xset s off 2> /dev/null
xset s noblank 2> /dev/null
xset -dpms 2> /dev/null

# disable swap, swapping could block `MCU host` process which would lead to MCU shutdown.
# We have plenty other reasons swapping is very bad for the printing. We simply have to have enough RAM.
sudo swapoff -a

# ensure full FS check every reboot. This ensures the printer will work reliably.
grep fsck.mode= /boot/firmware/cmdline.txt >/dev/null
retVal=$?
if [ $retVal -eq 1 ]; then # mode missing, add
    sudo sed -i 's|fsck.repair=|fsck.mode=force fsck.repair=|g' /boot/firmware/cmdline.txt
fi

# ensure we can disable paging (NOTE: this should primarily go to install script)
sudo_grep_tee /etc/security/limits.conf "$USER    -    memlock  4294967296"

# run
while :; do
    # check if there is newly staged version
    if [ -d $STAGING_DIR ]; then
        show_splash "$SPLASH_DIR/sls4all_updating.gif"
        echo "Applying staging version..."
        mkdir -p $ARCHIVE_DIR
        if [ -d $PREVIOUS_CURRENT_DIR ]; then
            PREVIOUS_DATE_EPOCH=$(stat -c '%Y' $PREVIOUS_CURRENT_DIR)
            PREVIOUS_DATE=$(date "+%Y-%m-%d_%H-%M-%S" -d "1970-01-01 + $PREVIOUS_DATE_EPOCH secs")
            ARCHIVE_BACKUP_DIR="$ARCHIVE_DIR/$PREVIOUS_DATE"
            echo "* archiving select old previous '$PREVIOUS_CURRENT_DIR' -> '$ARCHIVE_BACKUP_DIR' ($ARCHIVE_DIR)"
            mkdir -p $ARCHIVE_BACKUP_DIR
            find $PREVIOUS_CURRENT_DIR '(' -name 'appsettings*' -o -name 'appinfo*' ')' -exec cp '{}' $ARCHIVE_BACKUP_DIR ';'
        fi
        rm -rf $PREVIOUS_DIR
        touch $UNVERIFIED_FILE
        mkdir -p $PREVIOUS_DIR
        for STAGING_SUBDIR in $STAGING_DIR/*/ ; do
            TARGET_SUBDIR="$APPROOT_DIR/$(basename $STAGING_SUBDIR)"
            if [ "$TARGET_SUBDIR" = "$STAGING_DIR" ]; then
                continue
            fi
            if [ -d $TARGET_SUBDIR ]; then
                PREVIOUS_SUBDIR="$PREVIOUS_DIR/$(basename $STAGING_SUBDIR)"
                echo "* moving '$TARGET_SUBDIR' -> '$PREVIOUS_SUBDIR'"
                mv $TARGET_SUBDIR $PREVIOUS_SUBDIR
            fi
            echo "* moving '$STAGING_SUBDIR' -> '$TARGET_SUBDIR'"
            mv $STAGING_SUBDIR $TARGET_SUBDIR
        done
        rm -rf $STAGING_DIR
        echo "Syncing file system"
        sudo sync
        sleep 1
        sudo sync
        echo "Staging version applied"
        if [[ $0 = $CURRENT_DIR/* ]]; then
            echo "Exiting with code 5 since the current script has been updated, this script is expected to be extrernally restarted"
            exit 5
        fi
    else
        show_splash "$SPLASH_DIR/sls4all_loading.gif"
        echo "Syncing file system"
        sudo sync
    fi

    if [ "$NO_BROWSER" == false ]; then
        (
            while :; do
                curl -v --silent $BASEURL/$PINGURL 2>&1 | grep PAGE_HAS_LOADED
                retVal=$?
                if [ $retVal -eq 0 ]; then # application is fully running
                    # remove unverified status from the current version
                    if [ -f $UNVERIFIED_FILE ]; then
                        echo "Application verified"
                        rm -rf $UNVERIFIED_FILE
                        sync
                    fi

                    echo "Starting browser..."
                    # start chromium kiosk
                    chromium -disable \
                        --disable-translate \
                        --disable-infobars \
                        --disable-suggestions-service \
                        --disable-save-password-bubble \
                        --enable-features=WebUIDarkMode \
                        --force-dark-mode \
                        --start-maximized \
                        --start-fullscreen \
                        --kiosk $BASEURL/$INDEXURL \
                        --js-flags="--expose-gc" \
			            --no-sandbox >/dev/null 2>&1 &
                    CHROMIUM_PID=$!
                    while :; do
                        if ps -p $CHROMIUM_PID > /dev/null; then
                            # chromium running, push splash to the top very aggressively
                            SPLASH_PID=$(pidof startup_splash)
                            retVal=$?
                            if [ $retVal -eq 0 ]; then 
                                xdotool windowactivate $(xdotool search --pid $SPLASH_PID | tail -1) >/dev/null 2>&1
                                sleep 0.1
                            else
                                wait $CHROMIUM_PID
                                echo "Browser exited"
                                exit
                            fi
                        else
                            exit
                        fi
                    done
                fi
                sleep 1
            done
        )&
    fi

    (
        while :; do
            curl -v --silent $BASEURL/$PINGURL 2>&1 | grep PAGE_HAS_RENDERED
            retVal=$?
            if [ $retVal -eq 0 ]; then # application has rendered in browser, time to kill the splash
                SPLASH_PID=$(pidof startup_splash)
                retVal=$?
                if [ $retVal -eq 0 ]; then 
                    kill $SPLASH_PID
                    break
                else
                    break
                fi
            fi
            sleep 0.5
        done
        sleep infinity
    )&

    # enable listening on ports below 1024
    # enable changing time
    sudo setcap 'cap_sys_admin,cap_net_bind_service,cap_sys_time=+eip' $CURRENT_DIR/$ENTRY_POINT
    # enable execute on aux scripts
    sudo chmod +x $CURRENT_DIR/sls4all_settime.sh

    if [ "$RUN_PROXY" == true ]; then
        (
            echo "Starting proxy..."
            cd $CURRENT_DIR
            chmod +x $ENTRY_POINT
            ./$ENTRY_POINT --environment $APPENV_NAME --Application:Proxy true --Application:Port $PROXY_PORT $ADDITIONAL_ARGS
            echo "Proxy exited"
        )&
    else
        (
            echo "Starting application..."
            cd $CURRENT_DIR
            chmod +x $ENTRY_POINT
            if [ -f $LIFETIME_COMMAND_FILE ]; then
                rm -f $LIFETIME_COMMAND_FILE
            fi
            ./$ENTRY_POINT --environment $APPENV_NAME --PrinterLifetime:CommandOutputFilename=$LIFETIME_COMMAND_FILE $ADDITIONAL_ARGS
            echo "Application exited"
        )&
    fi

    wait -n
    echo "One of subprocesses has exited"

    if [ -f $UNVERIFIED_FILE ]; then # application very probably crashed since unverified flag is still set
        if [ ! -d $STAGING_DIR ]; then # there is no new staged version
            if [ -d $PREVIOUS_DIR ]; then # there is a backup
                echo "Application rolling back, setting previous version as staging..."
                rm -rf $ROLLBACK_DIR
                mv $PREVIOUS_DIR $STAGING_DIR
                mv $CURRENT_DIR $ROLLBACK_DIR
                sync
            fi
        fi
    fi

    # kill everything
    kill_descendants
    wait

    # if there is a no new staged version, exit
    if [ ! -d $STAGING_DIR ]; then
        if [ -f $LIFETIME_COMMAND_FILE ]; then
            LIFETIME_COMMAND=$(cat $LIFETIME_COMMAND_FILE)
            case "$LIFETIME_COMMAND" in
                shutdown) 
                    echo "Initiating system shutdown per lifetime command"
                    shutdown now
                    exit 0
                    ;;
                reboot) 
                    echo "Initiating system reboot per lifetime command"
                    reboot
                    exit 0
                    ;;
                exit) 
                    echo "Exiting script per lifetime command"
                    $SHELL_EXEC
                    exit 0
                    ;;
                restart) 
                    echo "Initiating software restart per lifetime command"
                    continue
                    ;;
            esac
        fi
        exit 0
    fi
done


