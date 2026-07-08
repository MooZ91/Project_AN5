#!/usr/bin/env bash
# Revierte reject_robot_xmlrpc.sh. Correr antes de conectar el robot real
# (esa regla no deberia existir cuando 192.168.58.2 es el controlador de
# verdad).
set -eu

ROBOT_IP="192.168.58.2"
ROBOT_PORT="20003"

if sudo iptables -C OUTPUT -p tcp -d "${ROBOT_IP}" --dport "${ROBOT_PORT}" \
        -j REJECT --reject-with tcp-reset 2>/dev/null; then
    sudo iptables -D OUTPUT -p tcp -d "${ROBOT_IP}" --dport "${ROBOT_PORT}" \
        -j REJECT --reject-with tcp-reset
    echo "Regla eliminada."
else
    echo "No habia regla que eliminar."
fi
