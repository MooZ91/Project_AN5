#!/usr/bin/env bash
# Soluciona un cuelgue real detectado en modo sim: code/publisher_subscriber.py
# tiene un timer de 20ms que llama por XML-RPC (sin timeout) a
# 192.168.58.2:20003 (IP del controlador real, hardcodeada, no la podemos
# tocar). En una red donde esa IP no existe, el connect() TCP queda
# colgado en SYN-SENT en vez de fallar rapido -- y como rclpy usa un
# executor de un solo hilo, ese timer bloqueado nunca le da lugar a la
# suscripcion de /api_command. Resultado: ningun comando enviado por
# /api_command llega nunca al servicio, aunque el servicio en si funcione
# perfecto llamado directo.
#
# Este script agrega una regla de firewall LOCAL (no toca ningun archivo
# del stack real) para que esa conexion falle al instante con "connection
# refused" en vez de colgarse ~2 minutos. Con esto /api_command vuelve a
# procesarse con normalidad en modo sim.
#
# Requiere sudo. Es reversible con disable_reject_robot_xmlrpc.sh.
set -eu

ROBOT_IP="192.168.58.2"
ROBOT_PORT="20003"

if sudo iptables -C OUTPUT -p tcp -d "${ROBOT_IP}" --dport "${ROBOT_PORT}" \
        -j REJECT --reject-with tcp-reset 2>/dev/null; then
    echo "La regla ya existia."
else
    sudo iptables -A OUTPUT -p tcp -d "${ROBOT_IP}" --dport "${ROBOT_PORT}" \
        -j REJECT --reject-with tcp-reset
    echo "Regla agregada: conexiones TCP a ${ROBOT_IP}:${ROBOT_PORT} ahora fallan al instante."
fi

echo "Para revertir: scripts/disable_reject_robot_xmlrpc.sh"
