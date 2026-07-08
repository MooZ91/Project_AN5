#!/usr/bin/env bash
# Revierte enable_robot_ip_alias.sh. Correr antes de conectar el robot real.
set -eu

ROBOT_IP="192.168.58.2"

if ip addr show dev lo | grep -q "${ROBOT_IP}/32"; then
    sudo ip addr del "${ROBOT_IP}/32" dev lo
    echo "Alias ${ROBOT_IP} eliminado de la interfaz loopback (lo)."
else
    echo "No habia alias ${ROBOT_IP} en 'lo'."
fi
