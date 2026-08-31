#pragma once

#include <cstddef>
#include <cstdint>
#include <limits>

namespace gua::ws::platform {

using SocketHandle = std::uintptr_t;
inline constexpr SocketHandle invalid_socket = std::numeric_limits<SocketHandle>::max();

class NetworkSession {
public:
    NetworkSession();
    NetworkSession(const NetworkSession&) = delete;
    NetworkSession& operator=(const NetworkSession&) = delete;
    ~NetworkSession();
};

class Socket {
public:
    explicit Socket(SocketHandle value = invalid_socket) noexcept;
    Socket(const Socket&) = delete;
    Socket& operator=(const Socket&) = delete;
    Socket(Socket&& other) noexcept;
    Socket& operator=(Socket&& other) noexcept;
    ~Socket();

    [[nodiscard]] SocketHandle get() const noexcept;
    [[nodiscard]] bool valid() const noexcept;
    SocketHandle release() noexcept;
    void close() noexcept;

private:
    SocketHandle value_;
};

Socket create_listen_socket(unsigned short port);
unsigned short bound_port(SocketHandle listen_socket);
Socket accept_socket(SocketHandle listen_socket) noexcept;
bool wake_listener(unsigned short port) noexcept;
std::ptrdiff_t send_some(SocketHandle socket, const std::uint8_t* data, std::size_t size) noexcept;
std::ptrdiff_t receive_some(SocketHandle socket, std::uint8_t* data, std::size_t size) noexcept;
void shutdown_socket(SocketHandle socket) noexcept;
void close_socket(SocketHandle socket) noexcept;

} // namespace gua::ws::platform
