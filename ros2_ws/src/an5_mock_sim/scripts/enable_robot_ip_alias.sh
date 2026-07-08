#!/usr/bin/env bash
# Uso opcional/avanzado. Ver README.md, seccion "Simular la IP del robot
# para publisher_subscriber". Agrega la IP del controlador (hardcodeada en
# code/publisher_subscriber.py) como alias local, para que las llamadas
# XML-RPC de ese nodo (que no podemos modificar) lleguen al mock XML-RPC de
# an5_mock_sim en vez de intentar salir a la red real.
#
# ADVERTENCIA:
#   - Requiere sudo (modifica el estado de red del host).
#   - Si mas adelante vas a conectar el robot real en la red 192.168.58.0/24,
#     corre disable_robot_ip_alias.sh primero.
#   - Choca de puerto con el TCPServer propio de publisher_subscriber.py
#     (tambien usa el puerto 20003, bind 0.0.0.0) si corren en el mismo host.
#     Esto solo funciona limpio si ese nodo corre en otra maquina/contenedor,
#     o si aceptas que el passthrough TCP de "AppDesigner AN5" no funcione
#     durante la simulacion.
set -eu

ROBOT_IP="192.168.58.2"

if ip addr show dev lo | grep -q "${ROBOT_IP}/32"; then
    echo "El alias ${ROBOT_IP} ya existe en 'lo'."
else
    sudo ip addr add "${ROBOT_IP}/32" dev lo
    echo "Alias ${ROBOT_IP} agregado a la interfaz loopback (lo)."
fi

echo "Recorda correr mock_cmd_server con xmlrpc_mock.enabled:=true"
echo "y despues 'disable_robot_ip_alias.sh' cuando termines de simular."
