# ros2_ws — AN5/FR5 (Fairino FR5) ROS 2 workspace

Workspace ROS 2 para el robot colaborativo AN5/FR5, con un modo de
**simulación (mock)** que permite desarrollar y probar la integración con
Unity (RosSharp) sin necesidad del brazo físico conectado. Ver
[`src/an5_mock_sim/README.md`](src/an5_mock_sim/README.md) para el detalle
de arquitectura, parámetros y troubleshooting del modo simulado.

## Requisitos

- Ubuntu 24.04 (Noble) + ROS 2 **Jazzy** — distro con la que se probó este
  workspace.
- También es compatible con ROS 2 **Humble** (Ubuntu 22.04): ningún launch
  file ni código de `an5_mock_sim` usa APIs específicas de Jazzy. Solo
  cambia el nombre del paquete apt de rosbridge (`ros-humble-rosbridge-server`
  en vez de `ros-jazzy-rosbridge-server`).
- No requiere el robot físico conectado para el modo simulado
  (`sim.launch.py`). El modo real (`real.launch.py`) sí necesita el
  controlador FR5/AN5 accesible en la red.

## Instalación

### 1. Instalar ROS 2 Jazzy (si la máquina no lo tiene)

Requiere Ubuntu 24.04 (Noble). Pasos oficiales resumidos (ver
[docs.ros.org](https://docs.ros.org/en/jazzy/Installation/Ubuntu-Install-Debs.html)
si necesitás el detalle completo):

```bash
locale  # confirmar que ya hay soporte UTF-8; si no, configurarlo:
sudo apt update && sudo apt install locales
sudo locale-gen en_US en_US.UTF-8
sudo update-locale LC_ALL=en_US.UTF-8 LANG=en_US.UTF-8
export LANG=en_US.UTF-8

sudo apt install software-properties-common
sudo add-apt-repository universe

sudo apt update && sudo apt install curl -y
export ROS_APT_SOURCE_VERSION=$(curl -s https://api.github.com/repos/ros-infrastructure/ros-apt-source/releases/latest | grep -F "tag_name" | awk -F\" '{print $4}')
curl -L -o /tmp/ros2-apt-source.deb "https://github.com/ros-infrastructure/ros-apt-source/releases/download/${ROS_APT_SOURCE_VERSION}/ros2-apt-source_${ROS_APT_SOURCE_VERSION}.$(. /etc/os-release && echo $VERSION_CODENAME)_all.deb"
sudo apt install /tmp/ros2-apt-source.deb

sudo apt update && sudo apt upgrade -y
sudo apt install ros-jazzy-desktop ros-dev-tools   # ros-dev-tools trae colcon y rosdep

echo "source /opt/ros/jazzy/setup.bash" >> ~/.bashrc
source /opt/ros/jazzy/setup.bash

sudo rosdep init
rosdep update
```

En Ubuntu 22.04/Humble es el mismo procedimiento cambiando `jazzy` por
`humble` en los paquetes y en el `source`.

### 2. Clonar y compilar el workspace

```bash
# Clonar (el repo ya es el workspace, con carpeta src/ adentro)
git clone git@github.com:MooZ91/ros2_ws.git
cd ros2_ws

# Dependencias del workspace
rosdep install --from-paths src --ignore-src -r -y
sudo apt install ros-jazzy-rosbridge-server   # cambiar "jazzy" por tu distro

# Compilar
colcon build --packages-select frhal_msgs code an5_mock_sim
# (fr_ros2 -- el driver del robot real -- solo hace falta compilarlo si vas
# a correr real.launch.py: agregalo a --packages-select)

# Source
source install/setup.bash
```

## Uso

```bash
# Modo simulado (sin robot físico)
ros2 launch an5_mock_sim sim.launch.py

# Modo real (requiere el controlador FR5/AN5 accesible en la red)
ros2 launch an5_mock_sim real.launch.py
```

Ambos levantan `rosbridge_websocket` en el puerto **9090** para que Unity
(RosSharp) se conecte sin ningún cambio de configuración entre modos. No
corras los dos launch al mismo tiempo (ver advertencia en
[`src/an5_mock_sim/README.md`](src/an5_mock_sim/README.md)).

## Docker (alternativa para macOS/Windows, o para no instalar ROS 2 en el host)

ROS 2 Jazzy no tiene binarios oficiales para macOS y en Windows requiere
WSL2; la forma mas simple de correr el modo simulado en cualquier SO con
Docker instalado es esta imagen, que compila el workspace adentro y expone
`rosbridge_websocket:9090` al host para que Unity se conecte igual que en
modo nativo.

```bash
git clone git@github.com:MooZ91/ros2_ws.git
cd ros2_ws
docker compose up --build
```

Unity apunta a `ws://localhost:9090` (o `ws://<ip-del-host>:9090` si Unity
corre en otra máquina). Para pasar argumentos del launch (ver tabla de
parámetros en
[`src/an5_mock_sim/README.md`](src/an5_mock_sim/README.md)), sobreescribir
el `command` en `docker-compose.yml` o correr directo:

```bash
docker build -t an5_mock_sim .
docker run --rm -p 9090:9090 an5_mock_sim \
    ros2 launch an5_mock_sim sim.launch.py easing:=linear
```

La imagen compila `frhal_msgs`, `code`, `fr_ros2` y `an5_mock_sim` (base
`ros:jazzy-ros-base`, sin GUI). `real.launch.py` dentro del contenedor
necesita que el controlador FR5/AN5 sea alcanzable en la red desde adentro
del contenedor (`docker run --network host` en Linux, o mapeo de red
explícito según el setup).

Probado de punta a punta (`docker compose up --build`): compila los 4
paquetes, `mock_cmd_server` arranca y publica `/joint_states`, y el
`rosbridge_websocket` responde el handshake (`101 Switching Protocols`) en
`localhost:9090` desde el host.

## Licencia

Apache License 2.0 (ver [`LICENSE`](LICENSE)) para el código propio del
workspace. `src/code/code/frcobot_description` es contenido de terceros
(Fair Innovation) también licenciado Apache 2.0, incluido tal cual.
