#ifndef STATE_FEEDBACK_H
#define STATE_FEEDBACK_H

#include "string"
#include "sys/socket.h"
#include "sys/types.h"
#include "sys/time.h"
#include "netinet/in.h"
#include "arpa/inet.h"
#include "fcntl.h"
#include "unistd.h"
#include <cerrno>
#include <chrono>
#include "rclcpp/rclcpp.hpp"
#include "global_val.h"
#include "data_type_def.h"
#include "frhal_msgs/msg/fr_state.hpp"

class state_recv_thread:public rclcpp::Node{//接受非实时和实时反馈数据的节点
public:
    explicit state_recv_thread(const std::string node_name);
    ~state_recv_thread();
private:
    std::string _controller_ip;
    bool _connect_state_socket();//创建并连接状态socket(8083)
    void _close_state_socket();
    void _state_recv_callback();
    rclcpp::Publisher<frhal_msgs::msg::FRState>::SharedPtr _state_publisher;//进程内通信，用于发送状态数据字符串
    rclcpp::TimerBase::SharedPtr _locktimer;
    rclcpp::TimerBase::SharedPtr _rt_locktimer;
    int port1 = 8083;//非实时状态数据获取端口
    int port2 = 30004;//实时状态数据获取端口
    int _socketfd1 = -1;
    size_t _recv_offset = 0;//当前状态帧已累积的字节数(TCP流可能分段到达)
    std::chrono::steady_clock::time_point _next_reconnect_time;//断线后限制重连频率
};

#endif // STATE_FEEDBACK_H
