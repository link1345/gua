#include "socket_platform.hpp"

#include <cerrno>
#include <climits>
#include <stdexcept>

#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>

namespace gua::ws::platform {
namespace {

int native(SocketHandle socket) noexcept
{
    return static_cast<int>(socket);
}

SocketHandle portable(int socket) noexcept
{
    return socket < 0 ? invalid_socket : static_cast<SocketHandle>(socket);
}

void configure_no_sigpipe(int socket)
{
#if defined(__APPLE__)
    int enabled = 1;
    if (::setsockopt(socket, SOL_SOCKET, SO_NOSIGPIPE, &enabled, sizeof(enabled)) != 0)
        throw std::runtime_error("setsockopt SO_NOSIGPIPE failed");
#else
    (void)socket;
#endif
}

} // namespace

NetworkSession::NetworkSession() = default;
NetworkSession::~NetworkSession() = default;
Socket::Socket(SocketHandle value) noexcept : value_(value) {}
Socket::Socket(Socket&& other) noexcept : value_(other.release()) {}
Socket& Socket::operator=(Socket&& other) noexcept
{
    if (this != &other) {
        close();
        value_ = other.release();
    }
    return *this;
}
Socket::~Socket() { close(); }
SocketHandle Socket::get() const noexcept { return value_; }
bool Socket::valid() const noexcept { return value_ != invalid_socket; }
SocketHandle Socket::release() noexcept
{
    const SocketHandle value = value_;
    value_ = invalid_socket;
    return value;
}
void Socket::close() noexcept
{
    if (valid()) {
        close_socket(value_);
        value_ = invalid_socket;
    }
}

Socket create_listen_socket(unsigned short port)
{
    Socket listen_socket(portable(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)));
    if (!listen_socket.valid()) throw std::runtime_error("socket failed");
    configure_no_sigpipe(native(listen_socket.get()));

    int reuse = 1;
    if (::setsockopt(native(listen_socket.get()), SOL_SOCKET, SO_REUSEADDR, &reuse, sizeof(reuse)) != 0)
        throw std::runtime_error("setsockopt failed");

    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = htons(port);
    if (::bind(native(listen_socket.get()), reinterpret_cast<sockaddr*>(&address), sizeof(address)) != 0)
        throw std::runtime_error("bind failed");
    if (::listen(native(listen_socket.get()), SOMAXCONN) != 0)
        throw std::runtime_error("listen failed");
    return listen_socket;
}

Socket accept_socket(SocketHandle listen_socket) noexcept
{
    int accepted;
    do {
        accepted = ::accept(native(listen_socket), nullptr, nullptr);
    } while (accepted < 0 && errno == EINTR);
    if (accepted >= 0) {
        try {
            configure_no_sigpipe(accepted);
        } catch (...) {
            (void)::close(accepted);
            return Socket();
        }
    }
    return Socket(portable(accepted));
}

bool wake_listener(unsigned short port) noexcept
{
    Socket wake(portable(::socket(AF_INET, SOCK_STREAM, IPPROTO_TCP)));
    if (!wake.valid()) return false;
    sockaddr_in address {};
    address.sin_family = AF_INET;
    address.sin_addr.s_addr = htonl(INADDR_LOOPBACK);
    address.sin_port = htons(port);
    int result;
    do {
        result = ::connect(native(wake.get()), reinterpret_cast<sockaddr*>(&address), sizeof(address));
    } while (result != 0 && errno == EINTR);
    return result == 0;
}

std::ptrdiff_t send_some(SocketHandle socket, const std::uint8_t* data, std::size_t size) noexcept
{
    ssize_t result;
    do {
#if defined(__linux__)
        result = ::send(native(socket), data, size, MSG_NOSIGNAL);
#else
        result = ::send(native(socket), data, size, 0);
#endif
    } while (result < 0 && errno == EINTR);
    return result;
}

std::ptrdiff_t receive_some(SocketHandle socket, std::uint8_t* data, std::size_t size) noexcept
{
    ssize_t result;
    do {
        result = ::recv(native(socket), data, size, 0);
    } while (result < 0 && errno == EINTR);
    return result;
}

void shutdown_socket(SocketHandle socket) noexcept
{
    if (socket != invalid_socket) (void)::shutdown(native(socket), SHUT_RDWR);
}

void close_socket(SocketHandle socket) noexcept
{
    if (socket != invalid_socket) (void)::close(native(socket));
}

} // namespace gua::ws::platform
