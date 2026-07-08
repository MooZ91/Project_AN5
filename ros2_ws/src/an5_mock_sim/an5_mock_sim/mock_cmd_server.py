#!/usr/bin/env python3
"""Mock del nodo ros2_cmd_server (fr_ros2) para simular el robot AN5/FR5.

Expone la MISMA interfaz ROS2 que el driver real (fr_ros2):
  - Servicio  /FR_ROS_API_service   (frhal_msgs/srv/ROSCmdInterface)
  - Topico    nonrt_state_data      (frhal_msgs/msg/FRState), 100 ms

y agrega, para consumidores nuevos (RViz/Unity), algo que el stack real
hoy NO publica en ningun lado del workspace:
  - Topico    /joint_states         (sensor_msgs/msg/JointState)

Ademas, en modo mock, se suscribe DIRECTO a 'api_command' (std_msgs/String)
y publica 'current_joint_position'/'current_cartesian_position'
(std_msgs/String, CSV), replicando lo que en el stack real hace
`robot_publisher` (code/publisher_subscriber.py). Esto es necesario porque
`robot_publisher` no se puede modificar y, en modo mock, su
timer_callback se bloquea intentando contactar por XML-RPC la IP real e
inalcanzable del robot -- eso le impide procesar '/api_command' o publicar
esos dos topics con datos utiles. El mock cubre ambos huecos sin tocar
`code/publisher_subscriber.py` (ver README).

Nombres de joints y limites tomados de:
  src/code/code/frcobot_description/urdf/fr5v6.urdf (idénticos en fr5_robot.urdf)

Gramatica de comandos replicada de fr_ros2/src/ROS_API.cpp (_selectfunc /
_ParseROSCommandData_callback): JNTPoint/CARTPoint guardan puntos con
indice 1-based; MoveJ/MoveL(JNT<idx>|CART<idx>,speed) dispara el
movimiento hacia el punto guardado. El mock NO distingue trayectoria
MoveJ vs MoveL (ambas son interpolacion suave en espacio articular): un
JNT<idx> interpola directo hacia esos angulos, y un CART<idx> primero
resuelve cinematica inversa numerica (damped least squares, ver
_inverse_kinematics) arrancando desde la posicion articular actual, y
recien despues interpola en espacio articular hacia esa solucion. Si la
IK no converge (target fuera de alcance o en una singularidad
persistente), el comando falla con un warning y no mueve el robot.

SplineStart()/SplinePTP(JNT<idx>,speed)/SplineEnd() encolan una secuencia
de puntos que se ejecutan EN ORDEN, uno despues de que termina el
anterior (como el robot real) -- no hace falta agregar delays del lado de
Unity. El mock no traza una curva spline real por todos los puntos: cada
segmento es una interpolacion recta independiente hacia el siguiente
punto de la cola.

Convencion de cmd_res usada por este mock (documentada en el .srv):
  "0" = exito, "-1" = fallo. (El servidor real, por un detalle de
  implementacion, tambien devuelve "0" cuando no reconoce el nombre de la
  funcion -- ese caso especial NO se replica aqui a proposito, para que el
  mock sea internamente consistente; ver README.)
"""
import math
import random
import re
import threading
import time
from xmlrpc.server import SimpleXMLRPCServer

import rclpy
from rclpy.node import Node
from sensor_msgs.msg import JointState
from std_msgs.msg import String
from frhal_msgs.msg import FRState
from frhal_msgs.srv import ROSCmdInterface

SERVICE_NAME = 'FR_ROS_API_service'
FRSTATE_TOPIC = 'nonrt_state_data'
JOINT_STATES_TOPIC = 'joint_states'

# Nombres exactos de los joints revolute del FR5, en el orden j1..j6
# (fuente: frcobot_description/urdf/fr5v6.urdf, tags <joint name="j1".."j6")
JOINT_NAMES = ['j1', 'j2', 'j3', 'j4', 'j5', 'j6']

# limite inferior/superior (rad) y velocidad maxima (rad/s) por joint,
# leidos de los tags <limit> del mismo urdf.
JOINT_LIMITS = [
    (-3.0543, 3.0543, 3.15),   # j1
    (-4.6251, 1.4835, 3.15),   # j2
    (-2.8274, 2.8274, 3.15),   # j3
    (-4.6251, 1.4835, 3.20),   # j4
    (-3.0543, 3.0543, 3.20),   # j5
    (-3.0543, 3.0543, 3.20),   # j6
]

# Longitudes de eslabon (m) para el chequeo grueso de alcance (esfera desde
# el origen), igual que isPoseReachable() en fr5_ik.m -- se aplica ANTES de
# correr el solver numerico, para no gastar iteraciones en algo obviamente
# imposible. A diferencia de la caja x/y/z y la exclusion de rx que traia el
# pipeline MATLAB de referencia (retiradas: esas dependen de donde esta
# montada la base del robot en el frame de mundo de ESA celda especifica, que
# no tenemos forma de saber si coincide con este proyecto -- y en la practica
# NO coincidia: rechazaba posiciones perfectamente normales de este URDF,
# como x=50mm), esta esfera es intrinseca al brazo (deriva de las longitudes
# de eslabon del mismo fr5v6.urdf), no del frame de mundo, asi que sí es
# portable sin recalibrar.
_LINK_LENGTHS_M = (0.425, 0.395, 0.109, 0.100)
_MAX_REACH_M = sum(_LINK_LENGTHS_M)
_MIN_REACH_M = _LINK_LENGTHS_M[2]

# NOTA: el pipeline MATLAB de referencia tambien restringia J4/J5 a una rama
# especifica (evitando colision/rama indeseada en ESA celda). Se probo aca y
# se saco: al igual que la caja x/y/z, rechazaba puntos legitimos de este
# proyecto (p.ej. varias trayectorias grabadas con ry~85 grados, orientacion
# casi vertical, caen fuera de esa rama para algunos puntos cercanos entre si
# pero no otros) sin tener certeza de que la restriccion aplique a esta
# celda/tarea. Si mas adelante se necesita evitar colision real, conviene
# resolverlo con datos propios de este proyecto, no reciclando el de otro.


def _target_in_safe_workspace(target_mm_deg):
    """Chequeo barato (sin resolver IK) del target CART: esfera de alcance
    grueso alrededor del origen (base del brazo). Devuelve (ok, motivo) --
    motivo solo tiene contenido si ok=False, pensado para el mensaje de
    warning del caller."""
    x, y, z = target_mm_deg[0], target_mm_deg[1], target_mm_deg[2]

    distance_mm = math.sqrt(x * x + y * y + z * z)
    distance_m = distance_mm / 1000.0
    if not (_MIN_REACH_M <= distance_m <= _MAX_REACH_M):
        return False, (
            f'distancia al origen {distance_mm:.1f}mm fuera del alcance grueso '
            f'[{_MIN_REACH_M * 1000:.0f},{_MAX_REACH_M * 1000:.0f}]mm'
        )
    return True, ''

# Cinematica directa (forward kinematics): transformacion fija (xyz metros,
# rpy rad) del <origin> de cada joint j1..j6 en el URDF, aplicada ANTES de
# la rotacion propia del joint (Rz(q), ya que el <axis> de los 6 es "0 0 1"
# en su propio frame). Fuente: mismo urdf que JOINT_LIMITS.
_JOINT_ORIGINS = [
    ((0.0, 0.0, 0.0), (0.0, 0.0, 0.0)),               # j1
    ((0.0, 0.0, 0.152), (math.pi / 2, 0.0, 0.0)),     # j2
    ((-0.425, 0.0, 0.0), (0.0, 0.0, 0.0)),            # j3
    ((-0.39501, 0.0, 0.0), (0.0, 0.0, 0.0)),          # j4
    ((0.0, 0.0, 0.1021), (math.pi / 2, 0.0, 0.0)),    # j5
    ((0.0, 0.0, 0.102), (-math.pi / 2, 0.0, 0.0)),    # j6
]
# <origin> del joint fijo "tool" (j6_Link -> tool_Link), mismo urdf.
_TOOL_ORIGIN = ((0.0, 0.0, 0.1), (0.0, 0.0, 0.0))


def _rotz(theta):
    c, s = math.cos(theta), math.sin(theta)
    return ((c, -s, 0.0), (s, c, 0.0), (0.0, 0.0, 1.0))


def _rpy_matrix(roll, pitch, yaw):
    # Convencion URDF/ROS: R = Rz(yaw) * Ry(pitch) * Rx(roll), ejes fijos
    # del frame padre (ver http://wiki.ros.org/urdf/XML/joint, <origin rpy>).
    cr, sr = math.cos(roll), math.sin(roll)
    cp, sp = math.cos(pitch), math.sin(pitch)
    cy, sy = math.cos(yaw), math.sin(yaw)
    return (
        (cy * cp, cy * sp * sr - sy * cr, cy * sp * cr + sy * sr),
        (sy * cp, sy * sp * sr + cy * cr, sy * sp * cr - cy * sr),
        (-sp, cp * sr, cp * cr),
    )


def _compose(r1, t1, r2, t2):
    """Compone dos transformaciones homogeneas (R,t): resultado = T1 * T2."""
    r = tuple(
        tuple(sum(r1[i][k] * r2[k][j] for k in range(3)) for j in range(3))
        for i in range(3)
    )
    t = tuple(t1[i] + sum(r1[i][k] * t2[k] for k in range(3)) for i in range(3))
    return r, t


def _fk_pose(joint_rad):
    """Pose homogenea (R, t) del tool (rad, metros) dados los 6 joints (rad).

    Encadena _JOINT_ORIGINS/_TOOL_ORIGIN como transformaciones homogeneas
    (sin numpy, para no agregar una dependencia nueva al paquete). Base de
    _forward_kinematics_mm_deg y de _inverse_kinematics (que la usa dentro
    de una iteracion numerica en vez de derivar una IK analitica).
    """
    r = ((1.0, 0.0, 0.0), (0.0, 1.0, 0.0), (0.0, 0.0, 1.0))
    t = (0.0, 0.0, 0.0)
    for (o_xyz, o_rpy), q in zip(_JOINT_ORIGINS, joint_rad):
        r, t = _compose(r, t, _rpy_matrix(*o_rpy), o_xyz)
        r, t = _compose(r, t, _rotz(q), (0.0, 0.0, 0.0))
    r, t = _compose(r, t, _rpy_matrix(*_TOOL_ORIGIN[1]), _TOOL_ORIGIN[0])
    return r, t


def _rot_to_rpy(r):
    """Extrae roll,pitch,yaw (rad) de una matriz de rotacion R, con la
    misma convencion de _rpy_matrix (R = Rz(yaw) * Ry(pitch) * Rx(roll))."""
    pitch = math.asin(max(-1.0, min(1.0, -r[2][0])))
    cp = math.cos(pitch)
    if abs(cp) > 1e-6:
        roll = math.atan2(r[2][1], r[2][2])
        yaw = math.atan2(r[1][0], r[0][0])
    else:
        # Gimbal lock (pitch = +-90 deg): roll y yaw quedan acoplados: se
        # asigna todo el giro a yaw y roll=0, una convencion arbitraria pero
        # estable (evita NaN/atan2(0,0)).
        roll = 0.0
        yaw = math.atan2(-r[0][1], r[1][1])
    return roll, pitch, yaw


def _forward_kinematics_mm_deg(joint_rad):
    """Pose cartesiana del tool (mm, grados RPY) dados los 6 joints (rad)."""
    r, t = _fk_pose(joint_rad)
    roll, pitch, yaw = _rot_to_rpy(r)
    return (
        t[0] * 1000.0, t[1] * 1000.0, t[2] * 1000.0,
        math.degrees(roll), math.degrees(pitch), math.degrees(yaw),
    )


def _mat3_transpose(r):
    return tuple(tuple(r[j][i] for j in range(3)) for i in range(3))


def _mat3_mul(a, b):
    return tuple(
        tuple(sum(a[i][k] * b[k][j] for k in range(3)) for j in range(3))
        for i in range(3)
    )


def _axis_angle_from_R(r):
    """Vector rotacion (eje*angulo, rad) de una matriz de rotacion R, usado
    como error de orientacion dentro de _inverse_kinematics. Cerca de
    angulo 0 (R ~ identidad, el caso normal a medida que la IK converge)
    usa la aproximacion de angulo pequeno para evitar dividir por
    sin(angle) ~ 0; el otro caso degenerado (angulo ~ 180 grados) es
    igual de inestable pero irrelevante en la practica: significaria que
    la orientacion actual esta invertida respecto del target, algo que no
    ocurre partiendo de una pose articular valida del brazo."""
    cos_angle = max(-1.0, min(1.0, (r[0][0] + r[1][1] + r[2][2] - 1.0) / 2.0))
    angle = math.acos(cos_angle)
    rx = r[2][1] - r[1][2]
    ry = r[0][2] - r[2][0]
    rz = r[1][0] - r[0][1]
    sin_angle = math.sin(angle)
    scale = angle / (2.0 * sin_angle) if sin_angle > 1e-6 else 0.5
    return (rx * scale, ry * scale, rz * scale)


def _mat_mul(a, b):
    """Producto de matrices genericas (listas de listas): a es m x k, b es k x n."""
    m, k, n = len(a), len(b), len(b[0])
    return [[sum(a[i][p] * b[p][j] for p in range(k)) for j in range(n)] for i in range(m)]


def _mat_transpose(a):
    return [[a[i][j] for i in range(len(a))] for j in range(len(a[0]))]


def _solve_linear(a, b):
    """Resuelve A x = b (A cuadrada n x n) por eliminacion Gaussiana con
    pivoteo parcial, sin numpy. Devuelve None si A resulta singular (no
    deberia pasar con el termino de amortiguacion que le suma
    _inverse_kinematics a la diagonal antes de llamar esta funcion)."""
    n = len(a)
    m = [row[:] + [b[i]] for i, row in enumerate(a)]
    for col in range(n):
        pivot = max(range(col, n), key=lambda row: abs(m[row][col]))
        if abs(m[pivot][col]) < 1e-12:
            return None
        m[col], m[pivot] = m[pivot], m[col]
        pv = m[col][col]
        m[col] = [v / pv for v in m[col]]
        for row in range(n):
            if row != col:
                factor = m[row][col]
                m[row] = [m[row][c] - factor * m[col][c] for c in range(n + 1)]
    return [row[n] for row in m]


def _jacobian(q, eps=1e-6):
    """Jacobiano geometrico 6x6 (numerico, por diferencias finitas) de la
    pose del tool respecto de los 6 angulos articulares. Se recalcula por
    diferencias finitas en vez de derivarlo analiticamente a mano de la
    cadena _JOINT_ORIGINS: con 6 joints y una sola resolucion de IK por
    comando MoveL/SplinePTP (no por frame de render), el costo extra es
    insignificante y evita el riesgo de un jacobiano analitico mal
    derivado para esta geometria especifica del FR5/AN5."""
    r0, t0 = _fk_pose(q)
    r0t = _mat3_transpose(r0)
    cols = []
    for j in range(6):
        q2 = list(q)
        q2[j] += eps
        r2, t2 = _fk_pose(q2)
        dt = [(t2[k] - t0[k]) / eps for k in range(3)]
        dr = [v / eps for v in _axis_angle_from_R(_mat3_mul(r2, r0t))]
        cols.append(dt + dr)
    return [[cols[j][i] for j in range(6)] for i in range(6)]


def _err_norm(e):
    return math.sqrt(sum(v * v for v in e))


def _ik_pose_error(target_t, target_r, q):
    r, t = _fk_pose(q)
    e_pos = [target_t[k] - t[k] for k in range(3)]
    e_rot = list(_axis_angle_from_R(_mat3_mul(target_r, _mat3_transpose(r))))
    return e_pos + e_rot


def _ik_converged(e):
    return max(abs(v) for v in e[:3]) < _IK_TOL_POS_M and max(abs(v) for v in e[3:]) < _IK_TOL_ROT_RAD


_IK_TOL_POS_M = 1e-4      # 0.1 mm
_IK_TOL_ROT_RAD = 1e-3    # ~0.06 grados
_IK_MAX_ITERS = 200
_IK_MAX_STEP = 0.5        # rad/iteracion, evita overshoot cerca de singularidades


def _ik_from_seed(target_t, target_r, q0_rad):
    """Un intento de Levenberg-Marquardt (damped least squares con
    amortiguacion adaptativa) arrancando desde q0_rad. lambda baja cuando
    un paso mejora el error y sube cuando lo empeora, en vez de usar una
    amortiguacion fija: eso agranda mucho la cuenca de convergencia
    respecto de DLS con lambda constante, a costa de mas iteraciones por
    intento (irrelevante: la IK corre una vez por comando MoveL/SplinePTP,
    no por frame)."""
    lam = 0.01
    q = list(q0_rad)
    e = _ik_pose_error(target_t, target_r, q)
    for _ in range(_IK_MAX_ITERS):
        if _ik_converged(e):
            return q, True

        j = _jacobian(q)
        jt = _mat_transpose(j)
        jjt = _mat_mul(j, jt)
        improved = False
        for _sub in range(10):
            a = [row[:] for row in jjt]
            for i in range(6):
                a[i][i] += lam
            y = _solve_linear(a, e)
            if y is None:
                lam *= 10.0
                continue
            dq = [row[0] for row in _mat_mul(jt, [[v] for v in y])]
            step_norm = max(abs(v) for v in dq)
            if step_norm > _IK_MAX_STEP:
                dq = [v * (_IK_MAX_STEP / step_norm) for v in dq]
            q_new = [
                min(max(q[i] + dq[i], JOINT_LIMITS[i][0]), JOINT_LIMITS[i][1])
                for i in range(6)
            ]
            e_new = _ik_pose_error(target_t, target_r, q_new)
            if _err_norm(e_new) < _err_norm(e):
                q, e = q_new, e_new
                lam = max(lam / 3.0, 1e-7)
                improved = True
                break
            lam *= 5.0
        if not improved:
            break
    return q, _ik_converged(e)


# Semilla "segura" de respaldo (grados), tomada de fr5_ik.m: una pose de
# codo/muneca alejada de singularidades usada ahi como punto de partida FIJO
# en cada llamada (a diferencia de nuestra continuidad por q0_rad). Se prueba
# como segundo intento, antes de caer a semillas aleatorias, porque es una
# eleccion con criterio (no al azar) que en el pipeline de referencia resulto
# confiable.
_MATLAB_SEED_DEG = (0.0, -90.0, 90.0, -90.0, -90.0, 90.0)


def _inverse_kinematics(target_mm_deg, q0_rad):
    """Cinematica inversa numerica (Levenberg-Marquardt, sin numpy) para
    CARTPoint: busca los 6 angulos articulares (rad) cuya FK coincide con
    target_mm_deg (mm, RPY grados -- misma convencion que
    _forward_kinematics_mm_deg y que los valores guardados por CARTPoint).

    Antes de resolver nada, valida el target contra _target_in_safe_workspace
    (esfera de alcance grueso, intrinseca al brazo) -- un rechazo aca no
    gasta ninguna iteracion del solver.

    Si pasa esa validacion, prueba semillas en este orden hasta que UNA
    converge: continuidad (q0_rad, la posicion articular actual o el ultimo
    punto encolado para SplinePTP), la pose fija _MATLAB_SEED_DEG, y por
    ultimo hasta 25 semillas aleatorias dentro de los limites articulares.
    25 en vez de 6 (valor anterior): se verifico empiricamente que algunos
    targets alcanzables solo convergen para una fraccion chica de semillas
    aleatorias (~9% en un caso real), asi que 6 intentos los perdia la
    mayoria de las veces (~56% de fallo) aunque el punto SI tuviera
    solucion -- con 25 esa probabilidad de fallo baja a un digito. El costo
    es un peor caso de bloqueo del hilo del executor de rclpy (que en este
    nodo tambien corre los timers de _tick_joint_states/_tick_frstate) de
    hasta ~10-15s en vez de ~4s, pagado solo en el caso raro de que
    continuidad Y la semilla fija fallen ambas.

    No se filtra por rama de codo/muneca (el pipeline MATLAB de referencia
    lo hacia para evitar colision en ESA celda; se probo aca y se saco
    porque rechazaba puntos legitimos de este proyecto sin certeza de que
    la restriccion aplicara -- ver el comentario junto a _target_in_safe_workspace).

    Devuelve (q_rad, ok, motivo). motivo solo tiene contenido si ok=False:
    el rechazo de _target_in_safe_workspace, o el generico de no
    convergencia si ninguna semilla convergio.
    """
    ok_ws, reason = _target_in_safe_workspace(target_mm_deg)
    if not ok_ws:
        return list(q0_rad), False, reason

    target_t = [v / 1000.0 for v in target_mm_deg[0:3]]
    target_r = _rpy_matrix(*(math.radians(v) for v in target_mm_deg[3:6]))

    seeds = [list(q0_rad), [math.radians(d) for d in _MATLAB_SEED_DEG]]
    rng = random.Random(0)
    for _ in range(25):
        seeds.append([rng.uniform(lo, hi) for (lo, hi, _speed) in JOINT_LIMITS])

    last_q = list(q0_rad)
    for seed in seeds:
        q, ok = _ik_from_seed(target_t, target_r, seed)
        if ok:
            return q, True, ''
        last_q = q

    return last_q, False, (
        'la cinematica inversa no convergio (target probablemente fuera de '
        'alcance del brazo o en una singularidad persistente)'
    )

# Lista completa de comandos "de configuracion" del driver real que no
# afectan la posicion articular (fuente: ROS_API.cpp::_selectfunc). El mock
# los acepta y responde exito inmediato, sin efecto, para no romper el
# flujo de quien los llama (publisher_subscriber, apps externas, etc).
NOOP_COMMANDS = {
    'DragTeachSwitch', 'RobotEnable', 'SetSpeed', 'Mode', 'SetToolCoord',
    'SetToolList', 'SetExToolCoord', 'SetExToolList', 'SetWObjCoord',
    'SetWObjList', 'SetLoadWeight', 'SetLoadCoord', 'SetRobotInstallPos',
    'SetRobotInstallAngle', 'SetAnticollision', 'SetCollisionStrategy',
    'SetLimitPositive', 'SetLimitNegative', 'ResetAllError',
    'FrictionCompensationOnOff', 'SetFrictionValue_level',
    'SetFrictionValue_wall', 'SetFrictionValue_ceiling',
    'SetFrictionValue_freedom', 'ActGripper', 'MoveGripper', 'SetDO',
    'SetToolDO', 'SetAO', 'SetToolAO', 'StartJOG', 'StopJOG', 'ImmStopJOG',
    'MoveC', 'Circle', 'ServoJTStart', 'ServoJT', 'ServoJTEnd',
    'NewSplineStart', 'NewSplinePoint', 'NewSplineEnd',
    'PointsOffsetEnable', 'PointsOffsetDisable', 'ProgramRun',
    # NOTA: 'SplineStart'/'SplinePTP'/'SplineEnd' NO estan aca -- Unity los
    # usa para mover el robot en su modo "sin delay" (ControlArticular.cs),
    # encolando varios puntos que deben ejecutarse EN ORDEN (ver
    # _spline_start/_spline_ptp/_spline_end). Tratarlos como no-op hacia
    # que el mock respondiera cmd_res=0 sin mover nada.
}

RESULT_OK = '0'
RESULT_FAIL = '-1'


def _smoothstep(t: float) -> float:
    return t * t * (3.0 - 2.0 * t)


# _ease_in/_ease_out: version "de un solo lado" de _smoothstep, para segmentos
# intermedios de una cadena de SplinePTP (ver _start_move_to_target_locked).
# Un punto de una curva fina (muchos puntos seguidos) no debe frenar a
# velocidad cero en cada punto intermedio -- solo el PRIMER segmento de la
# cadena arranca desde reposo (ease_in) y solo el ULTIMO frena hasta reposo
# (ease_out); todo lo del medio corre a velocidad continua (linear, sin
# ease). Antes _smoothstep se aplicaba a CADA segmento por separado, con
# velocidad cero en ambos extremos -- eso es lo que se veia como "saltos"
# en vez de una curva fluida al ejecutar trayectorias con muchos puntos.
def _ease_in(t: float) -> float:
    return t * t


def _ease_out(t: float) -> float:
    return 1.0 - (1.0 - t) * (1.0 - t)


class MockCmdServer(Node):

    def __init__(self):
        super().__init__('mock_cmd_server')

        self.declare_parameter('joint_states_rate_hz', 50.0)
        self.declare_parameter('frstate_rate_hz', 10.0)
        self.declare_parameter('easing', 'ease_in_out')  # 'ease_in_out'|'linear'
        self.declare_parameter('min_move_duration', 0.2)
        self.declare_parameter('max_move_duration', 8.0)
        self.declare_parameter('startup_conflict_check', True)
        self.declare_parameter('xmlrpc_mock.enabled', False)
        self.declare_parameter('xmlrpc_mock.host', '0.0.0.0')
        self.declare_parameter('xmlrpc_mock.port', 20003)
        # Pose articular de arranque, en grados, como CSV "j1,j2,j3,j4,j5,j6"
        # (mismo formato que la gramatica de comandos JNTPoint/CARTPoint).
        # Vacio (default) = arranca en cero como antes.
        self.declare_parameter('initial_joint_positions_deg', '')

        self._easing = self.get_parameter('easing').value
        self._min_dur = float(self.get_parameter('min_move_duration').value)
        self._max_dur = float(self.get_parameter('max_move_duration').value)

        self._lock = threading.Lock()
        initial_jnt_rad = self._parse_initial_joint_positions()
        self._current_jnt = list(initial_jnt_rad)   # rad, fuente de verdad
        self._move_from = list(initial_jnt_rad)
        self._move_target = list(initial_jnt_rad)
        self._move_active = False
        self._move_start = self.get_clock().now()
        self._move_duration = 0.0
        # Si el movimiento EN CURSO debe frenar suave en su propio arranque/
        # final, o si en cambio empalma con otro segmento de la misma cadena
        # de spline (ver _start_move_to_target_locked / _tick_joint_states).
        self._move_ease_in = True
        self._move_ease_out = True
        # True mientras el ultimo segmento arrancado pertenece a una cadena de
        # SplinePTP que todavia tiene mas puntos por venir (su propio ease_out
        # fue False) -- consultado al arrancar el PROXIMO segmento para saber
        # si debe (no) frenar de entrada. Se limpia solo cuando un segmento
        # arranca con ease_out=True (el ultimo de la cadena, o un MoveJ/MoveL
        # suelto), que es cuando el brazo realmente vuelve a quedar en reposo.
        self._spline_chain_active = False

        self._jnt_points = []   # list[list[float,6]] en grados, 1-based via idx-1
        self._cart_points = []  # list[list[float,6]] (no usado para mover, solo GET)

        # Cola de trayectoria para SplineStart/SplinePTP/SplineEnd: cada
        # elemento es (target_deg_snapshot, speed_pct). Se consume de a uno
        # por vez desde _tick_joint_states, arrancando el siguiente recien
        # cuando termina el movimiento anterior -- asi los puntos se
        # ejecutan EN ORDEN, como en el robot real, en vez de todos a la
        # vez (que era el bug: cada SplinePTP pisaba el _move_target del
        # anterior porque SplinePTP estaba mapeado 1 a 1 con MoveJ).
        self._spline_queue = []

        if bool(self.get_parameter('startup_conflict_check').value):
            self._startup_conflict_check()

        self._cmd_service = self.create_service(
            ROSCmdInterface, SERVICE_NAME, self._on_cmd)
        self._api_command_sub = self.create_subscription(
            String, 'api_command', self._on_api_command, 10)

        self._joint_pub = self.create_publisher(JointState, JOINT_STATES_TOPIC, 10)
        self._frstate_pub = self.create_publisher(FRState, FRSTATE_TOPIC, 10)
        # Unity (RosSharp) consume estos dos topics como texto CSV, no
        # /joint_states -- ver JointPositionSubscriber.cs /
        # CartesianPositionSubscriber.cs en el proyecto Unity. En modo real
        # los llena publisher_subscriber.py via XML-RPC al robot; en modo
        # mock ese XML-RPC nunca responde (IP inalcanzable), asi que los
        # publicamos aca directamente con el estado interno del mock.
        self._cur_jnt_str_pub = self.create_publisher(String, 'current_joint_position', 10)
        self._cur_cart_str_pub = self.create_publisher(String, 'current_cartesian_position', 10)
        # Setpoint cartesiano: pose objetivo del movimiento en curso (o la
        # posicion actual si no hay ninguno), para que Unity pueda graficar
        # setpoint vs real y visualizar el error de seguimiento de trayectoria.
        self._setpoint_cart_str_pub = self.create_publisher(String, 'setpoint_cartesian_position', 10)

        # input_cartesian_position/output_joint_position ya NO los sirve el
        # mock: matlab_ik_node (inverse_kinematics.m) se suscribe/publica en
        # los mismos topics, y con los dos nodos activos a la vez Unity
        # recibia ambas respuestas sin poder distinguir el origen -- en la
        # practica siempre ganaba el mock (numericamente mas simple, responde
        # antes) mientras el resultado de MATLAB llegaba tarde y quedaba
        # huerfano (se veia como "recibio resultado de IK sin haberlo
        # solicitado" del lado de Unity). Ahora que MATLAB esta disponible es
        # el unico que debe responder en ese topic.

        js_period = 1.0 / float(self.get_parameter('joint_states_rate_hz').value)
        fr_period = 1.0 / float(self.get_parameter('frstate_rate_hz').value)
        self.create_timer(js_period, self._tick_joint_states)
        self.create_timer(fr_period, self._tick_frstate)

        self._xmlrpc_server = None
        if bool(self.get_parameter('xmlrpc_mock.enabled').value):
            self._start_xmlrpc_mock()

        # segunda pasada del chequeo de conflicto, ahora que ya creamos
        # nuestros propios publishers (para poder filtrarlos y no
        # auto-reportarnos como conflicto). Timer de disparo unico.
        if bool(self.get_parameter('startup_conflict_check').value):
            self._post_check_timer = self.create_timer(
                1.5, self._post_startup_topic_check)

        self.get_logger().info(
            f"an5_mock_sim listo. Service='{SERVICE_NAME}' "
            f"Topics='/{JOINT_STATES_TOPIC}' (50Hz por defecto), "
            f"'{FRSTATE_TOPIC}' (paridad con ros2_cmd_server real) y "
            "suscripcion directa a 'api_command' (comandos de Unity, sin "
            "pasar por robot_publisher)."
        )

    def _parse_initial_joint_positions(self):
        """Lee 'initial_joint_positions_deg' (CSV en grados) y devuelve 6
        valores en radianes, clampeados a JOINT_LIMITS. Si el parametro esta
        vacio o es invalido, devuelve ceros (comportamiento previo)."""
        raw = str(self.get_parameter('initial_joint_positions_deg').value).strip()
        if not raw:
            return [0.0] * 6
        try:
            deg = [float(v) for v in raw.split(',')]
        except ValueError:
            self.get_logger().warning(
                f"initial_joint_positions_deg='{raw}' invalido (se esperan "
                "6 numeros separados por coma). Arrancando en cero.")
            return [0.0] * 6
        if len(deg) != 6:
            self.get_logger().warning(
                f"initial_joint_positions_deg='{raw}' tiene {len(deg)} "
                "valores, se esperan 6. Arrancando en cero.")
            return [0.0] * 6
        clamped_rad = [
            min(max(math.radians(d), JOINT_LIMITS[i][0]), JOINT_LIMITS[i][1])
            for i, d in enumerate(deg)
        ]
        if clamped_rad != [math.radians(d) for d in deg]:
            self.get_logger().warning(
                f"initial_joint_positions_deg='{raw}' fuera de "
                "JOINT_LIMITS en algun joint, se clampeo al limite.")
        return clamped_rad

    # ------------------------------------------------------------------
    # Chequeo de conflicto con el modo REAL
    # ------------------------------------------------------------------
    def _wait_graph(self, timeout_sec):
        end = time.time() + timeout_sec
        while time.time() < end:
            rclpy.spin_once(self, timeout_sec=0.1)

    def _startup_conflict_check(self):
        # OJO: get_service_names_and_types() lista un servicio si existe
        # CUALQUIER endpoint (cliente o servidor) -- publisher_subscriber ya
        # tiene un cliente de /FR_ROS_API_service esperando, asi que esa API
        # da falso positivo siempre. Para detectar un SERVIDOR real hay que
        # mirar nodo por nodo con get_service_names_and_types_by_node().
        self._wait_graph(1.0)
        full_name = '/' + SERVICE_NAME
        my_name, my_ns = self.get_name(), self.get_namespace()
        servers_found = []
        for node_name, node_ns in self.get_node_names_and_namespaces():
            if node_name == my_name and node_ns == my_ns:
                continue
            try:
                services = self.get_service_names_and_types_by_node(node_name, node_ns)
            except Exception:
                continue
            if any(name == full_name for name, _types in services):
                servers_found.append(f"{node_ns.rstrip('/')}/{node_name}")
        if servers_found:
            names = ', '.join(servers_found)
            self.get_logger().warn(
                '=' * 70 + '\n'
                f"POSIBLE CONFLICTO: ya hay un SERVIDOR de '{full_name}' "
                f'ofrecido por: {names} (probablemente ros2_cmd_server del '
                'modo REAL). Si arrancas mock_cmd_server junto al modo real, '
                'las llamadas al servicio quedan repartidas de forma '
                'AMBIGUA entre ambos servidores. NO corras sim.launch.py y '
                'real.launch.py al mismo tiempo.\n' + '=' * 70
            )

    def _post_startup_topic_check(self):
        self._post_check_timer.cancel()
        for topic in ('/' + JOINT_STATES_TOPIC, '/' + FRSTATE_TOPIC):
            infos = self.get_publishers_info_by_topic(topic)
            others = [i for i in infos if i.node_name != self.get_name()]
            if others:
                names = ', '.join(f'{i.node_namespace}/{i.node_name}' for i in others)
                self.get_logger().warn(
                    '=' * 70 + '\n'
                    f"POSIBLE CONFLICTO: hay publisher(es) externos en '{topic}' "
                    f'({names}). Verifica que no sea el modo REAL corriendo en '
                    'paralelo.\n' + '=' * 70
                )

    # ------------------------------------------------------------------
    # Servicio /FR_ROS_API_service
    # ------------------------------------------------------------------
    def _on_cmd(self, request, response):
        response.cmd_res = self._process_command_str(request.cmd_str)
        return response

    def _on_api_command(self, msg):
        # Unity (Ros2CommandSender.cs) publica comandos JNTPoint/MoveJ/...
        # directo en '/api_command', el mismo tópico que en el stack real
        # reenvía `robot_publisher` al servicio. En modo mock, `robot_publisher`
        # se bloquea intentando contactar por XML-RPC la IP real e inalcanzable
        # del robot (ver README), asi que nunca llega a reenviar nada. Para no
        # depender de ese puente roto, el mock procesa el comando el mismo,
        # aca, con la misma logica que expone por el servicio. No hay canal de
        # respuesta hacia Unity en este camino (es un tópico, no un servicio) --
        # igual que en el stack real, donde `robot_publisher` tampoco reenvia
        # la respuesta a quien publico en '/api_command'.
        cmd_res = self._process_command_str(msg.data)
        self.get_logger().info(f"/api_command: '{msg.data}' -> cmd_res={cmd_res}")

    def _process_command_str(self, cmd_str: str) -> str:
        m = re.match(r'^([A-Za-z_]+)\((.*)\)$', cmd_str)
        if not m:
            self.get_logger().error(
                f"Formato de comando invalido: '{cmd_str}' "
                "(se espera NombreFuncion(parametros)).")
            return RESULT_FAIL

        func_name, para_list = m.group(1), m.group(2)
        if not re.match(r'^[A-Za-z0-9.\-,]*$', para_list):
            self.get_logger().error(
                f"Parametros invalidos en '{cmd_str}': solo se permiten "
                "letras, numeros, punto, coma y signo menos.")
            return RESULT_FAIL

        if func_name == 'GET':
            return self._get_variable(para_list)
        elif func_name == 'JNTPoint':
            return self._def_jnt_position(para_list)
        elif func_name == 'CARTPoint':
            return self._def_cart_position(para_list)
        elif func_name in ('MoveJ', 'MoveL'):
            return self._move(para_list, func_name)
        elif func_name == 'SplineStart':
            return self._spline_start()
        elif func_name == 'SplinePTP':
            return self._spline_ptp(para_list)
        elif func_name == 'SplineEnd':
            return self._spline_end()
        elif func_name == 'StopMotion':
            return self._stop_motion()
        elif func_name in NOOP_COMMANDS:
            self.get_logger().debug(f'{func_name}: aceptado (no-op en mock).')
            return RESULT_OK
        else:
            self.get_logger().warn(f"Comando no reconocido por el mock: '{func_name}'")
            return RESULT_FAIL

    def _def_jnt_position(self, para: str) -> str:
        parts = para.split(',')
        if len(parts) != 7:
            self.get_logger().error(
                'JNTPoint requiere 7 parametros (indice + 6 joints), '
                f'se recibieron {len(parts)}.')
            return RESULT_FAIL
        try:
            idx = int(parts[0])
            values = [float(v) for v in parts[1:]]
        except ValueError:
            self.get_logger().error(f"JNTPoint: parametros no numericos en '{para}'.")
            return RESULT_FAIL
        with self._lock:
            if idx <= 0 or idx > len(self._jnt_points) + 1:
                self.get_logger().error(f'JNTPoint: indice {idx} fuera de rango.')
                return RESULT_FAIL
            if idx <= len(self._jnt_points):
                self._jnt_points[idx - 1] = values
            else:
                self._jnt_points.append(values)
        return RESULT_OK

    def _def_cart_position(self, para: str) -> str:
        parts = para.split(',')
        if len(parts) != 7:
            self.get_logger().error(
                'CARTPoint requiere 7 parametros (indice + x,y,z,rx,ry,rz), '
                f'se recibieron {len(parts)}.')
            return RESULT_FAIL
        try:
            idx = int(parts[0])
            values = [float(v) for v in parts[1:]]
        except ValueError:
            self.get_logger().error(f"CARTPoint: parametros no numericos en '{para}'.")
            return RESULT_FAIL
        with self._lock:
            if idx <= 0 or idx > len(self._cart_points) + 1:
                self.get_logger().error(f'CARTPoint: indice {idx} fuera de rango.')
                return RESULT_FAIL
            if idx <= len(self._cart_points):
                self._cart_points[idx - 1] = values
            else:
                self._cart_points.append(values)
        return RESULT_OK

    def _get_variable(self, para: str) -> str:
        m = re.match(r'^([A-Z]+),(\d+)$', para)
        if not m:
            self.get_logger().error(f"GET: formato invalido '{para}'.")
            return RESULT_FAIL
        kind, idx_s = m.group(1), m.group(2)
        idx = int(idx_s)
        with self._lock:
            if kind == 'JNT':
                pts = self._jnt_points
            elif kind == 'CART':
                pts = self._cart_points
            else:
                self.get_logger().error(f"GET: tipo desconocido '{kind}'.")
                return RESULT_FAIL
            if idx <= 0 or idx > len(pts):
                self.get_logger().error(f'GET {kind}: indice {idx} fuera de rango.')
                return RESULT_FAIL
            return ','.join(str(v) for v in pts[idx - 1])

    def _move(self, para: str, func_name: str) -> str:
        parts = para.split(',')
        if len(parts) < 2:
            self.get_logger().error(f'{func_name}: se esperaba "JNT<idx>,speed".')
            return RESULT_FAIL
        head, speed_s = parts[0], parts[1]
        m = re.match(r'^(JNT|CART)(\d+)$', head)
        if not m:
            self.get_logger().error(f"{func_name}: punto invalido '{head}'.")
            return RESULT_FAIL
        kind, idx = m.group(1), int(m.group(2))
        try:
            speed_pct = float(speed_s)
        except ValueError:
            speed_pct = 50.0

        if kind == 'CART':
            with self._lock:
                if idx <= 0 or idx > len(self._cart_points):
                    self.get_logger().error(f'{func_name}: indice CART{idx} fuera de rango.')
                    return RESULT_FAIL
                target = self._cart_points[idx - 1]
                q0 = list(self._current_jnt)
            q_sol, ok, reason = _inverse_kinematics(target, q0)
            if not ok:
                self.get_logger().warn(
                    f'{func_name}(CART{idx},...): {reason}, el comando no mueve '
                    'el robot simulado.')
                return RESULT_FAIL
            target_deg = [math.degrees(v) for v in q_sol]
            with self._lock:
                self._start_move_to_target_locked(target_deg, speed_pct)
            return RESULT_OK

        with self._lock:
            if idx <= 0 or idx > len(self._jnt_points):
                self.get_logger().error(f'{func_name}: indice JNT{idx} fuera de rango.')
                return RESULT_FAIL
            self._start_move_to_target_locked(self._jnt_points[idx - 1], speed_pct)
        return RESULT_OK

    def _start_move_to_target_locked(self, target_deg, speed_pct, ease_in=True, ease_out=True):
        """Arranca una interpolacion hacia target_deg (grados, 6 valores).

        ease_in/ease_out=False se usa para segmentos intermedios de una
        cadena de SplinePTP, que no deben frenar/arrancar en seco en su
        propio limite porque empalman con el segmento anterior/siguiente
        (ver _tick_joint_states y los comentarios de _ease_in/_ease_out).

        Asume que self._lock ya esta tomado por el caller.
        """
        target_rad = [math.radians(d) for d in target_deg]
        target_rad = [
            min(max(v, JOINT_LIMITS[i][0]), JOINT_LIMITS[i][1])
            for i, v in enumerate(target_rad)
        ]
        self._move_from = list(self._current_jnt)
        self._move_target = target_rad
        self._move_start = self.get_clock().now()
        self._move_duration = self._estimate_duration(self._current_jnt, target_rad, speed_pct)
        self._move_ease_in = ease_in
        self._move_ease_out = ease_out
        self._move_active = True
        # Solo queda "en cadena" (sin frenar) si este segmento tampoco frena
        # al final -- en cuanto uno termina con ease_out=True el brazo vuelve
        # a quedar en reposo, y el proximo segmento que arranque (de la
        # cadena que sea) debe volver a arrancar con ease_in.
        self._spline_chain_active = not ease_out

    def _spline_start(self) -> str:
        # Nueva trayectoria: se descarta lo que haya quedado encolado de un
        # SplineStart/SplinePTP*/SplineEnd anterior que no haya terminado de
        # ejecutarse (el movimiento EN CURSO, si hay uno, no se interrumpe:
        # solo se vacia la cola de puntos pendientes).
        with self._lock:
            self._spline_queue = []
        return RESULT_OK

    def _spline_ptp(self, para: str) -> str:
        parts = para.split(',')
        if len(parts) < 2:
            self.get_logger().error('SplinePTP: se esperaba "JNT<idx>,speed".')
            return RESULT_FAIL
        head, speed_s = parts[0], parts[1]
        m = re.match(r'^(JNT|CART)(\d+)$', head)
        if not m:
            self.get_logger().error(f"SplinePTP: punto invalido '{head}'.")
            return RESULT_FAIL
        kind, idx = m.group(1), int(m.group(2))
        try:
            speed_pct = float(speed_s)
        except ValueError:
            speed_pct = 50.0

        if kind == 'CART':
            with self._lock:
                if idx <= 0 or idx > len(self._cart_points):
                    self.get_logger().error(f'SplinePTP: indice CART{idx} fuera de rango.')
                    return RESULT_FAIL
                target = self._cart_points[idx - 1]
                # IK arranca desde el ULTIMO punto ya encolado (o de la
                # posicion articular actual si la cola esta vacia), no desde
                # _current_jnt siempre: asi la secuencia completa converge a
                # una rama continua del brazo en vez de mezclar soluciones
                # de ramas distintas entre puntos consecutivos.
                q0_deg = self._spline_queue[-1][0] if self._spline_queue else None
            if q0_deg is None:
                with self._lock:
                    q0_deg = [math.degrees(v) for v in self._current_jnt]
            q0_rad = [math.radians(d) for d in q0_deg]
            q_sol, ok, reason = _inverse_kinematics(target, q0_rad)
            if not ok:
                self.get_logger().warn(
                    f'SplinePTP(CART{idx},...): {reason}, este punto se omite '
                    'de la trayectoria.')
                return RESULT_FAIL
            target_deg = [math.degrees(v) for v in q_sol]
            with self._lock:
                self._spline_queue.append((target_deg, speed_pct))
            return RESULT_OK

        with self._lock:
            if idx <= 0 or idx > len(self._jnt_points):
                self.get_logger().error(f'SplinePTP: indice JNT{idx} fuera de rango.')
                return RESULT_FAIL
            # Se guarda una COPIA de los valores, no el indice: Unity reusa
            # los mismos indices JNTPoint(1..5) para cada lote de puntos
            # (ver ControlArticular.cs), asi que si el lote siguiente
            # redefine JNTPoint(1,...) antes de que este punto se ejecute,
            # no debe corromper la posicion ya encolada.
            self._spline_queue.append((list(self._jnt_points[idx - 1]), speed_pct))
        return RESULT_OK

    def _spline_end(self) -> str:
        # No hace falta arrancar nada aca: _tick_joint_states (50Hz por
        # defecto) ya detecta cola pendiente sin movimiento activo y
        # arranca el primer punto en el proximo tick (<=20ms de latencia).
        # SplineEnd solo marca, para quien lea el codigo, el cierre logico
        # del lote -- no hay estado adicional que tocar.
        return RESULT_OK

    def _estimate_duration(self, frm, to, speed_pct):
        speed_pct = min(max(speed_pct, 1.0), 100.0)
        worst = 0.0
        for i in range(6):
            delta = abs(to[i] - frm[i])
            vmax = JOINT_LIMITS[i][2] * (speed_pct / 100.0)
            if vmax <= 1e-6:
                continue
            worst = max(worst, delta / vmax)
        return min(max(worst, self._min_dur), self._max_dur)

    def _stop_motion(self) -> str:
        with self._lock:
            self._move_target = list(self._current_jnt)
            self._move_active = False
            self._spline_queue = []
        return RESULT_OK

    # ------------------------------------------------------------------
    # Timers de publicacion
    # ------------------------------------------------------------------
    def _tick_joint_states(self):
        now = self.get_clock().now()
        with self._lock:
            if self._move_active:
                elapsed = (now - self._move_start).nanoseconds / 1e9
                t = 1.0 if self._move_duration <= 0 else min(1.0, elapsed / self._move_duration)
                if self._easing != 'ease_in_out':
                    eased = t
                elif self._move_ease_in and self._move_ease_out:
                    eased = _smoothstep(t)
                elif self._move_ease_in:
                    eased = _ease_in(t)
                elif self._move_ease_out:
                    eased = _ease_out(t)
                else:
                    eased = t  # segmento intermedio de una cadena: velocidad continua
                self._current_jnt = [
                    frm + (to - frm) * eased
                    for frm, to in zip(self._move_from, self._move_target)
                ]
                if t >= 1.0:
                    self._move_active = False
            # Trayectoria en cola (SplineStart/SplinePTP/SplineEnd): arranca
            # el siguiente punto recien cuando no hay movimiento activo, uno
            # por vez, para que se ejecuten EN ORDEN en vez de todos juntos.
            # ease_in solo si el segmento anterior de la cadena ya freno del
            # todo (o no habia ninguno); ease_out solo si este es el ultimo
            # punto pendiente -- ver _start_move_to_target_locked.
            if not self._move_active and self._spline_queue:
                target_deg, speed_pct = self._spline_queue.pop(0)
                ease_in = not self._spline_chain_active
                ease_out = len(self._spline_queue) == 0
                self._start_move_to_target_locked(
                    target_deg, speed_pct, ease_in=ease_in, ease_out=ease_out)
            position = list(self._current_jnt)
            # Target del movimiento en curso (rad), o la posicion actual si no
            # hay ninguno activo -- asi setpoint == real en reposo, en vez de
            # arrastrar el ultimo target ya alcanzado.
            setpoint_rad = list(self._move_target) if self._move_active else list(position)

        msg = JointState()
        msg.header.stamp = now.to_msg()
        msg.name = list(JOINT_NAMES)
        msg.position = position
        msg.velocity = []
        msg.effort = []
        self._joint_pub.publish(msg)

        deg = [math.degrees(v) for v in position]
        jnt_str = String()
        jnt_str.data = ','.join(f'{v:.2f}' for v in deg)
        self._cur_jnt_str_pub.publish(jnt_str)

        cart = _forward_kinematics_mm_deg(position)
        cart_str = String()
        cart_str.data = ','.join(f'{v:.2f}' for v in cart)
        self._cur_cart_str_pub.publish(cart_str)

        # Mismo timer/cadencia que current_cartesian_position (ver
        # joint_states_rate_hz) para que el graph de tendencia en Unity
        # (SecTrendGraphController.cs) muestree setpoint y real sincronizados.
        setpoint_cart = _forward_kinematics_mm_deg(setpoint_rad)
        setpoint_str = String()
        setpoint_str.data = ','.join(f'{v:.2f}' for v in setpoint_cart)
        self._setpoint_cart_str_pub.publish(setpoint_str)

    def _tick_frstate(self):
        with self._lock:
            jnt_rad = list(self._current_jnt)
            moving = self._move_active
        deg = [math.degrees(v) for v in jnt_rad]
        cart = _forward_kinematics_mm_deg(jnt_rad)
        msg = FRState()
        msg.prg_state = 0
        msg.error_code = 0
        msg.robot_mode = 0
        msg.j1_cur_pos, msg.j2_cur_pos, msg.j3_cur_pos = deg[0], deg[1], deg[2]
        msg.j4_cur_pos, msg.j5_cur_pos, msg.j6_cur_pos = deg[3], deg[4], deg[5]
        msg.cart_x_cur_pos, msg.cart_y_cur_pos, msg.cart_z_cur_pos = cart[0], cart[1], cart[2]
        msg.cart_a_cur_pos, msg.cart_b_cur_pos, msg.cart_c_cur_pos = cart[3], cart[4], cart[5]
        msg.tool_num = 0
        msg.work_num = 0
        msg.j1_cur_tor = 0.0
        msg.j2_cur_tor = 0.0
        msg.j3_cur_tor = 0.0
        msg.j4_cur_tor = 0.0
        msg.j5_cur_tor = 0.0
        msg.j6_cur_tor = 0.0
        msg.prg_name = ''
        msg.prg_total_line = 0
        msg.prg_cur_line = 0
        msg.dgt_output_h = 0
        msg.dgt_output_l = 0
        msg.tl_dgt_output_l = 0
        msg.dgt_input_h = 0
        msg.dgt_input_l = 0
        msg.tl_dgt_input_l = 0
        msg.ft_fx_data = 0.0
        msg.ft_fy_data = 0.0
        msg.ft_fz_data = 0.0
        msg.ft_tx_data = 0.0
        msg.ft_ty_data = 0.0
        msg.ft_tz_data = 0.0
        msg.ft_actstatus = 0
        msg.emg = 0
        msg.robot_motion_done = 0 if moving else 1
        msg.grip_motion_done = 1
        msg.exaxispos1 = 0.0
        msg.exaxispos2 = 0.0
        msg.exaxispos3 = 0.0
        msg.exaxispos4 = 0.0
        msg.check_sum = 0
        msg.start_return = ''
        self._frstate_pub.publish(msg)

    # ------------------------------------------------------------------
    # Mock opcional del endpoint XML-RPC que publisher_subscriber.py
    # consulta directamente (FRCRobot), fuera de ROS2.
    # ------------------------------------------------------------------
    def _start_xmlrpc_mock(self):
        host = self.get_parameter('xmlrpc_mock.host').value
        port = int(self.get_parameter('xmlrpc_mock.port').value)
        node = self

        class _Handler:
            def GetActualJointPosDegree(self_inner, _id):
                with node._lock:
                    deg = [math.degrees(v) for v in node._current_jnt]
                return [0] + deg

            def GetActualTCPPose(self_inner, _id):
                with node._lock:
                    jnt_rad = list(node._current_jnt)
                return [0] + list(_forward_kinematics_mm_deg(jnt_rad))

        try:
            server = SimpleXMLRPCServer((host, port), allow_none=True, logRequests=False)
        except OSError as exc:
            self.get_logger().error(
                f'No se pudo levantar el mock XML-RPC en {host}:{port} ({exc}). '
                'Probablemente el puerto ya esta ocupado (p.ej. por el '
                'TCPServer propio de publisher_subscriber.py, que tambien usa '
                "el puerto 20003). Ver README, seccion 'Simular la IP del "
                "robot para publisher_subscriber'.")
            return
        server.register_instance(_Handler())
        self._xmlrpc_server = server
        thread = threading.Thread(target=server.serve_forever, daemon=True)
        thread.start()
        self.get_logger().warn(
            f'Mock XML-RPC activo en {host}:{port} (GetActualJointPosDegree, '
            'GetActualTCPPose). Esto solo lo alcanza publisher_subscriber.py '
            'si 192.168.58.2 esta aliasado a este host (ver README) y NO hay '
            'otro proceso ya escuchando en el puerto 20003.')

    def destroy_node(self):
        if self._xmlrpc_server is not None:
            self._xmlrpc_server.shutdown()
        super().destroy_node()


def main(args=None):
    rclpy.init(args=args)
    node = MockCmdServer()
    try:
        rclpy.spin(node)
    except KeyboardInterrupt:
        pass
    finally:
        node.destroy_node()
        rclpy.shutdown()


if __name__ == '__main__':
    main()
