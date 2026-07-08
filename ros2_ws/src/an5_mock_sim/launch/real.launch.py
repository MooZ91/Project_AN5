"""Modo REAL del AN5/FR5.

Agrupa en un solo launch los tres procesos que hoy se corrian a mano en
terminales separadas (no existia ningun launch file para esto en el
workspace -- fr_ros2 y code no tienen carpeta launch/). No se modifica
ningun archivo de fr_ros2 ni de code: este launch solo ejecuta, sin cambios,
los mismos ejecutables/nodos que ya existian.

Levanta:
  - fr_ros2 / ros2_cmd_server (C++): el driver real. Se conecta por TCP a
    192.168.58.2 (IP hardcodeada en fr_ros2/src/ROS_API.cpp y
    state_feedback.cpp) y hace exit(0) si no logra conectar -- por lo tanto
    este launch SOLO tiene sentido con el controlador real accesible en esa
    IP/red.
  - rosbridge_websocket (puerto 9090), igual que en modo sim.
  - code / publisher_subscriber: igual que en modo sim.

fr_ros2_para.yaml (src/fr_ros2/fr_ros2_para.yaml) NO esta instalado en el
share/ de fr_ros2 (su CMakeLists.txt no lo copia), asi que no se puede
referenciar de forma portable via ament_index. Sus valores por defecto ya
estan hardcodeados en ROS_API.cpp (declare_parameter) y coinciden con el
yaml, asi que el nodo funciona igual sin pasarlo explicitamente. Si
necesitas overridear parametros, pasa fr_ros2_params_file:=/ruta/al/yaml.

NO correr junto con sim.launch.py (choque de /FR_ROS_API_service y de
publishers en nonrt_state_data).
"""
from launch import LaunchDescription
from launch.actions import (
    DeclareLaunchArgument, IncludeLaunchDescription, OpaqueFunction,
)
from launch.launch_description_sources import AnyLaunchDescriptionSource
from launch.substitutions import LaunchConfiguration, PathJoinSubstitution
from launch_ros.actions import Node
from launch_ros.substitutions import FindPackageShare


def _make_ros2_cmd_server_node(context, *args, **kwargs):
    params_file = LaunchConfiguration('fr_ros2_params_file').perform(context)
    parameters = [params_file] if params_file else None
    return [Node(
        package='fr_ros2',
        executable='ros2_cmd_server',
        name='FR_ROS_API_node',
        output='screen',
        parameters=parameters,
    )]


def generate_launch_description():
    fr_ros2_params_file = DeclareLaunchArgument(
        'fr_ros2_params_file', default_value='',
        description=(
            'Opcional: ruta absoluta a un yaml de parametros para '
            'ros2_cmd_server (p.ej. src/fr_ros2/fr_ros2_para.yaml). No '
            'esta instalado en share/fr_ros2, por eso no hay un default '
            'automatico. Si se deja vacio, se usan los defaults '
            'hardcodeados en ROS_API.cpp (coinciden con el yaml de '
            'fabrica).'))

    rosbridge_launch = IncludeLaunchDescription(
        AnyLaunchDescriptionSource(
            PathJoinSubstitution([
                FindPackageShare('rosbridge_server'),
                'launch', 'rosbridge_websocket_launch.xml',
            ])
        ),
    )

    publisher_subscriber_node = Node(
        package='code',
        executable='publisher_subscriber',
        name='robot_publisher',
        output='screen',
    )

    return LaunchDescription([
        fr_ros2_params_file,
        rosbridge_launch,
        OpaqueFunction(function=_make_ros2_cmd_server_node),
        publisher_subscriber_node,
    ])
