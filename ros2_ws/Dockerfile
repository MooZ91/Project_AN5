FROM ros:jazzy-ros-base

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        cmake \
        python3-colcon-common-extensions \
        python3-rosdep \
        ros-jazzy-rosbridge-server \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /ros2_ws
COPY src ./src

# rosdep resolves exec/build deps declared in package.xml (rosbridge_server,
# etc); --ignore-src evita que intente resolver los paquetes locales del
# propio workspace (frhal_msgs, code, fr_ros2, an5_mock_sim) como si fueran
# dependencias externas.
RUN (rosdep init || true) \
    && rosdep update \
    && rosdep install --from-paths src --ignore-src -r -y

# fr_ros2 (driver del robot real) tambien se compila: no tiene dependencias
# de hardware a nivel de build, solo intenta conectarse por TCP en runtime.
RUN . /opt/ros/jazzy/setup.sh \
    && colcon build --packages-select frhal_msgs code fr_ros2 an5_mock_sim

COPY docker-entrypoint.sh /
RUN chmod +x /docker-entrypoint.sh

ENTRYPOINT ["/docker-entrypoint.sh"]
CMD ["ros2", "launch", "an5_mock_sim", "sim.launch.py"]
